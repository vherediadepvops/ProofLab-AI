/**
 * Local visual playback helpers. These are approximate pixel-space
 * candidates from FFmpeg background subtraction, NOT verified SAM masks.
 * Source coordinates are sampled at 480x270/12fps. 3 synchronized views must
 * always draw the same HTMLVideoElement current decoded frame.
 */
export interface CandidatePixelPoint {
  timeSeconds: number;
  x: number;
  y: number;
  approximateRadiusPixels: number;
  source?:'SAM_3_1_CACHED_PROVIDER_CENTROID_NOT_CALIBRATED'|
    'LOCAL_CANDIDATE_NOT_SAM';
}
export interface CandidateVector {
  timeSeconds: number;
  x: number;
  y: number;
  vxPixelsPerSecond: number;
  vyPixelsPerSecond: number;
  speedPixelsPerSecond: number;
  source: 'LOCAL_CANDIDATE_NOT_SAM'|
    'SAM_3_1_CACHED_PROVIDER_CENTROID_NOT_CALIBRATED';
}

export function nearestMotionTime(
  samples: ReadonlyArray<CandidatePixelPoint>,
  time: number
): number | null {
  if (!Number.isFinite(time) || samples.length===0) return null;
  let low=0,high=samples.length;
  while(low<high){
    const mid=(low+high)>>>1;
    if(samples[mid].timeSeconds<time) low=mid+1;
    else high=mid;
  }
  const right=Math.min(samples.length-1,low);
  const left=Math.max(0,right-1);
  const index=Math.abs(samples[right].timeSeconds-time)<
    Math.abs(samples[left].timeSeconds-time)?right:left;
  return Math.abs(samples[index].timeSeconds-time)<=.17?index:null;
}

/** No interpolation over large gaps. Vector uses time-indexed image points. */
export function motionAt(
  samples: ReadonlyArray<CandidatePixelPoint>, time: number
): CandidateVector | null {
  if(samples.length<2 || !Number.isFinite(time))return null;
  const index=nearestMotionTime(samples,time);
  if(index===null)return null;
  const anchor=samples[index];
  const before=samples[Math.max(0,index-1)];
  const after=samples[Math.min(samples.length-1,index+1)];
  const dt=after.timeSeconds-before.timeSeconds;
  if(!Number.isFinite(dt) || dt<=0 || dt>.4 ||
     ![anchor.x,anchor.y,before.x,before.y,after.x,after.y].every(Number.isFinite))
    return null;
  const vx=(after.x-before.x)/dt;
  const vy=(after.y-before.y)/dt;
  const speed=Math.hypot(vx,vy);
  if(!Number.isFinite(speed) || speed>3000)return null;
  return {
    timeSeconds:anchor.timeSeconds,x:anchor.x,y:anchor.y,
    vxPixelsPerSecond:vx,vyPixelsPerSecond:vy,
    speedPixelsPerSecond:speed,
    source:anchor.source??'LOCAL_CANDIDATE_NOT_SAM'
  };
}

export function trailingMotion(
  samples: ReadonlyArray<CandidatePixelPoint>,
  time: number, seconds=1.5
):CandidatePixelPoint[] {
  if(!Number.isFinite(time) || !Number.isFinite(seconds) || seconds<=0)return [];
  return samples.filter(p=>Number.isFinite(p.timeSeconds)&&
    p.timeSeconds>=time-seconds&&p.timeSeconds<=time+.02&&
    Number.isFinite(p.x)&&Number.isFinite(p.y));
}

export function seekByFrame(
  current:number,deltaFrames:number,fps:number,duration:number
):number{
  if(![current,deltaFrames,fps,duration].every(Number.isFinite) ||
      fps<=0 || duration<=0)return 0;
  // Nominal-rate fallback when no verified source timestamps are available.
  const frame=Math.round(current*fps)+Math.trunc(deltaFrames);
  return Math.max(0,Math.min(Math.max(0,duration-.001),frame/fps));
}

export function formatMediaTime(seconds:number):string{
  if(!Number.isFinite(seconds) || seconds<0)return '00:00.000';
  const minutes=Math.floor(seconds/60).toString().padStart(2,'0');
  const s=Math.floor(seconds%60).toString().padStart(2,'0');
  const milliseconds=Math.floor((seconds-Math.floor(seconds))*1000)
    .toString().padStart(3,'0');
  return minutes+':'+s+'.'+milliseconds;
}
/** Step by actual source presentation timestamps (including variable frame rate). */
export function seekSourceFrame(
  pts:ReadonlyArray<number>,current:number,delta:-1|1,fps:number,duration:number
):number {
  if(!pts.length || !Number.isFinite(current))return seekByFrame(current,delta,fps,duration);
  let lo=0,hi=pts.length;
  while(lo<hi){const mid=(lo+hi)>>>1;if(pts[mid]<current)lo=mid+1;else hi=mid;}
  const right=Math.min(pts.length-1,lo),left=Math.max(0,right-1);
  const nearest=Math.abs(pts[right]-current)<Math.abs(pts[left]-current)?right:left;
  const index=Math.max(0,Math.min(pts.length-1,nearest+delta));
  const start=pts[index],end=pts[index+1] ?? Math.min(duration,start+1/fps);
  // Serialized PTS are rounded to microseconds. Seeking exactly at that rounded
  // boundary can decode the preceding frame. Seek just inside the target frame;
  // rVFC still supplies its true PTS for masks and measurements.
  const inset=Math.min(.001,Math.max(0,end-start)/4);
  return Math.max(0,Math.min(duration-.000001,start+inset));
}
