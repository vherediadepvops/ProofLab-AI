import {TestBed} from '@angular/core/testing';
import {HttpRequest,provideHttpClient} from '@angular/common/http';
import {HttpTestingController,provideHttpClientTesting} from '@angular/common/http/testing';
import {SamVideoReplay} from './sam-video-replay';
import {LabConnection} from './lab-connection';

const sha='a'.repeat(64),study='b'.repeat(32),plan='c'.repeat(64),scene='d'.repeat(64),batch='e'.repeat(32),runId='1'.repeat(32);
const root='http://127.0.0.1:5391/api/science/director/pendulum/sessions/'+study;
const sam=root+'/sam/baseline',adaptive=root+'/sam-adaptive/baseline';
const media={sourceSha256:sha,sourceFrames:120,durationSeconds:4,estimatedCostUsd:.024,
  supportsSingleCall:true,codec:'h264',timingSource:'SYNTHETIC_TEST',framePtsSha256:'2'.repeat(64)};
const access={apiKeyConfigured:false,liveCallsPermitted:false,manualTokenCallsPermitted:true,
  paidRequestsSwitchEnabled:true,creditsConfirmedByOwner:true,ownerApprovedPaidRequests:true,
  remainingRequests:0,usedRequests:0,manualRequestsUnlimited:true};
const freshQuote={mode:'api-meta',sourceSha256:sha,sparkSceneKey:'',sourcePtsSha256:media.framePtsSha256,
  planSha256:plan,suggestedBatchId:batch,sourceFrames:120,parts:1,simultaneousRequests:1,
  submittedFrames:120,billableFrames:120,totalSamEstimatedUsd:.024,maximumAllowedSamUsd:.50,
  continuousRetryEstimatedUsd:0,maximumSamWithRetryEstimatedUsd:.024,
  remainingTrialSlots:null,canRunWithManualToken:true,existingSparkSceneVerified:false,requiresNewSparkCall:true,
  idealPartsWithoutSparkCoverage:1,verifiedSparkSeedCount:0,reducedForSparseSparkSeeds:false,sceneRunId:null};
const sparkQuote={cacheKey:'3'.repeat(64),videoSha256:sha,maximumEstimatedCostUsd:.35,requiredSparkCalls:2,visualReviewEstimatedCostUsd:.10,
  localizationPlan:'SPARK_ORIGINAL_50_OBJECT_REVIEW_V1',mode:'api-meta',
  freshRequired:true,protocol:'ASSEMBLY_FULL_VIDEO_GROUNDING_V2',cacheHit:false,priorAttempt:false,
  canRun:true,status:'NEEDS_LOCALIZATION',scene:null};
const located={...sparkQuote,freshRequired:false,canRun:false,status:'READY',scene:{key:scene,videoSha256:sha},evidence:{runId}};
const exactQuote={...freshQuote,sparkSceneKey:scene,sceneRunId:runId,existingSparkSceneVerified:true,
  requiresNewSparkCall:false,verifiedSparkSeedCount:3};
const reusable={sourceSha256:sha,cacheHit:true,analysisId:batch,adaptiveBatchId:batch,excludedResults:0,
  quality:{usable:true,reason:'MEASUREMENTS_AVAILABLE'},storage:'sqlite'};
const miss={...reusable,cacheHit:false,analysisId:null,adaptiveBatchId:null,quality:null,excludedResults:1};
const done={batchId:batch,sourceSha256:sha,sparkSceneKey:scene,planSha256:plan,parts:1,startedParts:1,
  completedParts:1,failedParts:0,skippedParts:0,estimatedSamUsd:.024,
  status:'PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW',derivedFramesWithMasks:120,derivedSeamsVerified:true};
const pathIs=(url:string)=>(r:HttpRequest<unknown>)=>new URL(r.urlWithParams).pathname===new URL(url).pathname;
const query=(r:HttpRequest<unknown>,name:string)=>new URL(r.urlWithParams).searchParams.get(name);
async function tick(){for(let i=0;i<12;i++)await Promise.resolve();}

describe('Processing source checkboxes',()=>{
  beforeEach(async()=>{await TestBed.configureTestingModule({imports:[SamVideoReplay],
    providers:[provideHttpClient(),provideHttpClientTesting()]}).compileComponents();});
  afterEach(()=>TestBed.inject(HttpTestingController).verify());
  function setup(withToken=false){
    const f=TestBed.createComponent(SamVideoReplay);
    f.componentRef.setInput('autoCheck',false);f.componentRef.setInput('guided',true);
    f.componentRef.setInput('studyId',study);f.componentRef.setInput('videoSha256',sha);f.detectChanges();
    const c=f.componentInstance;spyOn<any>(c,'pollSpark').and.callFake(async()=>{});
    const h=TestBed.inject(HttpTestingController),con=TestBed.inject(LabConnection);
    const field=document.createElement('input');field.type='password';con.register(field);
    if(withToken){field.value='LLM_SYNTHETIC_LOCAL_TEST_KEY_1234567890';expect(con.confirmToken()).toBeTrue();}
    return {f,c,h,con,host:f.nativeElement as HTMLElement};
  }
  type Context=ReturnType<typeof setup>;
  function checkbox(x:Context,kind:'api'|'cache'):HTMLInputElement{
    const pattern=kind==='api'?/^API Meta\b/i:/^(?:Caché|Cache)\s*\+\s*SQLite\b/i;
    const label=Array.from(x.host.querySelectorAll('label')).find(item=>pattern.test((item.textContent??'').replace(/\s+/g,' ').trim()));
    const input=label?.querySelector<HTMLInputElement>('input[type=checkbox]');
    if(!input)throw Error('SOURCE_CHECKBOX_NOT_FOUND_'+kind);return input;
  }
  function select(x:Context,kind:'api'|'cache',checked:boolean){const input=checkbox(x,kind);if(input.checked!==checked)input.click();x.f.detectChanges();}
  function cacheOnly(x:Context){select(x,'cache',true);select(x,'api',false);expect(x.c.mode()).toBe('cache-only');}
  function metadata(x:Context){const r=x.h.expectOne(pathIs(sam+'/preflight'));expect(r.request.method).toBe('GET');r.flush(media);}
  function paidQuotes(x:Context,mode:'api-meta'|'api-meta-cache',quote:unknown={...freshQuote,mode},spark:unknown={...sparkQuote,mode}){
    const q=x.h.expectOne(pathIs(adaptive+'/preflight'));expect(q.request.method).toBe('GET');expect(query(q.request,'mode')).toBe(mode);expect(query(q.request,'targetCoreFrames')).toBeNull();q.flush(quote as object);
    x.h.expectOne('http://127.0.0.1:5391/api/meta/readiness').flush(access);
    const g=x.h.expectOne(pathIs(adaptive+'/spark-quote'));expect(g.request.method).toBe('GET');expect(query(g.request,'mode')).toBe(mode);g.flush(spark as object);
  }
  async function checkFresh(x:Context,quote:unknown=freshQuote,spark:unknown=sparkQuote){
    const p=x.c.check();x.h.expectNone(pathIs(sam+'/reusable'));metadata(x);await tick();paidQuotes(x,'api-meta',quote,spark);await p;x.f.detectChanges();
  }
  it('starts with only API Meta selected and puts both sources below Process in ES and EN',()=>{
    const x=setup(),button=x.host.querySelector<HTMLButtonElement>('.sam-submit')!;
    for(const language of ['es','en']){x.f.componentRef.setInput('language',language);x.f.detectChanges();
      expect(checkbox(x,'api').checked).toBeTrue();expect(checkbox(x,'cache').checked).toBeFalse();
      for(const input of [checkbox(x,'api'),checkbox(x,'cache')])expect(!!(button.compareDocumentPosition(input)&Node.DOCUMENT_POSITION_FOLLOWING)).toBeTrue();}
    expect(x.c.mode()).toBe('api-meta');expect(x.c.segmentedMode()).toBeTrue();expect(button.disabled).toBeTrue();
    x.h.expectNone(()=>true);x.f.destroy();
  });
  it('shows the original 50-image plan and reduced quote in ES and EN without zoom rounds or paid requests',async()=>{
    const x=setup(true);await checkFresh(x);
    for(const language of ['es','en']){
      x.f.componentRef.setInput('language',language);x.f.detectChanges();
      const text=x.host.textContent??'';
      expect(text).toContain(language==='es'?'50 imágenes originales y revisión del objeto':'50 original images and object review');
      expect(text).toContain('US$ 0.35');expect(x.c.displayedCost()).toBeCloseTo(.374);
      expect(text).not.toMatch(/×1[.,]5|×3|0\.85/);
      expect(x.c.nextActionDescription()).not.toMatch(/zoom|acercamiento|×/i);
      expect(x.c.groundingQuote()?.requiredSparkCalls).toBe(2);
    }
    x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
  it('describes the combined object and contact audit in ES and EN without adding a Spark request or zoom budget',async()=>{
    const x=setup(true);
    await checkFresh(x,freshQuote,{...sparkQuote,localizationPlan:'SPARK_ORIGINAL_50_CONTACT_AUDIT_V2'});
    for(const language of ['es','en']){
      x.f.componentRef.setInput('language',language);x.f.detectChanges();
      const text=x.host.textContent??'';
      expect(text).toContain(language==='es'?'50 imágenes originales y revisión del objeto y la suspensión':
        '50 original images and object and suspension review');
      expect(text).toContain('US$ 0.35');expect(x.c.displayedCost()).toBeCloseTo(.374);
      expect(x.c.groundingQuote()?.requiredSparkCalls).toBe(2);
      expect(text).not.toMatch(/×1[.,]5|×3|0\.85/);
    }
    x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
  it('keeps recorded historical zoom stages and their durations readable',async()=>{
    const x=setup(true);await checkFresh(x);
    const processing=x.c.process(),spark=x.h.expectOne(pathIs(adaptive+'/spark-once'));
    x.c.sparkStages.set([
      {key:'prepare',state:'COMPLETED',elapsedMs:100,percent:100},
      {key:'inference',state:'COMPLETED',elapsedMs:200,percent:100},
      {key:'round-2-zoom-inference',state:'COMPLETED',elapsedMs:26000,percent:100},
      {key:'round-3-zoom-verify',state:'FAILED',elapsedMs:30,percent:null}]);
    spark.flush({...sparkQuote,status:'AMBIGUOUS',canRun:false});await processing;
    const spanish=x.c.stageRows();
    expect(spanish.find(r=>r.key==='spark-round-2-zoom-inference')).toEqual(jasmine.objectContaining({label:'Ronda 2 · Revisar contacto ×1.5',state:'COMPLETED',ms:26000}));
    x.f.componentRef.setInput('language','en');x.f.detectChanges();
    expect(x.c.stageRows().find(r=>r.key==='spark-round-3-zoom-verify')).toEqual(jasmine.objectContaining({label:'Round 3 · Validate zoom review',state:'FAILED',ms:30}));
    x.c.groundingQuote.set({...x.c.groundingQuote()!,localizationPlan:'SPARK_PROGRESSIVE_CONTACT_50_X1_5_X3_V2'});
    expect(x.c.localizationPlanDescription()).toContain('50 ×1.5 + 50 ×3');
    x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
  it('blocks processing and all reads when both sources are off',async()=>{
    const x=setup(true);select(x,'api',false);expect(x.c.mode()).toBe('none');await x.c.check();await x.c.process();x.f.detectChanges();
    expect(x.c.canProcess()).toBeFalse();expect(x.c.preflight()).toBeNull();expect(x.host.querySelector('.sam-token-needed')).toBeNull();
    expect(x.host.textContent).toContain('Selecciona al menos una fuente');x.h.expectNone(()=>true);x.f.destroy();
  });
  it('loads native and segmented cache-only hits without a token or paid requests',async()=>{
    for(const adaptiveBatchId of [null,batch]){const x=setup();cacheOnly(x);const p=x.c.check();
      x.h.expectOne(pathIs(sam+'/reusable')).flush({...reusable,adaptiveBatchId});metadata(x);await p;
      expect(x.c.canProcess()).toBeTrue();expect(x.c.displayedCost()).toBe(0);expect(x.con.hasToken()).toBeFalse();
      const emit=spyOn(x.c.cacheUpdated,'emit');await x.c.process();
      expect(emit).toHaveBeenCalledWith(jasmine.objectContaining({sourceSha256:sha,analysisId:batch,adaptiveBatchId:adaptiveBatchId??undefined}));
      x.h.expectNone(()=>true);x.f.destroy();}
  });
  it('stops on a cache-only miss without a key request or fallback to Meta',async()=>{
    const x=setup();cacheOnly(x);const p=x.c.check();x.h.expectOne(pathIs(sam+'/reusable')).flush(miss);metadata(x);await p;await x.c.process();x.f.detectChanges();
    expect(x.c.canProcess()).toBeFalse();expect(x.c.displayedCost()).toBe(0);expect(x.c.adaptiveQuote()).toBeNull();
    expect(x.c.readiness()).toBeNull();expect(x.c.groundingQuote()).toBeNull();expect(x.host.querySelector('.sam-token-needed')).toBeNull();
    expect(x.host.textContent).toContain('No hay un resultado guardado válido');x.h.expectNone(()=>true);x.f.destroy();
  });
  it('does not bill when cached evidence is invalid or its lookup fails',async()=>{
    for(const bad of ['quality','source','http']){const x=setup(true);cacheOnly(x);const p=x.c.check();
      const r=x.h.expectOne(pathIs(sam+'/reusable'));
      if(bad==='http')r.flush({}, {status:409,statusText:'Conflict'});
      else r.flush({...reusable,...(bad==='quality'?{quality:{usable:false,reason:'INSUFFICIENT'}}:{sourceSha256:'f'.repeat(64)})});
      metadata(x);await p;await x.c.process();expect(x.c.canProcess()).toBeFalse();expect(x.c.error()).not.toBe('');
      x.h.expectNone(()=>true);x.f.destroy();}
  });
  it('keeps the mixed flow after a miss and propagates its mode',async()=>{
    const x=setup(true);select(x,'cache',true);const p=x.c.check();
    x.h.expectOne(pathIs(sam+'/reusable')).flush(miss);metadata(x);await tick();paidQuotes(x,'api-meta-cache');await p;
    expect(x.c.canProcess()).toBeTrue();expect(x.c.displayedCost()).toBeCloseTo(.374);x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
  it('explains the complete-video frame limit in both languages without submitting',async()=>{
    for(const language of ['es','en'] as const){
      const x=setup();x.f.componentRef.setInput('language',language);x.f.detectChanges();
      const p=x.c.check();metadata(x);await tick();
      x.h.expectOne(pathIs(adaptive+'/preflight')).flush({error:'SAM_SINGLE_VIDEO_FRAME_LIMIT',
        minimumFrames:50,maximumFrames:2500,automaticSplitting:false},{status:400,statusText:'Bad Request'});
      x.h.expectOne(pathIs(adaptive+'/spark-quote')).flush(sparkQuote);
      x.h.expectOne(r=>r.url.endsWith('/api/meta/readiness')).flush(access);await p;
      expect(x.c.canProcess()).toBeFalse();expect(x.c.error()).toContain('50');
      expect(x.c.error()).toContain('2500');
      expect(x.c.error()).toContain(language==='es'?'no se dividirá automáticamente':'will not be split automatically');
      x.h.expectNone(r=>r.method==='POST');x.f.destroy();
    }
  });

  it('discovers the failed SAM stage when returning to an API-only investigation without charging',async()=>{
    const x=setup(true);
    x.f.componentRef.setInput('autoCheck',true);x.f.detectChanges();
    const p=x.c.check();metadata(x);await tick();paidQuotes(x,'api-meta');await p;await tick();
    const retryId='f'.repeat(32),previousId='8'.repeat(32);
    const r=x.h.expectOne(adaptive+'/manual-stage-retry/quote');
    expect(r.request.method).toBe('GET');expect(r.request.body).toBeNull();
    r.flush({failedBatchId:previousId,newBatchId:retryId,
      videoSha256:sha,sparkSceneKey:scene,sceneRunId:runId,planSha256:plan,
      frames:120,estimatedSamUsd:.024,targetCoreFrames:53,
      alreadyRequested:false,previousMayHaveBeenBilled:true});
    await tick();x.f.detectChanges();
    expect(x.c.stageRetryQuote()?.newBatchId).toBe(retryId);
    expect(x.c.canProcess()).toBeFalse();
    expect(x.c.canStageRetry()).toBeTrue();
    expect(x.c.displayedCost()).toBeCloseTo(.024,8);
    expect(x.host.textContent).toContain('Retomar desde la etapa SAM');
    expect(x.host.textContent).toContain('US$ 0.03');
    x.h.expectNone(req=>req.method==='POST');x.f.destroy();
  });
  it('quotes one fresh complete-video request without consulting reusable evidence',async()=>{
    const x=setup(true);await checkFresh(x);expect(x.c.cachedResultAvailable()).toBeFalse();expect(x.c.canProcess()).toBeTrue();
    expect(x.c.displayedCost()).toBeCloseTo(.374);
    expect(x.c.adaptiveQuote()).toEqual(jasmine.objectContaining({parts:1,simultaneousRequests:1,
      submittedFrames:120,billableFrames:120,continuousRetryEstimatedUsd:0,maximumSamWithRetryEstimatedUsd:.024}));
    x.h.expectNone(pathIs(sam+'/reusable'));x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
  it('rejects stale multi-request, retry and mismatched fresh quotes before any POST',async()=>{
    for(const q of [{...freshQuote,mode:'api-meta-cache'},{...exactQuote,sceneRunId:null},
      {...freshQuote,parts:2},{...freshQuote,simultaneousRequests:2},
      {...freshQuote,submittedFrames:121},{...freshQuote,billableFrames:121},
      {...freshQuote,continuousRetryEstimatedUsd:.024,maximumSamWithRetryEstimatedUsd:.048}]){
      const x=setup(true);await checkFresh(x,q);expect(x.c.canProcess()).toBeFalse();await x.c.process();x.h.expectNone(r=>r.method==='POST');x.f.destroy();}
  });
  it('discards old API quotes after switching to cache-only',async()=>{
    const x=setup(true),old=x.c.check(),stale=x.h.expectOne(pathIs(sam+'/preflight'));
    cacheOnly(x);const p=x.c.check();x.h.expectOne(pathIs(sam+'/reusable')).flush(reusable);metadata(x);await p;
    stale.flush(media);await old;expect(x.c.mode()).toBe('cache-only');expect(x.c.canProcess()).toBeTrue();
    expect(x.c.displayedCost()).toBe(0);expect(x.c.adaptiveQuote()).toBeNull();expect(x.c.readiness()).toBeNull();x.h.expectNone(()=>true);x.f.destroy();
  });
  it('pins exact Spark evidence to one full-video request in fresh and mixed modes',async()=>{
    for(const mode of ['api-meta','api-meta-cache'] as const){
      const x=setup(true);if(mode==='api-meta-cache')select(x,'cache',true);
      const checking=x.c.check();if(mode==='api-meta-cache')x.h.expectOne(pathIs(sam+'/reusable')).flush(miss);
      metadata(x);await tick();paidQuotes(x,mode,{...freshQuote,mode},{...sparkQuote,mode});await checking;
      const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
      const spark=x.h.expectOne(pathIs(adaptive+'/spark-once'));expect(spark.request.body.mode).toBe(mode);
      expect(spark.request.body.maximumEstimatedCostUsd).toBe(.35);x.f.detectChanges();
      expect(checkbox(x,'api').disabled).toBeTrue();expect(checkbox(x,'cache').disabled).toBeTrue();
      checkbox(x,'cache').click();expect(x.c.mode()).toBe(mode);
      await x.c.process();x.h.expectNone(r=>r.method==='POST');
      spark.flush({...located,mode});await tick();
      const exact=x.h.expectOne(pathIs(adaptive+'/preflight'));
      expect(query(exact.request,'mode')).toBe(mode);expect(query(exact.request,'sceneRunId')).toBe(runId);
      expect(query(exact.request,'targetCoreFrames')).toBeNull();exact.flush({...exactQuote,mode});await tick();
      const request=x.h.expectOne(pathIs(adaptive+'/manual-batch'));
      expect(request.request.body.mode).toBe(mode);expect(request.request.body.sceneRunId).toBe(runId);
      expect(request.request.body.sceneKey).toBe(scene);expect(request.request.body.clientBatchId).toBe(batch);
      expect(request.request.body.maxInFlight).toBe(1);expect(request.request.body.targetCoreFrames).toBeUndefined();
      expect(request.request.body.confirmOneContinuousRetry).toBeFalse();
      expect(request.request.body.maximumSamCostUsd).toBe(.024);
      expect(request.request.body.maximumCombinedSamCostUsd).toBe(.024);
      request.flush(done);await tick();
      x.h.expectOne(pathIs(adaptive+'/batches/'+batch+'/progress')).flush({
        batchId:batch,parts:1,started:1,completed:1,failed:0,phase:'COMPLETED',
        preparationElapsedMs:200,validationElapsedMs:30,totalElapsedMs:1430,
        perPart:[{part:0,state:'COMPLETED',elapsedMs:1200,events:2,deltas:1,outputChars:40}]
      });await p;
      expect(emit).toHaveBeenCalledOnceWith(jasmine.objectContaining({sourceSha256:sha,analysisId:batch,adaptiveBatchId:batch}));
      x.f.componentRef.setInput('validationState','ready');x.f.detectChanges();
      expect(x.c.processing()).toBeFalse();expect(x.con.busy()).toBeFalse();
      const rows=x.c.stageRows();
      expect(rows.some(r=>r.key.startsWith('spark-round-')||/×1[.,]5|×3/.test(r.label))).toBeFalse();
      expect(rows.filter(r=>/^sam-\d+$/.test(r.key)).length).toBe(1);
      expect(rows.find(r=>r.key==='sam-0')).toEqual(jasmine.objectContaining({state:'COMPLETED',ms:1200,label:'SAM · video completo'}));
      expect(rows.find(r=>r.key==='validation')).toEqual(jasmine.objectContaining({state:'COMPLETED',ms:30}));
      expect(rows.some(r=>r.key==='join'||r.key.startsWith('retry-'))).toBeFalse();
      x.h.expectNone(pathIs(sam+'/reusable'));x.h.expectNone(r=>r.method==='POST');x.f.destroy();
    }
  });
});
