import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

type Language = 'es' | 'en';
interface Point { timeSeconds: number; position: number; }
interface SourceSequence { filename: string; observations: Point[]; }
interface Preregistration {
  id: string;
  preregisteredAtUtc: string;
  hypothesis: {
    baselineLengthMeters: number;
    followUpLengthMeters: number;
    expectedBaselinePeriodSeconds: number;
    expectedFollowUpPeriodSeconds: number;
    predictedIncreasePercent: number;
    formula: string;
  };
  predictionSha256: string;
  limitation: string;
}
interface VideoFormats {
  decoderAvailable: boolean;
  supportedExtensions: string[];
  maximumVideoBytes: number;
  maximumDurationSeconds: number;
  maxTakesPerCondition: number;
  processingMethod: string;
}
interface SpectralReview {
  videoId: string;
  evaluatedAtUtc: string;
  originalCrossingPeriodSeconds: number | null;
  assessment: {
    algorithm: string;
    periodSeconds: number | null;
    fitRSquared: number;
    firstHalfPeriodSeconds: number | null;
    secondHalfPeriodSeconds: number | null;
    halfDifferencePercent: number | null;
    observationsUsed: number;
    status: string;
    limitations: string;
  };
}
interface VideoRecord {
  videoId: string;
  condition: 'baseline' | 'followup';
  take: number;
  originalFilename: string;
  sourceSha256: string;
  posterAvailable: boolean;
  sizeBytes: number;
  media: { codec: string; width: number; height: number; durationSeconds: number; nominalFramesPerSecond: number };
  tracking: { detectedFrames: number; framesAnalyzed: number; detectionRate: number; outcome: string; observations?: Point[] };
  preliminaryPeriodSeconds: number | null;
  outcome: string;
  provenanceNote: string;
  spectralReview?: SpectralReview | null;
}
interface VideoComparison {
  predictedIncreasePercent: number;
  observedIncreasePercent: number | null;
  baselineMeanPeriodSeconds: number | null;
  followUpMeanPeriodSeconds: number | null;
  baselineVideos: number;
  followUpVideos: number;
  baselineSampleStandardDeviation: number | null;
  followUpSampleStandardDeviation: number | null;
  differencePercentagePoints: number | null;
  scientificStatus: string;
  method: string;
  limitations: string;
}
interface Comparison {
  evaluationId: string;
  evaluatedAtUtc: string;
  inputSha256: string;
  comparison: {
    baseline: { observedValue: number; origin: string; sampleCount: number };
    followUp: { observedValue: number; origin: string; sampleCount: number };
    predictedIncreasePercent: number;
    observedIncreasePercent: number;
    differencePercentagePoints: number;
    evidenceClassification: string;
    scientificConclusion: string;
    limitations: string;
  };
}

@Component({
  selector: 'app-pendulum-study',
  imports: [CommonModule, FormsModule],
  templateUrl: './pendulum-study.html',
  styleUrl: './pendulum-study.scss'
})
export class PendulumStudy implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly api = 'http://127.0.0.1:5391';
  readonly language = input<Language>('es');
  readonly firstLength = signal(0.5);
  readonly secondLength = signal(0.8);
  readonly study = signal<Preregistration | null>(null);
  readonly firstSeries = signal<SourceSequence | null>(null);
  readonly secondSeries = signal<SourceSequence | null>(null);
  readonly result = signal<Comparison | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly formats = signal<VideoFormats | null>(null);
  readonly videos = signal<VideoRecord[]>([]);
  readonly uploadingVideo = signal('');
  readonly reanalyzingVideos = signal(false);
  readonly videoComparison = signal<VideoComparison | null>(null);
  readonly videoError = signal('');

  get isSpanish(): boolean { return this.language() === 'es'; }

  newStudy(): void {
    if (this.busy() || this.uploadingVideo() || this.reanalyzingVideos()) return;
    localStorage.removeItem('prooflab-pendulum-study-id');
    this.study.set(null);
    this.videos.set([]);
    this.videoComparison.set(null);
    this.firstSeries.set(null);
    this.secondSeries.set(null);
    this.result.set(null);
    this.error.set('');
    this.videoError.set('');
  }

  ngOnInit(): void {
    void this.loadFormats();
    const id = localStorage.getItem('prooflab-pendulum-study-id');
    if (id && /^[0-9a-f]{32}$/.test(id)) void this.restoreStudy(id);
  }

  private async loadFormats(): Promise<void> {
    try {
      this.formats.set(await firstValueFrom(this.http.get<VideoFormats>(
        this.api + '/api/science/pendulum/video-formats')));
    } catch {
      this.formats.set(null);
    }
  }

  async restoreStudy(id: string): Promise<void> {
    try {
      const result = await firstValueFrom(this.http.get<Preregistration>(
        this.api + '/api/science/pendulum/preregistrations/' + id));
      this.study.set(result);
      this.firstLength.set(result.hypothesis.baselineLengthMeters);
      this.secondLength.set(result.hypothesis.followUpLengthMeters);
      await this.refreshVideos();
    } catch { localStorage.removeItem('prooflab-pendulum-study-id'); }
  }

  async refreshVideos(): Promise<void> {
    const study = this.study();
    if (!study) return;
    try {
      this.videos.set(await firstValueFrom(this.http.get<VideoRecord[]>(
        this.api + '/api/science/pendulum/preregistrations/' + study.id + '/videos')));
    } catch { this.videos.set([]); }
  }

  videoPosterUrl(videoId: string): string {
    const s = this.study();
    return s ? this.api + '/api/science/pendulum/preregistrations/' +
      s.id + '/videos/' + videoId + '/poster' : '';
  }

  videoStatus(record: VideoRecord): string {
    if (record.spectralReview?.assessment.periodSeconds !== null &&
        record.spectralReview?.assessment.periodSeconds !== undefined) {
      return this.isSpanish
        ? 'Estimación espectral local; requiere verificar visualmente la plomada'
        : 'Local spectral estimate; visual bob verification required';
    }
    if (record.preliminaryPeriodSeconds !== null) {
      return this.isSpanish
        ? 'Seguimiento local estimado; pendiente de revisión visual'
        : 'Local tracking estimated; visual review required';
    }
    return this.isSpanish
      ? 'Video recibido; seguimiento requiere revisión manual'
      : 'Video received; tracking requires manual review';
  }

  trajectoryPoints(samples: Point[]): string {
    if (!samples || samples.length < 2) return '';
    const timeStart = samples[0].timeSeconds;
    const duration = Math.max(0.0001, samples[samples.length - 1].timeSeconds - timeStart);
    const positions = samples.map(x => x.position);
    const low = Math.min(...positions);
    const range = Math.max(0.0001, Math.max(...positions) - low);
    return samples.map(x =>
      (7 + 386 * (x.timeSeconds - timeStart) / duration).toFixed(1) + ',' +
      (99 - 84 * (x.position - low) / range).toFixed(1)).join(' ');
  }

  videoFor(condition: 'baseline' | 'followup', take: number): VideoRecord | undefined {
    return this.videos().find(x => x.condition === condition && x.take === take);
  }

  async reanalyzeSavedVideos(): Promise<void> {
    const study = this.study();
    if (!study || this.reanalyzingVideos() || this.uploadingVideo() || !this.videos().length) return;
    this.reanalyzingVideos.set(true);
    this.videoError.set('');
    this.videoComparison.set(null);
    let failed = 0;
    try {
      for (const record of [...this.videos()].sort((a,b) => a.take - b.take)) {
        if (record.spectralReview) continue; // Do not repeat an immutable analysis.
        try {
          const updated = await firstValueFrom(this.http.post<VideoRecord>(
            this.api + '/api/science/pendulum/preregistrations/' + study.id +
              '/videos/' + record.videoId + '/reanalyze', {}));
          this.videos.update(list =>
            list.map(x => x.videoId === updated.videoId ? updated : x));
        } catch { failed++; }
      }
      if (failed) {
        this.videoError.set(this.isSpanish
          ? failed + ' video(s) requieren revisión técnica. Los otros se conservaron.'
          : failed + ' video(s) need technical review. Other results were kept.');
      }
    } finally { this.reanalyzingVideos.set(false); }
  }

  get hasPendingReanalysis(): boolean {
    return this.videos().some(x => !x.spectralReview);
  }

  get canCompareVideos(): boolean {
    return this.videos().some(x => x.condition === 'baseline' && x.preliminaryPeriodSeconds !== null) &&
      this.videos().some(x => x.condition === 'followup' && x.preliminaryPeriodSeconds !== null);
  }

  async uploadVideo(event: Event, condition: 'baseline' | 'followup', take: number): Promise<void> {
    const fileInput = event.target as HTMLInputElement;
    const file = fileInput.files?.[0];
    const study = this.study();
    if (!file || !study || this.uploadingVideo()) return;
    this.videoComparison.set(null);
    this.videoError.set('');
    const extension = file.name.split('.').pop()?.toLowerCase();
    if (!extension || !['mov','mp4','m4v','webm','mkv','avi','3gp','3g2'].includes(extension) ||
        file.size > 240_000_000 || file.size < 10_000) {
      this.videoError.set(this.isSpanish
        ? 'Formato o tamaño no permitido. Usa MOV/MP4/WebM/MKV/AVI/3GP, hasta 240 MB.'
        : 'Unsupported format or size. Use MOV/MP4/WebM/MKV/AVI/3GP, up to 240 MB.');
      fileInput.value = '';
      return;
    }
    this.uploadingVideo.set(condition + '-' + take);
    try {
      const uploaded = await firstValueFrom(this.http.post<VideoRecord>(
        this.api + '/api/science/pendulum/preregistrations/' + study.id +
          '/video/' + condition + '/' + take + '?filename=' + encodeURIComponent(file.name),
        file));
      this.videos.update(all => [...all.filter(x => !(x.condition === condition && x.take === take)), uploaded]);
    } catch (err: unknown) {
      this.videoError.set(this.isSpanish
        ? 'No se pudo decodificar/subir el video. Comprueba que FFmpeg esté disponible y que el archivo sea legible; puede necesitar revisión manual.'
        : 'Video upload/decode failed. Check FFmpeg availability and media file validity; manual review may be needed.');
    } finally {
      this.uploadingVideo.set('');
      fileInput.value = '';
    }
  }

  async compareVideos(): Promise<void> {
    const s = this.study();
    if (!s || !this.canCompareVideos || this.busy()) return;
    this.busy.set(true);
    this.videoError.set('');
    this.videoComparison.set(null);
    try {
      const choose = (side: 'baseline' | 'followup') =>
        this.videos().filter(x => x.condition === side && x.preliminaryPeriodSeconds !== null)
          .sort((a,b) => a.take - b.take).slice(0,3).map(x => x.videoId);
      const r = await firstValueFrom(this.http.post<VideoComparison>(
        this.api + '/api/science/pendulum/preregistrations/' + s.id + '/compare-videos', {
          baselineVideoIds: choose('baseline'), followUpVideoIds: choose('followup')
        }));
      this.videoComparison.set(r);
    } catch {
      this.videoError.set(this.isSpanish
        ? 'El seguimiento local aún no permite comparar estos videos. Requiere revisar las coordenadas.'
        : 'Local tracking cannot yet compare these videos. Review the detected coordinates.');
    } finally { this.busy.set(false); }
  }

  async preregister(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.study.set(null);
    this.result.set(null);
    this.firstSeries.set(null);
    this.secondSeries.set(null);
    this.videos.set([]);
    this.videoComparison.set(null);
    try {
      const prediction = await firstValueFrom(this.http.post<Preregistration>(
        this.api + '/api/science/pendulum/preregister', {
          baselineLengthMeters: this.firstLength(),
          followUpLengthMeters: this.secondLength()
        }));
      this.study.set(prediction);
      localStorage.setItem('prooflab-pendulum-study-id', prediction.id);
    } catch {
      this.error.set(this.isSpanish
        ? 'No se pudo registrar la predicción. Verifica la API y las longitudes.'
        : 'Prediction could not be registered. Check the API and lengths.');
    } finally {
      this.busy.set(false);
    }
  }

  async loadCsv(event: Event, which: 'first' | 'second'): Promise<void> {
    const inputElement = event.target as HTMLInputElement;
    const file = inputElement.files?.[0];
    if (!file || !this.study()) return;
    this.result.set(null);
    this.error.set('');
    try {
      if (file.size > 1_000_000) throw new Error('CSV exceeds 1 MB.');
      const rows = PendulumStudy.parseCsv(await file.text());
      const source: SourceSequence = { filename: file.name.slice(0, 200), observations: rows };
      if (which === 'first') this.firstSeries.set(source);
      else this.secondSeries.set(source);
    } catch {
      this.error.set(this.isSpanish
        ? 'CSV inválido. Usa 120–30 000 filas con columnas tiempo_segundos,posicion, ordenadas por tiempo.'
        : 'Invalid CSV. Supply 120–30,000 time_seconds,position rows in increasing time order.');
      if (which === 'first') this.firstSeries.set(null);
      else this.secondSeries.set(null);
    }
  }

  async compare(): Promise<void> {
    const s = this.study();
    const a = this.firstSeries();
    const b = this.secondSeries();
    if (!s || !a || !b || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.result.set(null);
    try {
      const result = await firstValueFrom(this.http.post<Comparison>(
        this.api + '/api/science/pendulum/preregistrations/' + s.id + '/compare', {
          baseline: {
            setup: { kind: 'Pendulum', lengthMeters: s.hypothesis.baselineLengthMeters },
            observations: a.observations,
            origin: 'ManuallyEntered',
            reference: a.filename
          },
          followUp: {
            setup: { kind: 'Pendulum', lengthMeters: s.hypothesis.followUpLengthMeters },
            observations: b.observations,
            origin: 'ManuallyEntered',
            reference: b.filename
          }
        }));
      this.result.set(result);
    } catch {
      this.error.set(this.isSpanish
        ? 'El motor rechazó o no pudo comparar estas observaciones. Revisa que representen oscilaciones completas.'
        : 'The engine could not compare these observations. Check that they contain full oscillations.');
    } finally {
      this.busy.set(false);
    }
  }

  static parseCsv(input: string): Point[] {
    const lines = input.trim().split(/\r?\n/);
    if (lines.length < 120 || lines.length > 30001) throw new Error('Unexpected sample count.');
    const data: Point[] = [];
    for (const line of lines) {
      const cells = line.split(',').map(x => x.trim());
      if (cells.length !== 2) throw new Error('Exactly two columns required.');
      const t = Number(cells[0]);
      const position = Number(cells[1]);
      if (!Number.isFinite(t) || !Number.isFinite(position)) {
        if (!data.length && /[a-zA-Z]/.test(line) && !/\d/.test(line)) continue;
        throw new Error('Non-numeric value.');
      }
      if (data.length && t <= data[data.length - 1].timeSeconds)
        throw new Error('Unsorted timestamps.');
      data.push({ timeSeconds: t, position });
    }
    if (data.length < 120 || data.length > 30000) throw new Error('Insufficient data.');
    return data;
  }
}
