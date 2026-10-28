import {TestBed} from '@angular/core/testing';
import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController,provideHttpClientTesting} from '@angular/common/http/testing';
import {KnotReview,KnotEvidence} from './knot-review';
import {LabConnection} from './lab-connection';

const sha='a'.repeat(64),key='b'.repeat(64);
const evidence:KnotEvidence={cacheKey:key,videoSha256:sha,model:'muse-spark-1.3',
  protocol:'ROPE_EXIT_3_NATIVE_FRAMES_V1',
  frames:[0,1,2].map(i=>({index:i,timeSeconds:i+1,width:1080,height:1920,sha256:sha})),
  proposal:{status:'CANDIDATE_REQUIRES_HUMAN_CONFIRMATION',x:500,y:133.3,spreadPixels:0}};
describe('Meta knot review',()=>{
  beforeEach(()=>TestBed.configureTestingModule({imports:[KnotReview],
    providers:[provideHttpClient(),provideHttpClientTesting()]}));
  afterEach(()=>TestBed.inject(HttpTestingController).verify());
  function init(){
    const f=TestBed.createComponent(KnotReview);
    f.componentRef.setInput('studyId','c'.repeat(32));f.componentRef.setInput('role','baseline');
    f.componentRef.setInput('sha',sha);f.detectChanges();
    return {f,c:f.componentInstance,h:TestBed.inject(HttpTestingController),con:TestBed.inject(LabConnection)};
  }
  async function quote(ctx:ReturnType<typeof init>,cached=false,canRun=true){
    const p=ctx.c.open();
    ctx.h.expectOne(r=>r.url.endsWith('/preflight')).flush({cacheKey:key,videoSha256:sha,
      maximumEstimatedCostUsd:.05,cacheHit:cached,priorAttempt:false,canRun,evidence:cached?evidence:null});
    await p;ctx.f.detectChanges();
  }
  it('does no request on creation and cached evidence needs neither token nor credit gates',async()=>{
    const ctx=init();ctx.h.expectNone(()=>true);await quote(ctx,true,false);
    expect(ctx.c.candidate()).toBeTrue();expect(ctx.c.canLocate()).toBeFalse();
    expect(ctx.f.nativeElement.querySelectorAll('svg').length).toBe(3);
    expect(ctx.c.point()!.y).toBeCloseTo(133.3);
  });
  it('requires source quote, manual consent, token and server allowance',async()=>{
    const ctx=init();await quote(ctx);expect(ctx.c.canLocate()).toBeFalse();
    const field=document.createElement('input');field.value='LLM_SYNTHETIC_OFFLINE_TEST_ONLY';
    ctx.con.register(field);ctx.con.confirmToken();expect(ctx.c.canLocate()).toBeFalse();
    ctx.c.consent.set(true);expect(ctx.c.canLocate()).toBeTrue();
    ctx.con.busy.set(true);expect(ctx.c.canLocate()).toBeFalse();ctx.con.busy.set(false);
  });
  it('consumes the key exactly on the manual click and sends one bounded request',async()=>{
    const ctx=init();await quote(ctx);
    const field=document.createElement('input');field.value='LLM_SYNTHETIC_OFFLINE_TEST_ONLY';
    ctx.con.register(field);ctx.con.confirmToken();ctx.c.consent.set(true);
    const p=ctx.c.locate();expect(field.value).toBe('');expect(ctx.con.busy()).toBeTrue();
    const req=ctx.h.expectOne(r=>r.url.endsWith('/locate'));
    expect(req.request.body.maximumEstimatedCostUsd).toBe(.05);
    expect(req.request.headers.get('X-ProofLab-Manual-Action')).toBe('knot');
    req.flush(evidence);await p;
    expect(ctx.con.busy()).toBeFalse();expect(ctx.c.candidate()).toBeTrue();
    await ctx.c.locate();ctx.h.expectNone(r=>r.method==='POST');
  });
  it('never applies a point before all review images and explicit confirmation',async()=>{
    const ctx=init();await quote(ctx,true);
    ctx.c.reviewed.set(true);await ctx.c.confirm();ctx.h.expectNone(r=>r.method==='POST');
    for(let i=0;i<3;i++)ctx.c.imageReady(i);
    ctx.c.nudge(new KeyboardEvent('keydown',{key:'ArrowRight'}),0);
    expect(ctx.c.reviewed()).toBeFalse();
    expect(ctx.c.point()!.x).toBeCloseTo(500+1000/1080);
    ctx.c.reviewed.set(true);const p=ctx.c.confirm();
    const req=ctx.h.expectOne(r=>r.url.endsWith('/confirm'));
    expect(req.request.body.confirmRopeExit).toBeTrue();expect(req.request.body.videoSha256).toBe(sha);
    req.flush({});await p;
  });
  it('blocks a failed review image and does not guess uncertain coordinates',async()=>{
    const ctx=init();const p=ctx.c.open();
    ctx.h.expectOne(r=>r.url.endsWith('/preflight')).flush({cacheKey:key,videoSha256:sha,
      maximumEstimatedCostUsd:.05,cacheHit:true,priorAttempt:false,canRun:false,
      evidence:{...evidence,proposal:{status:'UNCERTAIN_MANUAL_SELECTION_REQUIRED',x:null,y:null,spreadPixels:0}}});
    await p;expect(ctx.c.candidate()).toBeFalse();
    ctx.c.imageReady(0);ctx.c.imageFailed();expect(ctx.c.loadedImages()).toEqual([]);
    await ctx.c.confirm();ctx.h.expectNone(r=>r.method==='POST');
  });
  it('discards stale responses after switching video and can open the next one',async()=>{
    const ctx=init();const p=ctx.c.open();const old=ctx.h.expectOne(r=>r.url.endsWith('/preflight'));
    ctx.f.componentRef.setInput('sha','d'.repeat(64));ctx.f.detectChanges();
    old.flush({videoSha256:sha,maximumEstimatedCostUsd:.05,cacheHit:true,evidence});
    await p;expect(ctx.c.evidence()).toBeNull();expect(ctx.c.busy()).toBeFalse();
  });
  it('uses the selected API port and preserves proposal on language change',async()=>{
    const ctx=init();ctx.f.componentRef.setInput('port',5398);ctx.f.detectChanges();
    const p=ctx.c.open();ctx.h.expectOne(r=>r.url.startsWith('http://127.0.0.1:5398/')).flush({
      cacheKey:key,videoSha256:sha,maximumEstimatedCostUsd:.05,cacheHit:true,
      priorAttempt:false,canRun:false,evidence});
    await p;ctx.f.componentRef.setInput('language','en');ctx.f.detectChanges();
    expect(ctx.c.candidate()).toBeTrue();expect(ctx.f.nativeElement.textContent).toContain('Check the cross');
  });
});
