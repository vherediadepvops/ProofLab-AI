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
  it('separates the paid Muse interpretation from the explicit later prediction freeze',async()=>{
    const t={es:'Propuesta condicional.',en:'Conditional proposal.'};
    const a={decision:'PROPOSE_EXPERIMENT',justification:t,limitations:t,protocol:t,
      hypothesis:t,falsification:t,nextLengthMeters:.8,periodSeconds:1.79,tolerancePercent:5};
    const evaluation={model:'muse-spark-1.3',responseId:'saved-receipt',
      observationId:'observation',studyId:'study',rawResponseSha256:'c'.repeat(64),
      tokenCostEstimateUsd:.001,answer:a};
    const f=await init({...prepared,evaluation});
    const c=f.componentInstance;f.detectChanges();
    expect(c.state()?.prediction).toBeNull();
    expect(f.nativeElement.textContent).toContain('Congelar predicción');
    c.reviewedPrediction.set(true);f.detectChanges();
    const run=c.freezePrediction(),post=http.expectOne(base+'/freeze');
    f.detectChanges();
    expect(f.nativeElement.querySelector('.v93-freeze').textContent).toContain('Guardando tu predicción');
    expect(f.nativeElement.querySelector('.v93-freeze button').disabled).toBeTrue();
    void c.freezePrediction();void c.load();
    f.componentRef.setInput('language','en');f.componentRef.setInput('source',{...source});f.detectChanges();
    expect(c.busy()).toBe('freezing');expect(TestBed.inject(LabConnection).busy()).toBeTrue();expect(c.state()?.evaluation?.responseId).toBe('saved-receipt');
    http.expectNone(base);http.expectNone(base+'/freeze');
    f.componentRef.setInput('language','es');f.detectChanges();
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({observationId:'observation',responseId:'saved-receipt'});
    const frozen={studyId:'study',observationId:'observation',responseId:'saved-receipt',
      frozenAtUtc:'2026-11-02',sha256:'hash',payloadJson:JSON.stringify({
        prediction:{nextLengthMeters:.8,periodSeconds:1.79,tolerancePercent:5}})};
    post.flush({...prepared,evaluation,prediction:frozen});await run;f.detectChanges();
    expect(c.prediction()?.periodSeconds).toBe(1.79);expect(TestBed.inject(LabConnection).busy()).toBeFalse();
    expect(f.nativeElement.textContent).toContain('Predicción congelada');
    http.expectNone(r=>r.url.endsWith('/evaluate'));f.destroy();
  });
  for(const committed of [true,false])it('reconciles a lost freeze response with one read: committed='+committed,async()=>{
    const t={es:'Propuesta conservada.',en:'Saved proposal.'};
    const evaluation={model:'muse-spark-1.3',responseId:'saved-receipt',observationId:'observation',
      studyId:'study',rawResponseSha256:'hash',tokenCostEstimateUsd:.001,
      answer:{decision:'PROPOSE_EXPERIMENT',justification:t,limitations:t,protocol:t,hypothesis:t,
        falsification:t,nextLengthMeters:.8,periodSeconds:1.79,tolerancePercent:5}};
    const frozen={studyId:'study',observationId:'observation',responseId:'saved-receipt',
      frozenAtUtc:'2026-11-03',sha256:'frozen-hash',payloadJson:JSON.stringify({
        prediction:{nextLengthMeters:.8,periodSeconds:1.79,tolerancePercent:5}})};
    const f=await init({...prepared,evaluation}),c=f.componentInstance;
    c.reviewedPrediction.set(true);const run=c.freezePrediction();
    http.expectOne(base+'/freeze').flush({}, {status:502,statusText:'Lost response'});
    await Promise.resolve();
    const read=http.expectOne(base);expect(read.request.method).toBe('GET');
    read.flush(committed?{...prepared,evaluation,prediction:frozen}:empty);
    await run;f.detectChanges();
    expect(c.state()?.evaluation?.responseId).toBe('saved-receipt');
    expect(c.state()?.prediction).toEqual(committed?frozen:null);
    expect(c.busy()).toBeNull();expect(TestBed.inject(LabConnection).busy()).toBeFalse();
    if(committed)expect(c.error()).toBe('');
    else expect(c.error()).toContain('No se pudo confirmar');
    http.expectNone(r=>r.method==='POST');f.destroy();
  });
  it('holds the shared navigation lock until a pending freeze settles after destruction',async()=>{
    const t={es:'Propuesta conservada.',en:'Saved proposal.'};
    const evaluation={model:'muse-spark-1.3',responseId:'saved-receipt',observationId:'observation',
      studyId:'study',rawResponseSha256:'hash',tokenCostEstimateUsd:.001,
      answer:{decision:'PROPOSE_EXPERIMENT',justification:t,limitations:t,protocol:t,hypothesis:t,
        falsification:t,nextLengthMeters:.8,periodSeconds:1.79,tolerancePercent:5}};
    const f=await init({...prepared,evaluation}),connection=TestBed.inject(LabConnection);
    f.componentInstance.reviewedPrediction.set(true);const run=f.componentInstance.freezePrediction();
    const request=http.expectOne(base+'/freeze');expect(connection.busy()).toBeTrue();
    f.destroy();expect(connection.busy()).toBeTrue();
    expect(connection.setPort(5393)).toBeFalse();
    request.flush({}, {status:502,statusText:'Lost response'});await run;
    expect(connection.busy()).toBeFalse();http.expectNone(r=>r.url.includes('/muse'));
  });
  it('keeps an existing result visible during a refresh and after a failed read',async()=>{
    const state=response('PENDULUM_COMPATIBLE'),f=await init(state),c=f.componentInstance;
    const run=c.load();f.detectChanges();
    expect(c.state()?.evaluation?.responseId).toBe(state.evaluation.responseId);
    expect(f.nativeElement.textContent).toContain('compatible con un péndulo');
    http.expectOne(base).flush({}, {status:503,statusText:'Unavailable'});await run;f.detectChanges();
    expect(c.state()?.evaluation?.responseId).toBe(state.evaluation.responseId);
    expect(c.error()).toContain('No se pudo consultar');
    http.expectNone(r=>r.method==='POST');f.destroy();
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
    f.componentRef.setInput('language','en');f.detectChanges();http.expectNone(base);await f.whenStable();f.detectChanges();
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
    f.componentRef.setInput('language','en');f.detectChanges();http.expectNone(base);
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
    expect(f.nativeElement.querySelectorAll('section').length).toBeGreaterThan(1);f.destroy();
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
    expect(f.componentInstance.verifiedComparison()?.observed).toBe(1.78);
    expect(f.nativeElement.querySelector('.v93-band svg')).not.toBeNull();
    expect(f.nativeElement.textContent).toContain('Predicción frente a medición del segundo video');
    f.destroy();
  });
  it('reconciles a failed contrast without resending to Meta',async()=>{
    const f=await init({...empty,evidence});const run=f.componentInstance.evaluateContrast();
    http.expectOne(base+'/contrast/evaluate').flush({}, {status:502,statusText:'Failed'});
    await Promise.resolve();http.expectOne(base+'/contrast').flush({...empty,evidence,attemptStatus:'ATTEMPT_REQUIRES_INSPECTION'});
    await run;expect(f.componentInstance.canContrast()).toBeFalse();expect(TestBed.inject(LabConnection).busy()).toBeFalse();f.destroy();
  });
});

describe('Muse interpretation in Understand',()=>{
  const source:SamPeriodSource={studyId:'d'.repeat(32),role:'baseline',originalVideoSha256:'a'.repeat(64),
    analysisId:'e'.repeat(32),adaptiveBatchId:'e'.repeat(32),providerResponseSha256:'f'.repeat(64),
    providerModel:'sam-3.1',objectId:'1',pivotRevisionId:'reviewed-pivot',pivotProvenance:'HUMAN_IMAGE'};
  const base='http://127.0.0.1:5391/api/science/director/pendulum/sessions/'+source.studyId+'/muse';
  const empty={observation:null,evaluation:null,prediction:null,attemptStatus:null,
    manualCallsPermitted:true,maximumEstimatedCostUsd:.05};
  const words={
    justification:{es:'La evidencia registrada permite interpretar el movimiento.',en:'The recorded evidence supports interpreting the motion.'},
    limitations:{es:'La incertidumbre total no está evaluada.',en:'Total uncertainty has not been assessed.'},
    phenomenon:{es:'La trayectoria medida conserva oscilaciones alrededor de la suspensión.',en:'The measured path preserves oscillations around the suspension.'},
    future:{es:'PROPUESTA_FUTURA_SOLO_PREDECIR',en:'FUTURE_PROPOSAL_PREDICT_ONLY'}
  };
  function prepared(selected:SamPeriodSource=source){
    const identity={protocol:'MUSE_SAM_INVESTIGATOR_V3',studyId:selected.studyId,
      videoSha256:selected.originalVideoSha256,analysisId:selected.analysisId,
      responseSha256:selected.providerResponseSha256,pivotRevisionId:selected.pivotRevisionId??null,
      adaptiveBatchId:selected.adaptiveBatchId??null};
    return {...empty,observation:{id:'b'.repeat(64),studyId:selected.studyId,
      analysisId:selected.analysisId,videoSha256:selected.originalVideoSha256,
      payloadSha256:'c'.repeat(64),payloadJson:JSON.stringify({identity,baselineLengthMeters:.5,
        measurement:{source:{...selected},assessment:{status:'CANDIDATE',periodSeconds:1.4,
          statisticalStandardUncertaintySeconds:.001,usedCycles:10}}})}};
  }
  function saved(selected:SamPeriodSource=source){
    const state=prepared(selected),responseId='synthetic-recorded-muse-receipt';
    return {...state,evaluation:{studyId:selected.studyId,observationId:state.observation.id,
      model:'muse-spark-1.3',responseId,rawResponseSha256:'6'.repeat(64),tokenCostEstimateUsd:.001,
      answer:{decision:'PROPOSE_EXPERIMENT',justification:words.justification,limitations:words.limitations,
        hypothesis:words.future,protocol:words.future,falsification:words.future,
        nextLengthMeters:.8,periodSeconds:1.4*Math.sqrt(1.6),tolerancePercent:5,
        phenomenon:{classification:'PENDULUM_COMPATIBLE',justification:words.phenomenon}}},
      prediction:{studyId:selected.studyId,observationId:state.observation.id,responseId,
        frozenAtUtc:'2026-10-30T00:00:00Z',sha256:'7'.repeat(64),
        payloadJson:JSON.stringify({prediction:{nextLengthMeters:.8,periodSeconds:1.4*Math.sqrt(1.6),tolerancePercent:5}})}};
  }
  function mutatePayload(state:{observation:{payloadJson:string}},change:(payload:any)=>void):void {
    const payload=JSON.parse(state.observation.payloadJson);change(payload);
    state.observation.payloadJson=JSON.stringify(payload);
  }
  let http:HttpTestingController;
  beforeEach(async()=>{
    await TestBed.configureTestingModule({imports:[MuseInvestigator],
      providers:[provideHttpClient(),provideHttpClientTesting()]}).compileComponents();
    http=TestBed.inject(HttpTestingController);
  });
  afterEach(()=>http.verify());
  function init(selected:SamPeriodSource=source){
    const fixture=TestBed.createComponent(MuseInvestigator);
    fixture.componentRef.setInput('source',selected);fixture.componentRef.setInput('view','interpretation');
    fixture.detectChanges();http.expectNone(r=>r.url.includes('/muse'));
    return fixture;
  }
  async function open(fixture:ReturnType<typeof init>,state:object=empty){
    void fixture.componentInstance.openInterpretation();
    const request=http.expectOne(base);expect(request.request.method).toBe('GET');request.flush(state);
    await fixture.whenStable();fixture.detectChanges();
  }
  function token(){
    const connection=TestBed.inject(LabConnection),field=document.createElement('input');
    field.type='password';field.value='LLM_SYNTHETIC_OFFLINE_TEST_ONLY';
    connection.register(field);connection.confirmToken();return connection;
  }
  it('automatically opens the recorded Step 3 answer read-only without a provider POST',async()=>{
    const fixture=TestBed.createComponent(MuseInvestigator);
    fixture.componentRef.setInput('source',source);
    fixture.componentRef.setInput('view','interpretation');
    fixture.componentRef.setInput('autoInterpretation',true);
    fixture.detectChanges();
    const request=http.expectOne(base);expect(request.request.method).toBe('GET');
    request.flush(saved());await fixture.whenStable();fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Muse Spark: compatible con un péndulo');
    expect(fixture.nativeElement.querySelector('.v93-spark-tabs')).not.toBeNull();
    const before=fixture.componentInstance.state()?.evaluation?.responseId;
    fixture.componentRef.setInput('language','en');fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Spark interprets SAM measurements');
    expect(fixture.componentInstance.state()?.evaluation?.responseId).toBe(before);
    http.expectNone(r=>r.url.includes('/muse') && r.method==='POST');
    http.expectNone(r=>r.url.includes('/muse'));fixture.destroy();
  });
  it('keeps SQLite untouched until the interpretation is explicitly opened',async()=>{
    const fixture=init(),connection=TestBed.inject(LabConnection);
    fixture.componentRef.setInput('language','en');fixture.detectChanges();
    connection.changed.next();fixture.detectChanges();
    http.expectNone(r=>r.url.includes('/muse'));
    expect(fixture.nativeElement.textContent).toContain('Estimated maximum for a new request');
    expect(fixture.nativeElement.textContent).toContain('US$ 0.05');
    const primary=fixture.nativeElement.querySelector('.interpretation-card button.primary') as HTMLButtonElement;
    expect(primary.disabled).toBeTrue();
    const button=Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(b=>b.textContent?.includes('View saved response'));
    expect(button).toBeDefined();button!.click();
    const request=http.expectOne(base);expect(request.request.method).toBe('GET');
    request.flush(empty);await fixture.whenStable();fixture.detectChanges();
    expect(fixture.componentInstance.canEvaluate()).toBeFalse();
    http.expectNone(r=>r.method==='POST');fixture.destroy();
  });
  it('shows the price before any request and completes a new consultation in one explicit click',async()=>{
    token();const fixture=init(),component=fixture.componentInstance;
    expect(fixture.nativeElement.textContent).toContain('Máximo estimado de una nueva consulta');
    expect(fixture.nativeElement.textContent).toContain('US$ 0,05');
    http.expectNone(r=>r.url.includes('/muse'));
    const run=component.consultMuse();
    void component.consultMuse(); // A second click during loading cannot duplicate an attempt.
    const get=http.expectOne(base);expect(get.request.method).toBe('GET');get.flush(empty);
    for(let i=0;i<5;i++)await Promise.resolve();
    const prepare=http.expectOne(base+'/prepare');expect(prepare.request.method).toBe('POST');
    expect(prepare.request.body).toEqual({videoSha256:source.originalVideoSha256,analysisId:source.analysisId,
      pivotRevisionId:source.pivotRevisionId,adaptiveBatchId:source.adaptiveBatchId});
    prepare.flush(prepared());
    for(let i=0;i<5;i++)await Promise.resolve();
    const paid=http.expectOne(base+'/evaluate');expect(paid.request.method).toBe('POST');
    expect(paid.request.body.observationId).toBe(prepared().observation.id);
    expect(paid.request.body.confirmPaidCall).toBeTrue();
    expect(paid.request.body.confirmMeasuredDataTransfer).toBeTrue();
    expect(paid.request.body.maximumEstimatedCostUsd).toBe(.05);
    paid.flush(saved());await run;fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(words.justification.es);
    expect(component.state()?.evaluation?.responseId).toBe(saved().evaluation.responseId);
    expect(component.canConsultMuse()).toBeFalse();http.expectNone(r=>r.url.includes('/muse'));
    fixture.destroy();
  });
  it('recovers a saved answer with one click and no extra paid request',async()=>{
    token();const fixture=init(),run=fixture.componentInstance.consultMuse();
    http.expectOne(base).flush(saved());await run;fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(words.justification.es);
    expect(fixture.nativeElement.querySelector('.consult-cost')).toBeNull();
    http.expectNone(r=>r.method==='POST');fixture.destroy();
  });
  it('does not automatically retry an already recorded Muse attempt',async()=>{
    token();const fixture=init(),run=fixture.componentInstance.consultMuse();
    http.expectOne(base).flush({...empty,attemptStatus:'FAILED_REQUIRES_RECONCILIATION'});
    await run;fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('requiere revisión');
    expect(fixture.componentInstance.canConsultMuse()).toBeFalse();
    http.expectNone(r=>r.method==='POST');fixture.destroy();
  });
  it('stops the one-click consultation if the source changes during recovery',async()=>{
    token();const fixture=init(),run=fixture.componentInstance.consultMuse();
    const old=http.expectOne(base),next={...source,analysisId:'4'.repeat(32),
      adaptiveBatchId:'4'.repeat(32),providerResponseSha256:'5'.repeat(64)};
    fixture.componentRef.setInput('source',next);fixture.detectChanges();
    old.flush(empty);await run;fixture.detectChanges();
    expect(fixture.componentInstance.state()).toBeNull();
    http.expectNone(r=>r.method==='POST');fixture.destroy();
  });
  it('does not pay if fresh evidence preparation fails',async()=>{
    token();const fixture=init(),run=fixture.componentInstance.consultMuse();
    http.expectOne(base).flush(empty);
    for(let i=0;i<5;i++)await Promise.resolve();
    http.expectOne(base+'/prepare').flush({}, {status:409,statusText:'Evidence conflict'});
    await run;fixture.detectChanges();
    expect(fixture.componentInstance.error()).toContain('No se pudo preparar la evidencia');
    http.expectNone(r=>r.url.endsWith('/evaluate'));fixture.destroy();
  });
  it('shows the recorded answer, limits and inputs in ES and EN without future-trial cards or another read',async()=>{
    const fixture=init(),state=saved(),received:unknown[]=[];
    fixture.componentInstance.frozen.subscribe(value=>received.push(value));
    await open(fixture,state);
    const root=fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Interpretación de la evidencia');
    expect(root.textContent).toContain(words.phenomenon.es);
    expect(root.textContent).toContain(words.justification.es);
    expect(root.textContent).toContain(words.limitations.es);
    const record=Array.from(root.querySelectorAll('.v93-spark-tabs button')).
      find(button=>button.textContent?.includes('Registro')) as HTMLButtonElement;
    expect(record).toBeDefined();record.click();fixture.detectChanges();
    expect(root.textContent).toContain(state.evaluation.responseId);
    expect(root.textContent).not.toContain(words.future.es);
    expect(root.querySelector('.prediction')).toBeNull();
    expect(root.querySelector('.statistics-scope')).toBeNull();
    const details=root.querySelector('details.evidence-input') as HTMLDetailsElement;
    expect(details).not.toBeNull();details.open=true;details.dispatchEvent(new Event('toggle'));
    fixture.componentRef.setInput('language','en');fixture.detectChanges();
    expect(root.textContent).toContain('Evidence interpretation');
    expect(root.textContent).toContain(words.justification.en);
    expect(root.textContent).toContain(words.limitations.en);
    expect(root.textContent).not.toContain(words.future.en);
    expect(fixture.componentInstance.state()?.prediction?.payloadJson).toBe(state.prediction.payloadJson);
    expect(received).toEqual([]);
    http.expectNone(r=>r.url.includes('/muse'));fixture.destroy();
  });
  it('prepares and evaluates only manually, withholding frozen output until the research view recovers it',async()=>{
    token();const fixture=init(),component=fixture.componentInstance,received:unknown[]=[];
    component.frozen.subscribe(value=>received.push(value));await open(fixture);
    const preparing=component.prepare(),prepare=http.expectOne(base+'/prepare');
    expect(prepare.request.method).toBe('POST');
    expect(prepare.request.body).toEqual({videoSha256:source.originalVideoSha256,analysisId:source.analysisId,
      pivotRevisionId:source.pivotRevisionId,adaptiveBatchId:source.adaptiveBatchId});
    expect(prepare.request.headers.get('X-ProofLab-Manual-Action')).toBe('muse-research');
    prepare.flush(prepared());await preparing;fixture.detectChanges();
    expect(component.canEvaluate()).toBeTrue();
    const evaluating=component.evaluate();void component.evaluate();
    const evaluate=http.expectOne(base+'/evaluate');
    expect(evaluate.request.body.observationId).toBe(prepared().observation.id);
    expect(evaluate.request.body.confirmPaidCall).toBeTrue();
    expect(evaluate.request.body.confirmMeasuredDataTransfer).toBeTrue();
    expect(evaluate.request.body.maximumEstimatedCostUsd).toBe(.05);
    const state=saved();evaluate.flush(state);await evaluating;fixture.detectChanges();
    expect(component.view()).toBe('interpretation');expect(received).toEqual([]);
    expect(component.state()?.prediction?.sha256).toBe(state.prediction.sha256);
    expect(component.canEvaluate()).toBeFalse();
    fixture.componentRef.setInput('view','research');fixture.detectChanges();
    http.expectNone(base);await fixture.whenStable();fixture.detectChanges();
    expect(received).toEqual([state.prediction]);
    http.expectNone(r=>r.method==='POST');fixture.destroy();
  });
  it('restores the same interpretation and frozen prediction across stage remounts with reads only',async()=>{
    const state=saved();
    const understand=init();await open(understand,state);
    const original=understand.componentInstance.state()?.prediction;
    understand.destroy();
    const predict=TestBed.createComponent(MuseInvestigator);
    predict.componentRef.setInput('source',source);predict.detectChanges();
    http.expectOne(base).flush(state);await predict.whenStable();predict.detectChanges();
    expect(predict.componentInstance.state()?.prediction).toEqual(original);
    expect(predict.nativeElement.textContent).toContain('Predicción congelada');predict.destroy();
    const back=init();back.componentRef.setInput('autoInterpretation',true);back.detectChanges();
    http.expectOne(base).flush(state);await back.whenStable();back.detectChanges();
    expect(back.nativeElement.textContent).toContain(words.justification.es);
    expect(back.nativeElement.querySelector('.consult-cost')).toBeNull();
    expect(back.componentInstance.state()?.prediction).toEqual(original);
    http.expectNone(r=>r.method==='POST');back.destroy();
  });
  it('keeps a failed history read local and retries only after another explicit action',async()=>{
    const fixture=init();void fixture.componentInstance.openInterpretation();
    http.expectOne(base).flush({}, {status:503,statusText:'Unavailable'});
    await fixture.whenStable();fixture.detectChanges();
    expect(fixture.componentInstance.error()).toBeTruthy();
    expect(fixture.componentInstance.state()).toBeNull();
    http.expectNone(r=>r.url.includes('/muse'));
    const pending=fixture.componentInstance.load();
    const retry=http.expectOne(base);expect(retry.request.method).toBe('GET');
    retry.flush(saved());await pending;fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(words.justification.es);
    http.expectNone(r=>r.method==='POST');fixture.destroy();
  });
  it('discards a late history response and requires a new click after selecting another SAM result',async()=>{
    const fixture=init();void fixture.componentInstance.openInterpretation();
    const old=http.expectOne(base),next={...source,analysisId:'9'.repeat(32),adaptiveBatchId:'9'.repeat(32),
      providerResponseSha256:'8'.repeat(64)};
    fixture.componentRef.setInput('source',next);fixture.detectChanges();
    http.expectNone(r=>r.url.includes('/muse'));
    old.flush(saved());await fixture.whenStable();fixture.detectChanges();
    expect(fixture.componentInstance.state()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain(words.justification.es);
    await open(fixture,saved(next));
    expect(fixture.nativeElement.textContent).toContain(words.justification.es);
    http.expectNone(r=>r.method==='POST');fixture.destroy();
  });
  it('does not attribute an answer whose stored source or receipt differs from the selected evidence',async()=>{
    const cases:{name:string;change:(state:ReturnType<typeof saved>)=>void}[]=[
      {name:'video SHA',change:s=>{s.observation.videoSha256='9'.repeat(64);}},
      {name:'analysis',change:s=>mutatePayload(s,p=>p.identity.analysisId='9'.repeat(32))},
      {name:'provider response',change:s=>mutatePayload(s,p=>p.identity.responseSha256='9'.repeat(64))},
      {name:'pivot revision',change:s=>mutatePayload(s,p=>p.identity.pivotRevisionId='different-pivot')},
      {name:'batch',change:s=>mutatePayload(s,p=>p.identity.adaptiveBatchId='9'.repeat(32))},
      {name:'measured object',change:s=>mutatePayload(s,p=>p.measurement.source.objectId='2')},
      {name:'evaluation owner',change:s=>{s.evaluation.studyId='9'.repeat(32);}},
      {name:'observation receipt',change:s=>{s.evaluation.observationId='9'.repeat(64);}},
      {name:'raw response receipt',change:s=>{s.evaluation.rawResponseSha256='';}}
    ];
    for(const item of cases){
      const fixture=init(),state=saved();item.change(state);await open(fixture,state);
      expect(fixture.nativeElement.textContent).withContext(item.name).not.toContain(words.justification.es);
      expect(fixture.nativeElement.textContent).withContext(item.name).not.toContain(words.phenomenon.es);
      expect(fixture.componentInstance.canEvaluate()).withContext(item.name).toBeFalse();
      http.expectNone(r=>r.method==='POST');fixture.destroy();
    }
  });
  it('does not send a prepared observation from a different pivot or batch to Meta',async()=>{
    token();
    for(const field of ['pivotRevisionId','adaptiveBatchId']){
      const fixture=init(),state=prepared();
      mutatePayload(state,p=>p.identity[field]='different-'+field);
      await open(fixture,state);
      expect(fixture.componentInstance.canEvaluate()).withContext(field).toBeFalse();
      await fixture.componentInstance.evaluate();
      http.expectNone(r=>r.method==='POST');fixture.destroy();
    }
  });
  it('keeps genuine historical explanations readable without inventing a phenomenon or a batch',async()=>{
    const legacySource={...source,adaptiveBatchId:null};
    for(const protocol of ['MUSE_SAM_INVESTIGATOR_V1','MUSE_SAM_INVESTIGATOR_V2']){
      const fixture=init(legacySource),state=saved(legacySource);
      mutatePayload(state,p=>{p.identity.protocol=protocol;delete p.identity.adaptiveBatchId;delete p.measurement.source.adaptiveBatchId;});
      const historical={...state,evaluation:{...state.evaluation,answer:{...state.evaluation.answer,phenomenon:null}}};
      await open(fixture,historical);
      expect(fixture.nativeElement.textContent).withContext(protocol).toContain(words.justification.es);
      expect(fixture.nativeElement.textContent).withContext(protocol).toContain(words.limitations.es);
      expect(fixture.nativeElement.textContent).withContext(protocol).not.toContain(words.phenomenon.es);
      expect(fixture.componentInstance.phenomenon()).toBeNull();
      http.expectNone(r=>r.method==='POST');fixture.destroy();
    }
  });
  it('keeps a recorded inconclusive interpretation visible without creating a prediction',async()=>{
    const fixture=init(),state=saved(),received:unknown[]=[];
    fixture.componentInstance.frozen.subscribe(value=>received.push(value));
    const inconclusive={...state,prediction:null,evaluation:{...state.evaluation,answer:{
      ...state.evaluation.answer,decision:'INCONCLUSIVE',nextLengthMeters:null,periodSeconds:null,tolerancePercent:null,
      phenomenon:{classification:'INCONCLUSIVE',justification:words.phenomenon}}}};
    await open(fixture,inconclusive);
    expect(fixture.nativeElement.textContent).toContain(words.justification.es);
    expect(fixture.nativeElement.textContent).toContain(words.limitations.es);
    expect(fixture.componentInstance.prediction()).toBeNull();expect(received).toEqual([]);
    http.expectNone(r=>r.method==='POST');fixture.destroy();
  });
});
