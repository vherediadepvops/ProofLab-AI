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

  for(const kind of ['report','contrast'] as const) {
    it('downloads '+kind+' as a local blob without leaving the laboratory',async()=>{
      const f=await init(),before=location.href;
      const create=spyOn(URL,'createObjectURL').and.returnValue('blob:http://localhost/fake-export');
      const click=spyOn(HTMLAnchorElement.prototype,'click');
      const p=f.componentInstance.downloadReport(kind);
      void f.componentInstance.downloadReport(kind);
      const request=http.expectOne(base+'/'+kind);
      expect(request.request.method).toBe('GET');
      expect(request.request.responseType).toBe('blob');
      request.flush(new Blob(['{"evidence":"stored"}'],{type:'application/json'}));await p;
      expect(create).toHaveBeenCalledTimes(1);expect(click).toHaveBeenCalledTimes(1);
      const anchor=click.calls.mostRecent().object as HTMLAnchorElement;
      expect(anchor.download).toBe('prooflab-muse-'+kind+'-study.json');
      expect(anchor.href).toBe('blob:http://localhost/fake-export');
      expect(anchor.isConnected).toBeFalse();expect(location.href).toBe(before);
      expect(f.componentInstance.downloading()).toBeFalse();f.destroy();
    });
  }
  it('keeps the lab visible and reports a failed download without retrying',async()=>{
    const f=await init(),create=spyOn(URL,'createObjectURL'),p=f.componentInstance.downloadReport('contrast');
    http.expectOne(base+'/contrast').flush(null,{status:500,statusText:'Unavailable'});await p;f.detectChanges();
    expect(create).not.toHaveBeenCalled();expect(f.nativeElement.textContent).toContain('Tus resultados siguen guardados');
    expect(f.componentInstance.downloading()).toBeFalse();f.destroy();
  });

  function token(){
    const c=TestBed.inject(LabConnection),field=document.createElement('input');
    field.type='password';field.value='LLM_SYNTHETIC_OFFLINE_TEST_ONLY';c.register(field);c.confirmToken();return c;
  }
  it('prepares only provenance and leaves paid processing disabled without a token',async()=>{
    const f=await init();const p=f.componentInstance.prepare();
    const r=http.expectOne(base+'/prepare');expect(r.request.body).toEqual({videoSha256:'video',analysisId:'analysis',pivotRevisionId:'pivot',adaptiveBatchId:null});
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
    expect(f.nativeElement.textContent).toContain('Interpretation and next experiment');f.destroy();
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

describe('Muse complete second-video workflow',()=>{
  const base='http://127.0.0.1:5391/api/science/director/pendulum/sessions/study/muse';
  const source:SamPeriodSource={studyId:'study',role:'followup',originalVideoSha256:'second',analysisId:'batch',adaptiveBatchId:'batch',providerResponseSha256:'raw',providerModel:'sam-3.1',objectId:'0',pivotRevisionId:null,pivotProvenance:'PIVOT_MISSING'};
  const state={observation:null,evaluation:null,prediction:{sha256:'frozen',payloadJson:JSON.stringify({prediction:{nextLengthMeters:.8,periodSeconds:1.79,tolerancePercent:5}})},attemptStatus:null,manualCallsPermitted:true,maximumEstimatedCostUsd:.05};
  const empty={evidence:null,evaluation:null,attemptStatus:null};
  const evidence={sha256:'evidence',payloadJson:JSON.stringify({analysisId:'batch',sourceSha256:'second',followupLengthMeters:.8})};
  let http:HttpTestingController;
  beforeEach(async()=>{await TestBed.configureTestingModule({imports:[MuseInvestigator],providers:[provideHttpClient(),provideHttpClientTesting()]}).compileComponents();http=TestBed.inject(HttpTestingController);});
  afterEach(()=>http.verify());
  async function init(contrast:object=empty){
    const c=TestBed.inject(LabConnection),field=document.createElement('input');field.type='password';field.value='LLM_SYNTHETIC_OFFLINE_TEST_ONLY';c.register(field);c.confirmToken();
    const f=TestBed.createComponent(MuseInvestigator);f.componentRef.setInput('source',source);f.detectChanges();
    http.expectOne(base).flush(state);await Promise.resolve();http.expectOne(base+'/contrast').flush(contrast);await f.whenStable();f.detectChanges();return f;
  }
  it('binds segmented second-video evidence before making exactly one manual inference',async()=>{
    const f=await init();expect(f.componentInstance.canContrast()).toBeFalse();
    const prep=f.componentInstance.prepareContrast();const r=http.expectOne(base+'/contrast/prepare');
    expect(r.request.body).toEqual({videoSha256:'second',analysisId:'batch',pivotRevisionId:null,adaptiveBatchId:'batch',followupLengthMeters:.8});
    r.flush({...empty,evidence});await prep;expect(f.componentInstance.canContrast()).toBeTrue();
    const run=f.componentInstance.evaluateContrast();void f.componentInstance.evaluateContrast();
    const sent=http.expectOne(base+'/contrast/evaluate');expect(sent.request.body.evidenceSha256).toBe('evidence');
    expect(sent.request.body.maximumEstimatedCostUsd).toBe(.05);
    const t={es:'Evidencia insuficiente.',en:'Insufficient evidence.'};
    sent.flush({evidence,evaluation:{responseId:'real-receipt',answer:{verdict:'INCONCLUSIVE',justification:t,limitations:t,nextStep:t}},attemptStatus:'COMPLETED'});
    await run;f.detectChanges();expect(f.nativeElement.textContent).toContain('Contraste inconcluso');expect(f.componentInstance.canContrast()).toBeFalse();
    expect(f.nativeElement.querySelectorAll('section').length).toBe(1);f.destroy();
  });
  it('does not use a comparison that belongs to another analysis',async()=>{
    const f=await init({...empty,evidence:{...evidence,payloadJson:JSON.stringify({analysisId:'old',sourceSha256:'second',followupLengthMeters:.7})}});
    expect(f.componentInstance.canContrast()).toBeFalse();expect(f.componentInstance.followupLength()).toBe(.7);f.destroy();
  });
  it('shows both 95% margins separately from the immutable tolerance and withholds global acceptance',async()=>{
    const assessment={status:'CANDIDATE',periodSeconds:1.4,statisticalStandardUncertaintySeconds:.001,usedCycles:10};
    const original={...state.prediction,studyId:'study',observationId:'baseline-o',responseId:'receipt',frozenAtUtc:'2026-10-30',
      payloadJson:JSON.stringify({prediction:{baselineLengthMeters:.5,nextLengthMeters:.8,periodSeconds:1.4*Math.sqrt(1.6),tolerancePercent:5}})};
    const f=await init({...empty,evidence:{...evidence,payloadJson:JSON.stringify({analysisId:'batch',sourceSha256:'second',
      measurement:{assessment:{...assessment,periodSeconds:1.78,usedCycles:8}}})}});
    f.componentInstance.state.set({...state,prediction:original,observation:{id:'baseline-o',analysisId:'baseline',payloadSha256:'hash',
      payloadJson:JSON.stringify({measurement:{assessment}})}});f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Video 1 · solo repetibilidad');
    expect(f.nativeElement.textContent).toContain('Video 2 · solo repetibilidad');
    expect(f.nativeElement.textContent).toContain('p-valor global: no disponibles');
    expect(f.componentInstance.statisticalSummary()?.comparison?.tolerancePercent).toBe(5);
    expect(f.componentInstance.statisticalSummary()?.comparison?.globalPValue).toBeNull();
    expect(f.componentInstance.state()?.prediction?.payloadJson).toBe(original.payloadJson);
    expect(f.nativeElement.querySelectorAll('section').length).toBe(1);f.destroy();
  });
  it('reconciles a failed contrast without resending to Meta',async()=>{
    const f=await init({...empty,evidence});const run=f.componentInstance.evaluateContrast();
    http.expectOne(base+'/contrast/evaluate').flush({}, {status:502,statusText:'Failed'});
    await Promise.resolve();http.expectOne(base+'/contrast').flush({...empty,evidence,attemptStatus:'ATTEMPT_REQUIRES_INSPECTION'});
    await run;expect(f.componentInstance.canContrast()).toBeFalse();expect(TestBed.inject(LabConnection).busy()).toBeFalse();f.destroy();
  });
});
