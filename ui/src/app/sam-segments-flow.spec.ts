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
 it('keeps terminal progress when an older poll returns after it',async()=>{
  const x=setup();await check(x);(x.c as any).startStageTrace();(x.c as any).pollSpark.and.callThrough();
  const first=(x.c as any).pollSpark(),second=(x.c as any).pollSpark();
  const reads=x.h.match(root+'/assembly/baseline/grounding/progress');expect(reads.length).toBe(2);
  const startedAtUtc=new Date().toISOString();
  reads[1].flush({startedAtUtc,stages:[{key:'prepare',state:'FAILED',elapsedMs:36000,percent:null}]});await second;
  reads[0].flush({startedAtUtc,stages:[{key:'prepare',state:'IN_PROGRESS',elapsedMs:35000,percent:null}]});await first;
  expect(x.c.sparkStages()[0].state).toBe('FAILED');x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('reads the final local failure and distinguishes preparation from image extraction in both languages',async()=>{
  for(const language of ['es','en'])for(const stage of ['prepare','references']){
   const x=setup();x.f.componentRef.setInput('language',language);x.f.detectChanges();await check(x);
   (x.c as any).pollSpark.and.callThrough();
   const p=x.c.process();
   x.h.expectOne(url+'/spark-once').flush({error:'Spark full-video provenance mismatch.'},{status:409,statusText:'Conflict'});await tick();
   x.h.expectOne(root+'/assembly/baseline/grounding/progress').flush({startedAtUtc:new Date().toISOString(),
    stages:[{key:'prepare',state:stage==='prepare'?'FAILED':'COMPLETED',elapsedMs:36000,percent:null},
     {key:'references',state:stage==='references'?'FAILED':'NOT_STARTED',elapsedMs:0,percent:null}]});
   await p;
   expect(x.c.error()).toContain(language==='es'?(stage==='prepare'?'preparar el video':'extraer las imágenes'):
    (stage==='prepare'?'prepared for analysis':'images could not be extracted'));
   expect(x.c.error()).toContain(language==='es'?'No se enviaron datos a Spark ni a SAM':'No data was sent to Spark or SAM');
   expect(x.c.processing()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  }
 });
 it('does not infer a local-only failure when final progress is unavailable or a transfer already began',async()=>{
  for(const progress of [null,{key:'upload',state:'IN_PROGRESS',elapsedMs:100,percent:10}]){
   const x=setup();await check(x);(x.c as any).pollSpark.and.callThrough();const p=x.c.process();
   x.h.expectOne(url+'/spark-once').flush({error:'Failed'},{status:409,statusText:'Conflict'});await tick();
   const read=x.h.expectOne(root+'/assembly/baseline/grounding/progress');
   if(progress)read.flush({startedAtUtc:new Date().toISOString(),stages:[
    {key:'prepare',state:'FAILED',elapsedMs:100,percent:null},progress]});
   else read.flush({},{status:503,statusText:'Unavailable'});
   await p;expect(x.c.error()).toContain('completar o validar la revisión de Spark');
   expect(x.c.error()).not.toContain('No se enviaron datos a Spark ni a SAM');x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  }
 });
 it('automatically interprets verified baseline SAM once with the disclosed Muse cost and no second video upload',async()=>{
  const x=setup();x.f.componentRef.setInput('autoMuseAfterSam',true);x.f.detectChanges();
  await check(x,exact,located);
  expect(x.f.nativeElement.textContent).toContain('Máximo combinado estimado: US$ 0.18');
  expect(x.f.nativeElement.textContent).toContain('Procesar + interpretar con Muse');
  const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
  x.h.expectOne(url+'/manual-batch').flush(done);await finalProgress(x);await tick();
  const muse=root+'/muse',emptyMuse={observation:null,evaluation:null,prediction:null,
    attemptStatus:null,manualCallsPermitted:true};
  const recorded={id:'f'.repeat(64),analysisId:batch,videoSha256:sha,
    payloadJson:JSON.stringify({identity:{studyId:id,videoSha256:sha,analysisId:batch,
      adaptiveBatchId:batch,pivotRevisionId:null}})};
  const initial=x.h.expectOne(muse);expect(initial.request.method).toBe('GET');initial.flush(emptyMuse);
  await tick();
  const prep=x.h.expectOne(muse+'/prepare');
  expect(prep.request.body).toEqual({videoSha256:sha,analysisId:batch,pivotRevisionId:null,
    adaptiveBatchId:batch});
  prep.flush({...emptyMuse,observation:recorded});await tick();
  const evaluate=x.h.expectOne(muse+'/evaluate');
  expect(evaluate.request.body.observationId).toBe(recorded.id);
  expect(evaluate.request.body.confirmPaidCall).toBeTrue();
  expect(evaluate.request.body.confirmMeasuredDataTransfer).toBeTrue();
  expect(evaluate.request.body.maximumEstimatedCostUsd).toBe(.05);
  expect(evaluate.request.headers.get('X-ProofLab-Manual-Action')).toBe('muse-research');
  evaluate.flush({...emptyMuse,observation:recorded,evaluation:{responseId:'saved-receipt'}});await p;
  expect(x.c.museAutoStatus()).toBe('saved');
  expect(emit).toHaveBeenCalledOnceWith(jasmine.objectContaining({analysisId:batch}));
  x.h.expectNone(r=>r.method==='POST');
  unlock(x);x.f.destroy();
 });
 it('uses a stored Muse interpretation after SAM and does not pay again',async()=>{
  const x=setup();x.f.componentRef.setInput('autoMuseAfterSam',true);x.f.detectChanges();
  await check(x,exact,located);
  const p=x.c.process();x.h.expectOne(url+'/manual-batch').flush(done);
  await finalProgress(x);await tick();
  x.h.expectOne(root+'/muse').flush({observation:{id:'f'.repeat(64),analysisId:batch,videoSha256:sha,
    payloadJson:JSON.stringify({identity:{studyId:id,videoSha256:sha,analysisId:batch,
      adaptiveBatchId:batch,pivotRevisionId:null}})},
    evaluation:{responseId:'existing'},prediction:null,attemptStatus:'COMPLETED',manualCallsPermitted:true});
  await p;expect(x.c.museAutoStatus()).toBe('saved');
  x.h.expectNone(r=>r.url.includes('/muse') && r.method==='POST');
  unlock(x);x.f.destroy();
 });
 it('shows the read-only exact preflight while Spark is complete and SAM has not started',async()=>{
  const x=setup();await check(x);const p=x.c.process();
  x.h.expectOne(url+'/spark-once').flush(located);await tick();
  const preflight=x.h.expectOne(url+'/preflight?mode=api-meta-cache');
  await (x.c as any).pollSpark();
  const rows=x.c.stageRows();
  expect(rows.filter(r=>r.key.startsWith('spark-')).every(r=>r.state==='COMPLETED')).toBeTrue();
  expect(rows.find(r=>r.key==='exact-preflight')?.state).toBe('IN_PROGRESS');
  expect(rows.find(r=>r.key==='exact-preflight')?.label).toBe('Verificando preparación para SAM');
  expect(rows.find(r=>r.key==='preparation')?.state).toBe('NOT_STARTED');
  expect(rows.find(r=>r.key==='sam-0')?.state).toBe('NOT_STARTED');
  x.h.expectNone(url+'/manual-batch');
  preflight.flush(exact);await tick();
  expect(x.c.stageRows().find(r=>r.key==='exact-preflight')?.state).toBe('COMPLETED');
  x.h.expectOne(url+'/manual-batch').flush(done);await finalProgress(x);await p;
  unlock(x);x.f.destroy();
 });
 it('marks an exact preflight failure and makes clear SAM was never submitted',async()=>{
  const x=setup();await check(x);const p=x.c.process();
  x.h.expectOne(url+'/spark-once').flush(located);await tick();
  x.h.expectOne(url+'/preflight?mode=api-meta-cache').flush({error:'Unavailable'},{status:503,statusText:'Unavailable'});
  await p;
  expect(x.c.stageRows().find(r=>r.key==='exact-preflight')?.state).toBe('FAILED');
  expect(x.c.stageRows().find(r=>r.key==='sam-0')?.state).toBe('NOT_STARTED');
  expect(x.c.error()).toContain('Spark terminó');expect(x.c.error()).toContain('SAM no se envió');
  expect(x.c.processing()).toBeFalse();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('discloses automatic followup comparison and its maximum cost before processing',async()=>{
  const x=setup();x.f.componentRef.setInput('autoMuseAfterSam',true);
  x.f.componentRef.setInput('videoRole','followup');x.f.detectChanges();
  x.c.adaptiveQuote.set(exact as any);x.c.groundingQuote.set(located as any);x.c.preflight.set(media as any);x.c.readiness.set(access as any);
  x.f.detectChanges();
  const text=x.f.nativeElement.textContent;
  expect(text).toContain('Procesar + interpretar con Muse');
  expect(text).toContain('contraste automático después de SAM');
  expect(text).toContain('Máximo adicional de Muse: US$ 0,05');
  expect(text).toContain('Máximo combinado estimado: US$ 0.18');
  x.c.selectSource('api',false);x.f.detectChanges();
  expect(x.f.nativeElement.textContent).not.toContain('contraste automático después de SAM');
  x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('reports whether the selected source allows API usage to its parent',()=>{
  const x=setup(),emit=spyOn(x.c.apiUsageChanged,'emit');
  x.c.selectSource('api',false);expect(emit).toHaveBeenCalledWith(false);
  x.c.selectSource('api',true);expect(emit).toHaveBeenCalledWith(true);
  const select=document.createElement('select');select.innerHTML='<option value="cache-only">Cache</option>';
  x.c.selectMode({target:select} as unknown as Event);expect(emit.calls.mostRecent().args).toEqual([false]);
  x.h.expectNone(()=>true);x.f.destroy();
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
 it('explains a failed Spark validation without claiming the object was absent',async()=>{
  const x=setup();await check(x);const p=x.c.process();
  x.h.expectOne(url+'/spark-once').flush({detail:'Spark provider output could not be validated.'},{status:502,statusText:'Bad Gateway'});await p;
  expect(x.c.error()).toContain('completar o validar la revisión de Spark');
  expect(x.c.error()).not.toContain('confirmar el objeto');
  x.h.expectNone(r=>r.method==='POST');x.f.destroy();
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
 it('offers an explicit SAM-only retry with the possible new charge and no Spark call',async()=>{
  const x=setup();await check(x,exact,located);x.c.mode.set('api-meta');
  const next='f'.repeat(32),plan='7'.repeat(64),runId='3'.repeat(32);
  const retry={failedBatchId:batch,newBatchId:next,videoSha256:sha,sparkSceneKey:scene,
    sceneRunId:runId,planSha256:plan,frames:667,estimatedSamUsd:.1334,
    targetCoreFrames:53,alreadyRequested:false,previousMayHaveBeenBilled:true};
  x.c.stageRetryQuote.set(retry);x.f.detectChanges();
  expect(x.f.nativeElement.textContent).toContain('Retomar desde la etapa SAM');
  expect(x.f.nativeElement.textContent).toContain('US$ 0.14');
  expect(x.c.canProcess()).toBeFalse();expect(x.c.canStageRetry()).toBeTrue();
  const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.retryStage();
  const post=x.h.expectOne(url+'/manual-stage-retry');
  expect(post.request.body).toEqual({failedBatchId:batch,newBatchId:next,
    videoSha256:sha,planSha256:plan,modelApiToken:'LLM_SYNTHETIC_LOCAL_TEST_KEY_1234567890',
    maximumEstimatedCostUsd:.1334,confirmPotentialSecondCharge:true,confirmVideoTransfer:true});
  expect(post.request.headers.get('X-ProofLab-Manual-Action')).toBe('sam-adaptive-stage-retry');
  expect(x.c.canStageRetry()).toBeFalse();
  await x.c.retryStage();x.h.expectNone(r=>r.method==='POST');
  post.flush({...done,batchId:next,planSha256:plan});await tick();
  x.h.expectOne(url+'/batches/'+next+'/progress').flush({...completedProgress,batchId:next});
  await p;expect(emit).toHaveBeenCalledOnceWith(jasmine.objectContaining({
    analysisId:next,adaptiveBatchId:next,sourceSha256:sha}));
  expect(x.c.stageRows().find(r=>r.key==='spark-reused')?.state).toBe('COMPLETED');
  expect(x.c.stageRows().find(r=>r.key==='clip-reused')?.state).toBe('COMPLETED');
  x.h.expectNone(r=>r.url.includes('/spark-once') || r.url.includes('/manual-batch'));
  unlock(x);x.f.destroy();
 });
 it('rejects a claimed retry report from a different plan before opening overlays',async()=>{
  const x=setup();await check(x,exact,located);x.c.mode.set('api-meta');
  const next='f'.repeat(32),plan='7'.repeat(64);
  x.c.stageRetryQuote.set({failedBatchId:batch,newBatchId:next,videoSha256:sha,
    sparkSceneKey:scene,sceneRunId:'3'.repeat(32),planSha256:plan,frames:667,
    targetCoreFrames:53,estimatedSamUsd:.1334,alreadyRequested:true,previousMayHaveBeenBilled:true});
  const emit=spyOn(x.c.cacheUpdated,'emit'),run=x.c.recoverStageRetry();
  x.h.expectOne(url+'/batches/'+next+'/report').flush({...done,batchId:next,planSha256:key});
  await run;expect(emit).not.toHaveBeenCalled();expect(x.c.error()).toContain('aún no tiene un informe');
  x.h.expectNone(r=>r.method==='POST');x.f.destroy();
 });
 it('recovers an already claimed SAM retry using GET without any new paid request',async()=>{
  const x=setup();await check(x,exact,located);x.c.mode.set('api-meta');
  const next='f'.repeat(32),plan='7'.repeat(64);
  x.c.stageRetryQuote.set({failedBatchId:batch,newBatchId:next,videoSha256:sha,
    sparkSceneKey:scene,sceneRunId:'3'.repeat(32),planSha256:plan,frames:667,
    targetCoreFrames:53,estimatedSamUsd:.1334,alreadyRequested:true,previousMayHaveBeenBilled:true});
  expect(x.c.canStageRetry()).toBeFalse();
  const emit=spyOn(x.c.cacheUpdated,'emit'),run=x.c.recoverStageRetry();
  const request=x.h.expectOne(url+'/batches/'+next+'/report');
  expect(request.request.method).toBe('GET');
  request.flush({...done,batchId:next,planSha256:plan});await run;
  expect(emit).toHaveBeenCalledOnceWith(jasmine.objectContaining({analysisId:next}));
  x.h.expectNone(r=>r.method==='POST');unlock(x);x.f.destroy();
 });
 for(const language of ['es','en']){
  it('requires a new Spark review instead of offering an invalid saved SAM retry in '+language,async()=>{
   const x=setup();await check(x,exact,located);x.c.mode.set('api-meta');
   x.f.componentRef.setInput('language',language);x.f.detectChanges();
   const request=x.c.findStageRetry();
   x.h.expectOne(url+'/manual-stage-retry/quote').flush({code:'SAM_INITIAL_REFERENCE_REVIEW_REQUIRED'},
    {status:409,statusText:'Conflict'});await request;x.f.detectChanges();
   expect(x.c.error()).toContain(language==='es'?'nueva revisión de Spark':'new Spark review');
   expect(x.c.error()).toContain(language==='es'?'Actualiza el costo':'Refresh the cost');
   expect(x.c.stageRetryQuote()).toBeNull();expect(x.c.canStageRetry()).toBeFalse();
   await x.c.retryStage();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
  it('keeps a reference rejection actionable if it arrives when retry is clicked in '+language,async()=>{
   const x=setup();await check(x,exact,located);x.c.mode.set('api-meta');
   x.f.componentRef.setInput('language',language);x.f.detectChanges();
   x.c.stageRetryQuote.set({failedBatchId:batch,newBatchId:'f'.repeat(32),videoSha256:sha,
    sparkSceneKey:scene,sceneRunId:'3'.repeat(32),planSha256:key,frames:667,
    targetCoreFrames:53,estimatedSamUsd:.1334,alreadyRequested:false,previousMayHaveBeenBilled:true});
   const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.retryStage();
   x.h.expectOne(url+'/manual-stage-retry').flush({code:'SAM_INITIAL_REFERENCE_REVIEW_REQUIRED'},
    {status:409,statusText:'Conflict'});await p;
   expect(x.c.error()).toContain(language==='es'?'nueva revisión de Spark':'new Spark review');
   expect(x.c.stageRetryQuote()).toBeNull();expect(x.c.canStageRetry()).toBeFalse();
   expect(emit).not.toHaveBeenCalled();await x.c.retryStage();
   x.h.expectNone(()=>true);x.f.destroy();
  });
  it('explains oversized SAM output without treating partial measurements as usable in '+language,async()=>{
   const x=setup();await check(x,exact,located);
   x.f.componentRef.setInput('language',language);x.f.detectChanges();
   const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
   x.h.expectOne(url+'/manual-batch').flush({...done,status:'PROVIDER_STREAM_FAILED',
    failureCode:'SAM_SSE_OUTPUT_LIMIT_EXCEEDED',completedParts:0,failedParts:1,
    derivedFramesWithMasks:100,derivedSeamsVerified:false});
   await finalProgress(x,true);await p;
   expect(x.c.error()).toContain(language==='es'?'superó el tamaño permitido':'exceeded the allowed size');
   expect(x.c.error()).toContain(language==='es'?'no se usarán sus mediciones':'measurements will not be used');
   expect(x.c.error()).toContain(language==='es'?'puede haber consumido créditos':'may have consumed credits');
   expect(emit).not.toHaveBeenCalled();x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
  it('explains the saved output limit and new reference review together in '+language,async()=>{
   const x=setup();await check(x,exact,located);x.c.mode.set('api-meta');
   x.f.componentRef.setInput('language',language);x.f.detectChanges();
   const next='f'.repeat(32);
   x.c.stageRetryQuote.set({failedBatchId:batch,newBatchId:next,videoSha256:sha,
    sparkSceneKey:scene,sceneRunId:'3'.repeat(32),planSha256:key,frames:667,
    targetCoreFrames:53,estimatedSamUsd:.1334,alreadyRequested:true,previousMayHaveBeenBilled:true});
   const emit=spyOn(x.c.cacheUpdated,'emit'),recovery=x.c.recoverStageRetry();
   x.h.expectOne(url+'/batches/'+next+'/report').flush({...done,batchId:next,status:'PROVIDER_STREAM_FAILED',
    failureCode:'SAM_SSE_OUTPUT_LIMIT_EXCEEDED',completedParts:0,failedParts:1,
    derivedFramesWithMasks:null,derivedSeamsVerified:null});await recovery;
   expect(x.c.error()).toContain(language==='es'?'superó el tamaño permitido':'exceeded the allowed size');
   const lookup=x.c.findStageRetry();
   x.h.expectOne(url+'/manual-stage-retry/quote').flush({code:'SAM_INITIAL_REFERENCE_REVIEW_REQUIRED'},
    {status:409,statusText:'Conflict'});await lookup;
   expect(x.c.error()).toContain(language==='es'?'superó el tamaño permitido':'exceeded the allowed size');
   expect(x.c.error()).toContain(language==='es'?'nueva revisión de Spark':'new Spark review');
   expect(emit).not.toHaveBeenCalled();expect(x.c.canStageRetry()).toBeFalse();
   x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
 }

});
