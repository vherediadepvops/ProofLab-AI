import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PendulumStudy } from './pendulum-study';

describe('Preregistered pendulum flagship (local-only)', () => {
  beforeEach(async () => {
    localStorage.removeItem('prooflab-pendulum-study-id');
    await TestBed.configureTestingModule({
      imports: [PendulumStudy],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  function setup() {
    const fixture = TestBed.createComponent(PendulumStudy);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://127.0.0.1:5391/api/science/pendulum/video-formats')
      .flush({ decoderAvailable: true, supportedExtensions: ['.mov','.mp4'],
        maximumVideoBytes: 240000000, maximumDurationSeconds: 80,
        maxTakesPerCondition: 3, processingMethod: 'LOCAL_NOT_SAM' });
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
      .find(button => button.textContent?.includes('Comparar las dos observaciones')) as HTMLButtonElement;
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

  it('accepts iPhone MOV raw bytes without sending them to Meta', async () => {
    const { component, fixture, http } = setup();
    const started = component.preregister();
    http.expectOne('http://127.0.0.1:5391/api/science/pendulum/preregister')
      .flush(prepared);
    await started;

    const movie = new File([new Uint8Array(12000)], 'iphone-hevc.MOV',
      { type: 'video/quicktime' });
    const promise = component.uploadVideo({
      target: { files: [movie] }
    } as unknown as Event, 'baseline', 1);

    const call = http.expectOne(req =>
      req.url.includes('/api/science/pendulum/preregistrations/' + prepared.id +
          '/video/baseline/1') &&
      req.urlWithParams.includes('filename=iphone-hevc.MOV'));
    expect(call.request.body).toBe(movie);
    call.flush({
      videoId: 'b'.repeat(32), condition: 'baseline', take: 1,
      originalFilename: movie.name,
      sourceSha256: 'f'.repeat(64),
      sizeBytes: 12000,
      media: { codec: 'hevc', width: 1920, height: 1080,
        durationSeconds: 30, nominalFramesPerSecond: 59.9 },
      tracking: { detectedFrames: 0, framesAnalyzed: 360, detectionRate: 0,
        outcome: 'NEEDS_MANUAL_REVIEW' },
      preliminaryPeriodSeconds: null,
      outcome: 'UPLOADED_NEEDS_MANUAL_TRACKING_REVIEW',
      provenanceNote: 'Local FFmpeg not SAM'
    });

    await promise;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('iphone-hevc.MOV');
    expect(fixture.nativeElement.textContent).toContain('HEVC');
    expect(fixture.nativeElement.textContent).toContain('seguimiento requiere revisión manual');
    expect(component.canCompareVideos).toBeFalse();
    http.expectNone(req => req.url.includes('/api/ai/'));
    http.verify();
  });

  it('rejects executable files before contacting API', async () => {
    const { component, http } = setup();
    const started = component.preregister();
    http.expectOne('http://127.0.0.1:5391/api/science/pendulum/preregister')
      .flush(prepared);
    await started;
    const bad = new File([new Uint8Array(12000)], 'trojan.exe');
    await component.uploadVideo({
      target: { files: [bad] }
    } as unknown as Event, 'followup', 2);
    expect(component.videoError()).toContain('Formato');
    http.verify();
  });

  it('keeps existing study on server while allowing the jury to start another', async () => {
    const { component, fixture, http } = setup();
    const started = component.preregister();
    http.expectOne('http://127.0.0.1:5391/api/science/pendulum/preregister')
      .flush(prepared);
    await started;
    fixture.detectChanges();
    expect(localStorage.getItem('prooflab-pendulum-study-id')).toBe(prepared.id);
    expect(fixture.nativeElement.textContent).toContain('50 cm');
    expect(fixture.nativeElement.textContent).toContain('80 cm');
    component.newStudy();
    fixture.detectChanges();
    expect(localStorage.getItem('prooflab-pendulum-study-id')).toBeNull();
    expect(component.study()).toBeNull();
    expect(component.videos().length).toBe(0);
    http.verify(); // Neither a delete nor a model call was made.
  });
});
