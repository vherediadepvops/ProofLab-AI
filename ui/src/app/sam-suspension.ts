import type {AuthenticatedSamVideoArchive,SamPlaybackTrack} from './sam-playback';
import type {ImagePivot} from './pendulum-kinematics';

export const automaticPivotProvenance='SPARK_SAM_ESTIMATE_REVIEW_PENDING';
export interface AutomaticSuspension {
  pivot:ImagePivot|null;reason:string;method:'SPARK'|'SPARK_WITH_SAM_ARC'|null;
  spreadPixels:number;radialRmsPixels:number|null;radiusPixels:number|null;samples:number;
  excludedFrameIndices:number[];
}
const none=(reason:string):AutomaticSuspension=>({
  pivot:null,reason,method:null,spreadPixels:0,radialRmsPixels:null,radiusPixels:null,samples:0,excludedFrameIndices:[]
});
const quantile=(values:number[],q:number)=>{
  const sorted=[...values].sort((a,b)=>a-b);
  return sorted[Math.min(sorted.length-1,Math.floor((sorted.length-1)*q))];
};

/** Spark identifies the visible suspension. SAM can refine a nearby point with
 * a stable image-space arc. This is an estimate, not a verified physical pivot
 * or a scientific verdict about the phenomenon. No theory/length is used. */
export function resolveSamSuspension(
  archive:AuthenticatedSamVideoArchive,track:SamPlaybackTrack|null
):AutomaticSuspension {
  let e=archive.suspension;const s=archive.receipt.scene;
  const review=archive.suspensionReview;
  // Authenticated, immutable review is bound to this exact SAM result.
  if(e && review && review.protocol==='SPARK_SAM_SUSPENSION_REVIEW_V1' &&
    /^[a-f0-9]{64}$/.test(review.key) && /^[a-f0-9]{64}$/.test(review.responseSha256) &&
    review.model==='muse-spark-1.3' && review.sourceSha256===archive.receipt.originalVideoSha256 &&
    review.analysisId===archive.receipt.analysisId &&
    review.samResponseSha256===archive.receipt.responseJsonSha256 && review.sceneKey===s?.key) {
    if(review.answer.camera!=='STATIC' || !['RELOCATED','CONFIRMED'].includes(review.answer.decision))
      return none('SPARK_REVIEW_INCONCLUSIVE');
    e={...e,points:review.answer.points};
  }
  if(!e||!s||!track)return none('MISSING_SPARK_SUSPENSION');
  if(s.origin!=='SPARK_GROUNDING'||s.arrangement!=='simple_pendulum'||
    e.sourceSha256!==archive.receipt.originalVideoSha256||e.sceneKey!==s.key||
    e.evidenceKey!==s.evidenceKey||e.responseSha256!==s.responseSha256||
    e.model!=='muse-spark-1.3'||e.width!==track.frameWidth||e.height!==track.frameHeight||
    e.width!==s.width||e.height!==s.height)return none('SUSPENSION_PROVENANCE_MISMATCH');
  const anchors=e.points;
  if(!Array.isArray(anchors)||anchors.length!==3||new Set(anchors.map(p=>p.frame)).size!==3||
    anchors.some(p=>![0,1,2].includes(p.frame)||!p.visible||
      !Number.isFinite(p.confidence)||p.confidence<.8||p.confidence>1||
      !Number.isFinite(p.x)||!Number.isFinite(p.y)||p.x<0||p.x>=1000||p.y<0||p.y>=1000))
    return none('SPARK_SUSPENSION_UNCERTAIN');
  const x=quantile(anchors.map(p=>p.x*e.width/1000),.5);
  const y=quantile(anchors.map(p=>p.y*e.height/1000),.5);
  const spread=Math.max(...anchors.map(p=>Math.hypot(p.x*e.width/1000-x,p.y*e.height/1000-y)));
  if(spread>Math.max(3,Math.min(e.width,e.height)*.04))
    return none('SUSPENSION_MOVES_OR_IS_AMBIGUOUS');
  const all=track.masks.map(m=>({x:m.centroidXpx,y:m.centroidYpx}));
  if(all.length<20||all.some(p=>!Number.isFinite(p.x)||!Number.isFinite(p.y)))
    return none('TOO_FEW_TRACKED_POSITIONS');
  const radii=all.map(p=>Math.hypot(p.x-x,p.y-y)),rough=quantile(radii,.5);
  if(rough<10||rough>2*Math.hypot(e.width,e.height)||spread>Math.max(3,rough*.05))
    return none('SUSPENSION_GEOMETRY_UNCERTAIN');
  let points=all.filter((_,i)=>Math.abs(radii[i]-rough)<=Math.max(6,rough*.08));
  if(points.length<20||points.length<all.length*.65)return none('TRACK_INCONSISTENT_WITH_SUSPENSION');
  const quality=(cx:number,cy:number)=>{
    const distances=points.map(p=>Math.hypot(p.x-cx,p.y-cy));
    const radius=quantile(distances,.5);
    const residuals=distances.map(r=>Math.abs(r-radius));
    const rms=Math.sqrt(residuals.reduce((sum,r)=>sum+r*r,0)/residuals.length);
    const angles=points.map(p=>Math.atan2(p.x-cx,p.y-cy));
    return {radius,rms,p95:quantile(residuals,.95),span:quantile(angles,.95)-quantile(angles,.05)};
  };
  let cx=x,cy=y,method:AutomaticSuspension['method']='SPARK';
  // Centering the algebraic fit avoids dependence on image origin/resolution.
  const mx=points.reduce((v,p)=>v+p.x,0)/points.length,my=points.reduce((v,p)=>v+p.y,0)/points.length;
  let xx=0,xy=0,yy=0,xq=0,yq=0;
  for(const p of points){const u=p.x-mx,v=p.y-my,q=u*u+v*v;
    xx+=u*u;xy+=u*v;yy+=v*v;xq+=u*q;yq+=v*q;}
  const det=xx*yy-xy*xy;
  if(det>1e-5*(xx+yy)**2){
    const fx=mx+(xq*yy-yq*xy)/(2*det),fy=my+(yq*xx-xq*xy)/(2*det);
    const fit=quality(fx,fy);
    if(Number.isFinite(fx)&&Number.isFinite(fy)&&fx>=0&&fx<e.width&&fy>=0&&fy<e.height&&
      Math.hypot(fx-x,fy-y)<=Math.max(4,rough*.06)&&fit.span>=.25&&
      fit.radius>rough*.75&&fit.radius<rough*1.25&&
      fit.rms<=Math.max(1.5,fit.radius*.01)&&fit.p95<=Math.max(3,fit.radius*.03)&&
      points.every(p=>p.y>fy)){
      cx=fx;cy=fy;method='SPARK_WITH_SAM_ARC';
    }
  }
  // Exclude gross spatial mismatches only from playback. The untouched SAM
  // centroids still enter the scientific period report; arc agreement must
  // never be improved by silently dropping inconvenient observations.
  const displayRadius=quality(cx,cy).radius;
  const residuals=points.map(p=>Math.abs(Math.hypot(p.x-cx,p.y-cy)-displayRadius));
  const limit=Math.max(4,rough*.025,quantile(residuals,.5)*4.5);
  points=points.filter((_,i)=>residuals[i]<=limit);
  if(points.length<20||points.length<all.length*.65)return none('TRACK_INCONSISTENT_WITH_SUSPENSION');
  const q=quality(cx,cy);
  if(points.some(p=>p.y<=cy)||q.rms>Math.max(2,q.radius*.025)||
    q.p95>Math.max(4,q.radius*.06))return none('TRACK_INCONSISTENT_WITH_SUSPENSION');
  return {pivot:{pivotXpx:cx/e.width*480,pivotYpx:cy/e.height*270},
    reason:'ESTIMATED_NOT_HUMAN_CONFIRMED',method,spreadPixels:spread,
    radialRmsPixels:q.rms,radiusPixels:q.radius,samples:points.length,
    excludedFrameIndices:all.flatMap((p,i)=>points.includes(p)?[]:[track.masks[i].frameIndex])};
}

/** Display-only exclusions. Provider masks and scientific reports stay intact. */
export function suspensionPlaybackTrack(track:SamPlaybackTrack,estimate:AutomaticSuspension|null):SamPlaybackTrack {
  if(!estimate?.pivot || !estimate.excludedFrameIndices.length)return track;
  const excluded=new Set(estimate.excludedFrameIndices);
  return {...track,masks:track.masks.filter(m=>!excluded.has(m.frameIndex)),
    points:track.points.filter((_,i)=>!excluded.has(track.masks[i].frameIndex))};
}
/** All-frame numerical evidence for a visual review, not a phenomenon verdict. */
export function samSuspensionDiagnostics(archive:AuthenticatedSamVideoArchive,track:SamPlaybackTrack|null) {
  const result=resolveSamSuspension({...archive,suspensionReview:null},track);
  const e=archive.suspension;
  const anchors=e?.points.filter(p=>p.visible&&Number.isFinite(p.x)&&Number.isFinite(p.y))??[];
  const center=e&&anchors.length?{
    x:quantile(anchors.map(p=>p.x*e.width/1000),.5),
    y:quantile(anchors.map(p=>p.y*e.height/1000),.5)}:null;
  const all=track?.masks??[];
  const radii=center?all.map(p=>Math.hypot(p.centroidXpx-center.x,p.centroidYpx-center.y)):[];
  const median=radii.length?quantile(radii,.5):null;
  const residuals=median!==null?radii.map(r=>Math.abs(r-median)):[];
  const count=Math.min(96,all.length);
  return {protocol:'SAM_SUSPENSION_DIAGNOSTICS_V1',result,
    sourceFrames:archive.receipt.sourceFrameCount,detectedFrames:all.length,
    imageWidth:track?.frameWidth??e?.width,imageHeight:track?.frameHeight??e?.height,
    proposedPivotPixels:center,originalSparkPoints:anchors,
    allFrameMedianRadiusPixels:median,
    allFrameRadialRmsPixels:residuals.length?Math.sqrt(residuals.reduce((s,r)=>s+r*r,0)/residuals.length):null,
    allFrameRadialP95Pixels:residuals.length?quantile(residuals,.95):null,
    samples:Array.from({length:count},(_,i)=>{
      const m=all[count===1?0:Math.round(i*(all.length-1)/(count-1))];
      return {frame:m.frameIndex,timeSeconds:m.timeSeconds,x:m.centroidXpx,y:m.centroidYpx};
    }),
    scope:'IMAGE_SPACE_ONLY; NO_PHYSICAL_CALIBRATION; RAW_SAM_POINTS_RETAINED; UNIFORM_SAMPLES_MAY_ALIAS'
  };
}
