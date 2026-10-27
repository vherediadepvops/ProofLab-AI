import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, input, signal } from '@angular/core';
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
export class PendulumStudy {
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

  get isSpanish(): boolean { return this.language() === 'es'; }

  async preregister(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.study.set(null);
    this.result.set(null);
    this.firstSeries.set(null);
    this.secondSeries.set(null);
    try {
      const prediction = await firstValueFrom(this.http.post<Preregistration>(
        this.api + '/api/science/pendulum/preregister', {
          baselineLengthMeters: this.firstLength(),
          followUpLengthMeters: this.secondLength()
        }));
      this.study.set(prediction);
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
