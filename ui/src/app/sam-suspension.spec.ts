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
  const archive={state:{},receipt:{originalVideoSha256:'a'.repeat(64),scene:{
    key:'b'.repeat(64),evidenceKey:'c'.repeat(64),responseSha256:'d'.repeat(64),
    origin:'SPARK_GROUNDING',arrangement:'simple_pendulum',width,height}},
    suspension:{sourceSha256:'a'.repeat(64),sceneKey:'b'.repeat(64),evidenceKey:'c'.repeat(64),
      responseSha256:'d'.repeat(64),model:'muse-spark-1.3',width,height,
      points:[0,1,2].map(frame=>({frame,visible:true,x:(cx+offset)/width*1000,
        y:(cy+offset)/height*1000,confidence:.9}))}} as unknown as AuthenticatedSamVideoArchive;
  return {archive,track,cx,cy,radius};
}
describe('Automatic Spark/SAM suspension estimates',()=>{
  for(const protocol of ['SPARK_VISUAL_OBJECT_AND_EXIT_CROP_V2','SPARK_VISUAL_BODY_AND_EXIT_CROPS_V3']) it('preserves visually localized exit '+protocol,()=>{
    const {archive,track,cx,cy}=experiment(1080,1920,3);
    archive.suspension!.refinement=protocol;
    const result=resolveSamSuspension(archive,track);
    expect(result.method).toBe('SPARK');
    expect(result.pivot!.pivotXpx/480*1080).toBeCloseTo(cx+3,8);
    expect(result.pivot!.pivotYpx/270*1920).toBeCloseTo(cy+3,8);
    archive.suspension!.points[0].x+=10;
    expect(resolveSamSuspension(archive,track).pivot).toBeNull();
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
