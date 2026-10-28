import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { App } from './app';
import { By } from '@angular/platform-browser';
import { ResearchWorkflow } from './research-workflow';

describe('ProofLab Competition Research Lab', () => {
  beforeEach(async () => {
    localStorage.removeItem('prooflab-research-current');
    localStorage.removeItem('prooflab-ui-language');
    localStorage.removeItem('prooflab-director-current');
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  function initialize(leanAvailable = false, metaEnabled = false): { app: App; root: HTMLElement; controller: HttpTestingController; fixture: any } {
    const fixture = TestBed.createComponent(App);
    // Legacy mission tests explicitly select the preserved hidden workspace.
    // The juror-facing default is now Pendulum only.
    fixture.componentInstance.workspace.set('math');
    fixture.detectChanges();
    const controller = TestBed.inject(HttpTestingController);
    controller.expectOne('http://127.0.0.1:5391/api/research/stages').flush([
      { index: 1, nameEs: 'Problema y antecedentes', nameEn: 'Problem & prior work',
        reportEs: 'Informe de planteamiento',reportEn: 'Problem statement report',
        descriptionEs:'Pregunta',descriptionEn:'Question',
        referenceFrameworks:['IMRaD'],fields:[
          {key:'question',nameEs:'Pregunta',nameEn:'Question',
            guidanceEs:'Pregunta',guidanceEn:'Question',minimumCharacters:25}
        ] },
      ...[2,3,4,5].map(index=>({index,nameEs:'Etapa '+index,nameEn:'Stage '+index,
        reportEs:'Informe '+index,reportEn:'Report '+index,
        descriptionEs:'Pendiente',descriptionEn:'Pending',
        referenceFrameworks:['TOP'],fields:[]}))
    ]);
    const problems = [{
      id: 'NAT-ADD-COMM', titleEnglish: 'Commutative law',
      titleSpanish: 'Ley conmutativa',
      statementEnglish: 'For all a and b, a + b = b + a.',
      statementSpanish: 'Para todos a y b, a + b = b + a.',
      researchStatus: 'KNOWN_THEOREM',
      domain: 'Number theory', source: 'Lean core arithmetic',
      allowedStrategies: ['rfl', 'omega', 'simp']
    }];
    const requests = controller.match('http://127.0.0.1:5391/api/math/problems');
    expect(requests.length).toBe(2);
    requests.forEach(request => request.flush(problems));
    controller.expectOne('http://127.0.0.1:5391/api/math/status')
      .flush({ leanAvailable, phase: 'KNOWN_BENCHMARK_VERIFICATION' });
    controller.expectOne('http://127.0.0.1:5391/api/health')
      .flush({ metaModelApiEnabled: metaEnabled, appVersion: '2026.10.26-alpha' });
    fixture.detectChanges();
    return { app: fixture.componentInstance, root: fixture.nativeElement as HTMLElement, controller, fixture };
  }

  it('opens Home with exactly three navigation entries and future video slots',()=>{
    const fixture=TestBed.createComponent(App);
    fixture.detectChanges();
    const http=TestBed.inject(HttpTestingController);
    http.expectOne('http://127.0.0.1:5391/api/health').flush({metaModelApiEnabled:false});
    const root=fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.workspace()).toBe('home');
    expect(Array.from(root.querySelectorAll('aside.sidebar .nav')).map(x=>x.textContent?.trim()))
      .toEqual(['Inicio','Laboratorio','Configuración']);
    expect(root.querySelectorAll('.guide-card').length).toBe(3);
    expect(root.textContent).toContain('Próximamente');
    expect(root.querySelector('app-pendulum-zone-one')).toBeNull();
    expect(root.querySelector('input[type="password"]')?.closest('[hidden]')).not.toBeNull();
    http.expectNone(r=>r.url.includes('/api/math'));
    http.expectNone(r=>r.method==='POST');
    http.verify();fixture.destroy();
  });

  it('keeps the laboratory instance and experiment when opening settings',async()=>{
    const fixture=TestBed.createComponent(App);
    fixture.detectChanges();
    const http=TestBed.inject(HttpTestingController);
    http.expectOne('http://127.0.0.1:5391/api/health').flush({metaModelApiEnabled:false});
    fixture.componentInstance.openWorkspace('settings');fixture.detectChanges();
    const token=fixture.nativeElement.querySelector('#settings-meta-token') as HTMLInputElement;
    token.value='LLM_LOCAL_NAVIGATION_TEST_123456789012345';
    token.dispatchEvent(new Event('input'));
    (fixture.nativeElement.querySelector('app-workspace-settings form') as HTMLFormElement)
      .dispatchEvent(new Event('submit',{cancelable:true}));
    expect(fixture.componentInstance.connection.hasToken()).toBeTrue();
    fixture.componentInstance.openWorkspace('pendulum');
    fixture.detectChanges();
    expect(fixture.componentInstance.connection.hasToken()).toBeTrue();
    http.match(()=>true).forEach(req=>req.flush({source:'DETERMINISTIC_NONLINEAR_THEORY',
      baseline:{nonlinearPeriodSeconds:1.425},followUp:null}));
    await fixture.whenStable();fixture.detectChanges();
    const first=fixture.nativeElement.querySelector('app-pendulum-zone-one');
    expect(first).not.toBeNull();
    fixture.componentInstance.openWorkspace('settings');fixture.detectChanges();
    expect(first.closest('[hidden]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-workspace-settings').closest('[hidden]')).toBeNull();
    fixture.componentInstance.openWorkspace('pendulum');fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-pendulum-zone-one')).toBe(first);
    expect(first.closest('[hidden]')).toBeNull();
    http.expectNone(r=>r.method==='POST');
    http.verify();fixture.destroy();
  });

  it('shows competition edition and honest known-theorem notice', () => {
    const ctx = initialize();
    expect(ctx.root.textContent).toContain('26 OCT 2026');
    expect(ctx.root.textContent).toContain('Laboratorio de descubrimiento matemático');
    expect(ctx.root.textContent).toContain('NO constituye un descubrimiento original');
  });

  it('keeps billable Meta calls off until explicitly enabled', () => {
    const ctx = initialize();
    ctx.app.workspace.set('physics');
    ctx.fixture.detectChanges();
    const buttons = Array.from(ctx.root.querySelectorAll('button')) as HTMLButtonElement[];
    const ai = buttons.find(b => b.textContent?.includes('Solicitar hipótesis'));
    expect(ai?.disabled).toBeTrue();
  });

  it('does not confuse synthetic evidence with a real observation', async () => {
    const ctx = initialize();
    ctx.app.workspace.set('physics');
    ctx.fixture.detectChanges();
    const promise = ctx.app.demo();
    ctx.controller.expectOne('http://127.0.0.1:5391/api/science/synthetic/Pendulum')
      .flush({ experiment: 'Pendulum', unit: 's', predictedValue: 1.42,
        observedValue: 1.44, differencePercent: 1.5, sampleCount: 1100,
        origin: 'Simulated', method: 'Simulated sample', evidenceStatement: 'SYNTHETIC',
        limitations: 'Not experimental' });
    await promise;
    ctx.fixture.detectChanges();
    expect(ctx.root.textContent).toContain('SIMULACIÓN · NO ES EVIDENCIA FÍSICA');
  });

  it('displays Lean result without claiming original discovery', async () => {
    const ctx = initialize(true);
    const promise = ctx.app.verifyMath();
    ctx.controller.expectOne('http://127.0.0.1:5391/api/math/verify')
      .flush({
        attemptId: 'test-1', problemId: 'NAT-ADD-COMM', strategy: 'omega',
        outcome: 'VERIFIED', diagnostic: 'Known theorem verified.',
        sourceSha256: 'abc123', checkedAtUtc: '2026-10-26T19:00:00Z',
        verificationMethod: 'Lean 4', isNewDiscovery: false
      });
    await promise;
    ctx.fixture.detectChanges();
    expect(ctx.root.textContent).toContain('VERIFICADO');
    expect(ctx.root.textContent).toContain('Lean 4 verificó correctamente');
    expect(ctx.root.textContent).toContain('Nuevo descubrimiento científico');
    expect(ctx.root.textContent).toContain('NO');
    ctx.app.setLanguage('en');
    ctx.fixture.detectChanges();
    expect(ctx.root.textContent).toContain('VERIFIED');
    expect(ctx.root.textContent).toContain('previously known theorem');
    expect(ctx.root.textContent).toContain('New scientific discovery');
  });


  it('attaches only a real Lean attempt ID to a stage-four report draft', async () => {
    const ctx = initialize(true);
    const workflow = ctx.fixture.debugElement.query(
      By.directive(ResearchWorkflow)).componentInstance as ResearchWorkflow;
    const id = 'abcdefabcdefabcdefabcdefabcdefab';
    const loading = workflow.load(id);
    ctx.controller.expectOne('http://127.0.0.1:5391/api/research/' + id).flush({
      id, title: 'Mathematical benchmark observation',
      discipline: 'Mathematics', createdAtUtc: '2026-10-26T23:00:00Z',
      activeStage: 4, disclaimer: 'No scientific certification.',
      reports: [1,2,3,4,5].map(index => ({
        index, fields: {}, status: index < 4 ? 'APPROVED_COMPLETENESS' :
          index === 4 ? 'DRAFT' : 'LOCKED',
        revision: 1, approvedAtUtc: index < 4 ? '2026-10-26T23:00:00Z' : null,
        sha256: index < 4 ? 'abc' : null
      }))
    });
    await loading;
    ctx.controller.expectOne('http://127.0.0.1:5391/api/research/' + id + '/math/attempts')
      .flush([]);
    ctx.fixture.detectChanges();
    expect(ctx.root.textContent).toContain('LEAN 4 · BITÁCORA DE EVIDENCIAS');
    expect(workflow.evidenceAttached()).toBeFalse();
    const cycle = workflow.runProofCycle();
    const verified = {
      researchId: id, executionMode: 'DETERMINISTIC_BASELINE',
      problemResearchStatus: 'KNOWN_THEOREM',
      verification: {
        attemptId: 'fabc000012345678abcdefabcdedfeed',
        problemId: 'NAT-ADD-COMM', strategy: 'omega',
        outcome: 'VERIFIED',
        sourceSha256: 'a'.repeat(64),
        checkedAtUtc: '2026-10-26T23:31:00Z',
        diagnostic: 'Known theorem verified by Lean 4.',
        isNewDiscovery: false
      }
    };
    ctx.controller.expectOne('http://127.0.0.1:5391/api/research/' + id + '/math/cycle')
      .flush({researchId: id,problemId:'NAT-ADD-COMM',
        mode:'DETERMINISTIC_BASELINE_NOT_AI',
        attempts:[verified],conclusion:'Known benchmark',isScientificDiscovery:false});
    await cycle;
    workflow.attachProof(verified);
    ctx.fixture.detectChanges();
    expect(workflow.evidenceAttached()).toBeTrue();
    expect(workflow.draft()['manifest']).toContain(verified.verification.attemptId);
    expect(ctx.root.textContent).toContain('REFERENCIA LOCAL · NO IA');
    expect(ctx.root.textContent).toContain('VERIFICADO');
    ctx.app.setLanguage('en');
    ctx.fixture.detectChanges();
    expect(ctx.root.textContent).toContain('VERIFIED');
    expect(ctx.root.textContent).toContain('LEAN 4 · EVIDENCE LEDGER');
  });

  it('translates rejected Lean tactics immediately after changing ES to EN', async () => {
    const ctx = initialize(true);
    ctx.app.mathStrategy.set('rfl');
    const execution = ctx.app.verifyMath();
    ctx.controller.expectOne('http://127.0.0.1:5391/api/math/verify')
      .flush({
        attemptId:'rejected-01',problemId:'NAT-ADD-COMM',strategy:'rfl',
        outcome:'REJECTED',diagnostic:"Tactic 'rfl' failed",
        sourceSha256:'abc123',checkedAtUtc:'2026-10-27T00:20:00Z',
        verificationMethod:'Lean 4',isNewDiscovery:false
      });
    await execution;
    ctx.fixture.detectChanges();
    expect(ctx.root.textContent).toContain('RECHAZADO');
    expect(ctx.root.textContent).toContain('no son idénticos por definición');
    expect((ctx.root.querySelector('details.lean-original-diagnostic') as HTMLDetailsElement).open).toBeFalse();
    ctx.app.setLanguage('en');
    ctx.fixture.detectChanges();
    expect(ctx.root.textContent).toContain('REJECTED');
    expect(ctx.root.textContent).toContain('not equal by definition');
  });

  it('sends the selected language to Muse Spark without sending actual API requests', async () => {
    const ctx = initialize(true, true);
    const spanishPlan = ctx.app.planMath();
    const spanishCall = ctx.controller.expectOne('http://127.0.0.1:5391/api/math/plan');
    expect(spanishCall.request.body.language).toBe('es');
    spanishCall.flush({
      problemId:'NAT-ADD-COMM', model:'muse-spark-1.3',
      suggestedApproaches:'Prueba la táctica omega.',
      verifiedProof:false,provenance:'TEST_ONLY'
    });
    await spanishPlan;
    expect(ctx.app.mathPlan()).toContain('Prueba');
    ctx.app.setLanguage('en');
    expect(ctx.app.mathPlan()).toBe('');
    const englishPlan = ctx.app.planMath();
    const englishCall = ctx.controller.expectOne('http://127.0.0.1:5391/api/math/plan');
    expect(englishCall.request.body.language).toBe('en');
    englishCall.flush({
      problemId:'NAT-ADD-COMM',model:'muse-spark-1.3',
      suggestedApproaches:'Try the omega tactic.',
      verifiedProof:false,provenance:'TEST_ONLY'
    });
    await englishPlan;
    expect(ctx.app.mathPlan()).toContain('Try the');
  });

  it('shows scientific method before any mathematical proof attempt', () => {
    const ctx = initialize();
    expect(ctx.root.textContent).toContain('Método científico · 5 etapas');
    expect(ctx.root.textContent).toContain('Iniciar investigación matemática');
    expect(ctx.root.textContent).toContain('no una acreditación');
  });

  it('requires a research report before the workflow can advance', async () => {
    const ctx = initialize();
    const workflow = ctx.fixture.debugElement.query(
      By.directive(ResearchWorkflow)).componentInstance as ResearchWorkflow;
    workflow.title.set('A reproducible mathematical conjecture study');
    const creating = workflow.create();
    ctx.controller.expectOne('http://127.0.0.1:5391/api/research').flush({
      id: '0123456789abcdef0123456789abcdef',
      title: 'A reproducible mathematical conjecture study',
      discipline: 'Mathematics', createdAtUtc:'2026-10-26T22:00:00Z',
      activeStage: 1, disclaimer:'No certification.',
      reports: [1,2,3,4,5].map(index=>({
        index, fields:{}, status:index===1?'DRAFT':'LOCKED',revision:1,
        approvedAtUtc:null,sha256:null
      }))
    });
    await creating;
    ctx.fixture.detectChanges();
    expect(ctx.root.querySelectorAll('.method-step').length).toBe(5);
    expect((ctx.root.querySelectorAll('.method-step')[1] as HTMLButtonElement).disabled).toBeTrue();
    expect(workflow.completion().ready).toBeFalse();
    expect(ctx.root.textContent).toContain('Informe de planteamiento');
    expect((ctx.root.querySelectorAll('.method-buttons .method-primary')[0] as HTMLButtonElement).disabled).toBeTrue();
  });

  it('provides separate Muse and SAM test buttons but never enables them without a temporary session', () => {
    const ctx = initialize();
    ctx.app.openMetaTests();
    ctx.controller.expectOne('http://127.0.0.1:5393/api/meta/readiness').flush({
      apiKeyConfigured:true, creditsConfirmedByOwner:false,
      ownerApprovedPaidRequests:false, paidRequestsSwitchEnabled:false,
      liveCallsPermitted:false, usedRequests:0,
      remainingRequests:2, maxRequestsPerProcess:2,
      museModel:'muse-spark-1.3', samModel:'sam-3.1'
    });
    ctx.fixture.detectChanges();
    expect(ctx.root.textContent).toContain('Pruebas de conexión · Meta Model API');
    const buttons = Array.from(ctx.root.querySelectorAll('button'));
    expect(buttons.find(x => x.textContent?.includes('Enviar UNA solicitud a Muse'))?.disabled)
      .toBeTrue();
    expect(buttons.find(x => x.textContent?.includes('Enviar UNA imagen a SAM'))?.disabled)
      .toBeTrue();
    ctx.controller.expectNone('http://127.0.0.1:5393/api/ai/plan');
    ctx.controller.expectNone('http://127.0.0.1:5393/api/ai/segment-frame');
  });

  it('sends one explicitly confirmed Muse request to only the temporary test API', async () => {
    const ctx = initialize();
    ctx.app.openMetaTests();
    const ready = {
      apiKeyConfigured:true, creditsConfirmedByOwner:true,
      ownerApprovedPaidRequests:true, paidRequestsSwitchEnabled:true,
      liveCallsPermitted:true, usedRequests:0,
      remainingRequests:2, maxRequestsPerProcess:2,
      museModel:'muse-spark-1.3', samModel:'sam-3.1'
    };
    ctx.controller.expectOne('http://127.0.0.1:5393/api/meta/readiness').flush(ready);
    const confirmation = spyOn(window, 'confirm').and.returnValue(false);
    await ctx.app.testMuseConnection();
    ctx.controller.expectNone('http://127.0.0.1:5393/api/ai/plan');
    confirmation.and.returnValue(true);
    const oneCall = ctx.app.testMuseConnection();
    const request = ctx.controller.expectOne('http://127.0.0.1:5393/api/ai/plan');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.language).toBe('es');
    request.flush({model:'muse-spark-1.3',
      generatedPlan:'Respuesta de prueba no verificada.',
      provenance:'META_MODEL_API_LIVE_OUTPUT'});
    await Promise.resolve();
    ctx.controller.expectOne('http://127.0.0.1:5393/api/meta/readiness')
      .flush({...ready,usedRequests:1,remainingRequests:1});
    await oneCall;
    ctx.fixture.detectChanges();
    expect(ctx.app.metaTestMuseAnswer()).toContain('Respuesta de prueba');
    expect(ctx.app.metaTestReadiness()?.usedRequests).toBe(1);
    expect(ctx.root.textContent).toContain('RESPUESTA REAL · NO VERIFICADA');
    ctx.controller.expectNone('http://127.0.0.1:5391/api/ai/plan');
  });

  it('shows sanitized upstream status and never retries Muse automatically', async () => {
    const ctx = initialize();
    ctx.app.openMetaTests();
    const ready = {
      apiKeyConfigured:true, creditsConfirmedByOwner:true,
      ownerApprovedPaidRequests:true, paidRequestsSwitchEnabled:true,
      liveCallsPermitted:true, usedRequests:0,
      remainingRequests:2, maxRequestsPerProcess:2,
      museModel:'muse-spark-1.3', samModel:'sam-3.1'
    };
    ctx.controller.expectOne('http://127.0.0.1:5393/api/meta/readiness').flush(ready);
    await Promise.resolve();
    spyOn(window, 'confirm').and.returnValue(true);
    const oneCall = ctx.app.testMuseConnection();
    ctx.controller.expectOne('http://127.0.0.1:5393/api/ai/plan')
      .flush({error:'META_PROVIDER_TEST_FAILED',
        providerHttpStatus:401,category:'TOKEN_REJECTED_OR_EXPIRED'},
        {status:502,statusText:'Bad Gateway'});
    await Promise.resolve();
    ctx.controller.expectOne('http://127.0.0.1:5393/api/meta/readiness')
      .flush({...ready,usedRequests:1,remainingRequests:1});
    await oneCall;
    expect(ctx.app.metaTestError()).toContain('Meta HTTP 401');
    expect(ctx.app.metaTestMuseAnswer()).toBe('');
    ctx.controller.expectNone('http://127.0.0.1:5393/api/ai/plan');
  });

  it('creates a synthetic sample and sends one confirmed SAM image request', async () => {
    const ctx = initialize();
    ctx.app.openMetaTests();
    const ready = {
      apiKeyConfigured:true, creditsConfirmedByOwner:true,
      ownerApprovedPaidRequests:true, paidRequestsSwitchEnabled:true,
      liveCallsPermitted:true, usedRequests:0,
      remainingRequests:2, maxRequestsPerProcess:2,
      museModel:'muse-spark-1.3', samModel:'sam-3.1'
    };
    ctx.controller.expectOne('http://127.0.0.1:5393/api/meta/readiness').flush(ready);
    await Promise.resolve();
    ctx.app.prepareSyntheticSamImage();
    expect(ctx.app.metaTestImage()).toMatch(/^data:image\/png;base64,/);
    spyOn(window, 'confirm').and.returnValue(true);
    const samCall = ctx.app.testSamConnection();
    const request = ctx.controller.expectOne('http://127.0.0.1:5393/api/ai/segment-frame');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.concept).toBe('red ball');
    expect(request.request.body.imageDataUrl).toMatch(/^data:image\/png;base64,/);
    const wire =
      "<0f>0<|box;x1=211;y1=228;x2=270;y2=254;w=320;h=334|>" +
      "<|mask;x=0;y=0;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7\`e.ioeAf6-k/#Xd8%dX9x(|>";
    const rawResponseJson=JSON.stringify({
      model:'sam-3.1',status:'completed',
      output:[{content:[{type:'output_text',text:wire}]}]
    });
    request.flush({model:'sam-3.1',rawResponseJson,
      decodedMasks:false,provenance:'META_MODEL_API_RAW_OUTPUT'});
    await new Promise<void>(resolve => setTimeout(resolve, 0));
    ctx.controller.expectOne('http://127.0.0.1:5393/api/meta/readiness')
      .flush({...ready,usedRequests:1,remainingRequests:1});
    await samCall;
    expect(ctx.app.samVisualization()?.objects.length).toBe(1);
    expect(ctx.app.metaTestLastResult()).toContain('MASK_DECODED_OFFLINE');
    ctx.fixture.detectChanges();
    const box = ctx.root.querySelector('svg.sam-detection-overlay rect.sam-detected-box');
    const mask = ctx.root.querySelector('svg.sam-detection-overlay path.sam-detected-mask');
    expect(box?.getAttribute('x')).toBe('211');
    expect(box?.getAttribute('y')).toBe('228');
    expect(mask?.getAttribute('d')?.startsWith('M')).toBeTrue();
    ctx.controller.expectNone('http://127.0.0.1:5393/api/ai/segment-frame');
  });
});