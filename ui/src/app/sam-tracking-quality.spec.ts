import {assessSamTracking} from './sam-tracking-quality';
import type {SamPlaybackTrack} from './sam-playback';

function recording(fps=60,duration=5) {
  const times=Array.from({length:Math.round(fps*duration)+1},(_,i)=>i/fps);
  const masks=times.map((t,frameIndex)=>({frameIndex,timeSeconds:t,
    left:280+80*Math.sin(t),top:480,right:320+80*Math.sin(t),bottom:520,
    centroidXpx:300+80*Math.sin(t),centroidYpx:500+15*Math.cos(t),
    foregroundPixels:1600,maskPath:''}));
  const track:SamPlaybackTrack={frameWidth:1000,frameHeight:1000,
    source:'META_SAM_3_1_VERIFIED_SEGMENTS',objectId:'1',masks,points:[]};
  return {track,times};
}
function change(record:ReturnType<typeof recording>,at:number) {
  for(const mask of record.track.masks)if(mask.timeSeconds>=at) {
    mask.foregroundPixels=400;mask.centroidXpx+=40;mask.centroidYpx+=15;
  }
}
describe('SAM mask continuity advisory',()=>{
  for(const fps of [10,30,60,120])it('locates a sustained size/position discontinuity at '+fps+' fps',()=>{
    const r=recording(fps);change(r,2);
    const result=assessSamTracking(r.track,r.times);
    expect(result.status).toBe('REVIEW_REQUIRED');
    expect(result.firstSuspiciousTimeSeconds).toBe(2);
    expect(result.changes.length).toBe(1);
    expect(result.changes[0].confirmation).toBe('PERSISTENT');
    expect(result.changes[0].areaRatio).toBe(.25);
    expect(result.missingFrames).toBe(0);
  });
  it('uses actual timestamps at variable frame intervals',()=>{
    const r=recording();
    r.times=r.times.map((_,i)=>i===0?0:Math.floor(i/2)*.04+(i%2)*.01);
    r.track.masks.forEach((m,i)=>{m.timeSeconds=r.times[i];m.centroidXpx=300+20*r.times[i];});
    change(r,2);
    expect(assessSamTracking(r.track,r.times).firstSuspiciousTimeSeconds).toBe(2);
  });
  it('does not mistake smooth scale/rotation change for a tracking jump',()=>{
    const r=recording();
    r.track.masks.forEach(m=>{m.foregroundPixels=1600*(1-.65*m.timeSeconds/5);});
    const result=assessSamTracking(r.track,r.times);
    expect(result.status).toBe('NO_DISCONTINUITY_DETECTED');
    expect(result.changes).toEqual([]);
  });
  it('does not classify size change alone as an object switch',()=>{
    const r=recording();r.track.masks.forEach(m=>{if(m.timeSeconds>=2)m.foregroundPixels=400;});
    expect(assessSamTracking(r.track,r.times).changes).toEqual([]);
  });
  it('does not persist a one-frame blur or a short partial occlusion',()=>{
    const r=recording();
    for(const i of [120,180,181,182]) {
      r.track.masks[i].foregroundPixels=300;r.track.masks[i].centroidXpx+=60;
    }
    expect(assessSamTracking(r.track,r.times).changes).toEqual([]);
  });
  it('reports missing masks separately without estimating through an occlusion',()=>{
    const r=recording();r.track.masks=r.track.masks.filter(m=>m.frameIndex<120||m.frameIndex>=140);
    r.track.masks.filter(m=>m.frameIndex>=140).forEach(m=>{m.foregroundPixels=400;m.centroidXpx+=40;});
    const result=assessSamTracking(r.track,r.times);
    expect(result.firstSuspiciousTimeSeconds).toBe(2);
    expect(result.changes).toEqual([]);
    expect(result.missingFrames).toBe(20);
    expect(result.missingRuns[0]).toEqual({startFrame:120,endFrame:139,startSeconds:2,endSeconds:140/60,frames:20});
  });
  it('does not calculate a jump across a native timestamp discontinuity',()=>{
    const r=recording();
    r.times=r.times.map((t,i)=>t+(i>=120?.5:0));
    r.track.masks.forEach((m,i)=>{m.timeSeconds=r.times[i];});change(r,2.5);
    expect(assessSamTracking(r.track,r.times).changes).toEqual([]);
  });
  it('labels sustained evidence near the end as limited rather than inventing follow-up',()=>{
    const r=recording();change(r,r.times[r.times.length-3]);
    const result=assessSamTracking(r.track,r.times);
    expect(result.changes.length).toBe(1);
    expect(result.changes[0].confirmation).toBe('ENDS_BEFORE_CONFIRMATION');
    expect(result.changes[0].confirmationMasks).toBe(3);
  });
  it('does not claim persistence from the final single frame',()=>{
    const r=recording();change(r,5);
    expect(assessSamTracking(r.track,r.times).changes).toEqual([]);
  });
  it('retains raw masks and does not equate no detected change with verified identity',()=>{
    const r=recording();change(r,2);const raw=JSON.stringify(r);
    const result=assessSamTracking(r.track,r.times);
    expect(JSON.stringify(r)).toBe(raw);
    expect(result.scope).toContain('NOT_OBJECT_IDENTITY_VERIFICATION');
  });
  it('rejects invalid ordering and cannot certify an empty track',()=>{
    const r=recording();r.track.masks[10].frameIndex=9;
    expect(assessSamTracking(r.track,r.times).reason).toBe('INVALID_INPUT');
    expect(assessSamTracking(null,r.times).status).toBe('INSUFFICIENT_EVIDENCE');
  });
  it('compares reviewed boxes with the exact native frame without accepting them as truth',()=>{
    const r=recording(),m=r.track.masks[60];
    const reference={role:'object',frame:60,x1:m.left,y1:m.top,x2:m.right,y2:m.bottom};
    const result=assessSamTracking(r.track,r.times,[reference,{...reference,role:'cord'}]);
    expect(result.referenceChecks.length).toBe(1);
    expect(result.referenceChecks[0]).toEqual({frameIndex:60,timeSeconds:1,status:'OBSERVED_MASK',
      maskBoxIou:1,maskBoxCoveredFraction:1,centroidInsideReference:true});
    expect(result.status).toBe('NO_DISCONTINUITY_DETECTED');
    r.track.masks=r.track.masks.filter(m=>m.frameIndex!==60);
    const missing=assessSamTracking(r.track,r.times,[reference]);
    expect(missing.referenceChecks[0].status).toBe('MISSING_MASK');
    expect(missing.referenceChecks[0].maskBoxIou).toBeNull();
  });
  it('retains discrepant reference comparisons without deleting or blocking masks',()=>{
    const r=recording(),raw=JSON.stringify(r);
    const result=assessSamTracking(r.track,r.times,[{role:'object',frame:60,x1:0,y1:0,x2:20,y2:20}]);
    expect(result.referenceChecks[0].maskBoxIou).toBe(0);
    expect(result.referenceChecks[0].centroidInsideReference).toBeFalse();
    expect(result.status).toBe('NO_DISCONTINUITY_DETECTED');
    expect(JSON.stringify(r)).toBe(raw);
  });
});
