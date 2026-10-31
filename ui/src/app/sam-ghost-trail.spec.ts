import {historicalSamSilhouettes} from './sam-ghost-trail';
import type {SamPlaybackMask} from './sam-playback';
const mask=(timeSeconds:number):SamPlaybackMask=>({frameIndex:Math.round(timeSeconds*60),timeSeconds,
  left:10,top:20,right:30,bottom:50,centroidXpx:20,centroidYpx:35,foregroundPixels:150,
  maskPath:'M0 0L20 0L10 30Z'});
describe('Observed ghost silhouettes',()=>{
  it('uses at most ten actual historical masks, never the current/future frame or stale history',()=>{
    const masks=Array.from({length:2400},(_,i)=>mask(i/240));
    const selected=historicalSamSilhouettes(masks,5);
    expect(selected.length).toBeGreaterThan(0);expect(selected.length).toBeLessThanOrEqual(10);
    expect(selected.every(m=>m.timeSeconds>=3.5 && m.timeSeconds<=4.92 && masks.includes(m))).toBeTrue();
    expect(selected.map(m=>m.timeSeconds)).toEqual([...selected.map(m=>m.timeSeconds)].sort((a,b)=>a-b));
  });
  it('does not fill gaps with synthetic shapes and gives the same trail after seeking backwards',()=>{
    const masks=[mask(1),mask(1.2),mask(2),{...mask(2.2),maskPath:''},mask(3)];
    const initial=historicalSamSilhouettes(masks,2.5);
    expect(initial.map(m=>m.timeSeconds)).toEqual([1,1.2,2]);
    historicalSamSilhouettes(masks,3.5);
    expect(historicalSamSilhouettes(masks,2.5)).toEqual(initial);
    expect(historicalSamSilhouettes(masks,.5)).toEqual([]);
    expect(historicalSamSilhouettes(masks,NaN)).toEqual([]);
  });
});
