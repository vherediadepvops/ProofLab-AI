// Local integration test. Isolated storage and disabled provider transport;
// never modifies the owner's API permissions or uses Meta credentials.
import fs from 'node:fs/promises';import path from 'node:path';import {spawn} from 'node:child_process';import assert from 'node:assert/strict';
const binary=path.resolve(process.argv[2]),port=Number(process.argv[3]||5494);
const fixture=path.resolve('.private','recovery-draft-tests',crypto.randomUUID());
await fs.mkdir(fixture,{recursive:true});
const env={...process.env};for(const key of Object.keys(env))if(/META|MODEL_API_KEY/i.test(key))delete env[key];
const child=spawn('dotnet',[binary,'--urls',`http://127.0.0.1:${port}`,'--contentRoot',fixture,'--Meta:AllowPaidRequests','false','--Meta:CreditsConfirmedByOwner','false','--Meta:OwnerApprovedPaidRequests','false'],{env,windowsHide:true,stdio:'pipe'});
let output='';child.stdout.on('data',d=>output+=d);child.stderr.on('data',d=>output+=d);
const base=`http://127.0.0.1:${port}`,api=base+'/api/science/director/pendulum/sessions';
const request=async(url,method='GET',body)=>fetch(url,{method,headers:{'Content-Type':'application/json'},...(body?{body:JSON.stringify(body)}:{})});
try{
  let started=false;for(let i=0;i<80;i++){if(child.exitCode!==null)throw Error(output);if(output.includes('Now listening on:')){started=true;break;}await new Promise(r=>setTimeout(r,100));}assert(started,'API did not start');
  const before=await(await request(base+'/api/meta/readiness')).json();assert.equal(before.paidRequestsSwitchEnabled,false);
  const input={baselineLengthMeters:.5,angleDegrees:15,massGrams:300,includeSecondTrial:false,followUpMode:'recommended',selectedFollowUpLengthMeters:null};
  const created=await(await request(api,'POST',input)).json();assert.match(created.id,/^[a-f0-9]{32}$/);
  const changed={...input,baselineLengthMeters:.8,massGrams:400};
  const updated=await(await request(api+'/'+created.id+'/draft','PUT',changed)).json();assert.equal(updated.id,created.id);assert.equal(updated.baselineLengthMeters,.8);assert.notEqual(updated.modelSha256,created.modelSha256);
  const restored=await(await request(api+'/'+created.id+'/recovery')).json();assert.equal(restored.session.baselineLengthMeters,.8);assert.equal(restored.session.massGrams,400);assert.equal(restored.operations.length,0);
  assert.equal((await request(api+'/'+created.id+'/draft','PUT',{...changed,baselineLengthMeters:-1})).status,400);
  assert.equal((await request(api+'/'+'a'.repeat(32)+'/recovery')).status,404);
  assert.equal((await request(api+'/'+created.id)).status,200);
  const synthetic={role:'baseline',videoId:'b'.repeat(32),originalFilename:'synthetic.mp4',bytes:12000,sha256:'c'.repeat(64),uploadedAtUtc:new Date().toISOString(),media:{codec:'h264',width:320,height:240,durationSeconds:1,nominalFramesPerSecond:30,containerFormat:'mp4'},tracking:{framesAnalyzed:0,detectedFrames:0,detectionRate:0,observations:[],pixelSamples:[],outcome:'SYNTHETIC_TEST',method:'SYNTHETIC',limitations:'SYNTHETIC'},spectral:{periodSeconds:null,fitRSquared:0,status:'SYNTHETIC'},posterAvailable:false,previewAvailable:false};
  await fs.writeFile(path.join(fixture,'.private','physics','director',created.id,'baseline.json'),JSON.stringify(synthetic));
  assert.equal((await request(api+'/'+created.id+'/draft','PUT',input)).status,409);
  const unchanged=await(await request(api+'/'+created.id)).json();assert.equal(unchanged.baselineLengthMeters,.8);assert.equal(unchanged.modelSha256,updated.modelSha256);
  const after=await(await request(base+'/api/meta/readiness')).json();assert.equal(after.usedRequests,before.usedRequests);
  console.log(JSON.stringify({checks:7,createdAndUpdatedSameCode:true,recoveredDraft:true,invalidInputRejected:true,missingSession404:true,registeredMediaLocksParameters:true,integrityPreserved:true,metaCalls:0,fixture}));
} finally {child.kill();}
