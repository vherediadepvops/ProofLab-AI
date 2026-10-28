/**
 * Scientific visualization ONLY: normalized, phase-aligned comparison of
 * theory with independently tracked pixel coordinates. No fitting of
 * theoretical period/frequency to video, no SAM, no physical cm calibration.
 * Visual phase and axis sign are optimized and must be disclosed to users.
 */
export interface VideoPositionSample {
  timeSeconds:number;
  position:number;
}
export interface TheoryPositionSample {
  timeSeconds:number;
  horizontalCentimeters:number;
}
export interface NormalizedOverlay {
  observedPoints:string;
  theoryPoints:string;
  phaseOffsetSeconds:number;
  invertedCameraAxis:boolean;
  observationCount:number;
  windowSeconds:number;
  source:'VISUAL_NORMALIZATION_NOT_SCIENTIFIC_FIT';
}

function median(values:number[]):number {
  const sorted=[...values].sort((a,b)=>a-b);
  return sorted[Math.floor(sorted.length/2)];
}

function linearAt(time:number, points:TheoryPositionSample[]):number {
  if(time<=points[0].timeSeconds)return points[0].horizontalCentimeters;
  if(time>=points[points.length-1].timeSeconds)
    return points[points.length-1].horizontalCentimeters;
  let low=0,high=points.length-1;
  while(high-low>1) {
    const middle=Math.floor((low+high)/2);
    if(points[middle].timeSeconds<time)low=middle;
    else high=middle;
  }
  const a=points[low],b=points[high];
  return a.horizontalCentimeters+(b.horizontalCentimeters-a.horizontalCentimeters)*
    (time-a.timeSeconds)/Math.max(.000001,b.timeSeconds-a.timeSeconds);
}

export function buildNormalizedVideoOverlay(
  theory:TheoryPositionSample[],
  theoreticalPeriodSeconds:number,
  observed:VideoPositionSample[]
):NormalizedOverlay|null {
  if(theory.length<10 || observed.length<80 ||
     !Number.isFinite(theoreticalPeriodSeconds) ||
     theoreticalPeriodSeconds<=0 || theoreticalPeriodSeconds>9) return null;

  const sortedTheory=[...theory].sort((a,b)=>a.timeSeconds-b.timeSeconds);
  const sortedVideo=[...observed].sort((a,b)=>a.timeSeconds-b.timeSeconds);
  const lastTheory=sortedTheory[sortedTheory.length-1].timeSeconds;
  if(!Number.isFinite(lastTheory) || lastTheory < 10) return null;
  const maximumSeconds=Math.min(14,lastTheory-theoreticalPeriodSeconds-.15);
  if(maximumSeconds<8)return null;
  const start=sortedVideo[0].timeSeconds+2;
  const usable=sortedVideo.filter(p=>
    p.timeSeconds>=start && p.timeSeconds<=start+maximumSeconds &&
    Number.isFinite(p.position) && Number.isFinite(p.timeSeconds));
  if(usable.length<70)return null;
  if(usable.some((p,i)=>i>0 && p.timeSeconds<=usable[i-1].timeSeconds))return null;
  const span=usable[usable.length-1].timeSeconds-usable[0].timeSeconds;
  if(span<7)return null;
  const smooth=usable.map((p,i)=>median(
    usable.slice(Math.max(0,i-2),Math.min(usable.length,i+3))
      .map(x=>x.position)));
  const sorted=[...smooth].sort((a,b)=>a-b);
  const low=sorted[Math.floor((sorted.length-1)*.08)];
  const high=sorted[Math.floor((sorted.length-1)*.92)];
  const amplitude=(high-low)/2;
  if(!Number.isFinite(amplitude) || amplitude<2)return null;
  const center=(high+low)/2;
  const normalized=smooth.map(x=>
    Math.max(-1.05,Math.min(1.05,(x-center)/amplitude)));

  const theoryAmplitude=Math.max(.00001,...sortedTheory.map(x=>
    Math.abs(x.horizontalCentimeters)));
  const samples=usable.map((p,i)=>({
    t:p.timeSeconds-start,
    value:normalized[i]
  }));

  // Align only presentation phase and camera sign, not frequency or period.
  // This may make curves look similar; it is never an error estimate.
  let bestScore=Number.POSITIVE_INFINITY,bestShift=0,bestSign=1;
  const count=Math.max(1,Math.ceil(theoreticalPeriodSeconds/.04));
  for(let k=0;k<=count;k++){
    const shift=theoreticalPeriodSeconds*k/count;
    for(const sign of [1,-1]){
      let error=0;
      for(const sample of samples){
        const expected=linearAt(sample.t+shift,sortedTheory)/theoryAmplitude;
        const delta=sign*sample.value-expected;
        error+=delta*delta;
      }
      if(error<bestScore){
        bestScore=error;bestShift=shift;bestSign=sign;
      }
    }
  }
  const draw=(x:number,y:number)=>x.toFixed(2)+','+y.toFixed(2);
  const pointX=(t:number)=>36+608*(t/span);
  const pointY=(v:number)=>95-70*v;
  return {
    observedPoints:samples.map(s=>draw(pointX(s.t),pointY(bestSign*s.value))).join(' '),
    theoryPoints:samples.map(s=>draw(pointX(s.t),
      pointY(linearAt(s.t+bestShift,sortedTheory)/theoryAmplitude))).join(' '),
    phaseOffsetSeconds:bestShift,
    invertedCameraAxis:bestSign===-1,
    observationCount:samples.length,
    windowSeconds:span,
    source:'VISUAL_NORMALIZATION_NOT_SCIENTIFIC_FIT'
  };
}
