/** Geometría de reproducción del péndulo: coordenadas de IMAGEN, no metros. */
export interface ImagePivot { pivotXpx:number; pivotYpx:number; }

export interface KinematicFrame {
  timeSeconds:number;
  bobXpx:number;
  bobYpx:number;
  angleDegrees:number;
  angularVelocityRadiansPerSecond:number|null;
  angularAccelerationRadiansPerSecondSquared:number|null;
  apparentTangentialSpeedPixelsPerSecond:number|null;
  tangentialSpeedMetersPerSecond:number|null;
  tangentialAccelerationMetersPerSecondSquared:number|null;
  centripetalAccelerationMetersPerSecondSquared:number|null;
  isValid:boolean;
}
export interface KinematicAnalysis {
  method:string;
  source:string;
  pivotXpx:number;
  pivotYpx:number;
  sourceWidth:number;
  sourceHeight:number;
  medianRadiusPixels:number;
  radiusMedianAbsoluteDeviationPercent:number;
  observedMaximumAngularSpeedRadPerSec:number|null;
  observedCenterAngularSpeedRadPerSec:number|null;
  observedExtremeAngularSpeedRadPerSec:number|null;
  theoreticalMaxAngularSpeedRadPerSec:number|null;
  physicalUnitsEnabled:boolean;
  qualityStatus:string;
  sourcePoints:number;
  derivativePoints:number;
  limitationsEs:string;
  limitationsEn:string;
  samples:KinematicFrame[];
}
export interface KinematicRevision {
  studyId:string;
  role:'baseline'|'followup';
  videoId:string;
  videoSha256:string;
  revisionId:string;
  calculatedAtUtc:string;
  pivotProvenance:string;
  analysis:KinematicAnalysis;
}

/** object-fit contain: rechaza clics sobre las barras del video. */
export function pivotFromClick(
  clientX:number,clientY:number,
  rect:{left:number;top:number;width:number;height:number},
  videoWidth:number,videoHeight:number,
  trackerWidth=480,trackerHeight=270
):ImagePivot|null {
  const args=[clientX,clientY,rect.left,rect.top,rect.width,rect.height,
    videoWidth,videoHeight,trackerWidth,trackerHeight];
  if(!args.every(Number.isFinite) || rect.width<=0 || rect.height<=0 ||
      videoWidth<=0 || videoHeight<=0 || trackerWidth<=0 || trackerHeight<=0)
    return null;
  const scale=Math.min(rect.width/videoWidth,rect.height/videoHeight);
  const shownWidth=videoWidth*scale,shownHeight=videoHeight*scale;
  const x=clientX-rect.left-(rect.width-shownWidth)/2;
  const y=clientY-rect.top-(rect.height-shownHeight)/2;
  if(x<0 || y<0 || x>shownWidth || y>shownHeight)return null;
  return {pivotXpx:x/shownWidth*trackerWidth,
    pivotYpx:y/shownHeight*trackerHeight};
}

/** Solo fotogramas medidos cercanos; no se interpolan huecos de rastreo. */
export function sampleAtTime(
  frames:ReadonlyArray<KinematicFrame>,time:number,tolerance=.066
):KinematicFrame|null {
  if(!Number.isFinite(time)||!frames.length)return null;
  let lo=0,hi=frames.length;
  while(lo<hi){
    const mid=(lo+hi)>>>1;
    if(frames[mid].timeSeconds<time)lo=mid+1;
    else hi=mid;
  }
  const i=Math.min(frames.length-1,lo),j=Math.max(0,i-1);
  const f=Math.abs(frames[i].timeSeconds-time) <
    Math.abs(frames[j].timeSeconds-time)?frames[i]:frames[j];
  return Math.abs(f.timeSeconds-time)<=tolerance?f:null;
}

export function vectorAt(
  sample:KinematicFrame,pivot:ImagePivot
):{vx:number;vy:number;atx:number;aty:number;arx:number;ary:number}|null {
  const omega=sample.angularVelocityRadiansPerSecond;
  const alpha=sample.angularAccelerationRadiansPerSecondSquared;
  if(!sample.isValid || omega===null || alpha===null ||
     ![sample.bobXpx,sample.bobYpx,pivot.pivotXpx,pivot.pivotYpx,
       omega,alpha].every(Number.isFinite))return null;
  const dx=sample.bobXpx-pivot.pivotXpx;
  const dy=sample.bobYpx-pivot.pivotYpx;
  const radius=Math.hypot(dx,dy);
  if(radius<5)return null;
  const tx=dy/radius,ty=-dx/radius;
  const centripetal=omega*omega;
  return {
    vx:omega*radius*tx,vy:omega*radius*ty,
    atx:alpha*radius*tx,aty:alpha*radius*ty,
    arx:-centripetal*dx,ary:-centripetal*dy
  };
}

/**
 * Independent IDEAL prediction evaluated at a supplied angle.
 * Not a measurement, and never used to change observed derivatives.
 * For a pendulum released at rest from the declared amplitude,
 * angular speed is maximum at theta=0 and zero at the ideal extremes.
 */
export function idealAngularSpeedAtAngle(
  angleDegrees:number,initialAngleDegrees:number,maximumRadiansPerSecond:number
):number|null {
  if(![angleDegrees,initialAngleDegrees,maximumRadiansPerSecond].every(Number.isFinite) ||
    initialAngleDegrees<=0 || initialAngleDegrees>85 ||
    maximumRadiansPerSecond<0)return null;
  const amplitude=initialAngleDegrees*Math.PI/180;
  const angle=angleDegrees*Math.PI/180;
  const denominator=1-Math.cos(amplitude);
  if(denominator<=0)return null;
  const fraction=(Math.cos(angle)-Math.cos(amplitude))/denominator;
  return maximumRadiansPerSecond*Math.sqrt(Math.min(1,Math.max(0,fraction)));
}
