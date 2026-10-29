import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {decodeSamVideoResponse} from '../ui/src/app/sam-video-evidence';
import {verifiedSamSceneTracks} from '../ui/src/app/sam-playback';
import {periodReportPayload} from '../ui/src/app/sam-period';

async function main() {
  // Trusted local worker. No network, token, model code or user-provided file paths.
  const wire=readFileSync(0,'utf8');
  if(Buffer.byteLength(wire)>32_000_000)throw Error('INPUT_LIMIT');
  const input=JSON.parse(wire),archive=input.archive;
  if(createHash('sha256').update(archive.rawResponseJson).digest('hex')!==archive.receipt.responseJsonSha256)
    throw Error('PROVIDER_RESPONSE_HASH_MISMATCH');
  const decoded=await decodeSamVideoResponse(archive.rawResponseJson);
  const track=verifiedSamSceneTracks(archive,decoded,input.videoSha256).object;
  if(!track)throw Error('SAM_OBJECT_TRACK_MISSING');
  const measurement={
    framePtsSeconds:archive.receipt.framePtsSeconds,
    centroids:track.masks.map(m=>({frameIndex:m.frameIndex,x:m.centroidXpx,y:m.centroidYpx})),
    sourceWidth:track.frameWidth,sourceHeight:track.frameHeight,
    imageWidth:track.frameWidth,imageHeight:track.frameHeight,
    pivot:input.pivot,arrangement:archive.receipt.scene?.arrangement??'legacy_pendulum'
  };
  const source={
    studyId:input.studyId,role:'baseline',originalVideoSha256:input.videoSha256,
    analysisId:archive.receipt.analysisId,providerResponseSha256:archive.receipt.responseJsonSha256,
    providerModel:'sam-3.1' as const,objectId:track.objectId,
    pivotRevisionId:input.pivotRevisionId,pivotProvenance:input.pivotProvenance
  };
  process.stdout.write(JSON.stringify(periodReportPayload(source,measurement)));
}
main().catch(()=>{process.stderr.write('SAM_PERIOD_WORKER_FAILED');process.exitCode=1;});
