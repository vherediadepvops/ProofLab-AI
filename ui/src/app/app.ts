import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

type Language = 'es' | 'en';
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
interface PlanResult { model: string; generatedPlan: string; provenance: string; }
interface SamResult { model: string; rawResponseJson: string; decodedMasks: boolean; provenance: string; }

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
    ready: 'Local computation available', footer: 'Built during Meta Global AI Developer Hackathon · 26 OCT 2026',
  }
};

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly api = 'http://127.0.0.1:5391';
  readonly language = signal<Language>('es');
  readonly words = computed(() => COPY[this.language()]);
  readonly kind = signal<Kind>('Pendulum');
  readonly length = signal(0.5);
  readonly angle = signal(15);
  readonly theory = signal<Theory | null>(null);
  readonly evaluation = signal<Evaluation | null>(null);
  readonly metaAvailable = signal(false);
  readonly running = signal(false);
  readonly error = signal('');
  readonly question = signal('How does changing the pendulum length affect its period?');
  readonly aiAnswer = signal('');
  readonly imageDataUri = signal('');
  readonly segmentation = signal('');
  readonly recordedAt = '2026-10-26';

  ngOnInit(): void {
    this.http.get<Health>(this.api + '/api/health')
      .subscribe({ next: response => this.metaAvailable.set(response.metaModelApiEnabled),
        error: () => this.error.set('Local API unavailable on port 5391.') });
  }

  setLanguage(value: Language) { this.language.set(value); }
  chooseKind(value: Kind): void {
    this.kind.set(value); this.theory.set(null); this.evaluation.set(null); this.error.set('');
    this.question.set(value === 'Pendulum'
      ? 'How does changing the pendulum length affect its period?'
      : 'How does the ramp inclination change the acceleration of a rolling sphere?');
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
      this.error.set(error?.error?.error || 'The API could not calculate this experiment.');
    } finally { this.running.set(false); }
  }

  async demo(): Promise<void> {
    this.running.set(true); this.error.set('');
    try {
      const report = await firstValueFrom(
        this.http.get<Evaluation>(this.api + '/api/science/synthetic/' + this.kind()));
      this.evaluation.set(report); this.theory.set(null);
    } catch { this.error.set('The synthetic test could not be completed.'); }
    finally { this.running.set(false); }
  }

  async plan(): Promise<void> {
    if (!this.metaAvailable()) return;
    this.running.set(true); this.error.set('');
    try {
      const output = await firstValueFrom(this.http.post<PlanResult>(
        this.api + '/api/ai/plan',
        { experiment: this.kind(), researchQuestion: this.question() }));
      this.aiAnswer.set(output.generatedPlan);
    } catch { this.error.set('Muse Spark request failed. Review the API server logs.'); }
    finally { this.running.set(false); }
  }

  onImage(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 1_500_000) {
      this.error.set('Choose a PNG/JPEG/WebP smaller than 1.5MB.');
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
    } catch { this.error.set('SAM 3.1 request failed. Review API server logs.'); }
    finally { this.running.set(false); }
  }

  download(): void {
    if (!this.evaluation()) return;
    const object = { platform: 'ProofLab AI', edition: this.recordedAt,
      generatedUtc: new Date().toISOString(), result: this.evaluation(),
      warning: 'Simulated data must never be claimed as real evidence.' };
    const url = URL.createObjectURL(new Blob(
      [JSON.stringify(object, null, 2)], { type: 'application/json' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = 'prooflab-scientific-report.json';
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
