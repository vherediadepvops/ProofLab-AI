import {TestBed} from '@angular/core/testing';
import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController,provideHttpClientTesting} from '@angular/common/http/testing';
import {SamVideoReplay} from './sam-video-replay';
import {LabConnection} from './lab-connection';
const sha='a'.repeat(64),id='b'.repeat(32),key='c'.repeat(64),scene='d'.repeat(64),batch='e'.repeat(32);
const root='http://127.0.0.1:5391/api/science/director/pendulum/sessions/'+id;
const url=root+'/sam-adaptive/baseline';
const quote={sourceSha256:sha,sparkSceneKey:'',planSha256:key,suggestedBatchId:batch,sourceFrames:667,
 parts:13,simultaneousRequests:4,totalSamEstimatedUsd:.2,remainingTrialSlots:null,canRunWithManualToken:true,
 existingSparkSceneVerified:false,requiresNewSparkCall:true};
const exact={...quote,sparkSceneKey:scene,parts:4,totalSamEstimatedUsd:.14,existingSparkSceneVerified:true,requiresNewSparkCall:false};
const spark={cacheKey:key,videoSha256:sha,maximumEstimatedCostUsd:.25,canRun:true,priorAttempt:false,status:'NEEDS_LOCALIZATION',scene:null};
const located={...spark,status:'READY',canRun:false,scene:{key:scene,videoSha256:sha}};
const access={manualTokenCallsPermitted:true,paidRequestsSwitchEnabled:true,creditsConfirmedByOwner:true,
 ownerApprovedPaidRequests:true,remainingRequests:0,usedRequests:43,manualRequestsUnlimited:true};
const media={sourceSha256:sha,sourceFrames:667,durationSeconds:22.23,estimatedCostUsd:.1334,supportsSingleCall:true,codec:'h264'};
const done={batchId:batch,parts:4,status:'PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW',
 derivedSeamsVerified:true,derivedFramesWithMasks:667};
async function tick(){for(let i=0;i<12;i++)await Promise.resolve();}
async function finalProgress(x:any,failed=false){
 await tick();x.h.expectOne(url+'/batches/'+batch+'/progress').flush({
  batchId:batch,parts:4,started:4,completed:4,failed:0,
  phase:failed?'FAILED':'COMPLETED',preparationElapsedMs:1200,validationElapsedMs:300,totalElapsedMs:14300,
  perPart:Array.from({length:4},(_,i)=>({part:i,state:'COMPLETED',events:50,deltas:45,outputChars:1000,elapsedMs:11000+i*100}))
 });
}
describe('Integrated manual Meta segments',()=>{
 beforeEach(async()=>{await TestBed.configureTestingModule({imports:[SamVideoReplay],
 providers:[provideHttpClient(),provideHttpClientTesting()]}).compileComponents();});
 afterEach(()=>TestBed.inject(HttpTestingController).verify());
 function setup(){
  const f=TestBed.createComponent(SamVideoReplay);
  f.componentRef.setInput('autoCheck',false);f.componentRef.setInput('studyId',id);
  f.componentRef.setInput('videoSha256',sha);f.detectChanges();
  const c=f.componentInstance;
  spyOn<any>(c,'pollSpark').and.callFake(async()=>{c.sparkStages.set(['prepare','references','upload','inference','verify','target-review'].map(key=>({key,state:'COMPLETED',elapsedMs:100,percent:100})));});
  const h=TestBed.inject(HttpTestingController),con=TestBed.inject(LabConnection);
  c.mode.set('api-meta-adaptive');
  const field=document.createElement('input');field.type='password';con.register(field);
  field.value='LLM_SYNTHETIC_LOCAL_TEST_KEY_1234567890';con.confirmToken();
  return {f,c,h,con};
 }
 async function check(x:ReturnType<typeof setup>,q:any=quote,g:any=spark){
  const p=x.c.check();
  x.h.expectOne(root+'/sam/baseline/reusable').flush({sourceSha256:sha,cacheHit:false,analysisId:null,adaptiveBatchId:null,excludedResults:1,quality:null,storage:'sqlite'});
  x.h.expectOne(root+'/sam/baseline/preflight').flush(media);
  await tick();const all=x.h.match(()=>true);expect(all.length).toBe(3);
  for(const r of all){expect(r.request.method).toBe('GET');
   if(r.request.url.includes('/sam-adaptive/')&&r.request.url.includes('/preflight?'))r.flush(q);
   else if(r.request.url.endsWith('/spark-quote'))r.flush(g);
   else if(r.request.url.endsWith('/readiness'))r.flush(access);else r.flush(media);}
  await p;x.f.detectChanges();
 }
 it('reuses checked native and segmented evidence for free without a token or paid quotes',async()=>{
  for(const adaptiveBatchId of [null,batch]) {
   const x=setup();x.c.mode.set('api-meta-cache');x.con.clearToken();
   const p=x.c.check();
   x.h.expectOne(root+'/sam/baseline/reusable').flush({sourceSha256:sha,cacheHit:true,analysisId:batch,adaptiveBatchId,excludedResults:2,quality:{usable:true,reason:'MEASUREMENTS_AVAILABLE'},storage:'sqlite'});
   x.h.expectOne(root+'/sam/baseline/preflight').flush(media);await p;
   expect(x.c.displayedCost()).toBe(0);expect(x.c.canProcess()).toBeTrue();
   const emit=spyOn(x.c.cacheUpdated,'emit');await x.c.process();
   expect(emit).toHaveBeenCalledWith(jasmine.objectContaining({analysisId:batch,adaptiveBatchId:adaptiveBatchId??undefined}));
   x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  }
 });
 it('does not reuse a quality verdict from a different video',async()=>{
  const x=setup();const p=x.c.check();
  x.h.expectOne(root+'/sam/baseline/reusable').flush({sourceSha256:'f'.repeat(64),cacheHit:true,analysisId:batch,quality:{usable:true}});
  x.h.expectOne(root+'/sam/baseline/preflight').flush(media);await p;
  expect(x.c.canProcess()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('keeps processing blocked if the cache check fails instead of silently billing',async()=>{
  const x=setup();const p=x.c.check();
  x.h.expectOne(root+'/sam/baseline/reusable').flush({}, {status:409,statusText:'Conflict'});
  x.h.expectOne(root+'/sam/baseline/preflight').flush(media);await p;
  expect(x.c.canProcess()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('reads byte progress without any provider POST and ignores stale runs',async()=>{
  const x=setup();await check(x);(x.c as any).startStageTrace();
  (x.c as any).pollSpark.and.callThrough();
  const current=(x.c as any).pollSpark();
  x.h.expectOne(root+'/assembly/baseline/grounding/progress').flush({startedAtUtc:new Date().toISOString(),stages:[{key:'upload',state:'IN_PROGRESS',elapsedMs:1000,percent:43}]});await current;
  expect(x.c.stageRows().find(r=>r.key==='spark-upload')?.percent).toBe(43);
  const stale=(x.c as any).pollSpark();
  x.h.expectOne(root+'/assembly/baseline/grounding/progress').flush({startedAtUtc:'2000-01-01T00:00:00Z',stages:[]});await stale;
  expect(x.c.sparkStages().length).toBe(1);x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('quotes on the selected normal server, includes Spark and needs only the explicit click',async()=>{
  const x=setup();await check(x);
  expect(x.c.localApiPort()).toBe(5391);expect(x.c.canProcess()).toBeTrue();
  expect(x.c.displayedCost()).toBeCloseTo(.45);x.h.expectNone(r=>r.method==='POST');
  expect(x.f.nativeElement.textContent).not.toContain('5410');
  const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  const locate=x.h.expectOne(url+'/spark-once');
  expect(locate.request.body.maximumEstimatedCostUsd).toBe(.25);
  expect(locate.request.body.confirmVideoTransfer).toBeTrue();
  await x.c.process();x.h.expectNone(r=>r.method==='POST');
  locate.flush(located);await tick();
  x.h.expectOne(url+'/preflight?targetCoreFrames=53').flush(exact);await tick();
  const sam=x.h.expectOne(url+'/manual-batch');
  expect(sam.request.body.sceneKey).toBe(scene);expect(sam.request.body.maximumSamCostUsd).toBe(.14);
  sam.flush(done);await finalProgress(x);await p;
  expect(emit).toHaveBeenCalledWith(jasmine.objectContaining({adaptiveBatchId:batch,analysisId:batch,port:5391}));
  expect(x.c.processing()).toBeTrue();
  x.f.componentRef.setInput('validationState','ready');x.f.detectChanges();
  expect(x.c.processing()).toBeFalse();expect(x.con.busy()).toBeFalse();
  const rows=x.c.stageRows();
  expect(rows.filter(r=>r.key.startsWith('sam-')).length).toBe(4);
  expect(rows.find(r=>r.key==='preparation')?.ms).toBe(1200);
  expect(rows.find(r=>r.key==='join')?.ms).toBe(300);
  expect(rows.every(r=>r.state==='COMPLETED')).toBeTrue();
  expect(x.c.canProcess()).toBeFalse(); // consumed batch identity requires a new read-only quote
  expect(x.f.nativeElement.querySelectorAll('.stage-track').length).toBe(13);
 });
 it('reuses a verified scene without Spark or an independent three-trial quota',async()=>{
  const x=setup();await check(x,exact,located);
  expect(x.c.displayedCost()).toBe(.14);expect(x.c.canProcess()).toBeTrue();
  const p=x.c.process();x.h.expectNone(url+'/spark-once');
  x.h.expectOne(url+'/manual-batch').flush(done);await finalProgress(x);await p;
  x.f.componentRef.setInput('validationState','ready');x.f.detectChanges();
 });
 it('stops before SAM on inconclusive Spark and does not retry',async()=>{
  const x=setup();await check(x);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  x.h.expectOne(url+'/spark-once').flush({...spark,status:'AMBIGUOUS'});await p;
  expect(emit).not.toHaveBeenCalled();expect(x.c.processing()).toBeFalse();x.h.expectNone(r=>r.method==='POST');
 });
 it('stops before SAM if the exact quote exceeds the approved estimate',async()=>{
  const x=setup();await check(x);const p=x.c.process();
  x.h.expectOne(url+'/spark-once').flush(located);await tick();
  x.h.expectOne(url+'/preflight?targetCoreFrames=53').flush({...exact,totalSamEstimatedUsd:.21});
  await p;expect(x.c.error()).toContain('actualiza el costo');x.h.expectNone(r=>r.method==='POST');
 });
 it('does not publish an incomplete or unverified seam result',async()=>{
  const x=setup();await check(x,exact,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  x.h.expectOne(url+'/manual-batch').flush({...done,derivedSeamsVerified:false});await finalProgress(x,true);await p;
  expect(emit).not.toHaveBeenCalled();expect(x.c.processing()).toBeFalse();
 });
 it('discards a Spark reply after changing the selected video',async()=>{
  const x=setup();await check(x);const p=x.c.process(),r=x.h.expectOne(url+'/spark-once');
  x.f.componentRef.setInput('videoSha256','f'.repeat(64));x.f.detectChanges();
  r.flush(located);await p;x.h.expectNone(req=>req.method==='POST'||req.url.includes('/preflight?'));
 });
 it('uses the quoted server concurrency instead of fixing four requests',async()=>{
  const x=setup();await check(x,{...exact,parts:19,simultaneousRequests:12},located);
  expect(x.c.canProcess()).toBeTrue();
  const p=x.c.process(),r=x.h.expectOne(url+'/manual-batch');
  expect(r.request.body.maxInFlight).toBe(12);
  r.flush({...done,derivedSeamsVerified:false});await p; // mismatched part count is rejected before polling
 });
 it('quotes continuous recovery before a click and sends exactly one SAM request using saved Spark',async()=>{
  const x=setup(),prior='9'.repeat(32);
  await check(x,{...exact,parts:1,simultaneousRequests:1,totalSamEstimatedUsd:.1334,recoveryFromBatchId:prior},located);
  expect(x.f.nativeElement.textContent).toContain('1 solicitud SAM');
  expect(x.c.canProcess()).toBeTrue();x.h.expectNone(r=>r.method==='POST');
  const p=x.c.process();x.h.expectNone(url+'/spark-once');
  const r=x.h.expectOne(url+'/manual-batch');
  expect(r.request.body.recoveryFromBatchId).toBe(prior);expect(r.request.body.maxInFlight).toBe(1);
  r.flush({...done,parts:1});await tick();
  x.h.expectOne(url+'/batches/'+batch+'/progress').flush({
   batchId:batch,parts:1,started:1,completed:1,failed:0,phase:'COMPLETED',
   preparationElapsedMs:1200,validationElapsedMs:100,totalElapsedMs:10000,
   perPart:[{part:0,state:'COMPLETED',elapsedMs:8700}]});
  await p;x.f.componentRef.setInput('validationState','ready');x.f.detectChanges();
  expect(x.c.processing()).toBeFalse();
  expect(x.c.stageRows().filter(r=>r.key.startsWith('sam-')).length).toBe(1);
  expect(x.c.stageRows().find(r=>r.key==='sam-0')?.label).toContain('video completo');
 });
 it('keeps Process disabled for an invalid or missing cost',async()=>{
  const x=setup();await check(x,{...quote,totalSamEstimatedUsd:Number.NaN});
  expect(x.c.canProcess()).toBeFalse();await x.c.process();x.h.expectNone(r=>r.method==='POST');
 });
 it('shows the combined estimate and opens only the linked full-video fallback',async()=>{
  const x=setup(),child='8'.repeat(32);
  const q={...exact,parts:3,simultaneousRequests:3,totalSamEstimatedUsd:.1354,
   continuousRetryEstimatedUsd:.1334,maximumSamWithRetryEstimatedUsd:.2688};
  await check(x,q,located);
  expect(x.c.displayedCost()).toBeCloseTo(.2688);
  expect(x.f.nativeElement.textContent).toContain('posible reintento');
  expect(x.c.nextActionDescription()).toContain('UN reintento');
  const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  const post=x.h.expectOne(url+'/manual-batch');
  expect(post.request.body.confirmOneContinuousRetry).toBeTrue();
  expect(post.request.body.maximumCombinedSamCostUsd).toBeCloseTo(.2688);
  post.flush({...done,batchId:child,parts:1,automaticRetryOfBatchId:batch});await tick();
  x.h.expectOne(url+'/batches/'+batch+'/progress').flush({
   batchId:batch,parts:3,started:3,completed:3,failed:0,phase:'FAILED',perPart:[],
   continuousRetryBatchId:child,continuousRetry:{batchId:child,parts:1,started:1,completed:1,failed:0,
    phase:'COMPLETED',preparationElapsedMs:200,validationElapsedMs:100,perPart:[{part:0,state:'COMPLETED',elapsedMs:24000}]}});
  await p;x.f.componentRef.setInput('validationState','ready');x.f.detectChanges();
  expect(emit).toHaveBeenCalledWith(jasmine.objectContaining({adaptiveBatchId:child}));
  expect(x.c.stageRows().find(r=>r.key==='retry-sam')?.ms).toBe(24000);
  expect(x.c.stageRows().find(r=>r.key==='retry-validation')?.state).toBe('COMPLETED');
  x.h.expectNone(r=>r.method==='POST'); // server orchestrates; browser never submits a second action
 });
 it('marks the reserved fallback unnecessary if the three segments succeed',async()=>{
  const x=setup(),q={...exact,parts:3,simultaneousRequests:3,totalSamEstimatedUsd:.1354,
   continuousRetryEstimatedUsd:.1334,maximumSamWithRetryEstimatedUsd:.2688};
  await check(x,q,located);const p=x.c.process();
  x.h.expectOne(url+'/manual-batch').flush({...done,parts:3});await finalProgress(x);await p;
  x.f.componentRef.setInput('validationState','ready');x.f.detectChanges();
  expect(x.c.stageRows().filter(r=>r.key.startsWith('retry-')).every(r=>r.state==='SKIPPED')).toBeTrue();
  x.h.expectNone(r=>r.method==='POST');
 });
 it('ends inconclusively after a failed fallback without inventing playback evidence',async()=>{
  const x=setup(),q={...exact,parts:3,simultaneousRequests:3,totalSamEstimatedUsd:.1354,
   continuousRetryEstimatedUsd:.1334,maximumSamWithRetryEstimatedUsd:.2688};
  await check(x,q,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  x.h.expectOne(url+'/manual-batch').flush({...done,batchId:'8'.repeat(32),parts:1,automaticRetryOfBatchId:batch,
   status:'PARTIAL_PROVIDER_ATTEMPTS_REQUIRE_MANUAL_RECONCILIATION',derivedFramesWithMasks:0,derivedSeamsVerified:false});
  await finalProgress(x,true);await p;
  expect(emit).not.toHaveBeenCalled();expect(x.c.processing()).toBeFalse();
  expect(x.c.error()).toContain('reintento');x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('rejects a fallback attributed to another initial batch',async()=>{
  const x=setup(),q={...exact,parts:3,simultaneousRequests:3,totalSamEstimatedUsd:.1354,
   continuousRetryEstimatedUsd:.1334,maximumSamWithRetryEstimatedUsd:.2688};
  await check(x,q,located);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  x.h.expectOne(url+'/manual-batch').flush({...done,batchId:'8'.repeat(32),parts:1,automaticRetryOfBatchId:'7'.repeat(32)});
  await p;expect(emit).not.toHaveBeenCalled();x.f.destroy();
 });
 it('rejects an incomplete combined quote before any paid request',async()=>{
  const x=setup();await check(x,{...exact,parts:3,continuousRetryEstimatedUsd:.1334,maximumSamWithRetryEstimatedUsd:.14},located);
  expect(x.c.canProcess()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });

});
