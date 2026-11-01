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
  planSha256:plan,suggestedBatchId:batch,sourceFrames:120,parts:3,simultaneousRequests:3,
  submittedFrames:140,billableFrames:140,totalSamEstimatedUsd:.028,maximumAllowedSamUsd:.50,
  remainingTrialSlots:null,canRunWithManualToken:true,existingSparkSceneVerified:false,requiresNewSparkCall:true,
  idealPartsWithoutSparkCoverage:3,verifiedSparkSeedCount:0,reducedForSparseSparkSeeds:false,sceneRunId:null};
const sparkQuote={cacheKey:'3'.repeat(64),videoSha256:sha,maximumEstimatedCostUsd:.25,mode:'api-meta',
  freshRequired:true,protocol:'ASSEMBLY_FULL_VIDEO_GROUNDING_V2',cacheHit:false,priorAttempt:false,
  canRun:true,status:'NEEDS_LOCALIZATION',scene:null};
const located={...sparkQuote,freshRequired:false,canRun:false,status:'READY',scene:{key:scene,videoSha256:sha},evidence:{runId}};
const exactQuote={...freshQuote,sparkSceneKey:scene,sceneRunId:runId,existingSparkSceneVerified:true,
  requiresNewSparkCall:false,verifiedSparkSeedCount:3};
const reusable={sourceSha256:sha,cacheHit:true,analysisId:batch,adaptiveBatchId:batch,excludedResults:0,
  quality:{usable:true,reason:'MEASUREMENTS_AVAILABLE'},storage:'sqlite'};
const miss={...reusable,cacheHit:false,analysisId:null,adaptiveBatchId:null,quality:null,excludedResults:1};
const done={batchId:batch,sourceSha256:sha,sparkSceneKey:scene,planSha256:plan,parts:3,startedParts:3,
  completedParts:3,failedParts:0,skippedParts:0,estimatedSamUsd:.028,
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
    const q=x.h.expectOne(pathIs(adaptive+'/preflight'));expect(q.request.method).toBe('GET');expect(query(q.request,'mode')).toBe(mode);q.flush(quote as object);
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
    expect(x.c.canProcess()).toBeTrue();expect(x.c.displayedCost()).toBeCloseTo(.278);x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
  it('quotes fresh segments without consulting reusable evidence',async()=>{
    const x=setup(true);await checkFresh(x);expect(x.c.cachedResultAvailable()).toBeFalse();expect(x.c.canProcess()).toBeTrue();
    expect(x.c.displayedCost()).toBeCloseTo(.278);x.h.expectNone(pathIs(sam+'/reusable'));x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
  it('rejects mixed or cached initial quotes in API-only mode',async()=>{
    for(const q of [{...freshQuote,mode:'api-meta-cache'},{...exactQuote,sceneRunId:null}]){
      const x=setup(true);await checkFresh(x,q);expect(x.c.canProcess()).toBeFalse();await x.c.process();x.h.expectNone(r=>r.method==='POST');x.f.destroy();}
  });
  it('discards old API quotes after switching to cache-only',async()=>{
    const x=setup(true),old=x.c.check(),stale=x.h.expectOne(pathIs(sam+'/preflight'));
    cacheOnly(x);const p=x.c.check();x.h.expectOne(pathIs(sam+'/reusable')).flush(reusable);metadata(x);await p;
    stale.flush(media);await old;expect(x.c.mode()).toBe('cache-only');expect(x.c.canProcess()).toBeTrue();
    expect(x.c.displayedCost()).toBe(0);expect(x.c.adaptiveQuote()).toBeNull();expect(x.c.readiness()).toBeNull();x.h.expectNone(()=>true);x.f.destroy();
  });
  it('pins fresh Spark evidence to the exact batch and freezes the source controls',async()=>{
    const x=setup(true);await checkFresh(x);const emit=spyOn(x.c.cacheUpdated,'emit'),p=x.c.process();
    const spark=x.h.expectOne(pathIs(adaptive+'/spark-once'));expect(spark.request.body.mode).toBe('api-meta');
    expect(spark.request.body.maximumEstimatedCostUsd).toBe(.25);x.f.detectChanges();
    expect(checkbox(x,'api').disabled).toBeTrue();expect(checkbox(x,'cache').disabled).toBeTrue();
    checkbox(x,'cache').click();expect(x.c.mode()).toBe('api-meta');spark.flush(located);await tick();
    const exact=x.h.expectOne(pathIs(adaptive+'/preflight'));expect(query(exact.request,'mode')).toBe('api-meta');expect(query(exact.request,'sceneRunId')).toBe(runId);
    exact.flush(exactQuote);await tick();const request=x.h.expectOne(pathIs(adaptive+'/manual-batch'));
    expect(request.request.body.mode).toBe('api-meta');expect(request.request.body.sceneRunId).toBe(runId);
    expect(request.request.body.sceneKey).toBe(scene);expect(request.request.body.clientBatchId).toBe(batch);request.flush(done);await tick();
    x.h.expectOne(pathIs(adaptive+'/batches/'+batch+'/progress')).flush({batchId:batch,parts:3,started:3,completed:3,failed:0,phase:'COMPLETED',perPart:[]});await p;
    expect(emit).toHaveBeenCalledWith(jasmine.objectContaining({sourceSha256:sha,analysisId:batch,adaptiveBatchId:batch}));
    x.f.componentRef.setInput('validationState','ready');x.f.detectChanges();expect(x.c.processing()).toBeFalse();expect(x.con.busy()).toBeFalse();
    x.h.expectNone(pathIs(sam+'/reusable'));x.h.expectNone(r=>r.method==='POST');x.f.destroy();
  });
});
