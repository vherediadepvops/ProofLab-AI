import {
  verifiedSamPlaybackTrack, verifiedSamSceneTracks, nearestSamMask, samAngularFrameAt,
  type AuthenticatedSamVideoArchive, type SamPlaybackTrack
} from './sam-playback';
import type { SamVideoEvidence } from './sam-video-evidence';

const sha='a'.repeat(64);
function archive():AuthenticatedSamVideoArchive {
  return {
    state:{status:'CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW',
      sourceSha256:sha,provenance:'META_SAM_3_1_CACHED_PROVIDER_OUTPUT'},
    receipt:{
      model:'sam-3.1',concept:'metal pendulum bob',
      originalVideoSha256:sha,sourceFrameCount:50,
      providerEvidenceStatus:'COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW',
      framePtsSeconds:Array.from({length:50},(_,i)=>i/60)
    },
    rawResponseJson:'PROVIDER_JSON_PLACEHOLDER_NOT_USED_IN_PURE_TEST'
  };
}
function result():SamVideoEvidence {
  return {
    model:'sam-3.1',source:'USER_SUPPLIED_RESPONSE_UNVERIFIED',
    result:'DECODED_MASKS',frameWidth:1920,frameHeight:1080,
    objectIds:['bob'],detectionCount:8,warningCount:0,
    frames:Array.from({length:8},(_,i)=>({
      frameIndex:i*4,timeSeconds:null,
      detections:[{
        frameIndex:i*4,objectId:'bob',left:900,top:500,
        right:1020,bottom:620,
        centroidXpx:960+i*3,centroidYpx:600,
        foregroundPixels:5000,maskPath:'M 0 0 L 2 0 L 2 2 Z',
        boxConfidence:0.7,maskConfidence:0.8
      }]
    }))
  };
}
describe('scientific SAM playback archive adapter',()=>{
  it('accepts generic pendulum-object masks without material, color or shape attributes',()=>{
    const a=archive();a.receipt.concept='pendulum bob';
    a.receipt.requestProtocol='PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V3_GENERIC_BOB';
    const track=verifiedSamPlaybackTrack(a,result(),sha);
    expect(track.masks.length).toBe(8);
    expect(track.points[1].timeSeconds).toBeCloseTo(4/60);
  });
  it('preserves historical target provenance and rejects mixed or arbitrary prompt contracts',()=>{
    const a=archive();
    a.receipt.requestProtocol='PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2';
    expect(verifiedSamPlaybackTrack(a,result(),sha).masks.length).toBe(8);
    a.receipt.concept='pendulum bob';
    expect(()=>verifiedSamPlaybackTrack(a,result(),sha)).toThrowError(/INVALID_PROVENANCE/);
    a.receipt.requestProtocol='PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V3_GENERIC_BOB';
    a.receipt.concept='moving shadow';
    expect(()=>verifiedSamPlaybackTrack(a,result(),sha)).toThrowError(/INVALID_PROVENANCE/);
  });
  it('does not invent motion for a generic zero-match or ambiguous response',()=>{
    const a=archive();a.receipt.concept='pendulum bob';
    a.receipt.requestProtocol='PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V3_GENERIC_BOB';
    const zero=result();zero.result='NO_MATCH';zero.frames=[];zero.objectIds=[];
    expect(()=>verifiedSamPlaybackTrack(a,zero,sha)).toThrowError(/INVALID_PROVENANCE/);
    const multiple=result();multiple.objectIds.push('another');
    expect(()=>verifiedSamPlaybackTrack(a,multiple,sha)).toThrowError(/INVALID_PROVENANCE/);
  });
  it('takes positions from provider masks, matching real frame PTS not proxy FPS',()=>{
    const t=verifiedSamPlaybackTrack(archive(),result(),sha);
    expect(t.source).toBe('META_SAM_3_1_CACHED_PROVIDER_EVIDENCE');
    expect(t.points[0].x).toBeCloseTo(240,6);
    expect(t.points[0].y).toBeCloseTo(150,6);
    expect(t.points[1].timeSeconds).toBeCloseTo(4/60,6);
    expect(t.points[0].source).toBe('SAM_3_1_CACHED_PROVIDER_CENTROID_NOT_CALIBRATED');
    expect(t.points[1].timeSeconds).not.toBeCloseTo(1/12,3);
    expect(t.masks.length).toBe(8);
  });
  it('rejects a different source, ambiguous SAM identities or non-monotonic frame PTS',()=>{
    expect(()=>verifiedSamPlaybackTrack(archive(),result(),'b'.repeat(64)))
      .toThrowError(/INVALID_PROVENANCE/);
    const ambiguous=result();
    ambiguous.objectIds.push('another');
    expect(()=>verifiedSamPlaybackTrack(archive(),ambiguous,sha))
      .toThrowError(/INVALID_PROVENANCE/);
    const wrong=archive();
    wrong.receipt.framePtsSeconds[7]=wrong.receipt.framePtsSeconds[6];
    expect(()=>verifiedSamPlaybackTrack(wrong,result(),sha)).toThrowError('SAM_PTS_INVALID');
  });
  it('refuses out-of-source frame index and avoids guessing missing positions',()=>{
    const data=result();
    data.frames[3].frameIndex=60;
    expect(()=>verifiedSamPlaybackTrack(archive(),data,sha))
      .toThrowError('SAM_FRAME_NOT_IN_UPLOADED_SOURCE');
  });
  it('chooses only a nearby original frame and never invents mask between gaps',()=>{
    const track=verifiedSamPlaybackTrack(archive(),result(),sha);
    expect(nearestSamMask(track,0)?.frameIndex).toBe(0);
    expect(nearestSamMask(track,0.2)?.frameIndex).toBe(12);
    expect(nearestSamMask(track,10)).toBeNull();
    expect(samAngularFrameAt(track,{pivotXpx:240,pivotYpx:20},10)).toBeNull();
  });
  it('calculates candidate angular velocity from SAM-centroid positions, independently of ideal model',()=>{
    const points=Array.from({length:7},(_,i)=>{
      const t=i*.04;
      const theta=.5*t;
      return {
        timeSeconds:t,x:240+100*Math.sin(theta),y:20+100*Math.cos(theta),
        approximateRadiusPixels:8,
        source:'SAM_3_1_CACHED_PROVIDER_CENTROID_NOT_CALIBRATED' as const
      };
    });
    const track:SamPlaybackTrack={
      frameWidth:1920,frameHeight:1080,
      source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',
      objectId:'0',points,masks:points.map((p,frameIndex)=>({frameIndex,timeSeconds:p.timeSeconds,centroidXpx:p.x,centroidYpx:p.y,left:0,top:0,right:1,bottom:1,maskPath:'',foregroundPixels:1}))
    };
    const frame=samAngularFrameAt(track,{pivotXpx:240,pivotYpx:20},.12);
    expect(frame?.isValid).toBeTrue();
    expect(frame?.angularVelocityRadiansPerSecond).toBeCloseTo(.5,2);
    expect(frame?.angularAccelerationRadiansPerSecondSquared).toBeCloseTo(0,2);
    expect(frame?.apparentTangentialSpeedPixelsPerSecond).toBeCloseTo(200,1); // 100 grid pixels = 400 original pixels
    expect(frame?.tangentialSpeedMetersPerSecond).toBeNull();
    for(let i=3;i<track.points.length;i++)
      track.points[i].timeSeconds=.4+(i-3)*.04; // genuine temporal gap
    expect(samAngularFrameAt(track,{pivotXpx:240,pivotYpx:20},.4)?.isValid).toBeFalse();
  });
  for(const [width,height] of [[1080,1920],[1920,1080],[1080,1080],[2160,3840],[3840,2160]]) {
    for(const degrees of [-30,0,30]) {
      it('preserves known angle and derivatives at '+width+'x'+height+' / '+degrees+' degrees',()=>{
        const radius=Math.min(width,height)/4,at=.12;
        const pivot={pivotXpx:240,pivotYpx:40.5};
        const points=[.04,.08,.12,.16,.20].map(t=>{
          const dt=t-at,theta=degrees*Math.PI/180+.4*dt+.4*dt*dt;
          return {timeSeconds:t,
            x:(width/2+radius*Math.sin(theta))/width*480,
            y:(height*.15+radius*Math.cos(theta))/height*270,
            approximateRadiusPixels:8};
        });
        const track:SamPlaybackTrack={frameWidth:width,frameHeight:height,
          source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId:'bob',points,masks:points.map((p,frameIndex)=>({frameIndex,timeSeconds:p.timeSeconds,centroidXpx:p.x,centroidYpx:p.y,left:0,top:0,right:1,bottom:1,maskPath:'',foregroundPixels:1}))};
        const frame=samAngularFrameAt(track,pivot,at)!;
        expect(frame.isValid).toBeTrue();
        expect(frame.angleDegrees).toBeCloseTo(degrees,8);
        expect(frame.angularVelocityRadiansPerSecond).toBeCloseTo(.4,8);
        expect(frame.angularAccelerationRadiansPerSecondSquared).toBeCloseTo(.8,8);
        expect(frame.apparentTangentialSpeedPixelsPerSecond).toBeCloseTo(.4*radius,8);
        expect(frame.bobXpx).toBe(points[2].x); // saved/drawing coordinates stay compatible
        expect(frame.tangentialSpeedMetersPerSecond).toBeNull();
      });
    }
  }

  it('does not report an angular measurement without valid original dimensions',()=>{
    const track=verifiedSamPlaybackTrack(archive(),result(),sha);
    expect(samAngularFrameAt(track,{pivotXpx:240,pivotYpx:20},.12,
      {width:0,height:1920})).toBeNull();
  });
});
describe('Angular velocity uses source timestamps',()=>{
  for(const velocity of [-.6,0,.6])
    it('preserves '+velocity+' rad/s across unequal frame intervals and angle wrap',()=>{
      const image={width:1080,height:1920},pivot={pivotXpx:240,pivotYpx:135};
      const points=[.10,.13,.18].map(timeSeconds=>{
        const dt=timeSeconds-.13,theta=Math.PI-.01+velocity*dt+.4*dt*dt;
        return {timeSeconds,x:240+300*Math.sin(theta)/1080*480,
          y:135+300*Math.cos(theta)/1920*270,approximateRadiusPixels:8};
      });
      const track:SamPlaybackTrack={frameWidth:1920,frameHeight:1080,
        source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId:'bob',points,masks:points.map((p,frameIndex)=>({frameIndex,timeSeconds:p.timeSeconds,centroidXpx:p.x,centroidYpx:p.y,left:0,top:0,right:1,bottom:1,maskPath:'',foregroundPixels:1}))};
      const value=samAngularFrameAt(track,pivot,.13,image);
      expect(value?.isValid).toBeTrue();
      expect(value?.angularVelocityRadiansPerSecond).toBeCloseTo(velocity,9);
      expect(value?.angularAccelerationRadiansPerSecondSquared).toBeCloseTo(.8,9);
      expect(samAngularFrameAt(track,pivot,.10,image)?.angularVelocityRadiansPerSecond).toBeNull();
      expect(samAngularFrameAt(track,pivot,.18,image)?.angularVelocityRadiansPerSecond).toBeNull();
    });
});
describe('multi-object assembly identity',()=>{
  function assembly(){
    const a=archive(),p=result();
    a.receipt.requestProtocol='PROOFLAB_SAM_MULTIOBJECT_SOURCE_PTS_V1';
    a.receipt.concept='<0f>1<|box;x1=1;y1=1;x2=5;y2=5;w=1920;h=1080|>';
    a.receipt.scene={key:'c'.repeat(64),videoSha256:sha,width:1920,height:1080,
      arrangement:'simple_pendulum',pivotX:500,pivotY:100,
      roles:[{id:'1',role:'object'},{id:'2',role:'cord'},{id:'3',role:'knot'}]};
    p.objectIds=['1','2','3'];
    for(const f of p.frames){
      const d=f.detections[0];d.objectId='1';
      f.detections.push({...d,objectId:'2',centroidXpx:500,centroidYpx:300});
      f.detections.push({...d,objectId:'3',centroidXpx:480,centroidYpx:100});
    }
    return {a,p};
  }
  it('accepts a Spark-grounded object without inventing a human-reviewed pivot',()=>{
    const {a,p}=assembly();
    a.receipt.scene={...a.receipt.scene!,origin:'SPARK_GROUNDING',
      roles:[{id:'1',role:'object'}],pivotX:null,pivotY:null};
    p.objectIds=['1'];
    for(const f of p.frames)f.detections=f.detections.filter(d=>d.objectId==='1');
    expect(verifiedSamSceneTracks(a,p,sha)['object'].masks.length).toBe(8);
    a.receipt.scene.pivotX=500;a.receipt.scene.pivotY=100;
    expect(()=>verifiedSamSceneTracks(a,p,sha)).toThrow();
  });
  it('keeps the moving object centroid separate from cord and knot',()=>{
    const {a,p}=assembly(),tracks=verifiedSamSceneTracks(a,p,sha);
    expect(Object.keys(tracks)).toEqual(['object','cord','knot']);
    expect(tracks['object'].masks[0].centroidXpx).toBe(960);
    expect(tracks['cord'].masks[0].centroidXpx).toBe(500);
    expect(tracks['knot'].masks[0].centroidYpx).toBe(100);
    expect(tracks['object'].points[1].timeSeconds).toBeCloseTo(4/60);
  });
  it('does not substitute a cord when the object disappears',()=>{
    const {a,p}=assembly();
    p.frames[1].detections=p.frames[1].detections.filter(d=>d.objectId!=='1');
    const tracks=verifiedSamSceneTracks(a,p,sha);
    expect(nearestSamMask(tracks['object'],4/60,.5/60)).toBeNull();
    expect(nearestSamMask(tracks['cord'],4/60,.5/60)?.centroidXpx).toBe(500);
  });
  it('shows optional missing roles without inventing masks or centers',()=>{
    const {a,p}=assembly();p.objectIds=['2'];
    for(const f of p.frames)f.detections=f.detections.filter(d=>d.objectId==='2');
    const tracks=verifiedSamSceneTracks(a,p,sha);
    expect(tracks['object']).toBeUndefined();expect(tracks['knot']).toBeUndefined();
    expect(tracks['cord'].masks.length).toBe(8);
  });
  it('rejects unknown, duplicate or rebound IDs and different native geometry',()=>{
    const {a,p}=assembly();
    p.objectIds.push('9');expect(()=>verifiedSamSceneTracks(a,p,sha)).toThrow();
    p.objectIds.pop();p.frames[0].detections.push({...p.frames[0].detections[0]});
    expect(()=>verifiedSamSceneTracks(a,p,sha)).toThrowError('SAM_DUPLICATE_ROLE_FRAME');
    p.frames[0].detections.pop();a.receipt.scene!.roles[0].id='3';
    expect(()=>verifiedSamSceneTracks(a,p,sha)).toThrow();
    a.receipt.scene!.roles[0].id='1';a.receipt.scene!.width=1080;
    expect(()=>verifiedSamSceneTracks(a,p,sha)).toThrow();
  });
});
describe('verified segmented playback',()=>{
 function segmented(){
  const a=archive(),p=result(),batch='b'.repeat(32);
  a.state={...a.state,status:'VERIFIED_META_SAM_SEGMENTS_REQUIRES_VISUAL_REVIEW',analysisId:batch};
  a.receipt={...a.receipt,analysisId:batch,concept:'pendulum object',
    requestProtocol:'PROOFLAB_SAM_ADAPTIVE_SSE_SOURCE_PTS_V1',
    providerEvidenceStatus:'COMPLETED_PROVIDER_PARTS_DERIVED_REQUIRES_VISUAL_REVIEW',
    scene:{key:'c'.repeat(64),videoSha256:sha,width:1920,height:1080,
      arrangement:'simple_pendulum',origin:'SPARK_GROUNDING',pivotX:null,pivotY:null,
      roles:[{id:'1',role:'object'}]}};
  a.derivation={batchId:batch,nativeProviderResult:false,seamsVerified:true,
    sourcePtsSha256:'d'.repeat(64),partResponseSha256:['e'.repeat(64),'f'.repeat(64)]};
  p.objectIds=['1'];for(const f of p.frames)f.detections[0].objectId='1';
  return {a,p};
 }
 it('renders verified joined masks at original timestamps without pretending to be one provider response',()=>{
  const {a,p}=segmented(),t=verifiedSamPlaybackTrack(a,p,sha);
  expect(t.source).toBe('META_SAM_3_1_VERIFIED_SEGMENTS');
  expect(t.masks.length).toBe(8);expect(t.points[1].timeSeconds).toBeCloseTo(4/60);
  expect(verifiedSamSceneTracks(a,p,sha)['object'].masks.length).toBe(8);
 });
 it('rejects missing provenance, failed seams, a wrong batch or a native/derived mix',()=>{
  for(const change of [
    (a:AuthenticatedSamVideoArchive)=>{a.derivation=undefined;},
    (a:AuthenticatedSamVideoArchive)=>{a.derivation!.seamsVerified=false;},
    (a:AuthenticatedSamVideoArchive)=>{a.derivation!.batchId='0'.repeat(32);},
    (a:AuthenticatedSamVideoArchive)=>{Object.assign(a.derivation!,{nativeProviderResult:true});}
  ]){
   const {a,p}=segmented();change(a);
   expect(()=>verifiedSamPlaybackTrack(a,p,sha)).toThrowError(/INVALID_PROVENANCE/);
  }
 });
});
