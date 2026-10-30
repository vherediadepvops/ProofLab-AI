import {studentCritical95,repeatabilityInterval95,compareRepeatability95} from './period-confidence';
describe('95% period repeatability intervals',()=>{
  const sample={status:'CANDIDATE',periodSeconds:1.4157073372260922,
    statisticalStandardUncertaintySeconds:.00041850491917582144,usedCycles:10};
  it('matches reference two-sided Student critical values, including small sample sizes',()=>{
    for(const [df,q] of [[1,12.706204736432],[2,4.302652729696],[7,2.3646242510103],
      [9,2.262157162854],[30,2.042272456301],[100,1.983971518450]])
      expect(studentCritical95(df)).withContext('df='+df).toBeCloseTo(q,8);
  });
  it('uses cycles minus one and Student margin, not the practical tolerance or 1.96',()=>{
    const ci=repeatabilityInterval95(sample)!;
    expect(ci.degreesOfFreedom).toBe(9);
    expect(ci.marginSeconds).toBeCloseTo(.000946723887,10);
    expect(ci.lowerSeconds).toBeCloseTo(sample.periodSeconds-ci.marginSeconds,10);
    expect(ci.scope).toBe('REPEATABILITY_ONLY');expect(ci.confidenceLevel).toBe(.95);
  });
  it('does not invent margins for unavailable, invalid, inconclusive or unresolved-zero dispersion',()=>{
    for(const x of [null,{}, {...sample,status:'INCONCLUSIVE'}, {...sample,usedCycles:2},
      {...sample,statisticalStandardUncertaintySeconds:0},{...sample,statisticalStandardUncertaintySeconds:NaN},
      {...sample,periodSeconds:-1}])expect(repeatabilityInterval95(x)).toBeNull();
  });
  it('propagates both video repeatabilities but never returns a global p-value or acceptance',()=>{
    const a=repeatabilityInterval95(sample),b=repeatabilityInterval95({...sample,periodSeconds:1.7970434891972622,
      statisticalStandardUncertaintySeconds:.0005073771728373894,usedCycles:8});
    const p={baselineLengthMeters:.5,nextLengthMeters:.8,periodSeconds:1.7907438743386135,tolerancePercent:5};
    const c=compareRepeatability95(a,b,p)!;
    expect(c.prediction.standardUncertaintySeconds).toBeCloseTo(.0005293715026321094,12);
    expect(c.difference.estimateSeconds).toBeCloseTo(.0062996148586487,12);
    expect(c.difference.standardUncertaintySeconds).toBeCloseTo(Math.hypot(.0005293715026321094,.0005073771728373894),12);
    expect(c.difference.degreesOfFreedom).toBeGreaterThan(7);
    expect(c.difference.degreesOfFreedom).toBeLessThan(16.01);
    expect(c.globalPValue).toBeNull();expect(c.globalDecision).toBe('NOT_ASSESSED_MISSING_TOTAL_UNCERTAINTY');
    expect(c.tolerancePercent).toBe(5);expect(p.periodSeconds).toBe(1.7907438743386135);
    expect(compareRepeatability95(a,b,{...p,periodSeconds:1.5})).toBeNull();
  });
});
