import {resolveSamSuspension,automaticPivotProvenance} from '../ui/src/app/sam-suspension';
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {decodeSamVideoResponse} from '../ui/src/app/sam-video-evidence';
import {verifiedSamSceneTracks, validSamTarget} from '../ui/src/app/sam-playback';
import {periodReportPayload} from '../ui/src/app/sam-period';

async function main() {
  // Trusted local worker. No network, token, model code or user-provided file paths.
  const wire=readFileSync(0,'utf8');
  if(Buffer.byteLength(wire)>32_000_000)throw Error('INPUT_LIMIT');
  const input=JSON.parse(wire),archive=input.archive,receipt=archive.receipt;
  if(createHash('sha256').update(archive.rawResponseJson).digest('hex')!==receipt.responseJsonSha256)
    throw Error('PROVIDER_RESPONSE_HASH_MISMATCH');
  const decoded=await decodeSamVideoResponse(archive.rawResponseJson);
  // A completed empty SAM response is evidence of a detection failure, not
  // proof of another physical phenomenon. Keep it available for Muse to audit.
  if(decoded.result==='NO_MATCH' && (
    archive.state.sourceSha256!==input.videoSha256 ||
    receipt.originalVideoSha256!==input.videoSha256 ||
    archive.state.status!=='CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW' ||
    receipt.providerEvidenceStatus!=='COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW' ||
    receipt.model!=='sam-3.1' || !validSamTarget(archive,input.videoSha256) ||
    !Array.isArray(receipt.framePtsSeconds) || receipt.sourceFrameCount<50 ||
    receipt.sourceFrameCount>2500 || receipt.sourceFrameCount!==receipt.framePtsSeconds.length ||
    receipt.framePtsSeconds.some((t:number,i:number)=>!Number.isFinite(t)||t<0||
      (i>0&&t<=receipt.framePtsSeconds[i-1]))))
    throw Error('SAM_NO_MATCH_PROVENANCE_INVALID');
  const track=decoded.result==='NO_MATCH'?undefined:
    verifiedSamSceneTracks(archive,decoded,input.videoSha256).object;
  const width=track?.frameWidth??input.sourceWidth,height=track?.frameHeight??input.sourceHeight;
  if(!Number.isFinite(width)||!Number.isFinite(height)||width<1||height<1)
    throw Error('SOURCE_DIMENSIONS_MISSING');
  const automatic=!input.pivot&&track?resolveSamSuspension(archive,track):null;
  const pivot=input.pivot??(automatic?.pivot?{x:automatic.pivot.pivotXpx/480,y:automatic.pivot.pivotYpx/270}:null);
  const provenance=input.pivot?input.pivotProvenance:automatic?.pivot?automaticPivotProvenance:'PIVOT_MISSING';
  const measurement={
    framePtsSeconds:receipt.framePtsSeconds,
    centroids:track?.masks.map(m=>({frameIndex:m.frameIndex,x:m.centroidXpx,y:m.centroidYpx}))??[],
    sourceWidth:width,sourceHeight:height,imageWidth:width,imageHeight:height,
    pivot,arrangement:receipt.scene?.arrangement??'legacy_pendulum'
  };
  const source={
    studyId:input.studyId,role:'baseline',originalVideoSha256:input.videoSha256,
    analysisId:receipt.analysisId,providerResponseSha256:receipt.responseJsonSha256,
    providerModel:'sam-3.1' as const,objectId:track?.objectId??'NO_MATCH',
    pivotRevisionId:input.pivotRevisionId,pivotProvenance:provenance
  };
  process.stdout.write(JSON.stringify(periodReportPayload(source,measurement)));
}
main().catch(()=>{process.stderr.write('SAM_PERIOD_WORKER_FAILED');process.exitCode=1;});