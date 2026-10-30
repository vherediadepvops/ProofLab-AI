import {
  measureSamPeriod, periodReportEnvelope, periodReportPayload, SAM_PENDULUM_MOTION_POLICY,
  type SamPeriodInput, type SamPeriodSource
} from './sam-period';

function oscillation(fps=60,seconds=20,period=1.4,portrait=true):SamPeriodInput {
  const width=portrait?1080:1920,height=portrait?1920:1080,radius=height*.55;
  const times=Array.from({length:Math.floor(fps*seconds)},(_,i)=>i/fps);
  return {
    framePtsSeconds:times,sourceWidth:width,sourceHeight:height,imageWidth:width,imageHeight:height,
    pivot:{x:.5,y:.15},arrangement:'simple_pendulum',
    centroids:times.map((t,frameIndex)=>{
      const angle=.18*Math.sin(2*Math.PI*t/period-.7);
      return {frameIndex,x:width*.5+radius*Math.sin(angle),y:height*.15+radius*Math.cos(angle)};
    })
  };
}
function retimeMotion(input:SamPeriodInput,phase:(t:number)=>number):void {
  const radius=input.imageHeight*.55;
  input.centroids=input.framePtsSeconds.map((t,frameIndex)=>{
    const a=.18*Math.sin(phase(t));
    return {frameIndex,x:input.sourceWidth*(.5+radius*Math.sin(a)/input.imageWidth),
      y:input.sourceHeight*(.15+radius*Math.cos(a)/input.imageHeight)};
  });
}
const source:SamPeriodSource={
  studyId:'a'.repeat(32),role:'baseline',originalVideoSha256:'b'.repeat(64),analysisId:'c'.repeat(32),
  providerResponseSha256:'d'.repeat(64),providerModel:'sam-3.1',objectId:'0',
  pivotRevisionId:'human-pivot-1',pivotProvenance:'HUMAN_SELECTED'
};

describe('SAM period from native timestamps',()=>{
  it('excludes a bad frame explicitly, preserves raw points and uses unaffected cycles',()=>{
    const input=oscillation();input.centroids[400].x=15;input.centroids[400].y=10;
    const raw=JSON.stringify(input),r=measureSamPeriod(input);
    expect(r.geometryExcludedFrames).toEqual([400]);
    expect(r.issues).toContain('GEOMETRY_EXCLUSIONS');
    expect(r.usableFrames).toBe(input.centroids.length-1);
    expect(r.cycles.some(c=>c.startFrame<400&&c.endFrame>=400)).toBeFalse();
    expect(r.periodSeconds!).toBeCloseTo(1.4,3);expect(r.status).toBe('CANDIDATE');
    expect(r.rawRadialRmsPixels!).toBeGreaterThan(0);expect(JSON.stringify(input)).toBe(raw);
  });

  for(const fps of [60,120,240])it('recovers a known period at '+fps+' FPS',()=>{
    const r=measureSamPeriod(oscillation(fps));
    expect(r.status).toBe('CANDIDATE');
    expect(r.motion.status).toBe('RESOLVED_ARC');
    expect(r.periodSeconds!).toBeCloseTo(1.4,3);
    expect(r.usedCycles).toBeGreaterThanOrEqual(3);
    expect(r.statisticalStandardUncertaintySeconds).not.toBeNull();
  });
  it('uses irregular native PTS and does not assume nominal FPS',()=>{
    const input=oscillation();
    input.framePtsSeconds=input.framePtsSeconds.map((t,i)=>t+.002*Math.sin(i));
    retimeMotion(input,t=>2*Math.PI*t/1.73-.7);
    const r=measureSamPeriod(input);
    expect(r.status).toBe('CANDIDATE');expect(r.periodSeconds!).toBeCloseTo(1.73,3);
  });
  it('restores portrait geometry even when SAM raster dimensions differ',()=>{
    const original=oscillation(),scaled=structuredClone(original);
    scaled.sourceWidth=480;scaled.sourceHeight=270;
    scaled.centroids=original.centroids.map(c=>({frameIndex:c.frameIndex,
      x:c.x/original.sourceWidth*480,y:c.y/original.sourceHeight*270}));
    const a=measureSamPeriod(original),b=measureSamPeriod(scaled);
    expect(b.periodSeconds!).toBeCloseTo(a.periodSeconds!,10);
    expect(b.amplitudeDegrees!).toBeCloseTo(a.amplitudeDegrees!,10);
    expect(b.radiusMadFraction!).toBeLessThan(1e-10);
  });
  it('splits a missing interval and never bridges it with a cycle',()=>{
    const input=oscillation();
    input.centroids=input.centroids.filter(c=>c.frameIndex<400||c.frameIndex>=430);
    const r=measureSamPeriod(input);
    expect(r.status).toBe('CANDIDATE');expect(r.missingFrames).toBe(30);
    expect(r.longestMissingRunSeconds).toBeCloseTo(.5,6);
    expect(r.cycles.some(c=>c.startFrame<400&&c.endFrame>=430)).toBeFalse();
    expect(r.periodSeconds!).toBeCloseTo(1.4,3);
  });
  it('does not invent cycles from short fragments',()=>{
    const input=oscillation();
    input.centroids=input.centroids.filter(c=>c.frameIndex%60!==0);
    const r=measureSamPeriod(input);
    expect(r.status).toBe('INCONCLUSIVE');expect(r.usedCycles).toBe(0);
    expect(r.periodSeconds).toBeNull();expect(r.statisticalStandardUncertaintySeconds).toBeNull();
  });
  it('splits timestamp discontinuities even with consecutive frame indices',()=>{
    const input=oscillation();
    input.framePtsSeconds=input.framePtsSeconds.map((t,i)=>i>=600?t+2:t);
    const r=measureSamPeriod(input);
    expect(r.issues).toContain('TIME_GAPS');
    expect(r.cycles.some(c=>c.startFrame<600&&c.endFrame>=600)).toBeFalse();
    expect(r.periodSeconds!).toBeCloseTo(1.4,3);
  });
  it('reports insufficient cycles without fabricating uncertainty',()=>{
    const r=measureSamPeriod(oscillation(60,4));
    expect(r.status).toBe('INCONCLUSIVE');
    expect(r.issues).toContain('TOO_FEW_CYCLES');
    expect(r.statisticalStandardUncertaintySeconds).toBeNull();
  });
  it('rejects a stationary object and insufficient amplitude',()=>{
    const input=oscillation();input.centroids.forEach(c=>{c.x=540;c.y=1400;});
    const r=measureSamPeriod(input);
    expect(r.issues).toContain('LOW_AMPLITUDE');expect(r.periodSeconds).toBeNull();
  });
  it('reports missing detections without theoretical fallback',()=>{
    const input=oscillation();input.centroids=[];
    const r=measureSamPeriod(input);
    expect(r.status).toBe('INCONCLUSIVE');expect(r.issues).toContain('NO_DETECTIONS');
    expect(r.periodSeconds).toBeNull();expect(r.detectionCoverage).toBe(0);
  });
  it('marks low coverage inconclusive despite several clean cycles',()=>{
    const input=oscillation();input.centroids=input.centroids.filter(c=>c.frameIndex<700);
    const r=measureSamPeriod(input);
    expect(r.usedCycles).toBeGreaterThanOrEqual(3);
    expect(r.status).toBe('INCONCLUSIVE');expect(r.issues).toContain('LOW_COVERAGE');
  });
  it('rejects invalid timestamps, duplicate centroids and missing pivots',()=>{
    const a=oscillation();a.framePtsSeconds[2]=a.framePtsSeconds[1];
    expect(measureSamPeriod(a).issues).toContain('INVALID_TIMESTAMPS');
    const b=oscillation();b.centroids.push(b.centroids[0]);
    expect(measureSamPeriod(b).issues).toContain('INVALID_CENTROIDS');
    const c=oscillation();c.pivot=null;
    expect(measureSamPeriod(c).issues).toContain('MISSING_PIVOT');
  });
  it('does not apply the simple pendulum estimator to a pulley',()=>{
    const input=oscillation();input.arrangement='pulley_system';
    const r=measureSamPeriod(input);
    expect(r.issues).toContain('UNSUPPORTED_ASSEMBLY');expect(r.periodSeconds).toBeNull();
  });
  it('flags unstable periods and drift instead of removing inconvenient cycles',()=>{
    const input=oscillation();
    retimeMotion(input,t=>2*Math.PI*(t/1.7+.018*t*t)-.7);
    const r=measureSamPeriod(input);
    expect(r.status).toBe('INCONCLUSIVE');
    expect(r.issues).toContain('UNSTABLE_PERIOD');expect(r.issues).toContain('PERIOD_DRIFT');
    expect(r.cycles.length).toBeGreaterThan(10);
  });
  it('requires enough native samples per cycle',()=>{
    const r=measureSamPeriod(oscillation(10,20,.7));
    expect(r.issues).toContain('UNDERSAMPLED_CYCLES');expect(r.periodSeconds).toBeNull();
  });
  it('flags inconsistent apparent radius',()=>{
    const input=oscillation();
    input.centroids.forEach((c,i)=>{
      const factor=i%120<60?.65:1.3;
      c.x=540+(c.x-540)*factor;c.y=288+(c.y-288)*factor;
    });
    const r=measureSamPeriod(input);
    expect(r.issues).toContain('UNSTABLE_RADIUS');expect(r.status).toBe('INCONCLUSIVE');
  });
  it('selected cycles do not share endpoints; uncertainty remains repeatability only',()=>{
    const input=oscillation();
    retimeMotion(input,t=>2*Math.PI*t/1.4+.015*Math.sin(t*1.1)-.7);
    const r=measureSamPeriod(input),selected=r.cycles.filter(c=>c.usedForEstimate);
    for(let i=1;i<selected.length;i++)expect(selected[i].startSeconds).toBeGreaterThan(selected[i-1].endSeconds);
    expect(r.statisticalStandardUncertaintySeconds!).toBeCloseTo(
      r.cycleStandardDeviationSeconds!/Math.sqrt(selected.length),12);
    expect(r.uncertaintyScope).toBe('TYPE_A_REPEATABILITY_ONLY_NOT_TOTAL_OR_95_PERCENT_INTERVAL');
  });
  it('small crossing jitter does not create extra cycles',()=>{
    const input=oscillation();
    retimeMotion(input,t=>2*Math.PI*t/1.4-.7);
    input.centroids.forEach((c,i)=>c.x+=.15*Math.sin(i*1.7));
    const r=measureSamPeriod(input);
    expect(r.status).toBe('CANDIDATE');expect(r.periodSeconds!).toBeCloseTo(1.4,3);
    expect(r.completeCycles).toBeLessThan(15);
  });
  it('exports reproducible inputs and hashes the exact payload',async()=>{
    const input=oscillation(),payload=periodReportPayload(source,input),envelope=await periodReportEnvelope(payload);
    const parsed=JSON.parse(envelope.payloadJson);
    expect(measureSamPeriod(parsed.input)).toEqual(parsed.assessment);
    expect(parsed.source.originalVideoSha256).toBe(source.originalVideoSha256);
    const digest=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(envelope.payloadJson));
    expect(Array.from(new Uint8Array(digest),v=>v.toString(16).padStart(2,'0')).join('')).toBe(envelope.payloadSha256);
    const other=await periodReportEnvelope(periodReportPayload({...source,pivotRevisionId:'revision-2'},input));
    expect(other.payloadSha256).not.toBe(envelope.payloadSha256);
  });
});

describe('Pendular motion evidence, independent of object appearance',()=>{
  it('accepts a resolved circular swing with realistic subpixel centroid noise',()=>{
    const input=oscillation();
    input.centroids.forEach((c,i)=>{c.x+=.3*Math.sin(i*1.7);c.y+=.3*Math.cos(i*1.3);});
    const r=measureSamPeriod(input);
    expect(r.motion.status).toBe('RESOLVED_ARC');
    expect(r.motion.issues).toEqual([]);
  });
  it('does not treat periodic straight-line motion as a pendulum',()=>{
    const input=oscillation();input.centroids.forEach(c=>c.y=input.imageHeight*.7);
    const r=measureSamPeriod(input);
    expect(r.status).toBe('CANDIDATE'); // A period can exist without a pendular arc.
    expect(r.motion.status).toBe('INCONCLUSIVE');
    expect(r.motion.issues).toContain('ARC_NOT_BETTER_THAN_LINE');
  });
  it('does not classify an unresolved small arc even with a stable period',()=>{
    const input=oscillation();
    input.centroids=input.framePtsSeconds.map((t,frameIndex)=>{
      const a=.02*Math.sin(2*Math.PI*t/1.4-.7),radius=input.imageHeight*.55;
      return {frameIndex,x:540+radius*Math.sin(a),y:288+radius*Math.cos(a)};
    });
    const r=measureSamPeriod(input);
    expect(r.status).toBe('CANDIDATE');expect(r.motion.status).toBe('INCONCLUSIVE');
    expect(r.motion.issues).toContain('ARC_TOO_SMALL_TO_RESOLVE');
  });
  it('uses mask resolution, not upscaled display pixels, to resolve curvature',()=>{
    const input=oscillation();input.sourceWidth=120;input.sourceHeight=213;
    input.centroids.forEach(c=>{c.x=c.x/1080*120;c.y=c.y/1920*213;});
    const r=measureSamPeriod(input);
    expect(r.status).toBe('CANDIDATE');expect(r.motion.status).toBe('INCONCLUSIVE');
    expect(r.motion.issues).toContain('ARC_TOO_SMALL_TO_RESOLVE');
    expect(r.motion.minimumResolvableSagittaPixels!).toBeGreaterThan(18);
  });
  it('keeps stationary, one-pass, missing-pivot and fragmented observations inconclusive',()=>{
    const stationary=oscillation();stationary.centroids.forEach(c=>{c.x=540;c.y=1400;});
    const onePass=oscillation();
    onePass.centroids=onePass.framePtsSeconds.map((t,frameIndex)=>{
      const a=-.18+.36*t/20,r=1056;
      return {frameIndex,x:540+r*Math.sin(a),y:288+r*Math.cos(a)};
    });
    const missingPivot=oscillation();missingPivot.pivot=null;
    const fragments=oscillation();fragments.centroids=fragments.centroids.filter(c=>c.frameIndex%60!==0);
    for(const input of [stationary,onePass,missingPivot,fragments])
      expect(measureSamPeriod(input).motion.status).toBe('INCONCLUSIVE');
  });
  it('does not infer pendulum identity from an arrangement label',()=>{
    const input=oscillation();input.arrangement='legacy_pendulum';
    input.centroids.forEach(c=>c.y=1400);
    expect(measureSamPeriod(input).motion.status).toBe('INCONCLUSIVE');
    const ambiguous=oscillation();ambiguous.arrangement='unclear';
    expect(measureSamPeriod(ambiguous).motion.status).toBe('INCONCLUSIVE');
  });
  it('exports the recognition policy and inputs for reproduction',()=>{
    const payload=periodReportPayload(source,oscillation());
    expect(payload.motionPolicy).toEqual(SAM_PENDULUM_MOTION_POLICY);
    expect(payload.assessment.motion.method).toBe('SAM_PIVOT_ARC_VS_LINE_V1');
    expect(measureSamPeriod(payload.input).motion).toEqual(payload.assessment.motion);
  });
});
