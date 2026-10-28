import {
  verifiedSamPlaybackTrack, nearestSamMask, samAngularFrameAt,
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
      objectId:'0',points,masks:[]
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
          source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId:'bob',points,masks:[]};
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
        source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId:'bob',points,masks:[]};
      const value=samAngularFrameAt(track,pivot,.13,image);
      expect(value?.isValid).toBeTrue();
      expect(value?.angularVelocityRadiansPerSecond).toBeCloseTo(velocity,9);
      expect(value?.angularAccelerationRadiansPerSecondSquared).toBeCloseTo(.8,9);
      expect(samAngularFrameAt(track,pivot,.10,image)?.angularVelocityRadiansPerSecond).toBeNull();
      expect(samAngularFrameAt(track,pivot,.18,image)?.angularVelocityRadiansPerSecond).toBeNull();
    });
});
