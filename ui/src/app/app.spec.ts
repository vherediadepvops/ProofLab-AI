import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { App } from './app';

describe('Competition Evidence Studio', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('shows an explicitly dated independent competition edition', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('26 OCT 2026');
    const request = TestBed.inject(HttpTestingController).expectOne('http://127.0.0.1:5391/api/health');
    request.flush({ metaModelApiEnabled: false, appVersion: '2026.10.26-alpha' });
  });

  it('disables chargeable Meta AI controls while account credits are unconfirmed', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('http://127.0.0.1:5391/api/health')
      .flush({ metaModelApiEnabled: false, appVersion: '2026.10.26-alpha' });
    fixture.detectChanges();
    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')) as HTMLButtonElement[];
    const ai = buttons.find(b => b.textContent?.includes('Solicitar hipótesis'));
    expect(ai?.disabled).toBeTrue();
  });

  it('does not confuse synthetic evidence with a real observation', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const controller = TestBed.inject(HttpTestingController);
    controller.expectOne('http://127.0.0.1:5391/api/health')
      .flush({ metaModelApiEnabled: false, appVersion: '2026.10.26-alpha' });
    const call = fixture.componentInstance.demo();
    controller.expectOne('http://127.0.0.1:5391/api/science/synthetic/Pendulum')
      .flush({ experiment: 'Pendulum', unit: 's', predictedValue: 1.42,
        observedValue: 1.44, differencePercent: 1.5, sampleCount: 1100,
        origin: 'Simulated', method: 'Simulated sample', evidenceStatement: 'SYNTHETIC',
        limitations: 'Not experimental' });
    await call;
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('SYNTHETIC TEST ONLY');
  });
});
