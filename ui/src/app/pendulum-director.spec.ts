import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PendulumDirector } from './pendulum-director';

describe('Flagship two-video research experience — not pretending AI', () => {
  beforeEach(async () => {
    localStorage.removeItem('prooflab-director-current');
    await TestBed.configureTestingModule({
      imports: [PendulumDirector],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  const root = {
    id: 'd'.repeat(32), baselineLengthMeters: .5,
    createdAtUtc: '2026-10-27T20:00:00Z',
    step: 'AWAITING_FIRST_VIDEO', modelStatus: 'LOCAL_PROTOCOL_NOT_META_AI_NOT_SAM',
    baseline: null, humanReview: null, proposal: null,
    followup: null, comparison: null
  };
  const sample = (role: 'baseline' | 'followup', period: number) => ({
    role, videoId: role[0].repeat(32),
    originalFilename: 'video-'+role+'.MOV',bytes:47000000,
    sha256: 'b'.repeat(64),uploadedAtUtc:'2026-10-27T20:01:00Z',
    media: { codec: 'hevc', durationSeconds: 30, width:1920,height:1080,
      nominalFramesPerSecond:60,containerFormat:'mov' },
    tracking: {framesAnalyzed:360,detectedFrames:340,detectionRate:.944,
      outcome:'LOCAL_TRACKING_CANDIDATE_REQUIRES_VIDEO_REVIEW',
      method:'LOCAL_NOT_SAM', limitations:'review required',
      observations:[{timeSeconds:0,position:60},{timeSeconds:.1,position:100}]},
    spectral: {periodSeconds:period,fitRSquared:.978,
      status:'PERIOD_CANDIDATE_REQUIRES_MANUAL_BOB_VERIFICATION'},
    posterAvailable:false
  });
  const proposal = {
    source:'LOCAL_PROTOCOL_NOT_META_AI_NOT_SAM',
    reasonEs:'Control local, NO IA', reasonEn:'Local control, NOT AI',
    registeredAtUtc:'2026-10-27T20:03:00Z',
    baselineSha256:'b'.repeat(64),predictionSha256:'a'.repeat(64),
    theory: {baselineLengthMeters:.5,followUpLengthMeters:.8,
      expectedBaselinePeriodSeconds:1.419,expectedFollowUpPeriodSeconds:1.795,
      predictedIncreasePercent:26.49}
  };
  function init() {
    const fixture=TestBed.createComponent(PendulumDirector);
    fixture.detectChanges();
    const controller=TestBed.inject(HttpTestingController);
    return {fixture,component:fixture.componentInstance,controller};
  }

  it('begins with one video only, no six-video requirement', async () => {
    const {component,fixture,controller}=init();
    expect(fixture.nativeElement.textContent).toContain('Investiga un fenómeno con un video');
    const started=component.start();
    const req=controller.expectOne('http://127.0.0.1:5391/api/science/director/pendulum/sessions');
    expect(req.request.body).toEqual({baselineLengthMeters:.5});
    req.flush(root);
    await started;
    fixture.detectChanges();
    const inputs=(fixture.nativeElement as HTMLElement).querySelectorAll('input[type=file]');
    expect(inputs.length).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('01 · Observar el péndulo');
    expect(fixture.nativeElement.textContent).toContain('NO dirige este experimento');
    controller.expectNone(r=>r.url.includes('/api/meta/test'));
    controller.verify();
  });

  it('requires initial video and human confirmation before locally freezing a prediction', async () => {
    const {component,fixture,controller}=init();
    const started=component.start();
    controller.expectOne('http://127.0.0.1:5391/api/science/director/pendulum/sessions')
      .flush(root);
    await started;
    const file=new File([new Uint8Array(14000)],'iphone.MOV',
      {type:'video/quicktime'});
    const control={files:[file],value:'selected'};
    const uploading=component.upload({target:control} as unknown as Event,'baseline');
    const req=controller.expectOne(r=>r.url.includes('/video/baseline') &&
      r.urlWithParams.includes('filename=iphone.MOV'));
    expect(req.request.body).toBe(file);
    const first={...root,baseline:sample('baseline',1.416),
      step:'AWAITING_HUMAN_BOB_REVIEW'};
    req.flush(first);
    await uploading;
    fixture.detectChanges();
    expect(control.value).toBe('');
    expect(fixture.nativeElement.textContent).toContain('1.416 s');
    expect(fixture.nativeElement.textContent).not.toContain('80 cm');
    component.checkedVideo.set(true);
    const confirming=component.confirm();
    const check=controller.expectOne('http://127.0.0.1:5391/api/science/director/pendulum/sessions/'+root.id+'/review-baseline');
    expect(check.request.body).toEqual({bobConfirmed:true});
    const reviewed={...first,step:'READY_TO_PROPOSE',
      humanReview:{bobConfirmed:true,reviewedAtUtc:'2026-10-27T20:02:00Z'}};
    check.flush(reviewed);
    await confirming;

    const registering=component.propose();
    const proposeCall=controller.expectOne('http://127.0.0.1:5391/api/science/director/pendulum/sessions/'+root.id+'/propose-local-control');
    proposeCall.flush({...reviewed,proposal,step:'AWAITING_SECOND_VIDEO'});
    await registering;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('80 cm');
    expect(fixture.nativeElement.textContent).toContain('NO IA');
    expect(fixture.nativeElement.textContent).toContain('SHA-256');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('input[type=file]').length).toBe(1);
    controller.expectNone(r=>r.url.includes('/api/meta/') || r.url.includes('/api/math/'));
    controller.verify();
  });

  it('compares only after a separate follow-up video is available', async () => {
    const {component,fixture,controller}=init();
    const state={...root,baseline:sample('baseline',1.416),
      humanReview:{bobConfirmed:true,reviewedAtUtc:'2026-10-27T20:02:00Z'},
      proposal,followup:sample('followup',1.798),
      step:'READY_TO_COMPARE'};
    component.session.set(state);
    fixture.detectChanges();
    const comparing=component.compare();
    const result=controller.expectOne('http://127.0.0.1:5391/api/science/director/pendulum/sessions/'+root.id+'/compare');
    result.flush({...state,step:'COMPARED_PRELIMINARY',
      comparison:{status:'PRELIMINARY_LOCAL_COMPARISON_NOT_AI_SAM_OR_CERTIFIED',
        source:'LOCAL_PROTOCOL_NOT_META_AI_NOT_SAM',comparedAtUtc:'2026-10-27T20:04:00Z',
        baselinePeriodSeconds:1.416,followUpPeriodSeconds:1.798,
        predictedIncreasePercent:26.49,observedIncreasePercent:26.98,
        changeGapPercentagePoints:.49,
        limitationsEs:'Sin SAM ni revisión independiente.',
        limitationsEn:'No SAM or independent review.'}});
    await comparing;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('26.98');
    expect(fixture.nativeElement.textContent).toContain('Sin SAM ni revisión independiente.');
    controller.verify();
  });

  it('shows a browser-compatible review clip before asking to confirm bob identity', () => {
    const {component,fixture,controller}=init();
    component.session.set({
      ...root,
      baseline:{...sample('baseline',1.416),previewAvailable:true},
      step:'AWAITING_HUMAN_BOB_REVIEW'
    });
    fixture.detectChanges();
    const video=(fixture.nativeElement as HTMLElement).querySelector('video');
    expect(video).not.toBeNull();
    expect(video?.hasAttribute('controls')).toBeTrue();
    expect(video?.querySelector('source')?.getAttribute('src')).toBe(
      'http://127.0.0.1:5391/api/science/director/pendulum/sessions/' +
      root.id+'/preview/baseline');
    expect(fixture.nativeElement.textContent).toContain('Vista previa H.264');
    controller.verify();
  });
});
