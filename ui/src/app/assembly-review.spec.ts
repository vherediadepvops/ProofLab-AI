import {TestBed} from '@angular/core/testing';
import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController,provideHttpClientTesting} from '@angular/common/http/testing';
import {AssemblyReview,AssemblyEvidence,SceneDefinition} from './assembly-review';
import {LabConnection} from './lab-connection';
const sha='a'.repeat(64),key='b'.repeat(64);
const evidence:AssemblyEvidence={cacheKey:key,videoSha256:sha,model:'muse-spark-1.3',
  protocol:'ASSEMBLY_3_NATIVE_FRAMES_V1',
  frames:[0,1,2].map(i=>({index:i,timeSeconds:i+1,width:1080,height:1920,sha256:sha})),
  proposal:{arrangement:'simple_pendulum',suspension:{status:'CANDIDATE_REQUIRES_HUMAN_CONFIRMATION',x:500,y:100},
    observations:[0,1,2].flatMap(frame=>['object','cord','knot','pulley','support'].map(role=>({
      frame,role,visible:role!=='pulley',x1:400,y1:100,x2:500,y2:700,confidence:role==='pulley'?0:.9})))}};
const scene:SceneDefinition={key:'c'.repeat(64),videoSha256:sha,width:1080,height:1920,
  arrangement:'simple_pendulum',pivotX:500,pivotY:100,roles:[{id:'1',role:'object'}]};
describe('assembly review consent and identity',()=>{
  beforeEach(()=>TestBed.configureTestingModule({imports:[AssemblyReview],providers:[provideHttpClient(),provideHttpClientTesting()]}));
  afterEach(()=>TestBed.inject(HttpTestingController).verify());
  function init(){
    const f=TestBed.createComponent(AssemblyReview);
    f.componentRef.setInput('studyId','d'.repeat(32));f.componentRef.setInput('role','baseline');f.componentRef.setInput('sha',sha);f.detectChanges();
    return {f,c:f.componentInstance,h:TestBed.inject(HttpTestingController),con:TestBed.inject(LabConnection)};
  }
  async function quote(ctx:ReturnType<typeof init>,cached=true){
    const p=ctx.c.open();ctx.h.expectOne(r=>r.url.endsWith('/preflight')).flush({
      cacheKey:key,videoSha256:sha,maximumEstimatedCostUsd:.05,cacheHit:cached,
      priorAttempt:false,canRun:true,evidence:cached?evidence:null,approved:null});
    await p;ctx.f.detectChanges();
  }
  it('loads no images or model calls on creation; renders three original review frames',async()=>{
    const ctx=init();ctx.h.expectNone(()=>true);await quote(ctx);
    expect(ctx.f.nativeElement.querySelectorAll('svg').length).toBe(3);
    expect(ctx.c.available('pulley')).toBeFalse();expect(ctx.c.selected()).not.toContain('pulley');
    expect(ctx.c.boxes(1).map(o=>o.role)).toContain('object');
  });
  it('requires all three images, explicit review and the suspended object',async()=>{
    const ctx=init();await quote(ctx);ctx.c.reviewed.set(true);expect(ctx.c.canConfirm()).toBeFalse();
    [0,1,2].forEach(i=>ctx.c.imageReady(i));expect(ctx.c.canConfirm()).toBeTrue();
    ctx.c.toggle('object',false);ctx.c.reviewed.set(true);expect(ctx.c.canConfirm()).toBeFalse();
    ctx.c.toggle('object',true);expect(ctx.c.reviewed()).toBeFalse();
    ctx.c.reviewed.set(true);ctx.c.imageFailed();expect(ctx.c.canConfirm()).toBeFalse();
  });
  it('does not automatically start SAM after a review',async()=>{
    const ctx=init();await quote(ctx);[0,1,2].forEach(i=>ctx.c.imageReady(i));ctx.c.reviewed.set(true);
    const emit=spyOn(ctx.c.confirmed,'emit');const p=ctx.c.confirm();
    const req=ctx.h.expectOne(r=>r.url.endsWith('/confirm'));
    expect(req.request.headers.get('X-ProofLab-Manual-Action')).toBe('assembly');
    expect(req.request.body.roles).not.toContain('pulley');
    req.flush(scene);await p;expect(emit).toHaveBeenCalledOnceWith(scene);
    ctx.h.expectNone(r=>r.url.includes('/sam/'));expect(ctx.c.canConfirm()).toBeFalse();
  });
  it('takes a token once on a manually authorized bounded recognition',async()=>{
    const ctx=init();await quote(ctx,false);expect(ctx.c.canRecognize()).toBeFalse();
    const field=document.createElement('input');field.value='LLM_SYNTHETIC_OFFLINE_TEST_ONLY';
    ctx.con.register(field);ctx.con.confirmToken();ctx.c.consent.set(true);
    const p=ctx.c.recognize();expect(field.value).toBe('');expect(ctx.con.busy()).toBeTrue();
    const req=ctx.h.expectOne(r=>r.url.endsWith('/locate'));
    expect(req.request.body.maximumEstimatedCostUsd).toBe(.05);
    req.flush(evidence);await p;expect(ctx.con.busy()).toBeFalse();
    await ctx.c.recognize();ctx.h.expectNone(r=>r.method==='POST');
  });
  it('discards a stale review after changing video',async()=>{
    const ctx=init();const p=ctx.c.open();const req=ctx.h.expectOne(r=>r.url.endsWith('/preflight'));
    ctx.f.componentRef.setInput('sha','f'.repeat(64));ctx.f.detectChanges();
    req.flush({cacheKey:key,videoSha256:sha,maximumEstimatedCostUsd:.05,evidence});await p;
    expect(ctx.c.evidence()).toBeNull();expect(ctx.c.approved()).toBeNull();
  });
});
