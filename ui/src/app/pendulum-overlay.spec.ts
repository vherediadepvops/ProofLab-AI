import { buildNormalizedVideoOverlay } from './pendulum-overlay';

describe('Normalized physics-vs-video preview (visualization only)',()=>{
  const period=1.5;
  const theory=Array.from({length:501},(_,i)=>({
    timeSeconds:i*.04,
    horizontalCentimeters:13*Math.cos((i*.04)*2*Math.PI/period)
  }));
  const observed=Array.from({length:360},(_,i)=>{
    const t=i/12;
    return {
      timeSeconds:t,
      position:160+48*Math.cos(2*Math.PI*(t-2)/1.62+.5)+
        (i%49===0?5:0)
    };
  });

  it('normalizes observed pixels without fabricating physical centimetres',()=>{
    const overlay=buildNormalizedVideoOverlay(theory,period,observed);
    expect(overlay).not.toBeNull();
    expect(overlay!.source).toBe('VISUAL_NORMALIZATION_NOT_SCIENTIFIC_FIT');
    expect(overlay!.observationCount).toBeGreaterThan(150);
    expect(overlay!.windowSeconds).toBeGreaterThan(8);
    expect(overlay!.phaseOffsetSeconds).toBeGreaterThanOrEqual(0);
    expect(overlay!.phaseOffsetSeconds).toBeLessThanOrEqual(period);
    expect(overlay!.observedPoints).not.toBe(overlay!.theoryPoints);
    expect(overlay!.observedPoints.split(' ').length).toBe(overlay!.observationCount);
  });

  it('does not invent an overlay for stationary or insufficient video evidence',()=>{
    const flat=observed.map(p=>({...p,position:100}));
    expect(buildNormalizedVideoOverlay(theory,period,flat)).toBeNull();
    expect(buildNormalizedVideoOverlay(theory,period,observed.slice(0,20)))
      .toBeNull();
  });

  it('keeps the same theoretical period even when the observed period differs',()=>{
    const a=buildNormalizedVideoOverlay(theory,period,observed);
    const b=buildNormalizedVideoOverlay(theory,period,observed);
    expect(a).toEqual(b);
    expect(a?.source).toContain('NOT_SCIENTIFIC_FIT');
  });
});
