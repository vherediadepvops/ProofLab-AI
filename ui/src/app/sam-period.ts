/** Deterministic observations only: no theoretical period, length or nominal FPS input. */
export const SAM_PERIOD_METHOD='SAM_NATIVE_PTS_UPCROSSINGS_ALTERNATE_CYCLES_V1';
export const SAM_PERIOD_POLICY=Object.freeze({
  minimumAmplitudeDegrees:.5,minimumHysteresisDegrees:.05,hysteresisFraction:.08,
  minimumSamplesPerCycle:20,minimumSelectedCycles:3,
  minimumCoverage:.8,maximumCycleCv:.05,maximumRelativeDrift:.05,
  maximumRadiusMadFraction:.1
});
/** Local arc diagnostics only; Muse Spark interprets the phenomenon from the supplied evidence. */
export const SAM_PENDULUM_MOTION_METHOD='SAM_PIVOT_ARC_VS_LINE_V1';
export const SAM_PENDULUM_MOTION_POLICY=Object.freeze({
  minimumArcSagittaSourcePixels:2,maximumRadialRmsToSagitta:.2,
  maximumArcRmsToLineRms:.7
});
export type PendulumMotionIssue='PERIOD_OR_TRACKING_INCONCLUSIVE'|'ARC_TOO_SMALL_TO_RESOLVE'|
  'RADIAL_RESIDUAL_TOO_LARGE'|'ARC_NOT_BETTER_THAN_LINE';
export interface PendulumMotionAssessment {
  method:typeof SAM_PENDULUM_MOTION_METHOD;
  status:'RESOLVED_ARC'|'INCONCLUSIVE';issues:PendulumMotionIssue[];
  radialRmsPixels:number|null;lineRmsPixels:number|null;arcSagittaPixels:number|null;
  minimumResolvableSagittaPixels:number|null;
}
export interface PeriodCentroid {frameIndex:number;x:number;y:number}
export interface SamPeriodInput {
  framePtsSeconds:number[];centroids:PeriodCentroid[];
  sourceWidth:number;sourceHeight:number;imageWidth:number;imageHeight:number;
  pivot:{x:number;y:number}|null; // normalized image coordinates [0,1]
  arrangement:'simple_pendulum'|'legacy_pendulum'|'pulley_system'|'unclear';
}
export interface PeriodCycle {
  startSeconds:number;endSeconds:number;periodSeconds:number;
  startFrame:number;endFrame:number;segment:number;
  adequateSampling:boolean;usedForEstimate:boolean;
}
export type PeriodIssue='INVALID_TIMESTAMPS'|'INVALID_CENTROIDS'|'NO_DETECTIONS'|'MISSING_PIVOT'|
  'UNSUPPORTED_ASSEMBLY'|'INVALID_GEOMETRY'|'LOW_AMPLITUDE'|'TOO_FEW_CYCLES'|
  'LOW_COVERAGE'|'GAPS'|'GEOMETRY_EXCLUSIONS'|'UNDERSAMPLED_CYCLES'|
  'UNSTABLE_PERIOD'|'PERIOD_DRIFT'|'UNSTABLE_RADIUS'|'TIME_GAPS';
export interface SamPeriodAssessment {
  method:typeof SAM_PERIOD_METHOD;status:'CANDIDATE'|'INCONCLUSIVE';issues:PeriodIssue[];
  periodSeconds:number|null;cycleStandardDeviationSeconds:number|null;
  statisticalStandardUncertaintySeconds:number|null;
  uncertaintyScope:'TYPE_A_REPEATABILITY_ONLY_NOT_TOTAL_OR_95_PERCENT_INTERVAL';
  completeCycles:number;usedCycles:number;cycles:PeriodCycle[];
  totalFrames:number;detectedFrames:number;usableFrames:number;missingFrames:number;
  detectionCoverage:number;missingFraction:number;longestMissingRunSeconds:number;
  sourceSpanSeconds:number;medianFrameIntervalSeconds:number|null;
  equilibriumAngleDegrees:number|null;amplitudeDegrees:number|null;
  radiusMadFraction:number|null;relativePeriodDrift:number|null;
  motion:PendulumMotionAssessment;
}
const mean=(v:number[])=>v.reduce((a,b)=>a+b,0)/v.length;
function quantile(v:number[],q:number):number {
  const s=[...v].sort((a,b)=>a-b),p=(s.length-1)*q,lo=Math.floor(p),hi=Math.ceil(p);
  return s[lo]+(s[hi]-s[lo])*(p-lo);
}
const median=(v:number[])=>quantile(v,.5);

/**
 * Crossings are interpolated only between consecutive observed source frames.
 * Gaps reset the detector. Alternating cycles never share a crossing endpoint.
 * s/sqrt(n) describes repeatability conditional on approximately independent cycles;
 * it does not quantify tracking bias, pivot/camera error or source-clock accuracy.
 */
export function measureSamPeriod(input:SamPeriodInput):SamPeriodAssessment {
  const p=SAM_PERIOD_POLICY,times=input.framePtsSeconds;
  const r:SamPeriodAssessment={
    method:SAM_PERIOD_METHOD,status:'INCONCLUSIVE',issues:[],
    periodSeconds:null,cycleStandardDeviationSeconds:null,statisticalStandardUncertaintySeconds:null,
    uncertaintyScope:'TYPE_A_REPEATABILITY_ONLY_NOT_TOTAL_OR_95_PERCENT_INTERVAL',
    completeCycles:0,usedCycles:0,cycles:[],totalFrames:times.length,detectedFrames:0,usableFrames:0,
    missingFrames:times.length,detectionCoverage:0,missingFraction:1,longestMissingRunSeconds:0,
    sourceSpanSeconds:0,medianFrameIntervalSeconds:null,equilibriumAngleDegrees:null,
    amplitudeDegrees:null,radiusMadFraction:null,relativePeriodDrift:null,
    motion:{method:SAM_PENDULUM_MOTION_METHOD,status:'INCONCLUSIVE',
      issues:['PERIOD_OR_TRACKING_INCONCLUSIVE'],radialRmsPixels:null,lineRmsPixels:null,
      arcSagittaPixels:null,minimumResolvableSagittaPixels:null}
  };
  const issue=(v:PeriodIssue)=>{if(!r.issues.includes(v))r.issues.push(v);};
  if(times.length<2 || times.length>15000 || times.some((t,i)=>!Number.isFinite(t)||t<0||(i>0&&t<=times[i-1]))) {
    issue('INVALID_TIMESTAMPS');return r;
  }
  r.sourceSpanSeconds=times[times.length-1]-times[0];
  const intervals=times.slice(1).map((t,i)=>t-times[i]),dt=median(intervals);
  r.medianFrameIntervalSeconds=dt;
  const dimensions=[input.sourceWidth,input.sourceHeight,input.imageWidth,input.imageHeight];
  if(dimensions.some(d=>!Number.isFinite(d)||d<1||d>16384)){issue('INVALID_GEOMETRY');return r;}
  const byFrame=new Map<number,PeriodCentroid>();
  for(const c of input.centroids) {
    if(!Number.isInteger(c.frameIndex)||c.frameIndex<0||c.frameIndex>=times.length||
      !Number.isFinite(c.x)||!Number.isFinite(c.y)||c.x<0||c.y<0||
      c.x>=input.sourceWidth||c.y>=input.sourceHeight||byFrame.has(c.frameIndex)) {
      issue('INVALID_CENTROIDS');return r;
    }
    byFrame.set(c.frameIndex,c);
  }
  r.detectedFrames=byFrame.size;r.missingFrames=times.length-byFrame.size;
  r.detectionCoverage=byFrame.size/times.length;r.missingFraction=1-r.detectionCoverage;
  // Source-frame bins; last-frame extent uses the median native interval.
  const edge=(i:number)=>i===0?times[0]:i===times.length?times[i-1]+dt:(times[i-1]+times[i])/2;
  let gapStart=-1;
  for(let i=0;i<=times.length;i++) {
    if(i<times.length&&!byFrame.has(i)){if(gapStart<0)gapStart=i;}
    else if(gapStart>=0){r.longestMissingRunSeconds=Math.max(r.longestMissingRunSeconds,edge(i)-edge(gapStart));gapStart=-1;}
  }
  if(r.missingFrames>0)issue('GAPS');
  if(!byFrame.size){issue('NO_DETECTIONS');return r;}
  if(input.arrangement!=='simple_pendulum'&&input.arrangement!=='legacy_pendulum') {
    issue('UNSUPPORTED_ASSEMBLY');return r;
  }
  const pivot=input.pivot;
  if(!pivot){issue('MISSING_PIVOT');return r;}
  if(!Number.isFinite(pivot.x)||!Number.isFinite(pivot.y)||pivot.x<0||pivot.x>1||pivot.y<0||pivot.y>1) {
    issue('INVALID_GEOMETRY');return r;
  }
  const samples:{frame:number;time:number;angle:number;radius:number;segment:number;x:number;y:number}[]=[];
  let previousFrame=-2,segment=0;
  for(let i=0;i<times.length;i++) {
    const c=byFrame.get(i);if(!c)continue;
    const dx=(c.x/input.sourceWidth-pivot.x)*input.imageWidth;
    const dy=(c.y/input.sourceHeight-pivot.y)*input.imageHeight;
    const radius=Math.hypot(dx,dy);
    if(dy<=0||radius<5){issue('GEOMETRY_EXCLUSIONS');continue;}
    // A native timestamp discontinuity also breaks a segment even if the indices touch.
    const timeGap=previousFrame>=0 && times[i]-times[previousFrame]>dt*3;
    if(timeGap && i===previousFrame+1)issue('TIME_GAPS');
    if(i!==previousFrame+1 || timeGap)segment++;
    samples.push({frame:i,time:times[i],angle:Math.atan2(dx,dy)*180/Math.PI,radius,segment,x:dx,y:dy});
    previousFrame=i;
  }
  r.usableFrames=samples.length;
  if(samples.length<3){issue('INVALID_GEOMETRY');return r;}
  const angles=samples.map(v=>v.angle),radii=samples.map(v=>v.radius),radius=median(radii);
  r.radiusMadFraction=median(radii.map(v=>Math.abs(v-radius)))/radius;
  const low=quantile(angles,.05),high=quantile(angles,.95);
  const center=(low+high)/2,amplitude=(high-low)/2;
  r.equilibriumAngleDegrees=center;r.amplitudeDegrees=amplitude;
  if(amplitude<p.minimumAmplitudeDegrees){issue('LOW_AMPLITUDE');return r;}
  const hysteresis=Math.max(p.minimumHysteresisDegrees,amplitude*p.hysteresisFraction);
  type Crossing={time:number;frame:number;segment:number};
  const crosses:Crossing[]=[];
  let armed=false,pending:Crossing|null=null;
  for(let i=0;i<samples.length;i++) {
    const current=samples[i],prior=samples[i-1],value=current.angle-center;
    if(!prior||current.segment!==prior.segment){armed=value<=-hysteresis;pending=null;continue;}
    if(value<=-hysteresis){armed=true;pending=null;}
    const before=prior.angle-center;
    if(armed&&before<=0&&value>0) {
      pending={time:prior.time+(current.time-prior.time)*(-before)/(value-before),
        frame:current.frame,segment:current.segment};
    }
    if(armed&&pending&&value>=hysteresis) {
      crosses.push(pending);armed=false;pending=null;
    }
  }
  let indexInSegment=0;
  for(let i=1;i<crosses.length;i++) {
    const a=crosses[i-1],b=crosses[i];
    if(a.segment!==b.segment){indexInSegment=0;continue;}
    const adequate=b.frame-a.frame>=p.minimumSamplesPerCycle;
    r.cycles.push({startSeconds:a.time,endSeconds:b.time,periodSeconds:b.time-a.time,
      startFrame:a.frame,endFrame:b.frame,segment:a.segment,adequateSampling:adequate,
      usedForEstimate:adequate&&indexInSegment%2===0});
    indexInSegment++;
    if(!adequate)issue('UNDERSAMPLED_CYCLES');
  }
  r.completeCycles=r.cycles.length;
  const selected=r.cycles.filter(v=>v.usedForEstimate).map(v=>v.periodSeconds);
  r.usedCycles=selected.length;
  if(selected.length)r.periodSeconds=mean(selected);
  if(selected.length>=2)r.cycleStandardDeviationSeconds=Math.sqrt(
    selected.reduce((sum,v)=>sum+(v-r.periodSeconds!)**2,0)/(selected.length-1));
  if(selected.length>=p.minimumSelectedCycles)
    r.statisticalStandardUncertaintySeconds=r.cycleStandardDeviationSeconds!/Math.sqrt(selected.length);
  else issue('TOO_FEW_CYCLES');
  if(r.detectionCoverage<p.minimumCoverage || r.usableFrames/times.length<p.minimumCoverage)issue('LOW_COVERAGE');
  // Inspect ALL sampled cycles, not only those selected for the uncertainty estimate.
  const all=r.cycles.filter(v=>v.adequateSampling).map(v=>v.periodSeconds);
  if(all.length>=2) {
    const average=mean(all),sd=Math.sqrt(all.reduce((sum,v)=>sum+(v-average)**2,0)/(all.length-1));
    if(sd/average>p.maximumCycleCv)issue('UNSTABLE_PERIOD');
    const half=Math.floor(all.length/2);
    r.relativePeriodDrift=(mean(all.slice(-half))-mean(all.slice(0,half)))/average;
    if(Math.abs(r.relativePeriodDrift)>p.maximumRelativeDrift)issue('PERIOD_DRIFT');
  }
  if(r.radiusMadFraction>p.maximumRadiusMadFraction)issue('UNSTABLE_RADIUS');
  const fatal=r.issues.some(v=>!['GAPS','GEOMETRY_EXCLUSIONS','TIME_GAPS'].includes(v));
  if(!fatal&&r.statisticalStandardUncertaintySeconds!==null)r.status='CANDIDATE';
  // Periodicity alone also occurs in straight translations. Compare the observed
  // arc about the supplied pivot with the best unconstrained straight line.
  // This is image-space consistency, never semantic identification by SAM.
  const mp=SAM_PENDULUM_MOTION_POLICY,m=r.motion;
  const radiusMean=mean(radii),mx=mean(samples.map(v=>v.x)),my=mean(samples.map(v=>v.y));
  const vx=mean(samples.map(v=>(v.x-mx)**2)),vy=mean(samples.map(v=>(v.y-my)**2));
  const covariance=mean(samples.map(v=>(v.x-mx)*(v.y-my)));
  m.radialRmsPixels=Math.sqrt(mean(radii.map(v=>(v-radiusMean)**2)));
  m.lineRmsPixels=Math.sqrt(Math.max(0,(vx+vy-Math.hypot(vx-vy,2*covariance))/2));
  m.arcSagittaPixels=radiusMean*(1-Math.cos(amplitude*Math.PI/180));
  // Do not claim sub-raster curvature when masks were decoded at lower resolution.
  m.minimumResolvableSagittaPixels=mp.minimumArcSagittaSourcePixels*
    Math.max(1,input.imageWidth/input.sourceWidth,input.imageHeight/input.sourceHeight);
  m.issues=r.status==='CANDIDATE'?[]:['PERIOD_OR_TRACKING_INCONCLUSIVE'];
  if(m.arcSagittaPixels<m.minimumResolvableSagittaPixels)m.issues.push('ARC_TOO_SMALL_TO_RESOLVE');
  if(m.radialRmsPixels>mp.maximumRadialRmsToSagitta*m.arcSagittaPixels)
    m.issues.push('RADIAL_RESIDUAL_TOO_LARGE');
  if(m.lineRmsPixels<=1e-9 || m.radialRmsPixels>mp.maximumArcRmsToLineRms*m.lineRmsPixels)
    m.issues.push('ARC_NOT_BETTER_THAN_LINE');
  if(!m.issues.length)m.status='RESOLVED_ARC';
  return r;
}

export interface SamPeriodSource {
  studyId:string;role:string;originalVideoSha256:string;analysisId:string;
  providerResponseSha256:string;providerModel:'sam-3.1';objectId:string;
  pivotRevisionId:string|null;pivotProvenance:string;adaptiveBatchId?:string|null;
}
export function periodReportPayload(source:SamPeriodSource,input:SamPeriodInput,
  assessment=measureSamPeriod(input)) {
  return {schemaVersion:'PROOFLAB_PERIOD_REPORT_V1',source,method:SAM_PERIOD_METHOD,
    policy:SAM_PERIOD_POLICY,motionPolicy:SAM_PENDULUM_MOTION_POLICY,input,assessment,
    limitations:{
      es:'Estimación local desde centroides SAM y tiempos originales. u = s/√n de ciclos alternos; supone errores aproximadamente independientes. No incluye sesgo de seguimiento, pivote, cámara ni reloj. No es incertidumbre total ni intervalo del 95%. Diagnóstico geométrico local; la clasificación del fenómeno corresponde a una respuesta registrada de Muse Spark. Requiere revisar el objeto, el pivote y la cámara.',
      en:'Local estimate from SAM centroids and original timestamps. u = s/√n of alternating cycles assumes approximately independent errors. Tracking bias, pivot, camera and clock errors are excluded. Not total uncertainty or a 95% interval. Local geometry diagnostics; phenomenon classification belongs to a recorded Muse Spark response. Review the object, pivot and camera.'
    }};
}
/** Integrity checksum of the exact UTF-8 payload; not a digital signature or certification. */
export async function periodReportEnvelope(payload:ReturnType<typeof periodReportPayload>) {
  const payloadJson=JSON.stringify(payload);
  const digest=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(payloadJson));
  const payloadSha256=Array.from(new Uint8Array(digest),v=>v.toString(16).padStart(2,'0')).join('');
  return {format:'PROOFLAB_PERIOD_EXPORT_V1',payloadSha256,payloadJson};
}
