import type {SamPlaybackMask,SamPlaybackTrack} from './sam-playback';

/** Image-space change detector, not proof of identity, occlusion or physical motion. */
export const SAM_TRACKING_QUALITY_PROTOCOL='SAM_MASK_CONTINUITY_ADVISORY_V1';
export const SAM_TRACKING_QUALITY_POLICY=Object.freeze({
  referenceWindowSeconds:.25,minimumReferenceSeconds:.15,minimumReferenceMasks:5,
  minimumAreaRatio:.5,maximumAreaRatio:2,maximumReferenceAreaRatio:1.6,
  confirmationSeconds:.12,minimumConfirmationMasks:3,
  minimumPositionResidualPixels:4,positionResidualObjectScale:.2,
  positionResidualNoiseMultiple:6,maximumCadenceMultiple:3,
  longMissingRunSeconds:.12
});
export interface SamTrackingChange {
  frameIndex:number;timeSeconds:number;
  kind:'ABRUPT_MASK_CHANGE';
  confirmation:'PERSISTENT'|'ENDS_BEFORE_CONFIRMATION';
  areaRatio:number;previousAreaPixels:number;currentAreaPixels:number;
  positionResidualPixels:number;positionResidualLimitPixels:number;
  observedPersistenceSeconds:number;confirmationMasks:number;
}
export interface SamMissingMaskRun {
  startFrame:number;endFrame:number;startSeconds:number;endSeconds:number;frames:number;
}
export interface SamTrackingReference {
  role:string;frame:number;x1:number;y1:number;x2:number;y2:number;
}
export interface SamTrackingReferenceCheck {
  frameIndex:number;timeSeconds:number|null;
  status:'OBSERVED_MASK'|'MISSING_MASK'|'INVALID_REFERENCE';
  maskBoxIou:number|null;maskBoxCoveredFraction:number|null;centroidInsideReference:boolean|null;
}
export interface SamTrackingAssessment {
  protocol:typeof SAM_TRACKING_QUALITY_PROTOCOL;
  status:'REVIEW_REQUIRED'|'NO_DISCONTINUITY_DETECTED'|'INSUFFICIENT_EVIDENCE';
  firstSuspiciousFrame:number|null;firstSuspiciousTimeSeconds:number|null;
  observedFrames:number;totalFrames:number;missingFrames:number;
  changes:SamTrackingChange[];missingRuns:SamMissingMaskRun[];referenceChecks:SamTrackingReferenceCheck[];
  reason:'MASK_CHANGE_OR_GAP_REQUIRES_REVIEW'|'NO_LARGE_CHANGE_DETECTED'|'TOO_FEW_MASKS'|'INVALID_INPUT';
  scope:'ADVISORY_ONLY_RAW_MASKS_RETAINED_NOT_OBJECT_IDENTITY_VERIFICATION';
}
const median=(values:number[])=>{
  const sorted=[...values].sort((a,b)=>a-b),i=Math.floor(sorted.length/2);
  return sorted.length%2?sorted[i]:(sorted[i-1]+sorted[i])/2;
};

/**
 * A size discontinuity alone can be rotation or blur. Require a simultaneous
 * unexpected centroid displacement and a sustained change of mask size.
 * Missing source frames are reported independently, never joined/interpolated.
 * This function never changes masks, centroids, period inputs or saved evidence.
 */
export function assessSamTracking(track:SamPlaybackTrack|null,framePtsSeconds:number[],
  reviewedSeeds:readonly SamTrackingReference[]=[]):SamTrackingAssessment {
  const policy=SAM_TRACKING_QUALITY_POLICY,times=framePtsSeconds,masks=track?.masks??[];
  const result:SamTrackingAssessment={protocol:SAM_TRACKING_QUALITY_PROTOCOL,
    status:'INSUFFICIENT_EVIDENCE',firstSuspiciousFrame:null,firstSuspiciousTimeSeconds:null,
    observedFrames:masks.length,totalFrames:times.length,missingFrames:times.length,
    changes:[],missingRuns:[],referenceChecks:[],reason:'TOO_FEW_MASKS',
    scope:'ADVISORY_ONLY_RAW_MASKS_RETAINED_NOT_OBJECT_IDENTITY_VERIFICATION'};
  if(times.length<2 || times.length>15000 || times.some((t,i)=>!Number.isFinite(t)||t<0||i>0&&t<=times[i-1]) ||
      masks.some((m,i)=>!Number.isSafeInteger(m.frameIndex)||m.frameIndex<0||m.frameIndex>=times.length||
        i>0&&m.frameIndex<=masks[i-1].frameIndex||!Number.isFinite(m.timeSeconds)||
        Math.abs(m.timeSeconds-times[m.frameIndex])>1e-6||!Number.isFinite(m.foregroundPixels)||m.foregroundPixels<=0||
        !Number.isFinite(m.centroidXpx)||!Number.isFinite(m.centroidYpx))) {
    result.reason='INVALID_INPUT';return result;
  }
  const dt=median(times.slice(1).map((t,i)=>t-times[i])),byFrame=new Set(masks.map(m=>m.frameIndex));
  const maskByFrame=new Map(masks.map(m=>[m.frameIndex,m]));
  // Reviewed model boxes remain estimates. Compare only the exact original
  // frame: overlap is corroboration, never independent identity verification.
  for(const seed of reviewedSeeds.filter(s=>s.role==='object')) {
    const check:SamTrackingReferenceCheck={frameIndex:seed.frame,timeSeconds:times[seed.frame]??null,
      status:'INVALID_REFERENCE',maskBoxIou:null,maskBoxCoveredFraction:null,centroidInsideReference:null};
    result.referenceChecks.push(check);
    if(!Number.isSafeInteger(seed.frame)||seed.frame<0||seed.frame>=times.length||
       [seed.x1,seed.y1,seed.x2,seed.y2].some(v=>!Number.isFinite(v)||v<0)||
       seed.x2<=seed.x1||seed.y2<=seed.y1||track&&(seed.x2>track.frameWidth||seed.y2>track.frameHeight))continue;
    const mask=maskByFrame.get(seed.frame);
    if(!mask){check.status='MISSING_MASK';continue;}
    const maskArea=(mask.right-mask.left)*(mask.bottom-mask.top);
    if(!Number.isFinite(maskArea)||maskArea<=0)continue;
    const overlap=Math.max(0,Math.min(seed.x2,mask.right)-Math.max(seed.x1,mask.left))*
      Math.max(0,Math.min(seed.y2,mask.bottom)-Math.max(seed.y1,mask.top));
    const referenceArea=(seed.x2-seed.x1)*(seed.y2-seed.y1);
    check.status='OBSERVED_MASK';check.maskBoxIou=overlap/(maskArea+referenceArea-overlap);
    check.maskBoxCoveredFraction=overlap/maskArea;
    check.centroidInsideReference=mask.centroidXpx>=seed.x1&&mask.centroidXpx<=seed.x2&&
      mask.centroidYpx>=seed.y1&&mask.centroidYpx<=seed.y2;
  }
  result.missingFrames=times.length-byFrame.size;
  let missingStart=-1;
  for(let i=0;i<=times.length;i++) {
    if(i<times.length&&!byFrame.has(i)){if(missingStart<0)missingStart=i;}
    else if(missingStart>=0){
      result.missingRuns.push({startFrame:missingStart,endFrame:i-1,startSeconds:times[missingStart],
        endSeconds:i<times.length?times[i]:times[times.length-1]+dt,frames:i-missingStart});
      missingStart=-1;
    }
  }
  if(masks.length<policy.minimumReferenceMasks+policy.minimumConfirmationMasks)return result;
  const contiguous=(a:SamPlaybackMask,b:SamPlaybackMask)=>b.frameIndex===a.frameIndex+1&&
    b.timeSeconds-a.timeSeconds<=dt*policy.maximumCadenceMultiple;
  let suppressUntil=-1;
  for(let i=1;i<masks.length;i++) {
    const current=masks[i],previous=masks[i-1];
    if(current.timeSeconds<suppressUntil || !contiguous(previous,current))continue;
    let start=i-1;
    const referenceWindow=Math.max(policy.referenceWindowSeconds,dt*(policy.minimumReferenceMasks+1));
    while(start>0&&current.timeSeconds-masks[start-1].timeSeconds<=referenceWindow&&
          contiguous(masks[start-1],masks[start]))start--;
    const history=masks.slice(start,i);
    if(history.length<policy.minimumReferenceMasks || previous.timeSeconds-history[0].timeSeconds<policy.minimumReferenceSeconds)continue;
    const areas=history.map(m=>m.foregroundPixels),typicalArea=median(areas);
    // A rapidly varying silhouette provides no stable size baseline.
    if(Math.max(...areas)/Math.min(...areas)>policy.maximumReferenceAreaRatio)continue;
    const ratio=current.foregroundPixels/typicalArea,adjacentRatio=current.foregroundPixels/previous.foregroundPixels;
    const small=ratio<policy.minimumAreaRatio&&adjacentRatio<policy.minimumAreaRatio;
    const large=ratio>policy.maximumAreaRatio&&adjacentRatio>policy.maximumAreaRatio;
    if(!small&&!large)continue;
    const velocities=history.slice(1).map((m,j)=>{
      const before=history[j],elapsed=m.timeSeconds-before.timeSeconds;
      return {x:(m.centroidXpx-before.centroidXpx)/elapsed,y:(m.centroidYpx-before.centroidYpx)/elapsed,elapsed};
    });
    const vx=median(velocities.map(v=>v.x)),vy=median(velocities.map(v=>v.y));
    const noise=median(velocities.map(v=>Math.hypot(v.x-vx,v.y-vy)*v.elapsed));
    const elapsed=current.timeSeconds-previous.timeSeconds;
    const residual=Math.hypot(current.centroidXpx-previous.centroidXpx-vx*elapsed,
      current.centroidYpx-previous.centroidYpx-vy*elapsed);
    const limit=Math.max(policy.minimumPositionResidualPixels,Math.sqrt(typicalArea)*policy.positionResidualObjectScale,
      noise*policy.positionResidualNoiseMultiple);
    if(residual<=limit)continue;
    let last=i;
    while(last+1<masks.length&&masks[last].timeSeconds-current.timeSeconds<policy.confirmationSeconds&&
          contiguous(masks[last],masks[last+1]))last++;
    const following=masks.slice(i,last+1);
    const changed=(m:SamPlaybackMask)=>small?m.foregroundPixels/typicalArea<policy.minimumAreaRatio:
      m.foregroundPixels/typicalArea>policy.maximumAreaRatio;
    if(following.length<policy.minimumConfirmationMasks||!following.every(changed))continue;
    const persistence=following[following.length-1].timeSeconds-current.timeSeconds;
    const complete=persistence>=policy.confirmationSeconds;
    const reachedEnd=following[following.length-1].frameIndex===times.length-1;
    if(!complete&&!reachedEnd)continue; // An occlusion is reported as a gap, not a confirmed change.
    result.changes.push({frameIndex:current.frameIndex,timeSeconds:current.timeSeconds,kind:'ABRUPT_MASK_CHANGE',
      confirmation:complete?'PERSISTENT':'ENDS_BEFORE_CONFIRMATION',areaRatio:ratio,
      previousAreaPixels:typicalArea,currentAreaPixels:current.foregroundPixels,
      positionResidualPixels:residual,positionResidualLimitPixels:limit,
      observedPersistenceSeconds:persistence,confirmationMasks:following.length});
    suppressUntil=current.timeSeconds+policy.referenceWindowSeconds;
  }
  const change=result.changes[0],gap=result.missingRuns.find(g=>g.endSeconds-g.startSeconds>=policy.longMissingRunSeconds);
  const first=change&&(!gap||change.timeSeconds<=gap.startSeconds)?{frame:change.frameIndex,time:change.timeSeconds}:
    gap?{frame:gap.startFrame,time:gap.startSeconds}:null;
  result.status=first?'REVIEW_REQUIRED':'NO_DISCONTINUITY_DETECTED';
  result.reason=first?'MASK_CHANGE_OR_GAP_REQUIRES_REVIEW':'NO_LARGE_CHANGE_DETECTED';
  result.firstSuspiciousFrame=first?.frame??null;result.firstSuspiciousTimeSeconds=first?.time??null;
  return result;
}
