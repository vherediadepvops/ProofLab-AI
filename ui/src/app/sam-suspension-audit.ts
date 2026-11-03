import type {AuthenticatedSamVideoArchive,SamSuspensionEvidence} from './sam-playback';

export const legacyContactAuditProtocol='SPARK_ORIGINAL_50_CONTACT_AUDIT_V2';
export const contactAuditProtocol='SPARK_ORIGINAL_50_CONTACT_AUDIT_V3';
export const isContactAuditProtocol=(protocol:string|null|undefined):boolean=>
  protocol===contactAuditProtocol||protocol===legacyContactAuditProtocol;
// C# Math.Round uses ties-to-even: the middle original ordinal is 24, not 25.
const auditOrdinals=[0,6,12,18,24,31,37,43,49];
type AuditPoint=NonNullable<SamSuspensionEvidence['audit']>['points'][number];
type AuditResolution={ok:false;reason:string}|{ok:true;x:number;y:number;spread:number;
  inliers:AuditPoint[];rejectedOrdinals:number[]};
const unresolved=():AuditResolution=>({ok:false,reason:'CORD_CONTACT_UNRESOLVED'});
const median=(values:number[])=>{
  const sorted=[...values].sort((a,b)=>a-b);
  return (sorted[Math.floor((sorted.length-1)/2)]+sorted[Math.floor(sorted.length/2)])/2;
};

/** Revalidate the independent contact audit. The original 50 points are kept as
 * evidence, but neither their consensus nor an arc fit can replace this audit.
 * Exact reference-to-source binding is authenticated by the server; here the
 * audited times must also belong to the original PTS and span its timeline. */
export function resolveContactAudit(
  evidence:SamSuspensionEvidence,receipt:AuthenticatedSamVideoArchive['receipt']
):AuditResolution {
  const audit=evidence.audit,original=evidence.points,pts=receipt.framePtsSeconds;
  const independent=evidence.refinement===contactAuditProtocol;
  if(!isContactAuditProtocol(evidence.refinement) || !audit ||
    (independent?audit.protocol!==contactAuditProtocol:audit.protocol!=null) || !Array.isArray(audit.points) || audit.points.some(p=>!p || typeof p!=='object'))return unresolved();
  if(audit.points.some(p=>p?.referenceKind==='MOVING_CONTACT'))
    return {ok:false,reason:'MOVING_CORD_CONTACT'};
  if(audit.camera!=='STATIC' || !Number.isFinite(evidence.width) || !Number.isFinite(evidence.height) ||
    evidence.width<2 || evidence.height<2 || !Array.isArray(original) || original.length!==50 ||
    original.some((p,i)=>!p || p.frame!==i || typeof p.visible!=='boolean' ||
      !validCoordinates(p) || !Number.isFinite(p.confidence) || p.confidence<0 || p.confidence>1) ||
    !Array.isArray(pts) || pts.length<50 || receipt.sourceFrameCount!==pts.length ||
    pts.some((t,i)=>!Number.isFinite(t) || t<0 || i>0&&t<=pts[i-1]) ||
    audit.points.length!==auditOrdinals.length)return unresolved();
  const points=audit.points,c=audit.consensus;
  const limit=Math.max(3,Math.min(evidence.width,evidence.height)*.005);
  const distance=(a:{x:number;y:number},b:{x:number;y:number})=>
    Math.hypot((a.x-b.x)*evidence.width/1000,(a.y-b.y)*evidence.height/1000);
  for(let i=0;i<points.length;i++) {
    const p=points[i];
    if(!p || p.frame!==auditOrdinals[i] || typeof p.visible!=='boolean' ||
      !['CONFIRMED','CORRECTED','UNCERTAIN'].includes(p.decision) ||
      !['FIXED_CORD_CONTACT','UNCLEAR'].includes(p.referenceKind) ||
      !validCoordinates(p) || !Number.isFinite(p.confidence) || p.confidence<0 || p.confidence>1 ||
      typeof p.visualReason!=='string' || !p.visualReason.trim() || p.visualReason.length>1000 ||
      !Number.isFinite(p.timeSeconds) || !pts.some(t=>Math.abs(t-p.timeSeconds)<=1e-6) ||
      i>0&&p.timeSeconds<=points[i-1].timeSeconds)return unresolved();
    if(independent) {
      const expected=!p.visible?'UNCERTAIN':original[p.frame].visible&&distance(p,original[p.frame])<=limit?'CONFIRMED':'CORRECTED';
      if(p.decision!==expected)return unresolved();
    }
    if(p.decision==='UNCERTAIN') {
      if(p.visible || p.x!==0 || p.y!==0 || p.confidence!==0 || p.referenceKind!=='UNCLEAR')return unresolved();
    }else {
      if(!p.visible || p.referenceKind!=='FIXED_CORD_CONTACT')return unresolved();
      // A correction may move the candidate. A confirmation must refer to the
      // original point in this exact image, even when its initial confidence is low.
      if(p.decision==='CONFIRMED' && (!original[p.frame].visible || distance(p,original[p.frame])>limit))
        return unresolved();
    }
  }
  if(Math.abs(points[0].timeSeconds-pts[0])>1e-6 ||
    Math.abs(points[points.length-1].timeSeconds-pts[pts.length-1])>1e-6 ||
    !c || c.status!=='CONSENSUS_ESTIMATE' || c.camera!=='STATIC' || c.submitted!==9 ||
    !Array.isArray(c.inlierOrdinals) || new Set(c.inlierOrdinals).size!==c.inlierOrdinals.length ||
    c.inlierOrdinals.some(i=>!auditOrdinals.includes(i)))return unresolved();
  const usable=points.filter(p=>p.visible && p.referenceKind==='FIXED_CORD_CONTACT' &&
    p.decision!=='UNCERTAIN' && p.confidence>=.8);
  if(usable.length<6 || c.usable!==usable.length)return unresolved();
  const groups=usable.map(p=>usable.filter(q=>distance(p,q)<=limit)).sort((a,b)=>b.length-a.length);
  const candidate={x:median(groups[0].map(p=>p.x)),y:median(groups[0].map(p=>p.y))};
  const best=usable.filter(p=>distance(candidate,p)<=limit);
  const outside=usable.filter(p=>!best.includes(p));
  const rival=outside.length?Math.max(...outside.map(p=>outside.filter(q=>distance(p,q)<=limit).length)):0;
  if(best.length<6 || best.length<usable.length*.6 || rival>=best.length*.5 ||
    best.length!==c.inlierOrdinals.length || best.some(p=>!c.inlierOrdinals.includes(p.frame)))return unresolved();
  const first=pts[0],duration=pts[pts.length-1]-first;
  const bins=new Set(best.map(p=>Math.min(4,Math.floor(5*(p.timeSeconds-first)/duration))));
  const coverage=(best[best.length-1].timeSeconds-best[0].timeSeconds)/duration;
  if(bins.size<3 || coverage<.6 || coverage>1+1e-7 || !Number.isFinite(c.timeCoverage) || c.timeCoverage<.6 || c.timeCoverage>1 ||
    Math.abs(c.timeCoverage-coverage)>1e-6)return unresolved();
  const center={x:median(best.map(p=>p.x)),y:median(best.map(p=>p.y))};
  const spread=Math.max(...best.map(p=>distance(p,center)));
  if(c.x===null || c.y===null || !Number.isFinite(c.x) || !Number.isFinite(c.y) ||
    Math.abs(c.x-center.x)>1e-6 || Math.abs(c.y-center.y)>1e-6 ||
    !Number.isFinite(c.spreadPixels) || Math.abs(c.spreadPixels-spread)>1e-5 || spread>limit)return unresolved();
  return {ok:true,x:center.x*evidence.width/1000,y:center.y*evidence.height/1000,spread,inliers:best,
    rejectedOrdinals:points.filter(p=>!best.includes(p)).map(p=>p.frame)};
}

function validCoordinates(p:{x:number;y:number}):boolean {
  return Number.isFinite(p.x)&&Number.isFinite(p.y)&&p.x>=0&&p.x<1000&&p.y>=0&&p.y<1000;
}
