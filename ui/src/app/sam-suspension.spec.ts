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
