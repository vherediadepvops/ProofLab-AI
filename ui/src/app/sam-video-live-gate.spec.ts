import {TestBed} from '@angular/core/testing';
import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController,provideHttpClientTesting,TestRequest} from '@angular/common/http/testing';
import {SamVideoReplay} from './sam-video-replay';
import {LabConnection} from './lab-connection';
const sha='a'.repeat(64),id='b'.repeat(32),key='c'.repeat(64);
const base='http://127.0.0.1:5391/api/science/director/pendulum/sessions/'+id;
const fake='LLM_SYNTHETIC_LOCAL_TEST_KEY_1234567890';
const pf={sourceSha256:sha,sourceFrames:1875,durationSeconds:31.28,estimatedCostUsd:.375,
  supportsSingleCall:true,codec:'hevc',timingSource:'CONTAINER_DECLARED_FRAME_COUNT_ESTIMATE',framePtsSha256:''};
const ready={apiKeyConfigured:false,liveCallsPermitted:false,manualTokenCallsPermitted:true,
  paidRequestsSwitchEnabled:true,creditsConfirmedByOwner:true,ownerApprovedPaidRequests:true,usedRequests:1,remainingRequests:1};
function state(extra:any={}) {return {status:'NOT_ANALYZED_WITH_SAM',sourceSha256:sha,
  cacheKey:key,sourceFrames:0,estimatedCostUsd:0,canRunLive:false,provenance:'test',
  mode:'api-meta-cache',cacheHit:false,analysisId:null,...extra};}
const cached=()=>state({status:'CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW',cacheHit:true,analysisId:'synthetic-analysis'});

describe('SAM guided manual processing and parallel quotes',()=>{
  beforeEach(async()=>{await TestBed.configureTestingModule({imports:[SamVideoReplay],
    providers:[provideHttpClient(),provideHttpClientTesting()]}).compileComponents();});
  afterEach(()=>TestBed.inject(HttpTestingController).verify());
  function setup(auto=false) {
    const f=TestBed.createComponent(SamVideoReplay);
    f.componentRef.setInput('autoCheck',auto);f.componentRef.setInput('studyId',id);
    f.componentRef.setInput('videoSha256',sha);f.detectChanges();
    const c=f.componentInstance,h=TestBed.inject(HttpTestingController),con=TestBed.inject(LabConnection);
    const field=document.createElement('input');field.type='password';con.register(field);
    return {f,c,h,con,field};
  }
  type Context=ReturnType<typeof setup>;
  function token(ctx:Context){ctx.field.value=fake;ctx.con.confirmToken();}
  function requests(ctx:Context):TestRequest[] {
    const all=ctx.h.match(()=>true);expect(all.length).toBe(3);
    all.forEach(r=>{expect(r.request.method).toBe('GET');expect(r.request.body).toBeNull();
      expect(r.request.headers.has('Authorization')).toBeFalse();});
    return all;
  }
  function flush(all:TestRequest[],options:{status?:any;quote?:any;readiness?:any}={}) {
    for(const r of all) {
      if(r.request.url.includes('/status?'))r.flush(options.status??state());
      else if(r.request.url.endsWith('/readiness'))r.flush(options.readiness??ready);
      else r.flush(options.quote??pf);
    }
  }
  async function quote(ctx:Context,options:Parameters<typeof flush>[1]={}) {
    const p=ctx.c.check();flush(requests(ctx),options);await p;ctx.f.detectChanges();
  }
  it('starts three independent GETs together and keeps Process disabled until the cost arrives',async()=>{
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
    access.flush({}, {status:503,statusText:'Offline'});
    flush(all.filter(r=>r!==access),{status:cached()});await p;ctx.f.detectChanges();
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.f.nativeElement.querySelector('.free-cost').textContent).toContain('0.00');
    const emit=spyOn(ctx.c.cacheUpdated,'emit');await ctx.c.process();
    expect(emit).toHaveBeenCalledTimes(1);ctx.h.expectNone(r=>r.method==='POST');
  });
  it('API-only stays red and asks for a fresh result even when the same video has cache',async()=>{
    const ctx=setup();token(ctx);ctx.c.selectMode({target:{value:'api-meta'}} as unknown as Event);
    expect(ctx.con.hasToken()).toBeTrue();
    await quote(ctx,{status:state({status:'READY_FOR_FRESH_META_ANALYSIS',mode:'api-meta',analysisId:'old'})});
    expect(ctx.c.cachedResultAvailable()).toBeFalse();expect(ctx.c.canProcess()).toBeTrue();
    expect(ctx.f.nativeElement.querySelector('.paid-cost')).not.toBeNull();
    const p=ctx.c.process(),req=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
    expect(req.request.body.mode).toBe('api-meta');req.flush({...cached(),cacheHit:false,analysisId:'new'});await p;
  });
  it('processes a new video directly with one remaining request and only the SAM cost',async()=>{
    const ctx=setup();token(ctx);await quote(ctx,{status:state({recognitionRequired:false})});
    expect(ctx.c.canProcess()).toBeTrue();expect(ctx.c.displayedCost()).toBe(.375);
    expect(ctx.f.nativeElement.textContent).not.toContain('Reconocer montaje');
    expect(ctx.f.nativeElement.textContent).not.toContain('2 envíos');
    const p=ctx.c.process(),req=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
    expect(req.request.body.sceneKey).toBeNull();expect(req.request.body.maximumEstimatedCostUsd).toBe(.375);
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
      const p=ctx.c.process(),r=ctx.h.expectOne(base+'/sam/baseline/manual-one-shot');
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
});