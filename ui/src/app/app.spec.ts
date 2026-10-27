import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { App } from './app';

describe('ProofLab Competition Research Lab', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  function initialize(leanAvailable = false, metaEnabled = false): { app: App; root: HTMLElement; controller: HttpTestingController; fixture: any } {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const controller = TestBed.inject(HttpTestingController);
    controller.expectOne('http://127.0.0.1:5391/api/math/problems').flush([{
      id: 'NAT-ADD-COMM', titleEnglish: 'Commutative law',
      titleSpanish: 'Ley conmutativa',
      statementEnglish: 'For all a and b, a + b = b + a.',
      statementSpanish: 'Para todos a y b, a + b = b + a.',
      researchStatus: 'KNOWN_THEOREM',
      domain: 'Number theory', source: 'Lean core arithmetic',
      allowedStrategies: ['rfl', 'omega', 'simp']
    }]);
    controller.expectOne('http://127.0.0.1:5391/api/math/status')
      .flush({ leanAvailable, phase: 'KNOWN_BENCHMARK_VERIFICATION' });
    controller.expectOne('http://127.0.0.1:5391/api/health')
      .flush({ metaModelApiEnabled: metaEnabled, appVersion: '2026.10.26-alpha' });
    fixture.detectChanges();
    return { app: fixture.componentInstance, root: fixture.nativeElement as HTMLElement, controller, fixture };
  }

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
    expect(ctx.root.textContent).toContain('SYNTHETIC TEST ONLY');
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
    expect(ctx.root.textContent).toContain('VERIFIED');
    expect(ctx.root.textContent).toContain('New scientific discovery');
    expect(ctx.root.textContent).toContain('NO');
  });
});
