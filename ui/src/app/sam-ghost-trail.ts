import type {SamPlaybackMask} from './sam-playback';

/** Bounded temporal samples from actual, sorted SAM masks. Never interpolate a missing mask. */
export function historicalSamSilhouettes(masks:SamPlaybackMask[],time:number):SamPlaybackMask[] {
  if(!Number.isFinite(time) || !masks.length)return [];
  const window=1.5,slots=10,start=time-window,end=time-.08;
  let lo=0,hi=masks.length;
  while(lo<hi){const mid=(lo+hi)>>>1;if(masks[mid].timeSeconds<start)lo=mid+1;else hi=mid;}
  const selected=new Map<number,SamPlaybackMask>();
  for(let i=lo;i<masks.length && masks[i].timeSeconds<=end;i++){
    const mask=masks[i];
    if(!mask.maskPath || mask.foregroundPixels<=0)continue;
    const bucket=Math.min(slots-1,Math.floor((mask.timeSeconds-start)/window*slots));
    selected.set(bucket,mask);
  }
  return [...selected.values()];
}
