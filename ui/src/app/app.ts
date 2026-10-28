import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LabConnection } from './lab-connection';
import { WorkspaceSettings } from './workspace-settings';
import { ResearchWorkflow } from './research-workflow';
import { PendulumZoneOne } from './pendulum-zone-one';
import { decodeSamResponse, type SamVisualization } from './sam-visualization';
import { Language, leanExplanation, leanOutcome, mathematicalAssumptions, scientificStatus, strategyDescription, friendlyApiError } from './locale';

type Kind = 'Pendulum' | 'RollingSphere';

interface Theory {
  kind: Kind;
  metric: string;
  unit: string;
  expectedValue: number;
  scientificModel: string;
  assumptions: string;
}
interface Evaluation {
  experiment: string;
  metric: string;
  unit: string;
  predictedValue: number;
  observedValue: number;
  differencePercent: number;
  sampleCount: number;
  method: string;
  origin: string;
  evidenceStatement: string;
  limitations: string;
}
interface Health { metaModelApiEnabled: boolean; appVersion: string; }
interface MathProblem {
  id: string; titleEnglish: string; titleSpanish: string;
  statementEnglish: string; statementSpanish: string;
  researchStatus: string; domain: string; source: string;
  allowedStrategies: string[];
}
interface MathProofResult {
  attemptId: string; problemId: string; strategy: string; outcome: string;
  verificationMethod: string; sourceSha256: string; checkedAtUtc: string;
  diagnostic: string; isNewDiscovery: boolean;
}
interface MathStatus { leanAvailable: boolean; phase: string; }
interface MathPlan { suggestedApproaches: string; verifiedProof: boolean; provenance: string; }
interface PlanResult { model: string; generatedPlan: string; provenance: string; }
interface SamResult { model: string; rawResponseJson: string; decodedMasks: boolean; provenance: string; }
interface MetaTestReadiness {
  apiKeyConfigured: boolean;
  creditsConfirmedByOwner: boolean;
  ownerApprovedPaidRequests: boolean;
  paidRequestsSwitchEnabled: boolean;
  liveCallsPermitted: boolean;
  usedRequests: number;
  remainingRequests: number;
  maxRequestsPerProcess: number;
  museModel: string;
  samModel: string;
}
interface MetaTestProblem {
  error?: string;
  category?: string;
  providerHttpStatus?: number | null;
}

const COPY = {
  es: {
    subtitle: 'Plataforma para experimentos científicos verificables',
    workspace: 'ESPACIO DE INVESTIGACIÓN', tools: 'MOTORES DE META',
    prepare: 'Diseña un experimento. Valida una hipótesis.',
    intro: 'Datos reales, cálculos reproducibles y razonamiento asistido por IA. Unimos observación y evidencia.',
    choose: 'Tipo de experimento', pendulum: 'Péndulo', ramp: 'Esfera en rampa',
    length: 'Longitud entre pivote y centro de masa (m)',
    angle: 'Ángulo de la rampa (°)',
    calculate: 'Calcular predicción', synthetic: 'Ejecutar demostración sintética',
    result: 'Resultado científico', waiting: 'Selecciona un experimento y calcula su predicción.',
    expected: 'Predicción', observed: 'Medición', deviation: 'Diferencia', observations: 'Observaciones',
    hypothesis: 'Agente científico · Muse Spark 1.3',
    prompt: 'Pregunta científica',
    muse: 'Solicitar hipótesis mediante Meta',
    vision: 'Observación visual · SAM 3.1',
    image: 'Selecciona un fotograma sin personas',
    detect: 'Segmentar imagen con SAM 3.1',
    source: 'Fuente de datos', noMeta: 'Sin conexión a Meta Model API',
    liveMeta: 'Model API habilitada', gated: 'Las llamadas facturables permanecen desactivadas hasta comprobar los créditos.',
    demoWarn: 'SIMULACIÓN — Los datos se generaron artificialmente. No son evidencia física.',
    samWarn: 'El resultado bruto de SAM todavía requiere interpretar las máscaras; no constituye seguimiento de video.',
    export: 'Exportar informe JSON',
    mathTitle: 'Laboratorio de descubrimiento matemático',
    mathIntro: 'Investigar conjeturas requiere verificar cada afirmación. Empezamos con teoremas conocidos para probar el verificador Lean 4.',
    mathProblem: 'Problema matemático', mathTactic: 'Estrategia de demostración',
    mathVerify: 'Verificar demostración con Lean 4', mathResult: 'Resultado de verificación',
    mathMuse: 'Solicitar estrategias a Muse Spark', mathMuseWarning: 'Muse Spark propone ideas, pero no demuestra teoremas. Llamadas facturables desactivadas.',
    mathWarning: 'Los problemas disponibles son teoremas conocidos. Verificarlos NO constituye un descubrimiento original.',
    mathEmpty: 'Selecciona una estrategia y verifica el teorema.',
    mathNotInstalled: 'Lean 4 todavía no está disponible en el backend.',
    sidebarMath: 'Misión 1 · Matemáticas',
    sidebarPhysics: 'Laboratorio de física', sidebarPendulum: 'Demo estrella · Péndulo', sidebarFuture: 'Misiones 2–4 · Planificadas',
    sidebarMetaTests: 'Pruebas Meta API',
    metaTestTitle: 'Pruebas de conexión · Meta Model API',
    metaTestIntro: 'Comprueba Muse Spark 1.3 y SAM 3.1 por separado, con una sesión local temporal, sin modificar investigaciones.',
    metaTestHeading: 'SESIÓN TEMPORAL · PUERTO 5393',
    competition: 'EDICIÓN DE COMPETENCIA',
    topArea: 'LABORATORIO', topWorkspace: 'Espacio de investigación',
    topOffline: 'META API SIN CONEXIÓN · 0 TOKENS', topActive: 'META API ACTIVA',
    heroMethod: 'RIGOR CIENTÍFICO', heroAdapters: 'ADAPTADORES META',
    heroOpen: 'CÓDIGO ABIERTO', mathSection: 'MISIÓN 01 · DESCUBRIMIENTO MATEMÁTICO',
    mathFormal: 'Lean 4 · Verificación formal',
    mathBenchmark: 'TEOREMAS CONOCIDOS · ALFA',
    mathChoose: 'SELECCIONAR · RAZONAR · VERIFICAR',
    mathIndependent: 'COMPROBACIÓN INDEPENDIENTE DE LEAN',
    mathTheoryLabel: 'Teorema conocido · Aritmética de Lean 4',
    mathResultProof: 'Resultado de la prueba', mathProofStrategy: 'Estrategia utilizada',
    mathNewDiscovery: 'Nuevo descubrimiento científico', mathNo: 'NO',
    mathTrace: 'Diagnóstico técnico original de Lean (puede estar en inglés)',
    mathAttempt: 'Intento', mathEvidenceSource: 'Huella SHA-256 del código Lean',
    mathProofEngine: 'VERIFICADOR DE DEMOSTRACIONES FORMALES',
    mathNext: 'Próximo avance: conjeturas abiertas verificadas, hipótesis con Muse Spark y comprobaciones independientes. No se ejecuta código Lean arbitrario generado por modelos.',
    experimentHeading: 'EXPERIMENTO / 01',
    setupTitle: 'Configuración física',
    gConstant: 'g · GRAVEDAD ESTÁNDAR',
    theoryMark: 'PREDICCIÓN TEÓRICA',
    noEvidence: 'SIN EVIDENCIA TODAVÍA',
    simulatedMark: 'SIMULACIÓN · NO ES EVIDENCIA FÍSICA',
    aiPipeline: 'HERRAMIENTAS DE IA / 02',
    metaMultimodal: 'Inteligencia multimodal de Meta',
    aiConnection: 'CONEXIÓN EN VIVO',
    aiReady: 'LISTA', aiOff: 'DESACTIVADA',
    aiPromptDescription: 'HIPÓTESIS Y DISEÑO EXPERIMENTAL',
    samDescription: 'SEGMENTACIÓN Y GEOMETRÍA',
    mathSuccessKnown: 'Teorema conocido verificado',
    ready: 'Motor local disponible', footer: 'Construido durante el Meta Global AI Developer Hackathon · 26 OCT 2026',
  },
  en: {
    subtitle: 'Verifiable scientific experimentation platform',
    workspace: 'RESEARCH WORKSPACE', tools: 'META RESEARCH ENGINES',
    prepare: 'Design an experiment. Verify a hypothesis.',
    intro: 'Real observations, reproducible computation, and AI-assisted reasoning. From observation to evidence.',
    choose: 'Experiment type', pendulum: 'Pendulum', ramp: 'Rolling sphere',
    length: 'Pivot to centre of mass length (m)',
    angle: 'Ramp inclination angle (°)',
    calculate: 'Compute prediction', synthetic: 'Run synthetic demonstration',
    result: 'Scientific results', waiting: 'Select an experiment and compute its theoretical prediction.',
    expected: 'Prediction', observed: 'Observed', deviation: 'Difference', observations: 'Observations',
    hypothesis: 'Research agent · Muse Spark 1.3',
    prompt: 'Research question',
    muse: 'Plan experiment with Meta',
    vision: 'Visual observations · SAM 3.1',
    image: 'Choose a frame containing no people',
    detect: 'Segment image with SAM 3.1',
    source: 'Evidence provenance', noMeta: 'Meta Model API offline',
    liveMeta: 'Model API enabled', gated: 'Paid API requests are disabled until promotional credits are verified.',
    demoWarn: 'SIMULATION — Data was artificially generated. Not physical evidence.',
    samWarn: 'Raw SAM output still requires mask decoding; this is not video tracking.',
    export: 'Export JSON report',
    mathTitle: 'Mathematical Discovery Laboratory',
    mathIntro: 'Research needs independent verification. First we verify known theorems to test the Lean 4 proof engine.',
    mathProblem: 'Mathematical problem', mathTactic: 'Proof strategy',
    mathVerify: 'Verify proof with Lean 4', mathResult: 'Verification result',
    mathMuse: 'Ask Muse Spark for proof strategies', mathMuseWarning: 'Muse Spark suggests ideas but cannot certify proofs. Paid calls are disabled.',
    mathWarning: 'Available problems are known theorems. Verifying them is NOT a novel discovery.',
    mathEmpty: 'Select a strategy and verify the theorem.',
    mathNotInstalled: 'Lean 4 is not yet available on the backend.',
    sidebarMath: 'Mission 1 · Mathematics',
    sidebarPhysics: 'Physics Evidence Studio', sidebarPendulum: 'Flagship Demo · Pendulum', sidebarFuture: 'Missions 2–4 · Planned',
    sidebarMetaTests: 'Meta API Tests',
    metaTestTitle: 'Connection tests · Meta Model API',
    metaTestIntro: 'Test Muse Spark 1.3 and SAM 3.1 separately through a temporary local session, without changing research dossiers.',
    metaTestHeading: 'TEMPORARY SESSION · PORT 5393',
    competition: 'COMPETITION EDITION',
    topArea: 'LABORATORY', topWorkspace: 'Research workspace',
    topOffline: 'META API OFFLINE · 0 TOKENS', topActive: 'META API ACTIVE',
    heroMethod: 'SCIENTIFIC RIGOR', heroAdapters: 'META MODEL ADAPTERS',
    heroOpen: 'OPEN SOURCE', mathSection: 'MISSION 01 · MATHEMATICAL DISCOVERY',
    mathFormal: 'Lean 4 · Formal verification',
    mathBenchmark: 'KNOWN BENCHMARKS · ALPHA',
    mathChoose: 'CHOOSE · REASON · VERIFY',
    mathIndependent: 'INDEPENDENT LEAN CHECK',
    mathTheoryLabel: 'Known theorem · Lean 4 core arithmetic',
    mathResultProof: 'Proof result', mathProofStrategy: 'Proof strategy',
    mathNewDiscovery: 'New scientific discovery', mathNo: 'NO',
    mathTrace: 'Original technical Lean diagnostic (may be in English)',
    mathAttempt: 'Attempt', mathEvidenceSource: 'Lean source SHA-256',
    mathProofEngine: 'FORMAL PROOF VERIFIER',
    mathNext: 'Next: vetted open conjectures, Muse Spark hypothesis generation, and independent verification. Arbitrary model-generated Lean code is not executed.',
    experimentHeading: 'EXPERIMENT / 01',
    setupTitle: 'Physical setup',
    gConstant: 'g · STANDARD GRAVITY',
    theoryMark: 'THEORETICAL ESTIMATE',
    noEvidence: 'NO EVIDENCE YET',
    simulatedMark: 'SIMULATION · NOT PHYSICAL EVIDENCE',
    aiPipeline: 'AI TOOLS / 02',
    metaMultimodal: 'Meta multimodal intelligence',
    aiConnection: 'LIVE CONNECTION',
    aiReady: 'READY', aiOff: 'OFF',
    aiPromptDescription: 'HYPOTHESIS & EXPERIMENT DESIGN',
    samDescription: 'SEGMENTATION & GEOMETRY',
    mathSuccessKnown: 'Known theorem verified',
    ready: 'Local computation available', footer: 'Built during Meta Global AI Developer Hackathon · 26 OCT 2026',
  }
};

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule, ResearchWorkflow, PendulumZoneOne, WorkspaceSettings],
  templateUrl: './app.html',
  styleUrls: ['./app.scss', './app-studio.scss']
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly connection = inject(LabConnection);
  readonly labOpened = signal(false);
  private readonly api = 'http://127.0.0.1:5391';
  readonly language = signal<Language>('es');
  readonly words = computed(() => COPY[this.language()]);
  readonly workspace = signal<'home' | 'settings' | 'math' | 'physics' | 'meta' | 'pendulum'>('home');
  readonly mathProblems = signal<MathProblem[]>([]);
  readonly mathChoice = signal('NAT-ADD-COMM');
  readonly mathStrategy = signal('omega');
  readonly mathSelected = computed(() =>
    this.mathProblems().find(p => p.id === this.mathChoice()) ?? null);
  readonly mathReport = signal<MathProofResult | null>(null);
  readonly mathPlan = signal('');
  readonly mathEngineAvailable = signal(false);
  readonly mathError = signal('');
  readonly kind = signal<Kind>('Pendulum');
  readonly length = signal(0.5);
  readonly angle = signal(15);
  readonly theory = signal<Theory | null>(null);
  readonly evaluation = signal<Evaluation | null>(null);
  readonly metaAvailable = signal(false);
  readonly running = signal(false);
  readonly error = signal('');
  readonly question = signal('¿Cómo afecta la longitud del péndulo a su período?');
  readonly aiAnswer = signal('');
  readonly imageDataUri = signal('');
  readonly segmentation = signal('');
  readonly recordedAt = '2026-10-26';
  private readonly metaTestApi = 'http://127.0.0.1:5393';
  readonly metaTestReadiness = signal<MetaTestReadiness | null>(null);
  readonly metaTestRunning = signal(false);
  readonly metaTestError = signal('');
  readonly metaTestMuseQuestion = signal(
    'En una frase, explica por qué la suma de dos números primos puede ser par.');
  readonly metaTestMuseAnswer = signal('');
  readonly metaTestSamConcept = signal('red ball');
  readonly metaTestImage = signal('');
  readonly metaTestSamAnswer = signal('');
  readonly samVisualization = signal<SamVisualization | null>(null);
  readonly samOfflineResponse = signal('');
  readonly metaTestLastResult = signal('');

  constructor() {
    this.connection.settingsRequested.pipe(takeUntilDestroyed()).subscribe(() => this.openWorkspace('settings'));
  }
  openWorkspace(page:'home'|'pendulum'|'settings'):void {
    if(page==='pendulum')this.labOpened.set(true);
    if(this.workspace()==='pendulum' && page!=='pendulum')
      this.host.nativeElement.querySelectorAll('app-pendulum-zone-one video').forEach(video => (video as HTMLVideoElement).pause());
    this.workspace.set(page);
    window.scrollTo(0,0);
  }
  pageTitle():string {
    const es=this.language()==='es';
    return this.workspace()==='home'?(es?'Inicio':'Home'):
      this.workspace()==='settings'?(es?'Configuración':'Settings'):
      this.workspace()==='pendulum'?(es?'Laboratorio':'Laboratory'):this.words().topWorkspace;
  }
  ngOnInit(): void {
    const saved = localStorage.getItem('prooflab-ui-language');
    if (saved === 'es' || saved === 'en') this.setLanguage(saved);
    document.documentElement.lang = this.language();
    if(this.workspace()==='math') {
    this.http.get<MathProblem[]>(this.api + '/api/math/problems')
      .subscribe({ next: problems => this.mathProblems.set(problems),
        error: () => this.mathError.set(this.language() === 'es'
          ? 'No se pudo cargar el catálogo de problemas matemáticos.'
          : 'The mathematical problem catalog is unavailable.') });
    this.http.get<MathStatus>(this.api + '/api/math/status')
      .subscribe({ next: info => this.mathEngineAvailable.set(info.leanAvailable),
        error: () => this.mathEngineAvailable.set(false) });
    }
    this.http.get<Health>(this.api + '/api/health')
      .subscribe({ next: response => this.metaAvailable.set(response.metaModelApiEnabled),
        error: () => this.error.set(this.language() === 'es'
          ? 'La API local no está disponible en el puerto 5391.'
          : 'Local API unavailable on port 5391.') });
  }

  openMetaTests(): void {
    this.workspace.set('meta');
    this.refreshMetaTest();
  }

  async refreshMetaTest(): Promise<void> {
    try {
      const ready = await firstValueFrom(this.http.get<MetaTestReadiness>(
        this.metaTestApi + '/api/meta/readiness'));
      this.metaTestReadiness.set(ready);
      if (!this.metaTestLastResult()) this.metaTestError.set('');
    } catch {
      this.metaTestReadiness.set(null);
      this.metaTestError.set(this.language() === 'es'
        ? 'No está iniciada la sesión temporal. Abre scripts\\Start-MetaTest.cmd desde el PC autorizado, confirma los créditos y vuelve a consultar.'
        : 'No temporary session is running. Start scripts\\Start-MetaTest.cmd on the authorized PC, acknowledge credits and refresh.');
    }
  }

  private metaTestFailure(error: any, model: 'Muse Spark' | 'SAM 3.1'): string {
    const info = error?.error as MetaTestProblem | undefined;
    const code = info?.providerHttpStatus;
    const title = this.language() === 'es'
      ? 'Solicitud fallida' : 'Request failed';
    const reason = info?.category || (error?.status === 409
      ? 'BILLING_GATE_CLOSED' : error?.status === 0
      ? 'LOCAL_TEST_SESSION_UNAVAILABLE' : 'UNCLASSIFIED_FAILURE');
    return title + ' · ' + model + ' · HTTP local ' +
      (typeof error?.status === 'number' ? error.status : '?') +
      (typeof code === 'number' ? ' · Meta HTTP ' + code : '') +
      ' · ' + reason + '. ' + (this.language() === 'es'
        ? 'Puede haber consumido créditos; no hay reintentos automáticos.'
        : 'Credits may have been consumed; there are no automatic retries.');
  }

  async testMuseConnection(): Promise<void> {
    const ready = this.metaTestReadiness();
    const question = this.metaTestMuseQuestion().trim();
    if (!ready?.liveCallsPermitted || ready.remainingRequests < 1 ||
        this.metaTestRunning() || question.length < 10 || question.length > 250) return;
    if (!window.confirm(this.language() === 'es'
      ? '¿Enviar UNA solicitud real y potencialmente facturable a Muse Spark 1.3? No hay reintentos automáticos.'
      : 'Send ONE real, potentially billable request to Muse Spark 1.3? No automatic retries.')) return;
    this.metaTestRunning.set(true);
    this.metaTestMuseAnswer.set('');
    this.metaTestError.set('');
    this.metaTestLastResult.set('');
    try {
      const response = await firstValueFrom(this.http.post<PlanResult>(
        this.metaTestApi + '/api/ai/plan', {
          experiment: 'ProofLab API connectivity check, not a discovery',
          researchQuestion: question, language: this.language()
        }));
      this.metaTestMuseAnswer.set(response.generatedPlan);
      this.metaTestLastResult.set('Muse Spark 1.3 · HTTP 200 · LIVE_UNVERIFIED');
    } catch (error: any) {
      this.metaTestError.set(this.metaTestFailure(error, 'Muse Spark'));
      this.metaTestLastResult.set('Muse Spark 1.3 · FAILED_OR_INCONCLUSIVE');
    } finally {
      this.metaTestRunning.set(false);
      await this.refreshMetaTest();
    }
  }

  prepareSyntheticSamImage(): void {
    // A local synthetic image only. No photo or private media is uploaded here.
    const canvas = document.createElement('canvas');
    canvas.width = 480;
    canvas.height = 320;
    const context = canvas.getContext('2d');
    if (!context) {
      this.metaTestError.set('Canvas image generation is unavailable.');
      return;
    }
    context.fillStyle = '#f9fafc';
    context.fillRect(0, 0, 480, 320);
    context.fillStyle = '#d9dde7';
    context.fillRect(0, 266, 480, 54);
    context.beginPath();
    context.ellipse(240, 269, 103, 14, 0, 0, 2 * Math.PI);
    context.fillStyle = 'rgba(35,42,50,0.20)';
    context.fill();
    const gradient = context.createRadialGradient(207, 130, 10, 246, 185, 100);
    gradient.addColorStop(0, '#ff8580');
    gradient.addColorStop(0.4, '#eb3133');
    gradient.addColorStop(1, '#930e18');
    context.fillStyle = gradient;
    context.beginPath();
    context.arc(240, 174, 92, 0, Math.PI * 2);
    context.fill();
    this.metaTestImage.set(canvas.toDataURL('image/png'));
    this.metaTestSamConcept.set('red ball');
    this.metaTestSamAnswer.set('');
    this.samVisualization.set(null);
    this.metaTestError.set('');
  }

  onMetaTestImage(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) ||
        file.size < 1 || file.size > 1_500_000) {
      this.metaTestError.set(this.language() === 'es'
        ? 'Solo se permiten PNG/JPEG/WebP de hasta 1,5 MB.'
        : 'Only PNG/JPEG/WebP files up to 1.5 MB are allowed.');
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      const value = String(reader.result || '');
      if (value.startsWith('data:image/')) this.metaTestImage.set(value);
    };
    reader.onerror = () => this.metaTestError.set('Unable to load test image.');
    reader.readAsDataURL(file);
    this.metaTestSamAnswer.set('');
    this.samVisualization.set(null);
    this.metaTestError.set('');
  }

  async visualizeSamRaw(raw: string): Promise<void> {
    try {
      const decoded = await decodeSamResponse(raw);
      this.samVisualization.set(decoded);
      this.metaTestSamAnswer.set(this.language() === 'es'
        ? decoded.objects.length + ' objeto(s) detectado(s), máscara decodificada localmente sin llamadas nuevas a Meta.'
        : decoded.objects.length + ' detected object(s), locally decoded without new Meta requests.');
      this.metaTestError.set('');
    } catch {
      this.samVisualization.set(null);
      this.metaTestSamAnswer.set(this.language() === 'es'
        ? 'La respuesta llegó, pero no pudo decodificarse como una máscara SAM 3.1 válida.'
        : 'A response was received but it could not be decoded as a valid SAM 3.1 mask.');
    }
  }

  async previewPreviousSam(): Promise<void> {
    // User-supplied offline replay only, with no Meta request.
    if (this.samOfflineResponse().trim().length === 0) return;
    await this.visualizeSamRaw(this.samOfflineResponse().trim());
    this.metaTestLastResult.set('SAM 3.1 · OFFLINE_REPLAY_NO_BILLING');
  }

  async testSamConnection(): Promise<void> {
    const ready = this.metaTestReadiness();
    const concept = this.metaTestSamConcept().trim();
    if (!ready?.liveCallsPermitted || ready.remainingRequests < 1 ||
        this.metaTestRunning() || !this.metaTestImage() ||
        concept.length < 3 || concept.length > 90) return;
    if (!window.confirm(this.language() === 'es'
      ? '¿Enviar UNA imagen y su descripción a SAM 3.1? Puede consumir créditos. Evita fotos de personas o contenido privado.'
      : 'Send ONE image and description to SAM 3.1? This may consume credits. Avoid private or personal images.')) return;
    this.metaTestRunning.set(true);
    this.metaTestSamAnswer.set('');
    this.samVisualization.set(null);
    this.metaTestError.set('');
    this.metaTestLastResult.set('');
    try {
      const result = await firstValueFrom(this.http.post<SamResult>(
        this.metaTestApi + '/api/ai/segment-frame', {
          concept, imageDataUrl: this.metaTestImage()
        }));
      await this.visualizeSamRaw(result.rawResponseJson);
      this.metaTestLastResult.set('SAM 3.1 · HTTP 200 · ' +
        (this.samVisualization() ? 'MASK_DECODED_OFFLINE' : 'RAW_ONLY'));
    } catch (error: any) {
      this.metaTestError.set(this.metaTestFailure(error, 'SAM 3.1'));
      this.metaTestLastResult.set('SAM 3.1 · FAILED_OR_INCONCLUSIVE');
    } finally {
      this.metaTestRunning.set(false);
      await this.refreshMetaTest();
    }
  }

  setLanguage(value: Language): void {
    if (this.language() === value) return;
    this.language.set(value);
    const q = this.question();
    if (q === '¿Cómo afecta la longitud del péndulo a su período?' ||
        q === 'How does changing the pendulum length affect its period?' ||
        q === '¿Cómo afecta la inclinación de la rampa a la aceleración de una esfera?' ||
        q === 'How does the ramp inclination change the acceleration of a rolling sphere?') {
      this.question.set(value === 'es'
        ? (this.kind() === 'Pendulum'
          ? '¿Cómo afecta la longitud del péndulo a su período?'
          : '¿Cómo afecta la inclinación de la rampa a la aceleración de una esfera?')
        : (this.kind() === 'Pendulum'
          ? 'How does changing the pendulum length affect its period?'
          : 'How does the ramp inclination change the acceleration of a rolling sphere?'));
    }
    localStorage.setItem('prooflab-ui-language', value);
    document.documentElement.lang = value;
    // A model's previous prose cannot be reliably translated without a new
    // model call. Clear it rather than presenting it in the wrong language.
    // No new (potentially paid) request is triggered by switching language.
    this.mathPlan.set('');
    this.aiAnswer.set('');
    this.mathError.set('');
    this.error.set('');
  }
  proofStatus(outcome: string) { return leanOutcome(outcome, this.language()); }
  proofExplanation(report: MathProofResult) { return leanExplanation(report, this.language()); }
  proofTactic(tactic: string) { return strategyDescription(tactic, this.language()); }
  proofResearchStatus(status: string) { return scientificStatus(status, this.language()); }
  theoryAssumptions(kind: string) { return mathematicalAssumptions(kind, this.language()); }
  chooseMathProblem(value: string): void {
    this.mathChoice.set(value);
    this.mathReport.set(null);
    this.mathPlan.set('');
    this.mathError.set('');
  }
  async planMath(): Promise<void> {
    if (!this.metaAvailable()) return;
    this.running.set(true);
    this.mathPlan.set('');
    this.mathError.set('');
    try {
      const output = await firstValueFrom(this.http.post<MathPlan>(
        this.api + '/api/math/plan', { problemId: this.mathChoice(),
          language: this.language() }));
      this.mathPlan.set(output.suggestedApproaches);
    } catch {
      this.mathError.set(this.language() === 'es'
        ? 'No se pudo obtener el plan de Muse Spark.'
        : 'Muse Spark planning is unavailable.');
    } finally { this.running.set(false); }
  }
  async verifyMath(): Promise<void> {
    this.running.set(true);
    this.mathReport.set(null);
    this.mathError.set('');
    try {
      const result = await firstValueFrom(this.http.post<MathProofResult>(
        this.api + '/api/math/verify', {
          problemId: this.mathChoice(), strategy: this.mathStrategy()
        }));
      this.mathReport.set(result);
    } catch (error: any) {
      this.mathError.set(friendlyApiError(error,this.language(),
        'No se pudo completar la verificación matemática.',
        'Proof verification was not completed.'));
    } finally { this.running.set(false); }
  }
  chooseKind(value: Kind): void {
    this.kind.set(value); this.theory.set(null); this.evaluation.set(null); this.error.set('');
    this.question.set(this.language() === 'es'
      ? (value === 'Pendulum'
        ? '¿Cómo afecta la longitud del péndulo a su período?'
        : '¿Cómo afecta la inclinación de la rampa a la aceleración de una esfera?')
      : (value === 'Pendulum'
        ? 'How does changing the pendulum length affect its period?'
        : 'How does the ramp inclination change the acceleration of a rolling sphere?'));
  }
  setLength(value: number): void { this.length.set(Number(value)); this.theory.set(null); this.evaluation.set(null); }
  setAngle(value: number): void { this.angle.set(Number(value)); this.theory.set(null); this.evaluation.set(null); }
  setup() { return this.kind() === 'Pendulum'
    ? { kind: 'Pendulum', lengthMeters: this.length() }
    : { kind: 'RollingSphere', angleDegrees: this.angle() }; }

  async calculate(): Promise<void> {
    this.running.set(true); this.error.set('');
    try {
      const result = await firstValueFrom(
        this.http.post<Theory>(this.api + '/api/science/predict', this.setup()));
      this.theory.set(result); this.evaluation.set(null);
    } catch (error: any) {
      this.error.set(this.language() === 'es'
        ? 'No se pudo calcular la predicción. Comprueba los parámetros del experimento.'
        : 'The API could not calculate this experiment. Check the experiment parameters.');
    } finally { this.running.set(false); }
  }

  async demo(): Promise<void> {
    this.running.set(true); this.error.set('');
    try {
      const report = await firstValueFrom(
        this.http.get<Evaluation>(this.api + '/api/science/synthetic/' + this.kind()));
      this.evaluation.set(report); this.theory.set(null);
    } catch { this.error.set(this.language() === 'es'
      ? 'No se pudo ejecutar la demostración sintética.'
      : 'The synthetic test could not be completed.'); }
    finally { this.running.set(false); }
  }

  async plan(): Promise<void> {
    if (!this.metaAvailable()) return;
    this.running.set(true); this.error.set('');
    try {
      const output = await firstValueFrom(this.http.post<PlanResult>(
        this.api + '/api/ai/plan',
        { experiment: this.kind(), researchQuestion: this.question(),
          language: this.language() }));
      this.aiAnswer.set(output.generatedPlan);
    } catch { this.error.set(this.language() === 'es'
      ? 'La solicitud a Muse Spark falló. Revisa los registros de la API.'
      : 'Muse Spark request failed. Review the API server logs.'); }
    finally { this.running.set(false); }
  }

  onImage(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 1_500_000) {
      this.error.set(this.language() === 'es'
        ? 'Selecciona una imagen PNG, JPEG o WebP de menos de 1,5 MB.'
        : 'Choose a PNG/JPEG/WebP smaller than 1.5MB.');
      return;
    }
    this.segmentation.set(''); this.error.set('');
    const reader = new FileReader();
    reader.onload = () => this.imageDataUri.set(String(reader.result || ''));
    reader.readAsDataURL(file);
  }

  async detect(): Promise<void> {
    if (!this.metaAvailable() || !this.imageDataUri()) return;
    this.running.set(true); this.error.set('');
    try {
      const result = await firstValueFrom(this.http.post<SamResult>(
        this.api + '/api/ai/segment-frame',
        { concept: this.kind() === 'Pendulum' ? 'pendulum bob' : 'rolling sphere',
          imageDataUrl: this.imageDataUri() }));
      this.segmentation.set(result.rawResponseJson.slice(0, 900));
    } catch { this.error.set(this.language() === 'es'
      ? 'La solicitud a SAM 3.1 falló. Revisa los registros de la API.'
      : 'SAM 3.1 request failed. Review API server logs.'); }
    finally { this.running.set(false); }
  }

  download(): void {
    if (!this.evaluation()) return;
    const object = { platform: 'ProofLab AI', edition: this.recordedAt,
      generatedUtc: new Date().toISOString(), result: this.evaluation(),
      language: this.language(),
      warning: this.language() === 'es'
        ? 'Los datos simulados nunca deben presentarse como observaciones reales.'
        : 'Simulated data must never be claimed as real evidence.' };
    const url = URL.createObjectURL(new Blob(
      [JSON.stringify(object, null, 2)], { type: 'application/json' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = 'prooflab-scientific-report.json';
    anchor.click();
    URL.revokeObjectURL(url);
  }
}