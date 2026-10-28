import type { SamVideoEvidence } from './sam-video-evidence';
import type { CandidatePixelPoint } from './pendulum-master-sync';
import { originalImageOffset, type ImagePivot, type ImageSize, type KinematicFrame } from './pendulum-kinematics';

export interface AuthenticatedSamVideoArchive {
  state: {
    status: string; sourceSha256: string; provenance: string; analysisId?:string;
  };
  receipt: {
    model: string; concept: string; originalVideoSha256: string;
    providerEvidenceStatus: string; framePtsSeconds: number[];
    sourceFrameCount: number; analysisId?:string;
  };
  rawResponseJson: string;
}

export interface SamPlaybackMask {
  frameIndex:number;
  timeSeconds:number;
  left:number;
  top:number;
  right:number;
  bottom:number;
  maskPath:string;
  centroidXpx:number;
  centroidYpx:number;
  foregroundPixels:number;
}
export interface SamPlaybackTrack {
  frameWidth:number;
  frameHeight:number;
  source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE';
  objectId:string;
  points:CandidatePixelPoint[];
  masks:SamPlaybackMask[];
}

const validSha=(value:string) => /^[a-f0-9]{64}$/i.test(value);

/**
 * This function ONLY accepts an archive previously verified by our backend.
 * A pasted SAM JSON is not evidence from Meta and cannot be used as a
 * physical observation source in the master player.
 */
export function verifiedSamPlaybackTrack(
  archive:AuthenticatedSamVideoArchive, parsed:SamVideoEvidence,
  currentOriginalSha:string
):SamPlaybackTrack {
  const r=archive.receipt, s=archive.state;
  if(!validSha(currentOriginalSha) ||
      r.originalVideoSha256.toLowerCase()!==currentOriginalSha.toLowerCase() ||
      s.sourceSha256.toLowerCase()!==currentOriginalSha.toLowerCase() ||
      s.status!=='CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW' ||
      r.providerEvidenceStatus!=='COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW' ||
      r.model!=='sam-3.1' || r.concept!=='metal pendulum bob' ||
      !Array.isArray(r.framePtsSeconds) ||
      r.sourceFrameCount!==r.framePtsSeconds.length ||
      r.sourceFrameCount<50 || r.sourceFrameCount>2500 ||
      parsed.model!=='sam-3.1' ||
      parsed.result!=='DECODED_MASKS' ||
      parsed.objectIds.length!==1 ||
      parsed.frameWidth<1 || parsed.frameHeight<1)
    throw new Error('SAM_EVIDENCE_REQUIRES_MANUAL_OBJECT_REVIEW_OR_INVALID_PROVENANCE');

  let previous=-1;
  for(const time of r.framePtsSeconds) {
    if(!Number.isFinite(time) || time<0 || time<=previous)
      throw new Error('SAM_PTS_INVALID');
    previous=time;
  }
  const masks:SamPlaybackMask[]=[];
  const objectId=parsed.objectIds[0];
  for(const frame of parsed.frames){
    if(!Number.isSafeInteger(frame.frameIndex) ||
        frame.frameIndex<0 || frame.frameIndex>=r.sourceFrameCount)
      throw new Error('SAM_FRAME_NOT_IN_UPLOADED_SOURCE');
    if(frame.detections.length!==1 ||
       frame.detections[0].objectId!==objectId)
      throw new Error('SAM_AMBIGUOUS_BOB_FRAME');
    const d=frame.detections[0];
    if(!d.foregroundPixels || d.centroidXpx===null || d.centroidYpx===null)
      continue;
    masks.push({
      frameIndex:frame.frameIndex,timeSeconds:r.framePtsSeconds[frame.frameIndex],
      left:d.left,top:d.top,right:d.right,bottom:d.bottom,
      maskPath:d.maskPath,centroidXpx:d.centroidXpx,
      centroidYpx:d.centroidYpx,foregroundPixels:d.foregroundPixels
    });
  }
  masks.sort((a,b)=>a.timeSeconds-b.timeSeconds);
  if(masks.length<5)
    throw new Error('SAM_TOO_FEW_VALID_MASKS_TO_COMPUTE_MOTION');

  // Existing drawing algorithms operate in 480×270 IMAGE coordinates.
  // Drawing only: measurements must undo this scale with originalImageOffset.
  // These are not metres; the source frame raster is scaled explicitly.
  const points:CandidatePixelPoint[]=masks.map(m=>({
    timeSeconds:m.timeSeconds,
    x:m.centroidXpx/parsed.frameWidth*480,
    y:m.centroidYpx/parsed.frameHeight*270,
    approximateRadiusPixels:Math.sqrt(m.foregroundPixels/Math.PI)*
      Math.sqrt(480*270/(parsed.frameWidth*parsed.frameHeight)),
    source:'SAM_3_1_CACHED_PROVIDER_CENTROID_NOT_CALIBRATED'
  }));
  return {
    frameWidth:parsed.frameWidth,frameHeight:parsed.frameHeight,
    source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId,points,masks
  };
}

export function nearestSamMask(track:SamPlaybackTrack,time:number,
  tolerance=.055):SamPlaybackMask|null {
  if(!Number.isFinite(time) || track.masks.length===0)return null;
  let lo=0,hi=track.masks.length;
  while(lo<hi){
    const mid=(lo+hi)>>>1;
    if(track.masks[mid].timeSeconds<time)lo=mid+1;
    else hi=mid;
  }
  const right=Math.min(track.masks.length-1,lo),left=Math.max(0,right-1);
  const mask=Math.abs(track.masks[right].timeSeconds-time)<
    Math.abs(track.masks[left].timeSeconds-time) ?
    track.masks[right]:track.masks[left];
  return Math.abs(mask.timeSeconds-time)<=tolerance?mask:null;
}

/**
 * Pixel-space angular derivatives from SAM mask centroids and a manually
 * selected pivot. Never force zero/max velocity to match the ideal model.
 * A gap makes the derivative unavailable rather than interpolating it.
 * Bob X/Y remain in the persisted 480x270 grid for drawing compatibility;
 * angle/derivatives and speed are measured in undistorted original image pixels.
 */
export function samAngularFrameAt(
  track:SamPlaybackTrack,pivot:ImagePivot,time:number,
  image:ImageSize={width:track.frameWidth,height:track.frameHeight},
  tolerance=.055
):KinematicFrame|null {
  const points=track.points;
  if(points.length<3 || !Number.isFinite(time))return null;
  let lo=0,hi=points.length;
  while(lo<hi){const mid=(lo+hi)>>>1;
    if(points[mid].timeSeconds<time)lo=mid+1;else hi=mid;}
  const right=Math.min(points.length-1,lo),left=Math.max(0,right-1);
  const i=Math.abs(points[right].timeSeconds-time)<
    Math.abs(points[left].timeSeconds-time)?right:left;
  if(Math.abs(points[i].timeSeconds-time)>tolerance)return null;
  const central=points[i];
  const offset=originalImageOffset(central.x,central.y,pivot,image);
  if(!offset)return null;
  const base:KinematicFrame={
    timeSeconds:central.timeSeconds,bobXpx:central.x,bobYpx:central.y,
    angleDegrees:Math.atan2(offset.dx,offset.dy)*180/Math.PI,
    angularVelocityRadiansPerSecond:null,
    angularAccelerationRadiansPerSecondSquared:null,
    apparentTangentialSpeedPixelsPerSecond:null,
    tangentialSpeedMetersPerSecond:null,
    tangentialAccelerationMetersPerSecondSquared:null,
    centripetalAccelerationMetersPerSecondSquared:null,
    isValid:false
  };
  if(i===0 || i===points.length-1)return base;
  const prev=points[i-1],next=points[i+1];
  const dt1=central.timeSeconds-prev.timeSeconds;
  const dt2=next.timeSeconds-central.timeSeconds;
  if(dt1<=0 || dt2<=0 || dt1>.2 || dt2>.2)return base;
  const radius=Math.hypot(offset.dx,offset.dy);
  if(!Number.isFinite(radius) || radius<5)return base;
  const before=originalImageOffset(prev.x,prev.y,pivot,image);
  const after=originalImageOffset(next.x,next.y,pivot,image);
  if(!before || !after)return base;
  const unwrap=(a:number,b:number)=>Math.atan2(Math.sin(a-b),Math.cos(a-b));
  const t0=Math.atan2(offset.dx,offset.dy);
  const tp=Math.atan2(before.dx,before.dy),tn=Math.atan2(after.dx,after.dy);
  const leftSlope=unwrap(t0,tp)/dt1;
  const rightSlope=unwrap(tn,t0)/dt2;
  const omega=(leftSlope*dt2+rightSlope*dt1)/(dt1+dt2);
  const alpha=2*(rightSlope-leftSlope)/(dt1+dt2);
  if(!Number.isFinite(omega) || !Number.isFinite(alpha) ||
     Math.abs(omega)>30 || Math.abs(alpha)>200)return base;
  return {...base,angleDegrees:t0*180/Math.PI,
    angularVelocityRadiansPerSecond:omega,
    angularAccelerationRadiansPerSecondSquared:alpha,
    apparentTangentialSpeedPixelsPerSecond:Math.abs(omega)*radius,
    isValid:true};
}