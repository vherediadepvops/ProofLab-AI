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
  const c=f.componentInstance,h=TestBed.inject(HttpTestingController),con=TestBed.inject(LabConnection);
  c.mode.set('api-meta-adaptive');
  const field=document.createElement('input');field.type='password';con.register(field);
  field.value='LLM_SYNTHETIC_LOCAL_TEST_KEY_1234567890';con.confirmToken();
  return {f,c,h,con};
 }
 async function check(x:ReturnType<typeof setup>,q:any=quote,g:any=spark){
  const p=x.c.check(),all=x.h.match(()=>true);expect(all.length).toBe(4);
  for(const r of all){expect(r.request.method).toBe('GET');
   if(r.request.url.includes('/sam-adaptive/')&&r.request.url.includes('/preflight?'))r.flush(q);
   else if(r.request.url.endsWith('/spark-quote'))r.flush(g);
   else if(r.request.url.endsWith('/readiness'))r.flush(access);else r.flush(media);}
  await p;x.f.detectChanges();
 }
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
  expect(x.f.nativeElement.querySelectorAll('.stage-track').length).toBe(8);
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
 it('keeps Process disabled for an invalid or missing cost',async()=>{
  const x=setup();await check(x,{...quote,totalSamEstimatedUsd:Number.NaN});
  expect(x.c.canProcess()).toBeFalse();await x.c.process();x.h.expectNone(r=>r.method==='POST');
 });
});
