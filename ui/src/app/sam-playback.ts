import type {SceneDefinition} from './assembly-review';
import type { SamVideoEvidence } from './sam-video-evidence';
import type { CandidatePixelPoint } from './pendulum-master-sync';
import { originalImageOffset, type ImagePivot, type ImageSize, type KinematicFrame } from './pendulum-kinematics';

export interface SamSuspensionEvidence {
  sourceSha256:string;sceneKey:string;evidenceKey:string;responseSha256:string;model:string;
  width:number;height:number;
  points:{frame:number;visible:boolean;x:number;y:number;confidence:number}[];
}
export interface AuthenticatedSamVideoArchive {
  suspension?:SamSuspensionEvidence|null;
  state: {
    status: string; sourceSha256: string; provenance: string; analysisId?:string;
  };
  receipt: {
    model: string; concept: string; originalVideoSha256: string;
    providerEvidenceStatus: string; framePtsSeconds: number[];
    sourceFrameCount: number; analysisId?:string; responseJsonSha256?:string; requestProtocol?:string; scene?:SceneDefinition|null;
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
  currentOriginalSha:string, role='object', minimumMasks=5
):SamPlaybackTrack {
  const r=archive.receipt, s=archive.state;
  const scene=r.scene;
  const objectId=scene?scene.roles.find(r=>r.role===role)?.id:parsed.objectIds[0];
  if(!validSha(currentOriginalSha) ||
      r.originalVideoSha256.toLowerCase()!==currentOriginalSha.toLowerCase() ||
      s.sourceSha256.toLowerCase()!==currentOriginalSha.toLowerCase() ||
      s.status!=='CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW' ||
      r.providerEvidenceStatus!=='COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW' ||
      r.model!=='sam-3.1' || !validSamTarget(archive,currentOriginalSha) ||
      !Array.isArray(r.framePtsSeconds) ||
      r.sourceFrameCount!==r.framePtsSeconds.length ||
      r.sourceFrameCount<50 || r.sourceFrameCount>2500 ||
      parsed.model!=='sam-3.1' ||
      parsed.result!=='DECODED_MASKS' ||
      (!scene && parsed.objectIds.length!==1) || !objectId ||
      (scene && (scene.width!==parsed.frameWidth || scene.height!==parsed.frameHeight ||
        parsed.objectIds.some(id=>!scene.roles.some(r=>r.id===id)))) ||
      parsed.frameWidth<1 || parsed.frameHeight<1)
    throw new Error('SAM_EVIDENCE_REQUIRES_MANUAL_OBJECT_REVIEW_OR_INVALID_PROVENANCE');

  let previous=-1;
  for(const time of r.framePtsSeconds) {
    if(!Number.isFinite(time) || time<0 || time<=previous)
      throw new Error('SAM_PTS_INVALID');
    previous=time;
  }
  const masks:SamPlaybackMask[]=[];
  for(const frame of parsed.frames){
    if(!Number.isSafeInteger(frame.frameIndex) ||
        frame.frameIndex<0 || frame.frameIndex>=r.sourceFrameCount)
      throw new Error('SAM_FRAME_NOT_IN_UPLOADED_SOURCE');
    if(!scene && (frame.detections.length!==1 || frame.detections[0].objectId!==objectId))
      throw new Error('SAM_AMBIGUOUS_BOB_FRAME');
    const selected=frame.detections.filter(d=>d.objectId===objectId);
    if(selected.length>1)throw new Error('SAM_DUPLICATE_ROLE_FRAME');
    if(selected.length===0)continue;
    const d=selected[0];
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
  if(masks.length<minimumMasks)
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

/** Role bindings originate only in the backend's reviewed scene receipt. */
export function validSamTarget(archive:AuthenticatedSamVideoArchive,sha:string):boolean {
  const r=archive.receipt,scene=r.scene;
  // New generic target and immutable historical receipts are distinct contracts.
  if(!scene)return (r.concept==='pendulum bob' &&
      r.requestProtocol==='PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V3_GENERIC_BOB') ||
    (r.concept==='metal pendulum bob' &&
      (!r.requestProtocol || r.requestProtocol==='PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2'));
  const roles=['object','cord','knot','pulley','support'];
  return r.requestProtocol==='PROOFLAB_SAM_MULTIOBJECT_SOURCE_PTS_V1' &&
    scene.videoSha256===sha && validSha(scene.key) &&
    (scene.origin==null || scene.origin==='SPARK_GROUNDING') &&
    (scene.origin!=='SPARK_GROUNDING' || (scene.roles.length===1 &&
      scene.roles[0].role==='object' && scene.pivotX===null && scene.pivotY===null)) &&
    scene.width>0 && scene.height>0 && scene.roles.length>0 && scene.roles.length<=5 &&
    new Set(scene.roles.map(r=>r.role)).size===scene.roles.length &&
    scene.roles.some(r=>r.role==='object'&&r.id==='1') &&
    scene.roles.every(r=>roles.includes(r.role)&&r.id===String(roles.indexOf(r.role)+1)) &&
    ['simple_pendulum','pulley_system','unclear'].includes(scene.arrangement) &&
    ((scene.pivotX===null&&scene.pivotY===null) ||
      (scene.arrangement==='simple_pendulum' && Number.isFinite(scene.pivotX) &&
       Number.isFinite(scene.pivotY) && scene.pivotX!>=0 && scene.pivotX!<1000 && scene.pivotY!>=0 && scene.pivotY!<1000));
}
export function verifiedSamSceneTracks(archive:AuthenticatedSamVideoArchive,parsed:SamVideoEvidence,
  sha:string):Record<string,SamPlaybackTrack> {
  if(!archive.receipt.scene)return {object:verifiedSamPlaybackTrack(archive,parsed,sha)};
  if(!validSamTarget(archive,sha) ||
     parsed.objectIds.some(id=>!archive.receipt.scene!.roles.some(r=>r.id===id)))
    throw new Error('SAM_SCENE_IDENTITY_MISMATCH');
  const result:Record<string,SamPlaybackTrack>={};
  for(const role of archive.receipt.scene.roles) {
    if(parsed.objectIds.includes(role.id))
      result[role.role]=verifiedSamPlaybackTrack(archive,parsed,sha,role.role,1);
  }
  return result;
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