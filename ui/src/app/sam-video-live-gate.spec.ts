import {TestBed} from '@angular/core/testing';
import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController,provideHttpClientTesting,TestRequest} from '@angular/common/http/testing';
import {SamVideoReplay} from './sam-video-replay';
import {LabConnection} from './lab-connection';
const sha='a'.repeat(64),id='b'.repeat(32),key='c'.repeat(64);
const base='http://127.0.0.1:5391/api/science/director/pendulum/sessions/'+id;
const sceneKey='d'.repeat(64),groundKey='e'.repeat(64);
const grounded={cacheKey:groundKey,videoSha256:sha,maximumEstimatedCostUsd:.25,mode:'api-meta-cache',freshRequired:false,
  protocol:'ASSEMBLY_FULL_VIDEO_GROUNDING_V2',
  cacheHit:true,priorAttempt:false,canRun:false,status:'READY',scene:{key:sceneKey,videoSha256:sha}};
const needsGrounding={...grounded,cacheHit:false,canRun:true,status:'NEEDS_LOCALIZATION',scene:null};
const groundUrl=base+'/assembly/baseline/grounding';
const unlimited={manualRequestsUnlimited:true};
const fake='LLM_SYNTHETIC_LOCAL_TEST_KEY_1234567890';
const pf={sourceSha256:sha,sourceFrames:1875,durationSeconds:31.28,estimatedCostUsd:.375,
  supportsSingleCall:true,codec:'hevc',timingSource:'CONTAINER_DECLARED_FRAME_COUNT_ESTIMATE',framePtsSha256:''};
const ready={apiKeyConfigured:false,liveCallsPermitted:false,manualTokenCallsPermitted:true,
  paidRequestsSwitchEnabled:true,creditsConfirmedByOwner:true,ownerApprovedPaidRequests:true,usedRequests:1,remainingRequests:1};
function state(extra:any={}) {return {status:'NOT_ANALYZED_WITH_SAM',sourceSha256:sha,
  cacheKey:key,sourceFrames:0,estimatedCostUsd:0,canRunLive:false,provenance:'test',
  mode:'api-meta-cache',cacheHit:false,analysisId:null,sceneKey,...extra};}
const cached=()=>state({status:'CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW',cacheHit:true,analysisId:'synthetic-analysis'});

describe('SAM guided manual processing and parallel quotes',()=>{
  beforeEach(async()=>{await TestBed.configureTestingModule({imports:[SamVideoReplay],
    providers:[provideHttpClient(),provideHttpClientTesting()]}).compileComponents();});
  afterEach(()=>TestBed.inject(HttpTestingController).verify());
  function setup(auto=false) {
    const f=TestBed.createComponent(SamVideoReplay);
    f.componentRef.setInput('autoCheck',auto);f.componentRef.setInput('studyId',id);
    f.componentRef.setInput('videoSha256',sha);f.detectChanges();
    const c=f.componentInstance;
  spyOn<any>(c,'pollSpark').and.callFake(async()=>{c.sparkStages.set(['prepare','references','upload','inference','verify'].map(key=>({key,state:'COMPLETED',elapsedMs:100,percent:100})));});
  const h=TestBed.inject(HttpTestingController),con=TestBed.inject(LabConnection);
    const field=document.createElement('input');field.type='password';con.register(field);
    return {f,c,h,con,field};
  }
  type Context=ReturnType<typeof setup>;
  function token(ctx:Context){ctx.field.value=fake;ctx.con.confirmToken();}
  function requests(ctx:Context):TestRequest[] {
    const all=ctx.h.match(()=>true);expect(all.length).toBe(4);
    all.forEach(r=>{expect(r.request.method).toBe('GET');expect(r.request.body).toBeNull();
      expect(r.request.headers.has('Authorization')).toBeFalse();});
    return all;
  }
  function flush(all:TestRequest[],options:{status?:any;quote?:any;readiness?:any;grounding?:any}={}) {
    for(const r of all) {
      if(r.request.url.includes('/status?'))r.flush(options.status??state());
      else if(r.request.url.endsWith('/readiness'))r.flush(options.readiness??ready);
      else if(r.request.url.includes('/grounding/'))r.flush(options.grounding??
        (r.request.url.endsWith('mode=api-meta')?{...needsGrounding,mode:'api-meta',freshRequired:true}:grounded));
      else r.flush(options.quote??pf);
    }
  }
  async function quote(ctx:Context,options:Parameters<typeof flush>[1]={}) {
    const p=ctx.c.check();flush(requests(ctx),options);await p;ctx.f.detectChanges();
  }


  it('offers one paid suspension review for cached SAM and reuses a completed review for free',async()=>{
    const ctx=setup();token(ctx);const url=base+'/sam/baseline/suspension-review';
    const p=ctx.c.check();
    flush(requests(ctx),{status:cached(),quote:{...pf,suspensionReviewMaximumCostUsd:.05}});
    await Promise.resolve();await Promise.resolve();
    const q={analysisId:'synthetic-analysis',required:true,priorAttempt:false,canRun:true,
      maximumEstimatedCostUsd:.05,reason:'TRACK_INCONSISTENT_WITH_SUSPENSION',evidence:null};
    await Promise.resolve();await Promise.resolve();
    ctx.h.expectOne(url).flush(q);await p;ctx.f.detectChanges();
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.c.displayedCost()).toBe(.05);
    expect(ctx.f.nativeElement.querySelector('.paid-cost')).not.toBeNull();
    const emit=spyOn(ctx.c.cacheUpdated,'emit'),run=ctx.c.process();
    ctx.h.expectOne(url).flush(q);await Promise.resolve();await Promise.resolve();
    const post=ctx.h.expectOne(url);expect(post.request.method).toBe('POST');
    post.flush({...q,required:false,evidence:{}});await run;
    expect(emit).toHaveBeenCalledTimes(1);expect(ctx.c.freeCacheAvailable()).toBeTrue();
    ctx.h.expectNone(r=>r.url.endsWith('/manual-one-shot')||r.url.includes('/grounding'));
  });

  it('includes a bounded conditional Spark review and invokes it only after SAM succeeds',async()=>{
    const ctx=setup();token(ctx);
    await quote(ctx,{quote:{...pf,suspensionReviewMaximumCostUsd:.05},readiness:{...ready,...unlimited}});
    expect(ctx.c.displayedCost()).toBeCloseTo(.425,8);
    const emit=spyOn(ctx.c.cacheUpdated,'emit'),p=ctx.c.process();
    ctx.h.expectOne(base+'/sam/baseline/manual-one-shot').flush(cached());
    await Promise.resolve();await Promise.resolve();
    const url=base+'/sam/baseline/suspension-review';
    ctx.h.expectOne(url).flush({analysisId:'synthetic-analysis',required:true,canRun:true,priorAttempt:false,
      maximumEstimatedCostUsd:.05,evidence:null,reason:'TRACK_INCONSISTENT_WITH_SUSPENSION'});
    await Promise.resolve();await Promise.resolve();
    const review=ctx.h.expectOne(url);
    expect(review.request.method).toBe('POST');
    expect(review.request.body.analysisId).toBe('synthetic-analysis');
    expect(review.request.body.maximumEstimatedCostUsd).toBe(.05);
    expect(review.request.headers.get('X-ProofLab-Manual-Action')).toBe('suspension-review');
    review.flush({analysisId:'synthetic-analysis',required:false,evidence:{},maximumEstimatedCostUsd:.05});
    await p;expect(emit).toHaveBeenCalledTimes(1);
  });
  it('preserves the SAM result when the separate Spark review fails and never retries',async()=>{
    const ctx=setup();token(ctx);
    await quote(ctx,{quote:{...pf,suspensionReviewMaximumCostUsd:.05},readiness:{...ready,...unlimited}});
    const emit=spyOn(ctx.c.cacheUpdated,'emit'),p=ctx.c.process();
    ctx.h.expectOne(base+'/sam/baseline/manual-one-shot').flush(cached());
    await Promise.resolve();await Promise.resolve();
    ctx.h.expectOne(base+'/sam/baseline/suspension-review').flush({}, {status:503,statusText:'Unavailable'});
    await p;expect(emit).toHaveBeenCalledTimes(1);
    expect(ctx.c.error()).toContain('SAM está guardado');ctx.h.expectNone(r=>r.method==='POST');
  });

  it('starts four independent GETs together and keeps Process disabled until the cost arrives',async()=>{
    const ctx=setup();token(ctx);const p=ctx.c.check();const all=requests(ctx);
    const cost=all.find(r=>r.request.url.includes('/sam/')&&r.request.url.endsWith('/preflight'))!;
    flush(all.filter(r=>r!==cost));await Promise.resolve();
    expect(ctx.c.canProcess()).toBeFalse();expect(ctx.c.checking()).toBeTrue();
    cost.flush(pf);await p;ctx.f.detectChanges();
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.field.value).toBe(fake);
    expect(ctx.f.nativeElement.querySelector('.paid-cost').textContent).toContain('0.38');
    ctx.h.expectNone(r=>r.method==='POST');
  });
  it('registration plus the visible Process click sends one request capped at the shown estimate',async()=>{
    const ctx=setup();await quote(ctx);expect(ctx.c.canProcess()).toBeFalse();token(ctx);
    expect(ctx.c.canProcess()).toBeTrue();
    const emit=spyOn(ctx.c.cacheUpdated,'emit');
    const p=ctx.c.process();const req=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
    expect(req.request.body.maximumEstimatedCostUsd).toBe(.375);
    expect(req.request.body.confirmPrivateVideoTransfer).toBeTrue();
    expect(req.request.body.confirmOneBillableSamCall).toBeTrue();
    expect(req.request.body.modelApiToken).toBe(fake);
    expect(req.request.headers.get('X-ProofLab-Manual-Action')).toBe('sam-one-shot');
    expect(ctx.con.busy()).toBeTrue();await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
    req.flush(cached());await p;
    expect(emit).toHaveBeenCalledTimes(1);expect(ctx.con.hasToken()).toBeTrue();expect(ctx.field.value).toBe(fake);
    expect(ctx.c.processing()).toBeTrue();
    ctx.f.componentRef.setInput('validationState','ready');ctx.f.detectChanges();
    expect(ctx.c.processing()).toBeFalse();expect(ctx.con.busy()).toBeFalse();
  });
  it('opens a verified cache for free without a token or billing readiness',async()=>{
    const ctx=setup();const p=ctx.c.check();const all=requests(ctx);
    const access=all.find(r=>r.request.url.endsWith('/readiness'))!;
    const ground=all.find(r=>r.request.url.includes('/grounding/'))!;
    access.flush({}, {status:503,statusText:'Offline'});ground.flush({}, {status:503,statusText:'Offline'});
    flush(all.filter(r=>r!==access && r!==ground),{status:cached()});await p;ctx.f.detectChanges();
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.f.nativeElement.querySelector('.free-cost').textContent).toContain('0.00');
    const emit=spyOn(ctx.c.cacheUpdated,'emit');await ctx.c.process();
    expect(emit).toHaveBeenCalledTimes(1);ctx.h.expectNone(r=>r.method==='POST');
  });
  it('API-only stays red and asks for a fresh result even when the same video has cache',async()=>{
    const ctx=setup();token(ctx);ctx.c.selectMode({target:{value:'api-meta'}} as unknown as Event);
    expect(ctx.con.hasToken()).toBeTrue();
    await quote(ctx,{status:state({status:'READY_FOR_FRESH_META_ANALYSIS',mode:'api-meta',analysisId:'old'}),
      readiness:{...ready,...unlimited}});
    expect(ctx.c.cachedResultAvailable()).toBeFalse();expect(ctx.c.canProcess()).toBeTrue();
    expect(ctx.f.nativeElement.querySelector('.paid-cost')).not.toBeNull();
    const p=ctx.c.process(),req=await freshSamRequest(ctx);
    expect(req.request.body.mode).toBe('api-meta');req.flush({...cached(),cacheHit:false,analysisId:'new'});await p;
  });
  it('reuses authenticated Spark localization with one remaining request and only the SAM cost',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{status:state({recognitionRequired:false})});
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.c.displayedCost()).toBe(.375);
    expect(ctx.f.nativeElement.textContent).not.toContain('Reconocer montaje');
    expect(ctx.f.nativeElement.textContent).not.toContain('2 envíos');
    const p=ctx.c.process(),req=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
    expect(req.request.body.sceneKey).toBe(sceneKey);expect(req.request.body.maximumEstimatedCostUsd).toBe(.375);
    ctx.h.expectNone(r=>r.url.includes('/assembly/'));req.flush(cached());await p;
  });
  it('still blocks spending when no requests remain',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{readiness:{...ready,remainingRequests:0,manualTokenCallsPermitted:false}});
    expect(ctx.c.canProcess()).toBeFalse();expect(ctx.c.processBlockedReason()).toContain('cupo');
    await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
  });
  it('allows repeated explicit manual clicks with zero remaining session allowance',async()=>{
    const ctx=setup();token(ctx);ctx.c.selectMode({target:{value:'api-meta'}} as unknown as Event);
    await quote(ctx,{status:state({status:'READY_FOR_FRESH_META_ANALYSIS',mode:'api-meta'}),
      readiness:{...ready,usedRequests:40,remainingRequests:0,manualRequestsUnlimited:true}});
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.f.nativeElement.textContent).not.toContain('Se agotó el cupo');
    ctx.h.expectNone(r=>r.method==='POST');
    for(let i=0;i<3;i++){
      const p=ctx.c.process(),r=await freshSamRequest(ctx);
      expect(r.request.body.maximumEstimatedCostUsd).toBe(.375);
      await ctx.c.process();ctx.h.expectNone(req=>req.method==='POST');
      r.flush({...cached(),cacheHit:false,mode:'api-meta',analysisId:'result-'+i});await p;
      ctx.f.componentRef.setInput('validationState','ready');ctx.f.detectChanges();
      await quote(ctx,{status:state({status:'READY_FOR_FRESH_META_ANALYSIS',mode:'api-meta'}),
        readiness:{...ready,usedRequests:41+i,remainingRequests:0,manualRequestsUnlimited:true}});
      ctx.f.componentRef.setInput('validationState','idle');ctx.f.detectChanges();
      expect(ctx.c.canProcess()).toBeTrue();ctx.h.expectNone(req=>req.method==='POST');
    }
  });
  it('unlimited count does not start work without a token or server permission',async()=>{
    const ctx=setup();await quote(ctx,{readiness:{...ready,remainingRequests:0,manualRequestsUnlimited:true}});
    expect(ctx.c.canProcess()).toBeFalse();token(ctx);expect(ctx.c.canProcess()).toBeTrue();
    await quote(ctx,{readiness:{...ready,remainingRequests:0,manualRequestsUnlimited:true,manualTokenCallsPermitted:false,paidRequestsSwitchEnabled:false}});
    expect(ctx.c.canProcess()).toBeFalse();await ctx.c.process();ctx.h.expectNone(req=>req.method==='POST');
  });
  it('explains server permission even with a registered token',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{readiness:{...ready,paidRequestsSwitchEnabled:false,
      creditsConfirmedByOwner:false,manualTokenCallsPermitted:false}});
    expect(ctx.c.canProcess()).toBeFalse();expect(ctx.c.processBlockedReason()).toContain('Token registrado.');
    await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
  });
  it('quotes automatically but never starts paid work on load or a mode change',async()=>{
    const ctx=setup(true);token(ctx);await Promise.resolve();flush(requests(ctx));
    await ctx.f.whenStable();expect(ctx.c.canProcess()).toBeTrue();ctx.h.expectNone(r=>r.method==='POST');
    ctx.c.selectMode({target:{value:'api-meta'}} as unknown as Event);
    expect(ctx.c.preflight()).toBeNull();expect(ctx.field.value).toBe(fake);
    await Promise.resolve();flush(requests(ctx),{status:state({mode:'api-meta'})});
    await ctx.f.whenStable();ctx.h.expectNone(r=>r.method==='POST');
  });
  it('discards old parallel replies after a video or server change',async()=>{
    const ctx=setup();token(ctx);const old=ctx.c.check(),oldRequests=requests(ctx);
    ctx.con.setPort(5393);expect(ctx.field.value).toBe('');
    const next=ctx.c.check(),newRequests=requests(ctx);
    flush(oldRequests,{status:cached()});await old;
    expect(ctx.c.cacheStatus()).toBeNull();expect(ctx.c.checking()).toBeTrue();
    flush(newRequests);await next;expect(ctx.c.checking()).toBeFalse();
    expect(ctx.c.localApiPort()).toBe(5393);
  });
  it('waits for both cost and original video readiness',async()=>{
    const ctx=setup();token(ctx);ctx.f.componentRef.setInput('mediaReady',false);ctx.f.detectChanges();
    await quote(ctx);expect(ctx.c.canProcess()).toBeFalse();
    ctx.f.componentRef.setInput('mediaReady',true);ctx.f.detectChanges();expect(ctx.c.canProcess()).toBeTrue();
    ctx.c.preflight.set(null);expect(ctx.c.canProcess()).toBeFalse();
  });
  it('fails closed on a mismatched source or failed estimate',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{status:cached(),quote:{...pf,sourceSha256:'f'.repeat(64)}});
    expect(ctx.c.canProcess()).toBeFalse();expect(ctx.c.cacheStatus()).toBeNull();
    const p=ctx.c.check(),all=requests(ctx);
    const cost=all.find(r=>r.request.url.includes('/sam/')&&r.request.url.endsWith('/preflight'))!;
    cost.flush({}, {status:503,statusText:'Unavailable'});flush(all.filter(r=>r!==cost),{status:cached()});
    await p;expect(ctx.c.canProcess()).toBeFalse();expect(ctx.c.preflight()).toBeNull();
  });
  it('never automatically retries a failed paid request and requires a fresh status check',async()=>{
    const ctx=setup();token(ctx);await quote(ctx);const p=ctx.c.process();
    ctx.h.expectOne(base+'/sam/baseline/manual-one-shot').flush({}, {status:502,statusText:'Synthetic failure'});
    await p;expect(ctx.c.error()).toContain('facturada');expect(ctx.c.cacheStatus()).toBeNull();
    expect(ctx.con.busy()).toBeFalse();expect(ctx.field.value).toBe(fake);
    await ctx.c.process();ctx.h.expectNone(()=>true);
  });
  it('requires the additional owner review for the single historical reconciled retry',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{status:state({status:'PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION'})});
    expect(ctx.c.canProcess()).toBeFalse();await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
    ctx.c.retryConsent.set(true);const p=ctx.c.process();
    const r=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
    expect(r.request.body.ownerApprovesOneReconciledRetry).toBeTrue();
    expect(r.request.body.previousAttemptBillingEvidence).toContain('OWNER_APPROVED_ONE_MORE');
    r.flush(cached());await p;
  });
  it('rejects a failed new analysis without offering a historical retry',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{status:state({status:'NEW_ANALYSIS_REQUIRES_MANUAL_RECONCILIATION'})});
    expect(ctx.c.canProcess()).toBeFalse();expect(ctx.f.nativeElement.querySelector('.sam-consent')).toBeNull();
    await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
  });
  it('ignores a paid result belonging to an earlier video without opening playback',async()=>{
    const ctx=setup();token(ctx);await quote(ctx);const emit=spyOn(ctx.c.cacheUpdated,'emit');
    const p=ctx.c.process(),r=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
    ctx.f.componentRef.setInput('videoSha256','e'.repeat(64));ctx.f.detectChanges();
    r.flush(cached());await p;expect(emit).not.toHaveBeenCalled();
    expect(ctx.con.busy()).toBeFalse();expect(ctx.c.cacheStatus()).toBeNull();
  });
  it('never changes server, token or mode during a paid request',async()=>{
    const ctx=setup();token(ctx);await quote(ctx);const p=ctx.c.process();
    const r=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
    expect(ctx.con.setPort(5393)).toBeFalse();expect(ctx.con.tokenForManualRequest()).toBe('');
    ctx.c.selectMode({target:{value:'api-meta'}} as unknown as Event);expect(ctx.c.mode()).toBe('api-meta-cache');
    r.flush(cached());await p;
  });
  async function freshSamRequest(ctx:Context):Promise<TestRequest> {
    const ground=ctx.h.expectOne(groundUrl);
    expect(ground.request.body.mode).toBe('api-meta');
    expect(ground.request.body.maximumEstimatedCostUsd).toBe(.25);
    ground.flush({...grounded,mode:'api-meta',cacheHit:false});
    await afterGrounding(ctx);
    return ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
  }
  async function afterGrounding(ctx:Context,options:{quote?:any;status?:any}={}) {
    await Promise.resolve();
    ctx.h.expectOne(base+'/sam/baseline/status?mode='+ctx.c.mode()).flush(options.status??state());
    ctx.h.expectOne(base+'/sam/baseline/preflight').flush(options.quote??pf);
    await Promise.resolve();
  }
  it('shows the combined maximum and runs Spark then SAM only after one explicit click',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding,readiness:{...ready,...unlimited}});
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.c.displayedCost()).toBeCloseTo(.625,6);
    expect(ctx.f.nativeElement.querySelector('.paid-cost').textContent).toContain('0.63');
    expect(ctx.f.nativeElement.textContent).toContain('Spark hasta US$ 0.25');
    expect(ctx.f.nativeElement.querySelector('app-assembly-review')).toBeNull();
    ctx.h.expectNone(r=>r.method==='POST');
    const p=ctx.c.process(),g=ctx.h.expectOne(groundUrl);
    expect(ctx.c.processingLabel()).toContain('Spark');
    expect(g.request.headers.get('X-ProofLab-Manual-Action')).toBe('assembly');
    expect(g.request.body).toEqual({mode:'api-meta-cache',videoSha256:sha,cacheKey:groundKey,modelApiToken:fake,
      confirmImageTransfer:true,confirmVideoTransfer:true,confirmPaidCall:true,maximumEstimatedCostUsd:.25});
    await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
    g.flush(grounded);await afterGrounding(ctx);
    const sam=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
    expect(ctx.c.processingLabel()).toContain('SAM');
    expect(sam.request.body.modelApiToken).toBe(fake);
    expect(sam.request.body.sceneKey).toBe(sceneKey);
    expect(sam.request.body.maximumEstimatedCostUsd).toBe(.375);
    sam.flush(cached());await p;expect(ctx.c.processingPhase()).toBe('validating');
  });
  it('shows a maximum of 0.75 when SAM costs 0.50 and Spark localization is needed',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{quote:{...pf,estimatedCostUsd:.5},
      grounding:needsGrounding,readiness:{...ready,...unlimited}});
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.c.displayedCost()).toBe(.75);
    expect(ctx.f.nativeElement.querySelector('.paid-cost').textContent).toContain('0.75');
    expect(ctx.c.nextActionDescription()).toContain('costo combinado estimado');
    ctx.h.expectNone(r=>r.method==='POST');
  });
  it('requires available allowance for both requests on legacy bounded sessions',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding});
    expect(ctx.c.canProcess()).toBeFalse();expect(ctx.c.processBlockedReason()).toContain('Spark y SAM');
    await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
  });
  for(const status of ['AMBIGUOUS','NOT_VISIBLE']) {
    it('stops before SAM when Spark returns '+status,async()=>{
      const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding,readiness:{...ready,...unlimited}});
      const p=ctx.c.process();ctx.h.expectOne(groundUrl).flush({...grounded,status,scene:null});
      await p;expect(ctx.c.error()).toContain('SAM no se envió');expect(ctx.con.busy()).toBeFalse();
      expect(ctx.c.canProcess()).toBeFalse();await ctx.c.process();ctx.h.expectNone(()=>true);
    });
    it('does not repeat a saved '+status+' localization',async()=>{
      const ctx=setup();token(ctx);await quote(ctx,{grounding:{...grounded,status,scene:null},readiness:{...ready,...unlimited}});
      expect(ctx.c.canProcess()).toBeFalse();await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
    });
  }
  it('does not automatically retry a failed Spark request or continue to SAM',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding,readiness:{...ready,...unlimited}});
    const p=ctx.c.process();ctx.h.expectOne(groundUrl).flush({}, {status:502,statusText:'Synthetic failure'});
    await p;expect(ctx.c.error()).toContain('SAM no se envió');expect(ctx.c.error()).toContain('facturada');
    expect(ctx.c.canProcess()).toBeFalse();expect(ctx.con.busy()).toBeFalse();
    await ctx.c.process();ctx.h.expectNone(()=>true);
  });
  for(const history of ['PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION',
    'SECOND_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION','NEW_ANALYSIS_REQUIRES_MANUAL_RECONCILIATION']) {
    it('offers a new explicit API Meta analysis while retaining '+history,async()=>{
      const ctx=setup();ctx.c.mode.set('api-meta');
      const current=state({status:'READY_FOR_FRESH_META_ANALYSIS',mode:'api-meta',previousAttemptStatus:history});
      await quote(ctx,{status:current,readiness:{...ready,...unlimited}});
      expect(ctx.c.canProcess()).toBeFalse();token(ctx);ctx.f.detectChanges();
      expect(ctx.c.canProcess()).toBeTrue();
      expect(ctx.c.nextActionDescription()).toContain('análisis nuevo');
      expect(ctx.f.nativeElement.querySelector('.sam-consent')).toBeNull();
      ctx.h.expectNone(r=>r.method==='POST');
      const p=ctx.c.process(),ground=ctx.h.expectOne(groundUrl);
      await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
      ground.flush({...grounded,mode:'api-meta',cacheHit:false});
      await afterGrounding(ctx,{status:current});
      const sam=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
      expect(sam.request.body.ownerApprovesOneReconciledRetry).toBeFalse();
      expect(sam.request.body.confirmOneBillableSamCall).toBeTrue();
      expect(sam.request.body.mode).toBe('api-meta');
      expect(sam.request.body.maximumEstimatedCostUsd).toBe(pf.estimatedCostUsd);
      sam.flush({...cached(),mode:'api-meta',cacheHit:false});await p;
      ctx.h.expectNone(r=>r.method==='POST');
    });
  }
  it('blocks any new Spark request when a previous SAM attempt requires reconciliation',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding,
      status:state({status:'PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION'}),readiness:{...ready,...unlimited}});
    ctx.c.retryConsent.set(true);expect(ctx.c.canProcess()).toBeFalse();
    expect(ctx.c.processBlockedReason()).toContain('revisión manual');
    expect(ctx.f.nativeElement.querySelector('.sam-consent')).toBeNull();
    await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
  });
  it('rejects a localization quote belonging to another video',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:{...grounded,videoSha256:'f'.repeat(64)}});
    expect(ctx.c.canProcess()).toBeFalse();expect(ctx.c.cacheStatus()).toBeNull();
    await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
  });
  it('discards Spark completion after the selected video changed',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding,readiness:{...ready,...unlimited}});
    const emit=spyOn(ctx.c.cacheUpdated,'emit'),p=ctx.c.process(),g=ctx.h.expectOne(groundUrl);
    ctx.f.componentRef.setInput('videoSha256','f'.repeat(64));ctx.f.detectChanges();
    g.flush(grounded);await p;
    expect(emit).not.toHaveBeenCalled();expect(ctx.con.busy()).toBeFalse();ctx.h.expectNone(()=>true);
  });
  it('rejects a mismatched Spark cache key before any SAM work',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding,readiness:{...ready,...unlimited}});
    const p=ctx.c.process();ctx.h.expectOne(groundUrl).flush({...grounded,cacheKey:'f'.repeat(64)});
    await p;expect(ctx.c.error()).toContain('SAM no se envió');ctx.h.expectNone(()=>true);
  });
  it('requires a new explicit consent if the SAM estimate increases while Spark runs',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding,readiness:{...ready,...unlimited}});
    const p=ctx.c.process();ctx.h.expectOne(groundUrl).flush(grounded);
    await afterGrounding(ctx,{quote:{...pf,estimatedCostUsd:.4}});
    await p;expect(ctx.c.error()).toContain('SAM no se envió');expect(ctx.c.canProcess()).toBeFalse();
    ctx.h.expectNone(r=>r.method==='POST');
  });
  it('does not send SAM when localization or quote identities diverge',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding,readiness:{...ready,...unlimited}});
    const p=ctx.c.process();ctx.h.expectOne(groundUrl).flush(grounded);
    await afterGrounding(ctx,{status:state({sceneKey:'f'.repeat(64)})});
    await p;expect(ctx.c.error()).toContain('SAM no se envió');ctx.h.expectNone(r=>r.method==='POST');
  });
  it('reuses a verified SAM result created during Spark without a second paid request',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding,readiness:{...ready,...unlimited}});
    const emit=spyOn(ctx.c.cacheUpdated,'emit'),p=ctx.c.process();ctx.h.expectOne(groundUrl).flush(grounded);
    await afterGrounding(ctx,{status:cached()});await p;
    expect(emit).toHaveBeenCalledTimes(1);ctx.h.expectNone(r=>r.method==='POST');
  });

  it('API-only rejects a cached Spark preflight and never silently reuses its scene',async()=>{
    const ctx=setup();token(ctx);ctx.c.selectMode({target:{value:'api-meta'}} as unknown as Event);
    await quote(ctx,{status:state({status:'READY_FOR_FRESH_META_ANALYSIS',mode:'api-meta'}),
      grounding:{...grounded,mode:'api-meta'},readiness:{...ready,...unlimited}});
    expect(ctx.c.canProcess()).toBeFalse();await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
  });
  it('uses the configurable Spark maximum and full-video consent before the click',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:{...needsGrounding,maximumEstimatedCostUsd:.4},
      readiness:{...ready,...unlimited}});
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.c.displayedCost()).toBeCloseTo(.775,6);
    expect(ctx.f.nativeElement.querySelector('.paid-cost').textContent).toContain('Estimado US$ 0.78');
    expect(ctx.c.nextActionDescription()).toContain('video completo y sus imágenes de referencia a Spark');
    expect(ctx.c.nextActionDescription()).not.toContain('tres fotogramas');
    const p=ctx.c.process(),r=ctx.h.expectOne(groundUrl);
    expect(r.request.body.maximumEstimatedCostUsd).toBe(.4);
    r.flush({}, {status:502,statusText:'Synthetic failure'});await p;ctx.h.expectNone(r=>r.method==='POST');
  });
  it('rejects stale protocol and mismatched mode on a paid preflight',async()=>{
    const ctx=setup();token(ctx);
    for(const bad of [{...needsGrounding,protocol:'ASSEMBLY_THREE_FRAME_V1'},
      {...needsGrounding,mode:'api-meta'}, {...needsGrounding,maximumEstimatedCostUsd:1.01}]){
      await quote(ctx,{grounding:bad,readiness:{...ready,...unlimited}});
      expect(ctx.c.canProcess()).toBeFalse();await ctx.c.process();ctx.h.expectNone(r=>r.method==='POST');
    }
  });
  it('preserves historical Spark cache compatibility without another Spark request',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:{...grounded,
      evidence:{protocol:'ASSEMBLY_THREE_FRAME_V1'}},readiness:{...ready,...unlimited}});
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.c.displayedCost()).toBe(.375);
    const p=ctx.c.process();const r=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
    ctx.h.expectNone(groundUrl);r.flush(cached());await p;
  });

  it('explains the exact no-inference 409 without implying a paid Spark request',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{grounding:needsGrounding,readiness:{...ready,...unlimited}});
    const failed=spyOn(ctx.c.processingFailed,'emit'),p=ctx.c.process();
    ctx.h.expectOne(groundUrl).flush({error:'GROUNDING_NO_INFERENCE_SENT_REVIEW_COST'},
      {status:409,statusText:'Conflict'});
    await p;expect(ctx.c.error()).toContain('Spark no llegó a ejecutar la inferencia');
    expect(ctx.c.error()).toContain('SAM no se envió');expect(ctx.c.error()).not.toContain('facturada');
    expect(failed).toHaveBeenCalledTimes(1);expect(ctx.c.processing()).toBeFalse();
    expect(ctx.con.busy()).toBeFalse();expect(ctx.c.canProcess()).toBeFalse();
    await ctx.c.process();ctx.h.expectNone(()=>true);
  });
  it('does not reinterpret the no-inference code outside the Spark phase',async()=>{
    const ctx=setup();token(ctx);await quote(ctx);const p=ctx.c.process();
    ctx.h.expectOne(base+'/sam/baseline/manual-one-shot').flush(
      {error:'GROUNDING_NO_INFERENCE_SENT_REVIEW_COST'}, {status:409,statusText:'Conflict'});
    await p;expect(ctx.c.error()).toContain('facturada');
    expect(ctx.c.error()).not.toContain('Spark no llegó');ctx.h.expectNone(()=>true);
  });

});
