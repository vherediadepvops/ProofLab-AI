import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PendulumZoneOne } from './pendulum-zone-one';

describe('Clean wide Pendulum Zone 1 for the hackathon jury', () => {
  const theoryUrl='http://127.0.0.1:5391/api/science/pendulum/mathematical-model';
  const studyUrl='http://127.0.0.1:5391/api/science/director/pendulum/sessions';

  beforeEach(async () => {
    localStorage.removeItem('prooflab-director-current');
    await TestBed.configureTestingModule({
      imports:[PendulumZoneOne],
      providers:[provideHttpClient(),provideHttpClientTesting()]
    }).compileComponents();
  });

  const firstSample={timeSeconds:0,angleDegrees:15,
    horizontalCentimeters:12,elevationCentimeters:2,kineticJoules:0,
    potentialJoules:.05,totalJoules:.05};
  const theory=(second=false)=>({
    source:'DETERMINISTIC_NONLINEAR_RK4_THEORY_NOT_META_NOT_SAM',
    equation:'d²θ/dt²+(g/L)sinθ=0',
    assumptions:'ideal',
    baseline:{lengthMeters:.5,initialAngleDegrees:15,massGrams:300,
      nonlinearPeriodSeconds:1.425,smallAnglePeriodSeconds:1.419,
      amplitudeCentimeters:13,initialPotentialJoules:.05,
      maximumRelativeEnergyDriftPercent:0,
      samples:[firstSample]},
    followUp:second?{lengthMeters:.8,initialAngleDegrees:15,massGrams:300,
      nonlinearPeriodSeconds:1.802,smallAnglePeriodSeconds:1.795,
      amplitudeCentimeters:21,initialPotentialJoules:.08,
      maximumRelativeEnergyDriftPercent:0,samples:[firstSample]}:null,
    predictedPeriodIncreasePercent:second?26.49:null
  });
  const session={
    id:'d'.repeat(32),baselineLengthMeters:.5,angleDegrees:15,
    massGrams:300,includeSecondTrial:false,followUpMode:'recommended' as const,
    selectedFollowUpLengthMeters:null,createdAtUtc:'2026-10-27T20:00:00Z',
    step:'AWAITING_FIRST_VIDEO',modelStatus:'LOCAL_PROTOCOL_NOT_META_AI_NOT_SAM',
    baseline:null,humanReview:null,proposal:null,followup:null,
    comparison:null,singleComparison:null,
    modelSha256:'1'.repeat(64)
  };
  const video={
    role:'baseline',videoId:'e'.repeat(32),
    originalFilename:'50CM_1.MOV',bytes:45708115,sha256:'b'.repeat(64),
    uploadedAtUtc:'2026-10-27T20:01:00Z',previewAvailable:true,
    posterAvailable:true,
    media:{codec:'hevc',durationSeconds:30.5,width:1920,height:1080,
      nominalFramesPerSecond:59.94,containerFormat:'mov'},
    tracking:{framesAnalyzed:366,detectedFrames:316,detectionRate:.86,
      observations:[{timeSeconds:0,position:150}],
      pixelSamples:[{timeSeconds:0,x:100,y:180,approximateRadiusPixels:9}],
      outcome:'LOCAL_TRACKING_CANDIDATE_REQUIRES_VIDEO_REVIEW',
      method:'LOCAL_NOT_SAM',limitations:'local candidate'},
    spectral:{periodSeconds:1.416,fitRSquared:.97,
      status:'PERIOD_CANDIDATE_REQUIRES_MANUAL_BOB_VERIFICATION'}
  };

  async function init(){
    const fixture=TestBed.createComponent(PendulumZoneOne);
    fixture.detectChanges();
    const http=TestBed.inject(HttpTestingController);
    const req=http.expectOne(theoryUrl);
    req.flush(theory());
    await Promise.resolve();
    fixture.detectChanges();
    return {fixture,http,component:fixture.componentInstance,
      root:fixture.nativeElement as HTMLElement};
  }

  it('shows only the requested configuration and three video view placeholders',async()=>{
    const {root,component,http}=await init();
    expect(root.textContent).toContain('Configura el péndulo');
    expect(root.textContent).toContain('Video y tres vistas sincronizadas');
    expect(root.querySelectorAll('.parameter-grid input').length).toBe(3);
    expect(root.querySelector('#focus-length')).not.toBeNull();
    expect(root.querySelector('#focus-angle')).not.toBeNull();
    expect(root.querySelector('#focus-mass')).not.toBeNull();
    expect(root.querySelectorAll('.placeholder-panel').length).toBe(3);
    expect(root.querySelector('.model-charts')).toBeNull();
    expect(root.querySelector('.model-chart')).toBeNull();
    expect(root.querySelector('.technical-protocol')).toBeNull();
    expect(component.includeSecondTrial()).toBeFalse();
    expect(root.querySelector('app-pendulum-director')).toBeNull();
    http.verify();
  });

  it('starts one-video research from the compact parameters and then shows the upload',async()=>{
    const {root,component,fixture,http}=await init();
    const starting=component.start();
    const req=http.expectOne(studyUrl);
    expect(req.request.body).toEqual({
      baselineLengthMeters:.5,angleDegrees:15,massGrams:300,
      includeSecondTrial:false,followUpMode:'recommended',
      selectedFollowUpLengthMeters:null
    });
    req.flush(session);
    await starting;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelectorAll('input[type=file]').length).toBe(1);
    expect(root.textContent).toContain('Sube tu primera grabación');
    expect((root.querySelector('#focus-length') as HTMLInputElement).disabled).toBeTrue();
    expect(root.querySelectorAll('.placeholder-panel').length).toBe(3);
    http.verify();
  });

  it('fills the full three-view viewer using one player for the source clip',async()=>{
    const {root,component,fixture,http}=await init();
    component.session.set({...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'});
    fixture.detectChanges();
    const player=root.querySelector('app-pendulum-master-player');
    expect(player).not.toBeNull();
    expect(root.querySelectorAll('video').length).toBe(1);
    expect(root.querySelectorAll('canvas').length).toBe(2);
    expect(root.querySelectorAll('.placeholder-panel').length).toBe(0);
    expect(player?.textContent).toContain('Rastro fantasma');
    expect(root.textContent).toContain('He revisado el video');
    expect(root.textContent).toContain('NO SAM 3.1');
    expect(root.querySelectorAll('.model-chart').length).toBe(0);
    http.verify();
  });

  it('still allows optional manual 80 cm experiments without displaying math graphs',async()=>{
    const {root,component,fixture,http}=await init();
    component.includeSecondTrial.set(true);
    const updating=component.refreshTheory();
    let req=http.expectOne(theoryUrl);
    expect(req.request.body.followUpLengthMeters).toBe(.8);
    req.flush(theory(true));
    await updating;
    fixture.detectChanges();
    expect(root.textContent).toContain('Propuesta local (sin IA)');
    component.followUpMode.set('manual');
    const manualUpdate=component.refreshTheory();
    req=http.expectOne(theoryUrl);
    req.flush(theory(true));
    await manualUpdate;
    fixture.detectChanges();
    expect(root.querySelectorAll('.followup-label input').length).toBe(1);
    expect(root.querySelectorAll('.model-chart').length).toBe(0);
    http.verify();
  });

  it('keeps the same player slot for two videos, never six simultaneous windows',async()=>{
    const {root,component,fixture,http}=await init();
    const second={...video,role:'followup',originalFilename:'80CM_1.MOV',
      videoId:'f'.repeat(32)};
    component.session.set({...session,includeSecondTrial:true,baseline:video,
      followup:second,step:'READY_TO_COMPARE',
      humanReview:{bobConfirmed:true,reviewedAtUtc:'2026-10-27T20:03:00Z'}});
    fixture.detectChanges();
    expect(root.querySelectorAll('.trial-tabs button').length).toBe(2);
    expect(root.querySelectorAll('video').length).toBe(1);
    component.activeTrial.set('followup');
    fixture.detectChanges();
    const source=root.querySelector('video.master-source') as HTMLVideoElement;
    expect(source.getAttribute('src')).toContain('/preview/followup');
    expect(root.querySelectorAll('canvas').length).toBe(2);
    expect(root.querySelectorAll('.model-chart').length).toBe(0);
    http.verify();
  });
});
