import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PendulumStudy } from './pendulum-study';

describe('Preregistered pendulum flagship (local-only)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PendulumStudy],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  function setup() {
    const fixture = TestBed.createComponent(PendulumStudy);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    return { fixture, component: fixture.componentInstance, http };
  }

  const prepared = {
    id: 'a'.repeat(32),
    preregisteredAtUtc: '2026-10-27T20:00:00Z',
    hypothesis: {
      baselineLengthMeters: 0.5,
      followUpLengthMeters: 0.8,
      expectedBaselinePeriodSeconds: 1.419,
      expectedFollowUpPeriodSeconds: 1.795,
      predictedIncreasePercent: 26.4911,
      formula: 'T = 2π√(L/g)'
    },
    predictionSha256: 'c'.repeat(64),
    limitation: 'Not authenticated physical evidence'
  };

  it('preregisters a prediction without any Meta API request', async () => {
    const { component, fixture, http } = setup();
    const started = component.preregister();
    const request = http.expectOne(
      'http://127.0.0.1:5391/api/science/pendulum/preregister');
    expect(request.request.body).toEqual({
      baselineLengthMeters: 0.5,
      followUpLengthMeters: 0.8
    });
    request.flush(prepared);
    await started;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('26.49%');
    expect(fixture.nativeElement.textContent).toContain('TODAVÍA SIN MEDICIONES');
    http.expectNone(req => req.url.includes('/api/ai/'));
    http.verify();
  });

  it('rejects too short or unordered CSV sequences', () => {
    const good = Array.from({ length: 120 }, (_, i) => `${i / 60},${Math.sin(i / 10)}`).join('\n');
    expect(PendulumStudy.parseCsv(good).length).toBe(120);
    expect(() => PendulumStudy.parseCsv('time,position\n0,1\n1,2')).toThrow();
    const unordered = good.replace('0.016666666666666666,', '-1,');
    expect(() => PendulumStudy.parseCsv(unordered)).toThrow();
  });

  it('requires both coordinate sources before local comparison', async () => {
    const { component, fixture, http } = setup();
    const start = component.preregister();
    http.expectOne('http://127.0.0.1:5391/api/science/pendulum/preregister')
      .flush(prepared);
    await start;
    fixture.detectChanges();
    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button');
    const compare = Array.from(buttons)
      .find(button => button.textContent?.includes('Comparar')) as HTMLButtonElement;
    expect(compare.disabled).toBeTrue();
    component.firstSeries.set({ filename: 'first.csv', observations: [{ timeSeconds: 0, position: 1 }] });
    component.secondSeries.set({ filename: 'second.csv', observations: [{ timeSeconds: 0, position: 2 }] });
    const requested = component.compare();
    const call = http.expectOne(
      'http://127.0.0.1:5391/api/science/pendulum/preregistrations/' +
      prepared.id + '/compare');
    expect(call.request.body.baseline.origin).toBe('ManuallyEntered');
    expect(call.request.body.followUp.reference).toBe('second.csv');
    call.flush({
      evaluationId: 'e'.repeat(32), evaluatedAtUtc: '2026-10-27T20:01:00Z',
      inputSha256: 'f'.repeat(64),
      comparison: {
        baseline: { observedValue: 1.419, origin: 'ManuallyEntered', sampleCount: 120 },
        followUp: { observedValue: 1.795, origin: 'ManuallyEntered', sampleCount: 120 },
        predictedIncreasePercent: 26.49, observedIncreasePercent: 26.5,
        differencePercentagePoints: 0.01,
        evidenceClassification: 'USER_SUPPLIED_COORDINATES_NOT_INDEPENDENTLY_AUTHENTICATED',
        scientificConclusion: 'DESCRIPTIVE_COMPARISON_ONLY_NOT_A_CONFIRMED_DISCOVERY',
        limitations: 'Video not verified'
      }
    });
    await requested;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('NO DESCUBRIMIENTO');
    expect(fixture.nativeElement.textContent).toContain('NOT_INDEPENDENTLY_AUTHENTICATED');
    http.verify();
  });
});
