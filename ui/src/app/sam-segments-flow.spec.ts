import {TestBed} from '@angular/core/testing';
import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController,provideHttpClientTesting} from '@angular/common/http/testing';
import {SamVideoReplay} from './sam-video-replay';
import {LabConnection} from './lab-connection';
const sha='a'.repeat(64),id='b'.repeat(32),key='c'.repeat(64),scene='d'.repeat(64),batch='e'.repeat(32),historical='8'.repeat(32);
const root='http://127.0.0.1:5391/api/science/director/pendulum/sessions/'+id,url=root+'/sam-adaptive/baseline';
const quote={mode:'api-meta-cache',sourceSha256:sha,sparkSceneKey:'',sourcePtsSha256:'f'.repeat(64),
 planSha256:key,suggestedBatchId:batch,sourceFrames:667,parts:1,simultaneousRequests:1,
 submittedFrames:667,billableFrames:667,totalSamEstimatedUsd:.1334,maximumAllowedSamUsd:.50,
 continuousRetryEstimatedUsd:0,maximumSamWithRetryEstimatedUsd:.1334,remainingTrialSlots:null,canRunWithManualToken:true,
 existingSparkSceneVerified:false,requiresNewSparkCall:true,idealPartsWithoutSparkCoverage:1,
 verifiedSparkSeedCount:0,reducedForSparseSparkSeeds:false,sceneRunId:null};
const exact={...quote,sparkSceneKey:scene,existingSparkSceneVerified:true,requiresNewSparkCall:false,verifiedSparkSeedCount:3};
const spark={mode:'api-meta-cache',cacheKey:key,videoSha256:sha,maximumEstimatedCostUsd:.25,
 freshRequired:false,protocol:'ASSEMBLY_FULL_VIDEO_GROUNDING_V2',cacheHit:false,canRun:true,priorAttempt:false,status:'NEEDS_LOCALIZATION',scene:null};
const located={...spark,status:'READY',canRun:false,scene:{key:scene,videoSha256:sha}};
const access={manualTokenCallsPermitted:true,paidRequestsSwitchEnabled:true,creditsConfirmedByOwner:true,
 ownerApprovedPaidRequests:true,remainingRequests:0,usedRequests:43,manualRequestsUnlimited:true};
const media={sourceSha256:sha,sourceFrames:667,durationSeconds:22.23,estimatedCostUsd:.1334,supportsSingleCall:true,codec:'h264'};
const done={batchId:batch,parts:1,startedParts:1,completedParts:1,failedParts:0,skippedParts:0,
 sourceSha256:sha,sparkSceneKey:scene,planSha256:key,estimatedSamUsd:.1334,
 status:'PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW',derivedSeamsVerified:true,derivedFramesWithMasks:667};
const completedProgress={batchId:batch,parts:1,started:1,completed:1,failed:0,phase:'COMPLETED',
 preparationElapsedMs:1200,validationElapsedMs:300,totalElapsedMs:12500,
 perPart:[{part:0,state:'COMPLETED',events:50,deltas:45,outputChars:1000,elapsedMs:11000}]};
async function tick(){for(let i=0;i<12;i++)await Promise.resolve();}
describe('One complete-video SAM request and archived-result recovery',()=>{
 beforeEach(async()=>{await TestBed.configureTestingModule({imports:[SamVideoReplay],
 providers:[provideHttpClient(),provideHttpClientTesting()]}).compileComponents();});
 afterEach(()=>TestBed.inject(HttpTestingController).verify());
 function setup(){
  const f=TestBed.createComponent(SamVideoReplay);
  f.componentRef.setInput('autoCheck',false);f.componentRef.setInput('studyId',id);
  f.componentRef.setInput('videoSha256',sha);f.detectChanges();
  const c=f.componentInstance;c.mode.set('api-meta-cache');
  spyOn<any>(c,'pollSpark').and.callFake(async()=>{c.sparkStages.set(['prepare','references','upload','inference','verify','target-review'].map(key=>({key,state:'COMPLETED',elapsedMs:100,percent:100})));});
  const h=TestBed.inject(HttpTestingController),con=TestBed.inject(LabConnection);
  const field=document.createElement('input');field.type='password';con.register(field);
  field.value='LLM_SYNTHETIC_LOCAL_TEST_KEY_1234567890';con.confirmToken();
  return {f,c,h,con};
 }
 type Context=ReturnType<typeof setup>;
 async function check(x:Context,q:any=quote,g:any=spark,m:any=media){
  const p=x.c.check();
  x.h.expectOne(root+'/sam/baseline/reusable').flush({sourceSha256:sha,cacheHit:false,analysisId:null,adaptiveBatchId:null,excludedResults:1,quality:null,storage:'sqlite'});
  x.h.expectOne(root+'/sam/baseline/preflight').flush(m);await tick();
  const all=x.h.match(()=>true);expect(all.length).toBe(3);
  for(const r of all){expect(r.request.method).toBe('GET');
   if(r.request.url===url+'/preflight?mode=api-meta-cache')r.flush(q);
   else if(r.request.url===url+'/spark-quote?mode=api-meta-cache')r.flush(g);
   else if(r.request.url.endsWith('/readiness'))r.flush(access);
   else throw Error('Unexpected quote request '+r.request.url);}
  await p;x.f.detectChanges();
 }
 async function finalProgress(x:Context,failed=false){
  await tick();x.h.expectOne(url+'/batches/'+batch+'/progress').flush(failed?
   {...completedProgress,phase:'FAILED',completed:0,failed:1,perPart:[{part:0,state:'FAILED',elapsedMs:11000}]}:completedProgress);
 }
 function unlock(x:Context){x.f.componentRef.setInput('validationState','ready');x.f.detectChanges();}
 async function recoverConflict(x:Context,report:any=done){
  const post=x.h.expectOne(url+'/manual-batch');expect(post.request.method).toBe('POST');
  post.flush({error:'Final response unavailable'},{status:409,statusText:'Conflict'});await tick();
  x.h.expectOne(url+'/batches/'+batch+'/progress').flush(completedProgress);await tick();
  x.h.expectOne(url+'/batches/'+batch+'/report').flush(report);await tick();
 }

 it('reuses historical native and segmented evidence for free using GET only',async()=>{
  for(const adaptiveBatchId of [null,historical]){
   const x=setup();x.con.clearToken();const p=x.c.check();
   x.h.expectOne(root+'/sam/baseline/reusable').flush({sourceSha256:sha,cacheHit:true,analysisId:historical,adaptiveBatchId,
    excludedResults:2,quality:{usable:true,reason:'MEASUREMENTS_AVAILABLE'},storage:'sqlite'});
   x.h.expectOne(root+'/sam/baseline/preflight').flush(media);await p;
   expect(x.c.displayedCost()).toBe(0);expect(x.c.canProcess()).toBeTrue();
   const emit=spyOn(x.c.cacheUpdated,'emit');await x.c.process();
   expect(emit).toHaveBeenCalledOnceWith(jasmine.objectContaining({analysisId:historical,adaptiveBatchId:adaptiveBatchId??undefined}));
   x.h.expectNone(()=>true);x.f.destroy();
  }
 });
 it('does not reuse a quality verdict from a different video',async()=>{
  const x=setup(),p=x.c.check();
  x.h.expectOne(root+'/sam/baseline/reusable').flush({sourceSha256:'f'.repeat(64),cacheHit:true,analysisId:batch,quality:{usable:true}});
  x.h.expectOne(root+'/sam/baseline/preflight').flush(media);await p;
  expect(x.c.canProcess()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('keeps processing blocked if the cache check fails instead of silently billing',async()=>{
  const x=setup(),p=x.c.check();
  x.h.expectOne(root+'/sam/baseline/reusable').flush({}, {status:409,statusText:'Conflict'});
  x.h.expectOne(root+'/sam/baseline/preflight').flush(media);await p;
  expect(x.c.canProcess()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('reads byte progress without a provider POST and ignores stale runs',async()=>{
  const x=setup();await check(x);(x.c as any).startStageTrace();(x.c as any).pollSpark.and.callThrough();
  const current=(x.c as any).pollSpark();
  x.h.expectOne(root+'/assembly/baseline/grounding/progress').flush({startedAtUtc:new Date().toISOString(),
   stages:[{key:'upload',state:'IN_PROGRESS',elapsedMs:1000,percent:43}]});await current;
  expect(x.c.stageRows().find(r=>r.key==='spark-upload')?.percent).toBe(43);
  const stale=(x.c as any).pollSpark();
  x.h.expectOne(root+'/assembly/baseline/grounding/progress').flush({startedAtUtc:'2000-01-01T00:00:00Z',stages:[]});await stale;
  expect(x.c.sparkStages().length).toBe(1);x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('quotes one whole video and starts Spark then one SAM only after the click',async()=>{
  const x=setup();await check(x);
  expect(x.c.localApiPort()).toBe(5391);expect(x.c.canProcess()).toBeTrue();
  expect(x.c.displayedCost()).toBeCloseTo(.3834);x.h.expectNone(r=>r.method==='POST');
  const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process(),locate=x.h.expectOne(url+'/spark-once');
  expect(locate.request.body.maximumEstimatedCostUsd).toBe(.25);expect(locate.request.body.confirmVideoTransfer).toBeTrue();
  await x.c.process();x.h.expectNone(r=>r.method==='POST');locate.flush(located);await tick();
  x.h.expectOne(url+'/preflight?mode=api-meta-cache').flush(exact);await tick();
  const sam=x.h.expectOne(url+'/manual-batch');
  expect(sam.request.body.maximumSamCostUsd).toBe(.1334);expect(sam.request.body.maxInFlight).toBe(1);
  expect(sam.request.body.targetCoreFrames).toBeUndefined();expect(sam.request.body.confirmOneContinuousRetry).toBeFalse();
  sam.flush(done);await finalProgress(x);await p;
  expect(emit).toHaveBeenCalledOnceWith(jasmine.objectContaining({adaptiveBatchId:batch,analysisId:batch,port:5391}));
  expect(x.c.processing()).toBeTrue();unlock(x);expect(x.c.processing()).toBeFalse();expect(x.con.busy()).toBeFalse();
  const rows=x.c.stageRows();
  expect(rows.filter(r=>r.key.startsWith('sam-')).length).toBe(1);
  expect(rows.find(r=>r.key==='preparation')?.ms).toBe(1200);expect(rows.find(r=>r.key==='validation')?.ms).toBe(300);
  expect(rows.every(r=>r.state==='COMPLETED')).toBeTrue();
  expect(x.c.canProcess()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('reuses a verified scene without Spark or an independent trial quota',async()=>{
  const x=setup();await check(x,exact,located);expect(x.c.displayedCost()).toBe(.1334);expect(x.c.canProcess()).toBeTrue();
  const p=x.c.process();x.h.expectNone(url+'/spark-once');x.h.expectOne(url+'/manual-batch').flush(done);
  await finalProgress(x);await p;unlock(x);x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('stops before SAM on inconclusive Spark and does not retry',async()=>{
  const x=setup();await check(x);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  x.h.expectOne(url+'/spark-once').flush({...spark,status:'AMBIGUOUS'});await p;
  expect(emit).not.toHaveBeenCalled();expect(x.c.processing()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('stops before SAM if the exact quote exceeds the approved estimate',async()=>{
  const x=setup();await check(x);const p=x.c.process();x.h.expectOne(url+'/spark-once').flush(located);await tick();
  x.h.expectOne(url+'/preflight?mode=api-meta-cache').flush({...exact,totalSamEstimatedUsd:.14,maximumSamWithRetryEstimatedUsd:.14});
  await p;expect(x.c.error()).toContain('actualiza el costo');x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('does not publish an incomplete full-video result',async()=>{
  const x=setup();await check(x,exact,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  x.h.expectOne(url+'/manual-batch').flush({...done,derivedSeamsVerified:false});await finalProgress(x,true);await p;
  expect(emit).not.toHaveBeenCalled();expect(x.c.processing()).toBeFalse();x.f.destroy();
 });
 it('discards a Spark reply after changing the selected video',async()=>{
  const x=setup();await check(x);const p=x.c.process(),r=x.h.expectOne(url+'/spark-once');
  x.f.componentRef.setInput('videoSha256','f'.repeat(64));x.f.detectChanges();r.flush(located);await p;
  x.h.expectNone(req=>req.method==='POST'||req.url.includes('/preflight?'));x.f.destroy();
 });
 it('rejects former multi-part or concurrent quotes before submission',async()=>{
  for(const change of [{parts:3},{simultaneousRequests:3}]){const x=setup();await check(x,{...exact,...change},located);
   expect(x.c.canProcess()).toBeFalse();await x.c.process();x.h.expectNone(r=>r.method==='POST');x.f.destroy();}
 });
 it('requires a click for manual historical recovery and sends one complete video using saved Spark',async()=>{
  const x=setup();await check(x,{...exact,recoveryFromBatchId:historical},located);
  expect(x.c.displayedCost()).toBe(.1334);x.h.expectNone(r=>r.method==='POST');
  const p=x.c.process(),r=x.h.expectOne(url+'/manual-batch');
  expect(r.request.body.recoveryFromBatchId).toBe(historical);expect(r.request.body.maxInFlight).toBe(1);
  expect(r.request.body.confirmOneContinuousRetry).toBeFalse();x.h.expectNone(url+'/spark-once');
  r.flush(done);await finalProgress(x);await p;unlock(x);x.f.destroy();
 });
 it('keeps Process disabled for invalid or missing cost',async()=>{
  for(const cost of [0,-1,NaN,Infinity,.5001]){const x=setup();
   await check(x,{...exact,totalSamEstimatedUsd:cost,maximumSamWithRetryEstimatedUsd:cost},located);
   expect(x.c.canProcess()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();}
 });
 it('rejects automatic-retry budgets instead of authorizing a second SAM',async()=>{
  const x=setup();await check(x,{...exact,continuousRetryEstimatedUsd:.1334,maximumSamWithRetryEstimatedUsd:.2668},located);
  expect(x.c.canProcess()).toBeFalse();await x.c.process();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('shows only the single SAM and validation timings after success',async()=>{
  const x=setup();await check(x,exact,located);const p=x.c.process();
  x.h.expectOne(url+'/manual-batch').flush(done);await finalProgress(x);await p;unlock(x);
  const rows=x.c.stageRows();expect(rows.find(r=>r.key==='sam-0')).toEqual(jasmine.objectContaining({state:'COMPLETED',ms:11000}));
  expect(rows.find(r=>r.key==='validation')).toEqual(jasmine.objectContaining({state:'COMPLETED',ms:300}));
  expect(rows.some(r=>r.key==='join'||r.key.startsWith('retry-'))).toBeFalse();x.f.destroy();
 });
 it('ends an unsuccessful full-video request without a fallback or invented overlays',async()=>{
  const x=setup();await check(x,exact,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  x.h.expectOne(url+'/manual-batch').flush({...done,completedParts:0,failedParts:1,
   status:'PARTIAL_PROVIDER_ATTEMPTS_REQUIRE_MANUAL_RECONCILIATION',derivedSeamsVerified:null,derivedFramesWithMasks:null});
  await finalProgress(x,true);await p;expect(emit).not.toHaveBeenCalled();expect(x.c.processing()).toBeFalse();
  expect(x.c.stageRows().find(r=>r.key==='view')?.state).toBe('FAILED');
  expect(x.c.stageRows().some(r=>r.key.startsWith('retry-'))).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('rejects a result attributed to a different request',async()=>{
  const x=setup();await check(x,exact,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  x.h.expectOne(url+'/manual-batch').flush({...done,batchId:historical});await p;
  expect(emit).not.toHaveBeenCalled();expect(x.c.error()).toContain('no coincide');x.h.expectNone(()=>true);x.f.destroy();
 });
 it('rejects truncated or overlap-inflated frame quotes before any provider request',async()=>{
  for(const change of [{submittedFrames:666},{submittedFrames:680,billableFrames:680},{billableFrames:668}]){
   const x=setup();await check(x,{...exact,...change},located);expect(x.c.canProcess()).toBeFalse();
   await x.c.process();x.h.expectNone(r=>r.method==='POST');x.f.destroy();}
 });
 it('recovers the archived single result after a final 409 using GET without resending',async()=>{
  const x=setup();await check(x,exact,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  await recoverConflict(x);await finalProgress(x);await p;
  expect(emit).toHaveBeenCalledOnceWith(jasmine.objectContaining({analysisId:batch,adaptiveBatchId:batch,sourceSha256:sha}));
  expect(x.c.error()).toBe('');unlock(x);expect(x.con.busy()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('ends recovery inconclusively when the archived report has no masks despite green progress',async()=>{
  const x=setup();await check(x,exact,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  await recoverConflict(x,{...done,status:'PROVIDER_PARTS_COMPLETE_SEAM_OR_DETECTION_INCONCLUSIVE',derivedFramesWithMasks:0});
  await finalProgress(x);await p;expect(emit).not.toHaveBeenCalled();expect(x.c.processing()).toBeFalse();
  expect(x.c.stageRows().find(r=>r.key==='view')?.state).toBe('FAILED');x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('refuses a recovered report from another source',async()=>{
  const x=setup();await check(x,exact,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  await recoverConflict(x,{...done,sourceSha256:'f'.repeat(64)});await p;
  expect(emit).not.toHaveBeenCalled();expect(x.c.error()).toContain('no coincide');x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('refuses a recovered report whose plan differs from the accepted quote',async()=>{
  const x=setup();await check(x,exact,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  await recoverConflict(x,{...done,planSha256:'7'.repeat(64)});await p;
  expect(emit).not.toHaveBeenCalled();expect(x.c.error()).toContain('no coincide');x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('pauses unfinished recovery after bounded GETs without resending or leaving animated rows',async()=>{
  const x=setup();await check(x,exact,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  x.h.expectOne(url+'/manual-batch').flush({}, {status:409,statusText:'Conflict'});
  for(let attempt=0;attempt<2;attempt++){
   await tick();x.h.expectOne(url+'/batches/'+batch+'/progress').flush({...completedProgress,completed:0,phase:'RUNNING',perPart:[{part:0,state:'IN_PROGRESS'}]});
   await tick();x.h.expectOne(url+'/batches/'+batch+'/report').flush({}, {status:404,statusText:'Not Found'});
   await tick();if(attempt===0)await new Promise(resolve=>setTimeout(resolve,1050));
  }
  await p;expect(emit).not.toHaveBeenCalled();expect(x.c.processing()).toBeFalse();expect(x.con.busy()).toBeFalse();
  expect(x.c.error()).toContain('puede continuar');expect(x.c.canProcess()).toBeFalse();
  expect(x.c.stageRows().some(r=>r.state==='IN_PROGRESS')).toBeFalse();
  expect(x.c.stageRows().find(r=>r.key==='view')?.state).toBe('RECOVERY_REQUIRED');
  expect(x.c.stageStateLabel('RECOVERY_REQUIRED')).toBe('Retomar con código');
  x.f.componentRef.setInput('language','en');x.f.detectChanges();expect(x.c.stageStateLabel('RECOVERY_REQUIRED')).toBe('Resume with code');
  await x.c.process();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
});
