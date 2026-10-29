#!/usr/bin/env node
// Decode a PREVIOUSLY VERIFIED full-video SAM response with the official
// @meta-sam/parser. Offline only: no network, no credentials, no provider calls.
// Store only geometry and frame IDs in an ignored private test file.
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createRequire} from 'node:module';
import {dirname,resolve,join,sep} from 'node:path';
import {pathToFileURL} from 'node:url';

const [responsePath,outputPath]=process.argv.slice(2);
if(!responsePath||!outputPath){
  process.stderr.write('Usage: node scripts/sam-parallel4-reference.mjs <private-response.json> <private-geometry.json>\n');
  process.exit(2);
}
const output=resolve(outputPath);
if(!output.split(sep).includes('.private')){
  process.stderr.write('Refuse to write reference geometry outside .private\n');
  process.exit(2);
}
const requireFromUi=createRequire(resolve('ui/package.json'));
const esbuild=requireFromUi('esbuild');
await mkdir(dirname(output),{recursive:true});
const imported=join(dirname(output),'sam-evidence-offline-bundle.mjs');
await esbuild.build({
 entryPoints:[resolve('ui/src/app/sam-video-evidence.ts')],
 outfile:imported,
 bundle:true,platform:'node',format:'esm',
 target:'es2022',logLevel:'silent'
});
const {decodeSamVideoResponse}=await import(pathToFileURL(imported).href);
const raw=await readFile(resolve(responsePath),'utf8');
const evidence=await decodeSamVideoResponse(raw);
if(evidence.result!=='DECODED_MASKS' || evidence.warningCount!==0)
  throw new Error('Full reference contains no valid masks or parser warnings.');
const rows=[];
for(const frame of evidence.frames){
 for(const detection of frame.detections){
   if(!Number.isFinite(detection.centroidXpx)||
      !Number.isFinite(detection.centroidYpx)) continue;
   rows.push({
      frameIndex:frame.frameIndex,
      objectId:detection.objectId,
      centroidX:detection.centroidXpx,centroidY:detection.centroidYpx,
      left:detection.left,top:detection.top,
      right:detection.right,bottom:detection.bottom,
      foregroundPixels:detection.foregroundPixels
   });
 }
}
if(rows.length===0) throw new Error('No usable mask centroids in reference.');
await writeFile(output,JSON.stringify({
  provenance:'PREVIOUS_COMPLETE_SAM_REFERENCE_OFFLINE_DECODED',
  sourceModel:'sam-3.1',
  sourceFrameCount:1828,
  frameWidth:evidence.frameWidth,frameHeight:evidence.frameHeight,
  framesWithMasks:evidence.frames.length,
  detectionCount:evidence.detectionCount,
  warnings:evidence.warningCount,
  rows
},null,2)+'\n',{flag:'wx'});
process.stdout.write('OFFLINE_REFERENCE_DECODED frames='+evidence.frames.length+
 ' detections='+evidence.detectionCount+' warnings='+evidence.warningCount+
 ' firstFrame='+rows[0].frameIndex+' lastFrame='+rows.at(-1).frameIndex+
 ' NO_PROVIDER_CALLS=true\n');
