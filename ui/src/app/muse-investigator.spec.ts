import {TestBed} from '@angular/core/testing';
import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController,provideHttpClientTesting} from '@angular/common/http/testing';
import {MuseInvestigator} from './muse-investigator';
import {LabConnection} from './lab-connection';
import type {SamPeriodSource} from './sam-period';

describe('Muse investigator',()=>{
  const base='http://127.0.0.1:5391/api/science/director/pendulum/sessions/study/muse';
  const source:SamPeriodSource={studyId:'study',role:'baseline',originalVideoSha256:'video',
    analysisId:'analysis',providerResponseSha256:'response',providerModel:'sam-3.1',objectId:'0',
    pivotRevisionId:'pivot',pivotProvenance:'HUMAN_IMAGE'};
  const empty={observation:null,evaluation:null,prediction:null,attemptStatus:null,manualCallsPermitted:true,maximumEstimatedCostUsd:.05};
  const prepared={...empty,observation:{id:'observation',analysisId:'analysis',payloadJson:'{}',payloadSha256:'sha'}};
  let http:HttpTestingController;
  beforeEach(async()=>{
    await TestBed.configureTestingModule({imports:[MuseInvestigator],providers:[provideHttpClient(),provideHttpClientTesting()]}).compileComponents();
    http=TestBed.inject(HttpTestingController);
  });
  afterEach(()=>http.verify());
  async function init(state:object=empty) {
    const f=TestBed.createComponent(MuseInvestigator);f.componentRef.setInput('source',source);f.detectChanges();
    http.expectOne(base).flush(state);await f.whenStable();f.detectChanges();return f;
  }
  function token(){
    const c=TestBed.inject(LabConnection),field=document.createElement('input');
    field.type='password';field.value='LLM_SYNTHETIC_OFFLINE_TEST_ONLY';c.register(field);c.confirmToken();return c;
  }
  it('prepares only provenance and leaves paid processing disabled without a token',async()=>{
    const f=await init();const p=f.componentInstance.prepare();
    const r=http.expectOne(base+'/prepare');expect(r.request.body).toEqual({videoSha256:'video',analysisId:'analysis',pivotRevisionId:'pivot'});
    expect(r.request.headers.get('X-ProofLab-Manual-Action')).toBe('muse-research');
    r.flush(prepared);await p;expect(f.componentInstance.canEvaluate()).toBeFalse();f.destroy();
  });
  it('sends one explicit request and unlocks only the persisted prediction',async()=>{
    token();const f=await init(prepared);const received:unknown[]=[];f.componentInstance.frozen.subscribe(p=>received.push(p));
    const p=f.componentInstance.evaluate();expect(f.componentInstance.canEvaluate()).toBeFalse();
    void f.componentInstance.evaluate();
    const r=http.expectOne(base+'/evaluate');
    expect(r.request.body.maximumEstimatedCostUsd).toBe(.05);
    expect(r.request.body.confirmMeasuredDataTransfer).toBeTrue();
    const frozen={studyId:'study',observationId:'observation',responseId:'receipt',frozenAtUtc:'2026-10-28',
      sha256:'hash',payloadJson:'{"prediction":{"periodSeconds":1.79,"nextLengthMeters":0.8,"tolerancePercent":4}}'};
    r.flush({...prepared,prediction:frozen});await p;
    expect(received).toEqual([frozen]);expect(f.componentInstance.prediction()?.periodSeconds).toBe(1.79);
    expect(TestBed.inject(LabConnection).busy()).toBeFalse();f.destroy();
  });
  it('reconciles a failed request with a read, never a second paid request',async()=>{
    token();const f=await init(prepared);const p=f.componentInstance.evaluate();
    http.expectOne(base+'/evaluate').flush({}, {status:502,statusText:'Failed'});
    await Promise.resolve();http.expectOne(base).flush({...prepared,attemptStatus:'FAILED_REQUIRES_RECONCILIATION'});
    await p;expect(f.componentInstance.canEvaluate()).toBeFalse();expect(f.componentInstance.error()).toContain('no se reintentó');f.destroy();
  });
  it('does not accept an old source response after a different video is selected',async()=>{
    const f=await init();const p=f.componentInstance.prepare();const old=http.expectOne(base+'/prepare');
    f.componentRef.setInput('source',{...source,studyId:'next'});f.detectChanges();
    const next=http.expectOne(base.replace('/study/','/next/'));next.flush(empty);await f.whenStable();
    old.flush(prepared);await p;expect(f.componentInstance.state()?.observation).toBeNull();f.destroy();
  });
  it('keeps an inconclusive real response visible without a prediction or upload unlock',async()=>{
    const t={es:'Faltan datos.',en:'Data is missing.'};
    const f=await init({...prepared,evaluation:{model:'muse-spark-1.3',responseId:'receipt',tokenCostEstimateUsd:.001,
      answer:{decision:'INCONCLUSIVE',justification:t,limitations:t,protocol:t,hypothesis:t,falsification:t}}});
    expect(f.nativeElement.textContent).toContain('Inconcluso');expect(f.componentInstance.prediction()).toBeNull();
    f.componentRef.setInput('language','en');f.detectChanges();http.expectOne(base).flush(empty);await f.whenStable();f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Research lab log');f.destroy();
  });
  function response(classification?:'PENDULUM_COMPATIBLE'|'OTHER_MOTION'|'INCONCLUSIVE',analysisId='analysis') {
    const t={es:'Justificación científica registrada.',en:'Recorded scientific justification.'};
    return {...prepared,observation:{...prepared.observation,analysisId,videoSha256:'video'},
      evaluation:{model:'muse-spark-1.3',responseId:'recorded-muse-receipt',
        observationId:'observation',studyId:'study',rawResponseSha256:'hash',tokenCostEstimateUsd:.001,
        answer:{decision:classification==='OTHER_MOTION'?'INCONCLUSIVE':'PROPOSE_EXPERIMENT',
          justification:t,limitations:t,protocol:t,hypothesis:t,falsification:t,
          nextLengthMeters:null,periodSeconds:null,tolerancePercent:null,
          ...(classification?{phenomenon:{classification,justification:{
            es:'Spark interpreta la trayectoria medida por SAM.',en:'Spark interprets the trajectory measured by SAM.'}}}:{})}}};
  }
  it('waits for Spark instead of classifying from local measurements or preparing evidence',async()=>{
    const f=await init(prepared);
    expect(f.nativeElement.textContent).toContain('La clasificación del movimiento está pendiente');
    expect(f.componentInstance.phenomenon()).toBeNull();
    expect(f.nativeElement.querySelector('.decision')).toBeNull();
    expect(f.nativeElement.querySelectorAll('section').length).toBe(1);f.destroy();
  });
  it('attributes the recorded phenomenon and justification to Muse Spark in ES and EN',async()=>{
    const recorded=response('PENDULUM_COMPATIBLE'),f=await init(recorded);
    expect(f.nativeElement.querySelector('.decision').textContent).toContain('Muse Spark: compatible con un péndulo');
    expect(f.nativeElement.textContent).toContain('Spark interpreta la trayectoria medida por SAM.');
    expect(f.nativeElement.textContent).toContain('Experimento propuesto');
    expect(f.nativeElement.querySelectorAll('section').length).toBe(1);
    f.componentRef.setInput('language','en');f.detectChanges();http.expectOne(base).flush(recorded);
    await f.whenStable();f.detectChanges();
    expect(f.nativeElement.querySelector('.decision').textContent).toContain('Muse Spark: consistent with a pendulum');
    expect(f.nativeElement.textContent).toContain('Spark interprets the trajectory measured by SAM.');
    expect(f.nativeElement.textContent).toContain('recorded-muse-receipt');f.destroy();
  });
  for(const classification of ['OTHER_MOTION','INCONCLUSIVE'] as const)
    it('shows Spark classification '+classification+' without local substitution',async()=>{
      const f=await init(response(classification));
      expect(f.nativeElement.querySelector('.decision').textContent).toContain(
        classification==='OTHER_MOTION'?'Muse Spark: otro tipo de movimiento':'Muse Spark: clasificación inconclusa');
      expect(f.nativeElement.querySelector('.decision').textContent).not.toContain('compatible con un péndulo');
      f.destroy();
    });
  it('never infers a phenomenon from a historical experiment proposal',async()=>{
    const f=await init(response());
    expect(f.nativeElement.querySelector('.decision').textContent).toContain('Experimento propuesto');
    expect(f.nativeElement.textContent).toContain('no incluye una clasificación del fenómeno');
    expect(f.componentInstance.phenomenon()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Muse Spark: compatible con un péndulo');f.destroy();
  });
  it('does not present a previous analysis classification as belonging to the current video',async()=>{
    const f=await init(response('PENDULUM_COMPATIBLE','previous-analysis'));
    expect(f.nativeElement.querySelector('.decision').textContent).toContain('Evaluación de un análisis anterior');
    expect(f.nativeElement.textContent).toContain('no clasifica el video actual');
    expect(f.componentInstance.phenomenon()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Muse Spark: compatible con un péndulo');
    expect(f.componentInstance.canEvaluate()).toBeFalse();f.destroy();
  });
  it('rejects classification attribution if the stored observation refers to another video',async()=>{
    const recorded=response('PENDULUM_COMPATIBLE');
    recorded.observation.videoSha256='another-video';
    const f=await init(recorded);
    expect(f.componentInstance.phenomenon()).toBeNull();
    expect(f.nativeElement.textContent).toContain('no clasifica el video actual');f.destroy();
  });

});
