import {resolveSamSuspension,suspensionPlaybackTrack} from './sam-suspension';
import {samAngularFrameAt,nearestSamMask,type AuthenticatedSamVideoArchive,type SamPlaybackTrack} from './sam-playback';

function experiment(width=1080,height=1920,offset=0) {
  const cx=width*.5,cy=height*.15,radius=Math.min(width,height)*.65;
  const masks=Array.from({length:601},(_,i)=>{
    const t=i/60,theta=.35*Math.sin(2*Math.PI*t/2);
    const x=cx+radius*Math.sin(theta),y=cy+radius*Math.cos(theta);
    return {frameIndex:i,timeSeconds:t,left:x-5,top:y-5,right:x+5,bottom:y+5,
      maskPath:'',centroidXpx:x,centroidYpx:y,foregroundPixels:100};
  });
  const track:SamPlaybackTrack={frameWidth:width,frameHeight:height,
    source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId:'1',masks,
    points:masks.map(m=>({timeSeconds:m.timeSeconds,x:m.centroidXpx/width*480,
      y:m.centroidYpx/height*270,approximateRadiusPixels:5,source:'SAM_3_1_CACHED_PROVIDER_CENTROID_NOT_CALIBRATED'}))};
  const archive={state:{},receipt:{originalVideoSha256:'a'.repeat(64),
    framePtsSeconds:masks.map(m=>m.timeSeconds),sourceFrameCount:masks.length,scene:{
    key:'b'.repeat(64),evidenceKey:'c'.repeat(64),responseSha256:'d'.repeat(64),
    origin:'SPARK_GROUNDING',arrangement:'simple_pendulum',width,height}},
    suspension:{sourceSha256:'a'.repeat(64),sceneKey:'b'.repeat(64),evidenceKey:'c'.repeat(64),
      responseSha256:'d'.repeat(64),model:'muse-spark-1.3',width,height,
      points:[0,1,2].map(frame=>({frame,visible:true,x:(cx+offset)/width*1000,
        y:(cy+offset)/height*1000,confidence:.9}))}} as unknown as AuthenticatedSamVideoArchive;
  return {archive,track,cx,cy,radius};
}
describe('Automatic Spark/SAM suspension estimates',()=>{
  for(const [protocol,count] of [['SPARK_ORIGINAL_50_OBJECT_REVIEW_V1',50],['SPARK_DENSE_OBJECT_AND_EXIT_50_V1',50],['SPARK_DENSE_OBJECT_AND_EXIT_50_V1',100],['SPARK_DENSE_OBJECT_AND_EXIT_50_V1',150],['SPARK_PROGRESSIVE_EXIT_50_X1_5_X3_V1',50],['SPARK_PROGRESSIVE_CONTACT_50_X1_5_X3_V2',50]] as const) it('uses '+protocol+' consensus from '+count+' frames without averaging the moving body',()=>{
    const {archive,track,cx,cy}=experiment();
    const e=archive.suspension!;
    e.refinement=protocol;
    if(protocol==='SPARK_PROGRESSIVE_CONTACT_50_X1_5_X3_V2')e.contactReviews=[0,1].map(()=>({
      referenceKind:'FIXED_CORD_CONTACT',loopMotion:'MOVING',reason:'Directly visible cord contact; lower loop moves.'}));
    e.points=Array.from({length:count},(_,frame)=>({frame,visible:true,x:500,y:150,confidence:.95}));
    e.consensus={status:'CONSENSUS_ESTIMATE',camera:'STATIC',inlierOrdinals:e.points.map(p=>p.frame),
      submitted:count,usable:count,timeCoverage:1,x:500,y:150,spreadPixels:0};
    const result=resolveSamSuspension(archive,track);
    expect(result.pivot).not.toBeNull();
    expect(result.pivot!.pivotXpx/480*1080).toBeCloseTo(cx,8);
    expect(result.pivot!.pivotYpx/270*1920).toBeCloseTo(cy,8);
    expect(samAngularFrameAt(track,result.pivot!,1)!.angularVelocityRadiansPerSecond!).toBeCloseTo(-.35*Math.PI,2);
    // A minority of outlying Spark anchors must not suppress every SAM measurement.
    e.points.forEach((p,i)=>{if(i%5===0){p.x=700;p.y=400;}});
    e.consensus.inlierOrdinals=e.points.filter(p=>p.frame%5!==0).map(p=>p.frame);
    const robust=resolveSamSuspension(archive,track);
    expect(robust.pivot).not.toBeNull();expect(robust.rejectedAnchorFrames?.length).toBe(count/5);
    if(e.contactReviews) {
      e.contactReviews[1].referenceKind='MOVING_CONTACT';
      expect(resolveSamSuspension(archive,track).reason).toBe('MOVING_CORD_CONTACT');
      e.contactReviews[1].referenceKind='UNRESOLVED';
      expect(resolveSamSuspension(archive,track).reason).toBe('CORD_CONTACT_UNRESOLVED');
      e.contactReviews=[];
      expect(resolveSamSuspension(archive,track).pivot).toBeNull();
      e.contactReviews=[0,1].map(()=>({referenceKind:'FIXED_CORD_CONTACT',loopMotion:'MOVING',reason:'Visible contact.'}));
    }
    e.consensus.status='ZOOM_REVIEW_INCONCLUSIVE';
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    e.consensus.status='CONSENSUS_ESTIMATE';
    e.consensus.camera='MOVING';
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    e.consensus.camera='STATIC';e.consensus.x=510;
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  it('preserves the original 50-point consensus without requiring zoom contact reviews or replacing it with a circle fit',()=>{
    const {archive,track,cx,cy}=experiment(1080,1920,3),e=archive.suspension!;
    e.refinement='SPARK_ORIGINAL_50_OBJECT_REVIEW_V1';e.contactReviews=null;
    const seed=e.points[0];
    e.points=Array.from({length:50},(_,frame)=>({...seed,frame}));
    e.consensus={status:'CONSENSUS_ESTIMATE',camera:'STATIC',inlierOrdinals:e.points.map(p=>p.frame),
      submitted:50,usable:50,timeCoverage:1,x:seed.x,y:seed.y,spreadPixels:0};
    const original=JSON.stringify(e),result=resolveSamSuspension(archive,track);
    expect(result.method).toBe('SPARK');
    expect(result.pivot!.pivotXpx/480*1080).toBeCloseTo(cx+3,8);
    expect(result.pivot!.pivotYpx/270*1920).toBeCloseTo(cy+3,8);
    expect(JSON.stringify(e)).toBe(original);
    for(const count of [49,100,150]){
      e.points=Array.from({length:count},(_,frame)=>({...seed,frame}));
      Object.assign(e.consensus,{submitted:count,usable:count,inlierOrdinals:e.points.map(p=>p.frame)});
      expect(resolveSamSuspension(archive,track).reason).toBe('SPARK_DENSE_SUSPENSION_INCONCLUSIVE');
    }
  });
  for(const protocol of ['SPARK_VISUAL_OBJECT_AND_EXIT_CROP_V2','SPARK_VISUAL_BODY_AND_EXIT_CROPS_V3']) it('preserves visually localized exit '+protocol,()=>{
    const {archive,track,cx,cy}=experiment(1080,1920,3);
    archive.suspension!.refinement=protocol;
    const result=resolveSamSuspension(archive,track);
    expect(result.method).toBe('SPARK');
    expect(result.pivot!.pivotXpx/480*1080).toBeCloseTo(cx+3,8);
    expect(result.pivot!.pivotYpx/270*1920).toBeCloseTo(cy+3,8);
    archive.suspension!.points[0].x+=10;
    const recovered=resolveSamSuspension(archive,track);
    expect(recovered.pivot).not.toBeNull();
    expect(recovered.rejectedAnchorFrames).toEqual([0]);
  });

  for(const [w,h] of [[1080,1920],[3840,2160]]) {
    it('recovers known native geometry and measured angular velocity at '+w+'x'+h,()=>{
      const {archive,track,cx,cy}=experiment(w,h,8);
      const result=resolveSamSuspension(archive,track);
      expect(result.method).toBe('SPARK_WITH_SAM_ARC');
      expect(result.pivot!.pivotXpx/480*w).toBeCloseTo(cx,5);
      expect(result.pivot!.pivotYpx/270*h).toBeCloseTo(cy,5);
      const center=samAngularFrameAt(track,result.pivot!,1)!;
      expect(center.angleDegrees).toBeCloseTo(0,6);
      expect(center.angularVelocityRadiansPerSecond!).toBeCloseTo(-.35*Math.PI,2);
      const extreme=samAngularFrameAt(track,result.pivot!,.5)!;
      expect(extreme.angleDegrees).toBeCloseTo(.35*180/Math.PI,5);
      expect(extreme.angularVelocityRadiansPerSecond!).toBeCloseTo(0,5);
    });
  }
  it('accepts authenticated dense reference ordinals without changing angular geometry',()=>{
    const {archive,track}=experiment();const old=resolveSamSuspension(archive,track);
    archive.suspension!.points.forEach((p,i)=>p.frame=[0,25,49][i]);
    expect(resolveSamSuspension(archive,track)).toEqual(old);
    archive.suspension!.points[1].frame=24;
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  it('rejects substituted source, scene, response or coordinate geometry',()=>{
    for(const change of [{sourceSha256:'e'.repeat(64)},{sceneKey:'e'.repeat(64)},
      {responseSha256:'e'.repeat(64)},{width:777},{model:'other'}]) {
      const {archive,track}=experiment();Object.assign(archive.suspension!,change);
      expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    }
  });
  it('keeps ambiguous or moving suspension inconclusive',()=>{
    const {archive,track}=experiment();
    archive.suspension!.points[0].confidence=.7;
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    archive.suspension!.points[0].confidence=.9;
    archive.suspension!.points[0].x+=150;
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  it('does not invent a point from SAM alone or from a straight line',()=>{
    const {archive,track}=experiment();
    const absent={...archive,suspension:null};
    expect(resolveSamSuspension(absent,track).pivot).toBeNull();
    track.masks.forEach((m,i)=>{m.centroidXpx=100+i;m.centroidYpx=400+i;});
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  it('suppresses gross bad frames for display while preserving every scientific observation',()=>{
    const {archive,track}=experiment();
    for(let i=120;i<150;i++){track.masks[i].centroidXpx=10;track.masks[i].centroidYpx=20;}
    const raw=JSON.stringify(track),result=resolveSamSuspension(archive,track);
    expect(result.pivot).not.toBeNull();expect(result.excludedFrameIndices.length).toBe(30);
    const display=suspensionPlaybackTrack(track,result);
    expect(display.masks.length).toBe(571);
    expect(nearestSamMask(display,2.2,.008)).toBeNull();
    expect(samAngularFrameAt(display,result.pivot!,2.2)).toBeNull();
    expect(JSON.stringify(track)).toBe(raw);
    expect(suspensionPlaybackTrack(track,null)).toBe(track);
  });
  it('recovers immediately after an isolated bad frame without differentiating across it',()=>{
    const {archive,track}=experiment();
    track.masks[120].centroidXpx=10;track.masks[120].centroidYpx=20;
    const result=resolveSamSuspension(archive,track),display=suspensionPlaybackTrack(track,result);
    expect(result.excludedFrameIndices).toEqual([120]);
    expect(samAngularFrameAt(display,result.pivot!,2,{width:1080,height:1920},.008)).toBeNull();
    const next=samAngularFrameAt(display,result.pivot!,121/60)!;
    expect(Number.isFinite(next.angleDegrees)).toBeTrue();
    expect(next.angularVelocityRadiansPerSecond).toBeNull();
    expect(samAngularFrameAt(display,result.pivot!,122/60)!.isValid).toBeTrue();
  });
  it('does not accept a minority arc among unrelated detections',()=>{
    const {archive,track}=experiment();
    for(let i=0;i<450;i++){track.masks[i].centroidXpx=100+(i%20)*10;track.masks[i].centroidYpx=100+(i%17)*20;}
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
});
describe('Spark review is a measured proposal, never a geometry override',()=>{
  function reviewed() {
    const data=experiment(408,720);
    data.archive.receipt.analysisId='one';
    data.archive.receipt.responseJsonSha256='e'.repeat(64);
    const points=structuredClone(data.archive.suspension!.points);
    data.archive.suspension!.points.forEach(p=>p.x=950);
    data.archive.suspensionReview={protocol:'SPARK_SAM_SUSPENSION_REVIEW_V1',key:'f'.repeat(64),
      analysisId:'one',sourceSha256:'a'.repeat(64),samResponseSha256:'e'.repeat(64),sceneKey:'b'.repeat(64),
      responseSha256:'f'.repeat(64),model:'muse-spark-1.3',
      answer:{decision:'RELOCATED',camera:'STATIC',justification:{es:'Prueba sintética',en:'Synthetic test'},points}};
    return data;
  }
  it('recovers angles only after a corrected visible suspension passes the same checks',()=>{
    const {archive,track}=reviewed();
    expect(resolveSamSuspension({...archive,suspensionReview:null},track).pivot).toBeNull();
    const result=resolveSamSuspension(archive,track);
    expect(result.pivot).not.toBeNull();
    expect(samAngularFrameAt(track,result.pivot!,1)!.angularVelocityRadiansPerSecond!).toBeCloseTo(-.35*Math.PI,2);
  });
  it('rejects a review for another source, scene, analysis, response or model',()=>{
    for(const change of [{analysisId:'two'},{sourceSha256:'0'.repeat(64)},
      {sceneKey:'0'.repeat(64)},{samResponseSha256:'0'.repeat(64)},{model:'other'}]) {
      const {archive,track}=reviewed();Object.assign(archive.suspensionReview!,change);
      expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    }
  });
  it('does not turn a moving camera, ambiguous review or poor geometry into angular values',()=>{
    for(const camera of ['MOVING','UNCLEAR'] as const) {
      const {archive,track}=reviewed();
      archive.suspension!.points=structuredClone(archive.suspensionReview!.answer.points);
      expect(resolveSamSuspension({...archive,suspensionReview:null},track).pivot).not.toBeNull();
      archive.suspensionReview!.answer.camera=camera;
      expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    }
    const {archive,track}=reviewed();
    archive.suspensionReview!.answer.decision='INCONCLUSIVE';
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    archive.suspensionReview!.answer.decision='CONFIRMED';
    archive.suspensionReview!.answer.points.forEach(p=>p.x=950);
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
});


describe('Independent contact audit over nine original references',()=>{
  const ordinals=[0,6,12,18,24,31,37,43,49];
  function audited(offset=0) {
    const data=experiment(1080,1920,offset),e=data.archive.suspension!,seed=e.points[0];
    e.refinement='SPARK_ORIGINAL_50_CONTACT_AUDIT_V2';
    e.points=Array.from({length:50},(_,frame)=>({...seed,frame}));
    e.consensus={status:'CONSENSUS_ESTIMATE',camera:'STATIC',inlierOrdinals:e.points.map(p=>p.frame),
      submitted:50,usable:50,timeCoverage:1,x:seed.x,y:seed.y,spreadPixels:0};
    e.audit={camera:'STATIC',points:ordinals.map(frame=>({...seed,frame,
      decision:'CONFIRMED',referenceKind:'FIXED_CORD_CONTACT',visualReason:'The loaded cord touches the fixed support here.',
      timeSeconds:data.archive.receipt.framePtsSeconds[Math.round(frame*600/49)]})),
      consensus:{status:'CONSENSUS_ESTIMATE',camera:'STATIC',inlierOrdinals:[...ordinals],
        submitted:9,usable:9,timeCoverage:1,x:seed.x,y:seed.y,spreadPixels:0}};
    return {...data,e,audit:e.audit};
  }
  function independentAudited() {
    const data=audited();data.e.refinement='SPARK_ORIGINAL_50_CONTACT_AUDIT_V3';
    data.audit.protocol='SPARK_ORIGINAL_50_CONTACT_AUDIT_V3';return data;
  }
  it('accepts V3 independent corrected locations without changing the fifty original observations',()=>{
    const {archive,track,e,audit,cx,cy}=independentAudited();
    e.points.forEach(p=>p.y-=14);audit.points.forEach(p=>p.decision='CORRECTED');
    const before=JSON.stringify(archive),result=resolveSamSuspension(archive,track);
    expect(result.method).toBe('SPARK');expect(result.pivot!.pivotYpx/270*1920).toBeCloseTo(cy,8);
    expect(result.pivot!.pivotXpx/480*1080).toBeCloseTo(cx,8);expect(JSON.stringify(archive)).toBe(before);
  });
  it('recomputes V3 decision instead of trusting a persisted correction or confirmation',()=>{
    const {archive,track,e,audit}=independentAudited();
    expect(resolveSamSuspension(archive,track).pivot).not.toBeNull();
    audit.points[0].decision='CORRECTED';expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    audit.points[0].decision='CONFIRMED';e.points.forEach(p=>p.y-=14);
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  it('requires matching V3 protocol and never upgrades historical V2 audit implicitly',()=>{
    for(const protocol of [undefined,null,'SPARK_ORIGINAL_50_CONTACT_AUDIT_V2','UNKNOWN']) {
      const {archive,track,audit}=independentAudited();audit.protocol=protocol;
      expect(resolveSamSuspension(archive,track).pivot).withContext(String(protocol)).toBeNull();
    }
    const {archive,track,e,audit}=independentAudited();e.refinement='SPARK_ORIGINAL_50_CONTACT_AUDIT_V2';
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    audit.protocol=undefined;expect(resolveSamSuspension(archive,track).pivot).not.toBeNull();
    e.points.forEach(p=>p.y-=14);expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  it('V3 still refuses moving contacts and unstable coordinates',()=>{
    const {archive,track,audit}=independentAudited();
    audit.points[3].referenceKind='MOVING_CONTACT';expect(resolveSamSuspension(archive,track).reason).toBe('MOVING_CORD_CONTACT');
    audit.points[3].referenceKind='FIXED_CORD_CONTACT';audit.points.forEach((p,i)=>{p.x=400+i*20;p.decision='CORRECTED';});
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  function selectInliers(data:ReturnType<typeof audited>,selected:number[]) {
    const {audit,archive}=data;
    const chosen=audit.points.filter(p=>selected.includes(p.frame));
    Object.assign(audit.consensus,{inlierOrdinals:selected,usable:audit.points.filter(p=>p.visible&&p.confidence>=.8).length,
      timeCoverage:(chosen[chosen.length-1].timeSeconds-chosen[0].timeSeconds)/archive.receipt.framePtsSeconds.at(-1)!});
  }
  function uncertain(p:ReturnType<typeof audited>['audit']['points'][number]) {
    Object.assign(p,{decision:'UNCERTAIN',referenceKind:'UNCLEAR',visible:false,x:0,y:0,confidence:0,
      visualReason:'The contact is obscured in this image.'});
  }
  it('confirms nine independent images while preserving the original 50 and their consensus',()=>{
    const {archive,track,e,audit,cx,cy}=audited();
    e.points.forEach(p=>p.confidence=.3); // A new independent review can confirm an initially uncertain candidate.
    e.consensus!.status='INSUFFICIENT_OBSERVATIONS';e.consensus!.inlierOrdinals=[];
    const raw=JSON.stringify({archive,track}),result=resolveSamSuspension(archive,track);
    expect(result.method).toBe('SPARK');expect(result.samples).toBe(601);
    expect(result.pivot!.pivotXpx/480*1080).toBeCloseTo(cx,8);
    expect(result.pivot!.pivotYpx/270*1920).toBeCloseTo(cy,8);
    expect(audit.consensus.inlierOrdinals).toContain(24);
    expect(JSON.stringify({archive,track})).toBe(raw);
  });
  it('accepts consistent independent corrections instead of inheriting the old 50-point center',()=>{
    const {archive,track,e,audit,cx,cy}=audited();
    e.points.forEach(p=>{p.x=800;p.y=300;});Object.assign(e.consensus!,{x:800,y:300});
    audit.points.forEach(p=>p.decision='CORRECTED');
    const result=resolveSamSuspension(archive,track);
    expect(result.method).toBe('SPARK');
    expect(result.pivot!.pivotXpx/480*1080).toBeCloseTo(cx,8);
    expect(result.pivot!.pivotYpx/270*1920).toBeCloseTo(cy,8);
    audit.points[0].decision='CONFIRMED';
    expect(resolveSamSuspension(archive,track).reason).toBe('CORD_CONTACT_UNRESOLVED');
  });
  it('allows mixed confirmations and corrections but never changes their center with a circle fit',()=>{
    const {archive,track,audit,cx,cy}=audited(3);
    audit.points.forEach((p,i)=>p.decision=i%2?'CONFIRMED':'CORRECTED');
    const result=resolveSamSuspension(archive,track);
    expect(result.method).toBe('SPARK');
    expect(result.pivot!.pivotXpx/480*1080).toBeCloseTo(cx+3,8);
    expect(result.pivot!.pivotYpx/270*1920).toBeCloseTo(cy+3,8);
  });
  it('uses full oriented-image coordinates for an off-center corrected contact',()=>{
    const {archive,track,e,audit}=audited();
    const dx=140,dy=20;
    track.masks.forEach(m=>{m.centroidXpx+=dx;m.centroidYpx+=dy;});
    audit.points.forEach(p=>{p.decision='CORRECTED';p.x+=dx/e.width*1000;p.y+=dy/e.height*1000;});
    Object.assign(audit.consensus,{x:audit.points[0].x,y:audit.points[0].y});
    const result=resolveSamSuspension(archive,track);
    expect(result.method).toBe('SPARK');
    expect(result.pivot!.pivotXpx/480*e.width).toBeCloseTo(680,8);
    expect(result.pivot!.pivotYpx/270*e.height).toBeCloseTo(308,8);
  });
  for(const decision of ['CONFIRMED','CORRECTED','UNCERTAIN'] as const) it('blocks any moving contact even with eight agreeing points: '+decision,()=>{
    const {archive,track,audit}=audited();
    if(decision==='UNCERTAIN')uncertain(audit.points[4]);
    audit.points[4].decision=decision;audit.points[4].referenceKind='MOVING_CONTACT';
    expect(resolveSamSuspension(archive,track).reason).toBe('MOVING_CORD_CONTACT');
  });
  it('requires the independent audit with no automatic fallback to 50 points or three anchors',()=>{
    const {archive,track,e}=audited();
    for(const audit of [undefined,null]) {
      e.audit=audit;
      expect(resolveSamSuspension(archive,track).reason).toBe('CORD_CONTACT_UNRESOLVED');
    }
    e.points=e.points.filter(p=>[0,24,49].includes(p.frame));
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  for(const camera of ['MOVING','UNCLEAR'] as const) it('requires a static audit and static consensus: '+camera,()=>{
    const {archive,track,audit}=audited();
    audit.camera=camera;expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    audit.camera='STATIC';audit.consensus.camera=camera;
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  it('accepts six reliable images spanning the source and excludes uncertain rows with zero coordinates',()=>{
    const data=audited(),selected=[0,6,18,31,43,49];
    data.audit.points.filter(p=>!selected.includes(p.frame)).forEach(uncertain);
    selectInliers(data,selected);
    const result=resolveSamSuspension(data.archive,data.track);
    expect(result.pivot).not.toBeNull();expect(result.rejectedAnchorFrames).toEqual([12,24,37]);
    const p=data.audit.points.find(p=>p.frame===12)!;p.x=1;
    expect(resolveSamSuspension(data.archive,data.track).pivot).toBeNull();
  });
  it('rejects five of nine reliable images even when they agree across the full timeline',()=>{
    const data=audited(),selected=[0,12,24,37,49];
    data.audit.points.filter(p=>!selected.includes(p.frame)).forEach(uncertain);selectInliers(data,selected);
    expect(resolveSamSuspension(data.archive,data.track).reason).toBe('CORD_CONTACT_UNRESOLVED');
  });
  it('rejects a strong rival cluster and does not trust a declared majority',()=>{
    const data=audited(),selected=[0,6,18,31,43,49];
    data.audit.points.filter(p=>!selected.includes(p.frame)).forEach(p=>{p.decision='CORRECTED';p.x=700;});
    selectInliers(data,selected);
    expect(resolveSamSuspension(data.archive,data.track).pivot).toBeNull();
    // Three separate outliers do not form a competing contact.
    data.audit.points.filter(p=>!selected.includes(p.frame)).forEach((p,i)=>p.x=650+50*i);
    expect(resolveSamSuspension(data.archive,data.track).pivot).not.toBeNull();
  });
  for(const invalid of ['duplicate','wrong middle','missing','extra'] as const) it('requires exactly the nine original reference ordinals: '+invalid,()=>{
    const {archive,track,audit}=audited();
    if(invalid==='duplicate')audit.points[4].frame=18;
    if(invalid==='wrong middle')audit.points[4].frame=25;
    if(invalid==='missing')audit.points.pop();
    if(invalid==='extra')audit.points.push({...audit.points[0],frame:1});
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  for(const field of ['x','y','confidence','timeSeconds'] as const) for(const value of [NaN,Infinity])
    it('rejects nonfinite '+field+' '+value,()=>{
      const {archive,track,audit}=audited();audit.points[3][field]=value;
      expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    });
  it('rejects invalid visibility, reasons, confidence and normalized coordinates',()=>{
    for(const change of [{x:-1},{x:1000},{y:1000},{confidence:1.1},{confidence:-.1},
      {visible:false},{visualReason:' '},{referenceKind:'UNCLEAR'}]) {
      const {archive,track,audit}=audited();Object.assign(audit.points[2],change);
      expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    }
  });
  it('requires source order and refuses malformed rows without throwing',()=>{
    const {archive,track,audit}=audited();
    audit.points.reverse();expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    audit.points.reverse();audit.points[3]=null as unknown as typeof audit.points[number];
    expect(()=>resolveSamSuspension(archive,track)).not.toThrow();
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  it('uses the confidence threshold inclusively and retains low-confidence located observations',()=>{
    const data=audited(),selected=[0,6,18,31,43,49];
    data.audit.points.forEach(p=>p.confidence=selected.includes(p.frame)?.8:.79);selectInliers(data,selected);
    expect(resolveSamSuspension(data.archive,data.track).pivot).not.toBeNull();
    data.audit.points[0].confidence=.799;data.audit.consensus.usable=5;
    expect(resolveSamSuspension(data.archive,data.track).pivot).toBeNull();
  });
  it('binds audit times to original ordered PTS and full source endpoints',()=>{
    for(const change of ['off PTS','duplicate','reversed','start','end','no source','bad source'] as const) {
      const {archive,track,audit}=audited();
      if(change==='off PTS')audit.points[3].timeSeconds+=.001;
      if(change==='duplicate')audit.points[3].timeSeconds=audit.points[2].timeSeconds;
      if(change==='reversed')audit.points[3].timeSeconds=audit.points[2].timeSeconds-.1;
      if(change==='start')audit.points[0].timeSeconds=1/60;
      if(change==='end')audit.points[8].timeSeconds=599/60;
      if(change==='no source')archive.receipt.framePtsSeconds=[];
      if(change==='bad source')archive.receipt.framePtsSeconds[2]=NaN;
      expect(resolveSamSuspension(archive,track).pivot).withContext(change).toBeNull();
    }
  });
  it('computes temporal coverage over the source instead of trusting the declared value',()=>{
    const data=audited(),selected=[6,12,18,24,31,37];
    data.audit.points.filter(p=>!selected.includes(p.frame)).forEach(uncertain);selectInliers(data,selected);
    data.audit.consensus.timeCoverage=1;
    expect(resolveSamSuspension(data.archive,data.track).pivot).toBeNull();
  });
  it('rejects actual insufficient coverage even when all six useful points agree',()=>{
    const data=audited(),selected=[0,6,12,18,24,31],times=[0,.5,1,1.5,2,2.5,7,8,10];
    data.audit.points.forEach((p,i)=>p.timeSeconds=times[i]);
    data.audit.points.filter(p=>!selected.includes(p.frame)).forEach(uncertain);selectInliers(data,selected);
    expect(data.audit.consensus.timeCoverage).toBe(.25);
    expect(resolveSamSuspension(data.archive,data.track).pivot).toBeNull();
  });
  it('requires three temporal bins even if a narrow pair of intervals spans the video',()=>{
    const data=audited(),selected=[0,6,12,37,43,49],times=[0,.1,.2,.3,.4,9.7,9.8,9.9,10];
    data.audit.points.forEach((p,i)=>p.timeSeconds=times[i]);
    data.audit.points.filter(p=>!selected.includes(p.frame)).forEach(uncertain);selectInliers(data,selected);
    expect(resolveSamSuspension(data.archive,data.track).pivot).toBeNull();
  });
  it('recomputes and verifies every consensus field using original rather than audit-row ordinals',()=>{
    for(const change of [{status:'INCONCLUSIVE'},{submitted:50},{usable:50},{timeCoverage:.9},
      {x:501},{y:151},{spreadPixels:1},{inlierOrdinals:[0,1,2,3,4,5,6,7,8]},
      {inlierOrdinals:[0,6,12,18,24,31,37,43,43]}]) {
      const {archive,track,audit}=audited();Object.assign(audit.consensus,change);
      expect(resolveSamSuspension(archive,track).pivot).toBeNull();
    }
  });
  it('still rejects bad SAM geometry after a valid contact audit',()=>{
    const {archive,track}=audited();
    track.masks.forEach((m,i)=>{m.centroidXpx=100+i;m.centroidYpx=400+i;});
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
  });
  it('preserves an explicitly bound manual review without allowing a circle fit or an invalid binding',()=>{
    const {archive,track,e,cx,cy}=audited(3);
    e.audit=null;archive.receipt.analysisId='audit-batch';archive.receipt.responseJsonSha256='e'.repeat(64);
    archive.suspensionReview={protocol:'SPARK_SAM_SUSPENSION_REVIEW_V1',key:'f'.repeat(64),
      analysisId:'audit-batch',sourceSha256:'a'.repeat(64),samResponseSha256:'e'.repeat(64),sceneKey:'b'.repeat(64),
      responseSha256:'f'.repeat(64),model:'muse-spark-1.3',answer:{decision:'CONFIRMED',camera:'STATIC',
        justification:{es:'Revisión manual solicitada.',en:'Explicitly requested manual review.'},
        points:[0,25,49].map(frame=>({...e.points[frame],frame}))}};
    const result=resolveSamSuspension(archive,track);
    expect(result.method).toBe('SPARK');
    expect(result.pivot!.pivotXpx/480*1080).toBeCloseTo(cx+3,8);
    expect(result.pivot!.pivotYpx/270*1920).toBeCloseTo(cy+3,8);
    archive.suspensionReview.analysisId='different-batch';
    expect(resolveSamSuspension(archive,track).reason).toBe('CORD_CONTACT_UNRESOLVED');
  });
});
