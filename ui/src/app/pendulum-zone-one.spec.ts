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
    expect(root.querySelectorAll('.upload-strip-ready input[type=file]').length).toBe(1);
    expect(root.textContent).toContain('Sube tu primera grabación');
    expect(root.textContent).toContain('Elige el video abajo para empezar automáticamente.');
    expect(root.querySelector('button.primary-button')).toBeNull();
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

  it('wires the immediately visible file input to the upload action',async()=>{
    const {root,component,http}=await init();
    const uploader=root.querySelector('.upload-strip-ready input[type=file]') as HTMLInputElement;
    expect(uploader).not.toBeNull();
    const trigger=spyOn(component,'uploadFocused').and.resolveTo();
    uploader.dispatchEvent(new Event('change',{bubbles:true}));
    expect(trigger).toHaveBeenCalled();
    expect(trigger.calls.mostRecent().args[1]).toBe('baseline');
    http.verify();
  });

  it('uploads directly: freezes the current model before transferring the first video',async()=>{
    const {root,component,fixture,http}=await init();
    const actual=new File([new Uint8Array(15000)],'50CM_1.MOV',
      {type:'video/quicktime'});
    const input=root.querySelector('.upload-strip-ready input[type=file]') as HTMLInputElement;
    expect(input).not.toBeNull();
    Object.defineProperty(input,'files',{configurable:true,value:[actual]});
    const uploading=component.uploadFocused({target:input} as unknown as Event,'baseline');
    expect(component.registeringAndUploading()).toBeTrue();

    // A new local theory calculation is performed from the selected parameters.
    const model=http.expectOne(theoryUrl);
    expect(model.request.body).toEqual({
      lengthMeters:.5,angleDegrees:15,massGrams:300,followUpLengthMeters:null
    });
    http.expectNone(studyUrl);
    model.flush(theory());
    await Promise.resolve();await Promise.resolve();

    const registering=http.expectOne(studyUrl);
    expect(registering.request.body).toEqual({
      baselineLengthMeters:.5,angleDegrees:15,massGrams:300,
      includeSecondTrial:false,followUpMode:'recommended',
      selectedFollowUpLengthMeters:null
    });
    http.expectNone(req=>req.url.includes('/video/baseline'));
    registering.flush(session);
    await Promise.resolve();await Promise.resolve();

    const transfer=http.expectOne(req=>req.url.includes('/video/baseline') &&
      req.urlWithParams.includes('filename=50CM_1.MOV'));
    expect(transfer.request.body).toBe(actual);
    transfer.flush({...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'});
    await uploading;
    fixture.detectChanges();

    expect(component.registeringAndUploading()).toBeFalse();
    expect(component.session()?.baseline?.originalFilename).toBe('50CM_1.MOV');
    expect(root.querySelectorAll('video').length).toBe(1);
    expect(root.querySelectorAll('canvas').length).toBe(3);
    expect(root.querySelectorAll('input[type=file]').length).toBe(0);
    expect(root.textContent).toContain('He revisado el video');
    http.expectNone(req=>req.url.includes('/api/meta/'));
    http.verify();
  });

  it('rejects unsupported videos BEFORE creating any research record',async()=>{
    const {root,component,fixture,http}=await init();
    const invalid=new File([new Uint8Array(14000)],'pendulo.exe',
      {type:'application/octet-stream'});
    const control={files:[invalid],value:'file-selected'};
    await component.uploadFocused({target:control} as unknown as Event,'baseline');
    fixture.detectChanges();
    expect(control.value).toBe('');
    expect(component.session()).toBeNull();
    expect(component.error()).toContain('Video inválido');
    expect(root.querySelector('.upload-strip-ready input[type=file]')).not.toBeNull();
    http.expectNone(studyUrl);
    http.expectNone(req=>req.url.includes('/video/baseline'));
    http.verify();
  });

  it('keeps the upload control available if registering the study fails',async()=>{
    const {root,component,fixture,http}=await init();
    const selected=new File([new Uint8Array(12000)],'50CM_2.MOV',
      {type:'video/quicktime'});
    const control={files:[selected],value:'file-selected'};
    const pending=component.uploadFocused({target:control} as unknown as Event,'baseline');
    http.expectOne(theoryUrl).flush(theory());
    await Promise.resolve();await Promise.resolve();
    http.expectOne(studyUrl).flush({error:'Unavailable'},{
      status:503,statusText:'Service Unavailable'
    });
    await pending;
    fixture.detectChanges();
    expect(component.registeringAndUploading()).toBeFalse();
    expect(component.session()).toBeNull();
    expect(component.error()).toContain('No se pudo crear la investigación');
    expect(control.value).toBe('');
    expect(root.querySelector('.upload-strip-ready input[type=file]')).not.toBeNull();
    http.expectNone(r=>r.url.includes('/video/baseline'));
    http.verify();
  });

  it('shows the video selector again immediately after New study',async()=>{
    const {root,component,fixture,http}=await init();
    component.session.set({...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'});
    fixture.detectChanges();
    expect(root.querySelector('.upload-strip-ready')).toBeNull();
    component.newFocusedStudy();
    fixture.detectChanges();
    expect(component.session()).toBeNull();
    expect(root.querySelector('.upload-strip-ready input[type=file]')).not.toBeNull();
    expect(root.textContent).toContain('Seleccionar video');
    expect(root.querySelectorAll('.placeholder-panel').length).toBe(3);
    expect((root.querySelector('#focus-length') as HTMLInputElement).disabled).toBeFalse();
    expect(localStorage.getItem('prooflab-director-current')).toBeNull();
    http.verify();
  });

  it('fills the four-perspective viewer with one video and a default combined primary',async()=>{
    const {root,component,fixture,http}=await init();
    component.session.set({...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'});
    fixture.detectChanges();
    const player=root.querySelector('app-pendulum-master-player');
    expect(player).not.toBeNull();
    expect(root.textContent).toContain('Cuatro perspectivas sincronizadas');
    expect(root.querySelectorAll('.view-panel').length).toBe(4);
    expect(root.querySelector('[data-view="combined"]')?.classList.contains('is-primary')).toBeTrue();
    expect(root.querySelectorAll('video').length).toBe(1);
    expect(root.querySelectorAll('canvas').length).toBe(3);
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
    expect(root.querySelectorAll('canvas').length).toBe(3);
    expect(root.querySelectorAll('.model-chart').length).toBe(0);
    http.verify();
  });
});
