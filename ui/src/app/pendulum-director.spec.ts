import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PendulumDirector } from './pendulum-director';

describe('ProofLab physical workbench and guided flagship experiment', () => {
  const modelApi='http://127.0.0.1:5391/api/science/pendulum/mathematical-model';
  const studyApi='http://127.0.0.1:5391/api/science/director/pendulum/sessions';
  beforeEach(async () => {
    localStorage.removeItem('prooflab-director-current');
    await TestBed.configureTestingModule({
      imports: [PendulumDirector],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  const sample=(t:number,x:number, kinetic:number) => ({
    timeSeconds:t,angleDegrees:15,angularVelocityRadiansPerSecond:0,
    horizontalCentimeters:x,elevationCentimeters:2,
    kineticJoules:kinetic,potentialJoules:.05-kinetic,totalJoules:.05
  });
  const trial=(length:number,period:number) => ({
    lengthMeters:length,initialAngleDegrees:15,massGrams:300,
    smallAnglePeriodSeconds:length===.5?1.419:1.795,
    nonlinearPeriodSeconds:period,
    amplitudeCentimeters:13,initialPotentialJoules:.05,
    maximumRelativeEnergyDriftPercent:0.00001,
    samples:[sample(0,13,0),sample(5,-13,.03),
      sample(10,13,0),sample(15,-13,.03),sample(20,13,0)]
  });
  const theory=(two:boolean) => ({
    source:'DETERMINISTIC_NONLINEAR_RK4_THEORY_NOT_META_NOT_SAM',
    standardGravityMetersPerSecondSquared:9.80665,
    equation:'d²θ/dt²+(g/L)sinθ=0',
    assumptions:'Ideal point mass, no damping',
    baseline:trial(.5,1.425),
    followUp:two?trial(.8,1.803):null,
    predictedPeriodIncreasePercent:two?26.491:null
  });
  const root=(two=false)=>({
    id:'d'.repeat(32),baselineLengthMeters:.5,
    angleDegrees:15,massGrams:300,includeSecondTrial:two,
    followUpMode:'recommended' as const,selectedFollowUpLengthMeters:null,
    createdAtUtc:'2026-10-27T20:00:00Z',
    step:'AWAITING_FIRST_VIDEO',modelStatus:'LOCAL_PROTOCOL_NOT_META_AI_NOT_SAM',
    baseline:null,humanReview:null,proposal:null,followup:null,
    comparison:null,singleComparison:null
  });
  const video=(role:'baseline'|'followup',period:number)=>({
    role,videoId:role[0].repeat(32),
    originalFilename:'iphone-'+role+'.MOV',bytes:47000000,
    sha256:'b'.repeat(64),uploadedAtUtc:'2026-10-27T20:01:00Z',
    media:{codec:'hevc',durationSeconds:30,width:1920,height:1080,
      nominalFramesPerSecond:60,containerFormat:'mov'},
    tracking:{framesAnalyzed:360,detectedFrames:340,detectionRate:.94,
      outcome:'LOCAL_TRACKING_CANDIDATE_REQUIRES_VIDEO_REVIEW',method:'LOCAL_NOT_SAM',
      limitations:'review',observations:[
        {timeSeconds:0,position:60},{timeSeconds:.1,position:100}]},
    spectral:{periodSeconds:period,fitRSquared:.97,
      status:'PERIOD_CANDIDATE_REQUIRES_MANUAL_BOB_VERIFICATION'},
    posterAvailable:false,previewAvailable:false
  });
  const proposal={
    source:'LOCAL_PROTOCOL_NOT_META_AI_NOT_SAM',
    reasonEs:'Control local, NO IA',reasonEn:'Local control, NOT AI',
    registeredAtUtc:'2026-10-27T20:03:00Z',baselineSha256:'b'.repeat(64),
    predictionSha256:'a'.repeat(64),
    theory:{baselineLengthMeters:.5,followUpLengthMeters:.8,
      expectedBaselinePeriodSeconds:1.419,expectedFollowUpPeriodSeconds:1.795,
      predictedIncreasePercent:26.49}
  };

  async function initialize(two=false) {
    const fixture=TestBed.createComponent(PendulumDirector);
    const component=fixture.componentInstance;
    component.includeSecondTrial.set(two);
    fixture.detectChanges();
    const http=TestBed.inject(HttpTestingController);
    const req=http.expectOne(modelApi);
    expect(req.request.body).toEqual({
      lengthMeters:.5,angleDegrees:15,massGrams:300,
      followUpLengthMeters:two?.8:null
    });
    req.flush(theory(two));
    await Promise.resolve();
    fixture.detectChanges();
    return {component,fixture,http,rootEl:fixture.nativeElement as HTMLElement};
  }

  it('displays three reproducible mathematical plots before any video', async () => {
    const {component,fixture,http,rootEl}=await initialize();
    expect(component.model()?.baseline.nonlinearPeriodSeconds).toBe(1.425);
    expect(rootEl.querySelectorAll('.model-chart').length).toBe(3);
    expect(rootEl.textContent).toContain('1.425 s');
    expect(rootEl.textContent).toContain('Energía inicial');
    expect(rootEl.textContent).toContain('No se harán llamadas a Meta');
    const started=component.start();
    const req=http.expectOne(studyApi);
    expect(req.request.body).toEqual({
      baselineLengthMeters:.5,angleDegrees:15,massGrams:300,
      includeSecondTrial:false,followUpMode:'recommended',
      selectedFollowUpLengthMeters:null
    });
    req.flush(root(false));
    await started;
    fixture.detectChanges();
    expect(rootEl.querySelectorAll('input[type=file]').length).toBe(1);
    expect(rootEl.textContent).toContain('Investiga un fenómeno con un video');
    http.expectNone(x=>x.url.includes('/meta/'));
    http.verify();
  });

  it('updates the entire theoretical model after angle or mass changes', async () => {
    const {component,fixture,http,rootEl}=await initialize();
    const before=component.paths()?.motionA;
    component.initialAngleDegrees.set(35);
    component.massGrams.set(500);
    const pending=component.refreshTheory();
    const req=http.expectOne(modelApi);
    expect(req.request.body).toEqual({
      lengthMeters:.5,angleDegrees:35,massGrams:500,
      followUpLengthMeters:null
    });
    req.flush({...theory(false),baseline:{
      ...trial(.5,1.46),initialAngleDegrees:35,massGrams:500,
      initialPotentialJoules:.155,samples:[
        sample(0,29,0),sample(5,-29,.09),sample(10,29,0),
        sample(15,-29,.09),sample(20,29,0)]
    }});
    await pending;
    fixture.detectChanges();
    expect(rootEl.textContent).toContain('1.460 s');
    expect(component.paths()?.motionA).not.toEqual(before);
    http.verify();
  });

  it('offers recommended or free second trial without Muse calls', async () => {
    const {component,fixture,http,rootEl}=await initialize();
    component.includeSecondTrial.set(true);
    let pending=component.refreshTheory();
    let req=http.expectOne(modelApi);
    expect(req.request.body.followUpLengthMeters).toBe(.8);
    req.flush(theory(true));
    await pending;
    fixture.detectChanges();
    expect(rootEl.textContent).toContain('80 cm');
    component.followUpMode.set('manual');
    component.manualFollowUpLength.set(.9);
    pending=component.refreshTheory();
    req=http.expectOne(modelApi);
    expect(req.request.body.followUpLengthMeters).toBe(.9);
    req.flush({...theory(true),followUp:trial(.9,1.92)});
    await pending;
    const starting=component.start();
    req=http.expectOne(studyApi);
    expect(req.request.body.includeSecondTrial).toBeTrue();
    expect(req.request.body.followUpMode).toBe('manual');
    expect(req.request.body.selectedFollowUpLengthMeters).toBe(.9);
    req.flush({...root(true),followUpMode:'manual',selectedFollowUpLengthMeters:.9});
    await starting;
    http.verify();
  });

  it('allows a complete single-trial theoretical comparison without another video', async () => {
    const {component,fixture,http,rootEl}=await initialize();
    const session={...root(false),baseline:video('baseline',1.416),
      humanReview:{bobConfirmed:true,reviewedAtUtc:'2026-10-27T20:02:00Z'},
      step:'READY_TO_PROPOSE'};
    component.session.set(session);
    fixture.detectChanges();
    expect(rootEl.textContent).toContain('Esta investigación solo necesita un video');
    const pending=component.compareSingle();
    const request=http.expectOne(studyApi+'/'+session.id+'/compare-single');
    request.flush({...session,step:'SINGLE_COMPARED_PRELIMINARY',
      singleComparison:{
        predictedNonlinearPeriodSeconds:1.425,
        predictedSmallAnglePeriodSeconds:1.419,
        observedLocalPeriodSeconds:1.416,
        relativePeriodErrorPercent:.63,
        status:'PRELIMINARY_LOCAL_SINGLE_VIDEO_NOT_SAM_OR_VALIDATED',
        limitationsEs:'Sin SAM ni revisión independiente.',
        limitationsEn:'No independent review.'
      }
    });
    await pending;
    fixture.detectChanges();
    expect(rootEl.textContent).toContain('0.63 %');
    expect(rootEl.querySelectorAll('input[type=file]').length).toBe(0);
    http.verify();
  });

  it('keeps manual review and a frozen local proposal before second video', async () => {
    const {component,fixture,http,rootEl}=await initialize(true);
    const started=component.start();
    http.expectOne(studyApi).flush(root(true));
    await started;
    const file=new File([new Uint8Array(14000)],'iphone.MOV',
      {type:'video/quicktime'});
    const control={files:[file],value:'selected'};
    const uploading=component.upload({target:control} as unknown as Event,'baseline');
    const uploaded=http.expectOne(req=>req.url.includes('/video/baseline') &&
      req.urlWithParams.includes('filename=iphone.MOV'));
    expect(uploaded.request.body).toBe(file);
    const observed={...root(true),baseline:video('baseline',1.416),
      step:'AWAITING_HUMAN_BOB_REVIEW'};
    uploaded.flush(observed);
    await uploading;
    fixture.detectChanges();
    expect(control.value).toBe('');
    expect(rootEl.textContent).toContain('1.416 s');
    component.checkedVideo.set(true);
    const confirming=component.confirm();
    const review=http.expectOne(studyApi+'/'+root(true).id+'/review-baseline');
    expect(review.request.body).toEqual({bobConfirmed:true});
    const reviewed={...observed,step:'READY_TO_PROPOSE',
      humanReview:{bobConfirmed:true,reviewedAtUtc:'2026-10-27T20:02:00Z'}};
    review.flush(reviewed);
    await confirming;
    const proposing=component.propose();
    http.expectOne(studyApi+'/'+root(true).id+'/propose-local-control')
      .flush({...reviewed,proposal,step:'AWAITING_SECOND_VIDEO'});
    await proposing;
    fixture.detectChanges();
    expect(rootEl.textContent).toContain('80 cm');
    expect(rootEl.textContent).toContain('NO IA');
    expect(rootEl.textContent).toContain('SHA-256');
    expect(rootEl.querySelectorAll('input[type=file]').length).toBe(1);
    http.expectNone(r=>r.url.includes('/meta/'));
    http.verify();
  });

  it('keeps original video for the human check', async () => {
    const {component,fixture,http,rootEl}=await initialize();
    component.session.set({
      ...root(false),baseline:{...video('baseline',1.416),previewAvailable:true},
      step:'AWAITING_HUMAN_BOB_REVIEW'
    });
    fixture.detectChanges();
    const master=rootEl.querySelector('app-pendulum-master-player');
    expect(master).not.toBeNull();
    const mediaElement=master?.querySelector('video.master-source');
    expect(mediaElement).not.toBeNull();
    expect(mediaElement?.getAttribute('src')).toBe(
      studyApi+'/'+root(false).id+'/original/baseline');
    expect(master?.querySelectorAll('canvas').length).toBe(3);
    expect(rootEl.textContent).toContain('Cuatro perspectivas, un solo reloj');
    http.verify();
  });

  it('renders video-derived pixel/velocity/phase plots separately from theoretical charts', async () => {
    const {component,fixture,http,rootEl}=await initialize();
    const captured=Array.from({length:30},(_,i)=>({
      timeSeconds:i/12, position:120+50*Math.sin(i/4)
    }));
    component.session.set({
      ...root(false),
      baseline:{
        ...video('baseline',1.416),
        tracking:{
          ...video('baseline',1.416).tracking,
          observations:captured
        }
      },
      step:'AWAITING_HUMAN_BOB_REVIEW'
    });
    fixture.detectChanges();
    expect(component.evidencePlots().baseline?.count).toBe(30);
    expect(rootEl.querySelectorAll('.theory-workbench .model-chart').length).toBe(3);
    expect(rootEl.querySelectorAll('.evidence-studio .model-chart').length).toBe(3);
    expect(rootEl.textContent).toContain('píxeles/segundo');
    expect(rootEl.textContent).toContain('NO de SAM ni de Muse');
    expect(component.baselinePeriodDiscrepancyPercent).toBeGreaterThan(0);
    http.verify();
  });

  it('renders a visibly labeled normalized overlay without changing measured periods', async () => {
    const {component,fixture,http,rootEl}=await initialize();
    const originalTheory=theory(false);
    component.model.set({
      ...originalTheory,
      baseline:{
        ...originalTheory.baseline,
        samples:Array.from({length:501},(_,i)=>({
          ...sample(i*.04,13*Math.cos(2*Math.PI*(i*.04)/1.425),0),
          horizontalCentimeters:13*Math.cos(2*Math.PI*(i*.04)/1.425)
        }))
      }
    });
    const tracked=Array.from({length:360},(_,i)=>({
      timeSeconds:i/12,
      position:190+42*Math.cos(2*Math.PI*((i/12)-2)/1.416+.4)
    }));
    component.session.set({
      ...root(false),
      baseline:{
        ...video('baseline',1.416),
        tracking:{...video('baseline',1.416).tracking,observations:tracked}
      },
      step:'AWAITING_HUMAN_BOB_REVIEW'
    });
    fixture.detectChanges();
    expect(component.normalizedOverlays().baseline).not.toBeNull();
    expect(rootEl.querySelectorAll('.overlay-comparison').length).toBe(1);
    expect(rootEl.textContent).toContain('SOLO VISUAL');
    expect(rootEl.textContent).toContain('NO se modifica la frecuencia teórica');
    expect(component.session()?.baseline?.spectral.periodSeconds).toBe(1.416);
    http.verify();
  });

  it('reports the observed two-trial change without claiming SAM', async () => {
    const {component,fixture,http,rootEl}=await initialize(true);
    const session={...root(true),baseline:video('baseline',1.416),
      followup:video('followup',1.798),proposal,
      humanReview:{bobConfirmed:true,reviewedAtUtc:'2026-10-27T20:02:00Z'},
      step:'READY_TO_COMPARE'};
    component.session.set(session);
    fixture.detectChanges();
    const pending=component.compare();
    http.expectOne(studyApi+'/'+session.id+'/compare')
      .flush({...session,step:'COMPARED_PRELIMINARY',comparison:{
        status:'PRELIMINARY_LOCAL_COMPARISON_NOT_AI_SAM_OR_CERTIFIED',
        source:'LOCAL_PROTOCOL_NOT_META_AI_NOT_SAM',
        baselinePeriodSeconds:1.416,followUpPeriodSeconds:1.798,
        predictedIncreasePercent:26.49,observedIncreasePercent:26.98,
        changeGapPercentagePoints:.49,comparedAtUtc:'2026-10-27T20:04:00Z',
        predictedBaselinePeriodSeconds:1.425,
        predictedFollowUpPeriodSeconds:1.803,
        baselinePeriodDiscrepancyPercent:.63,
        followUpPeriodDiscrepancyPercent:.28,
        limitationsEs:'Sin SAM ni revisión independiente.',
        limitationsEn:'No SAM or independent review.'
      }});
    await pending;
    fixture.detectChanges();
    expect(rootEl.textContent).toContain('26.98');
    expect(rootEl.textContent).toContain('Comparación independiente de períodos por prueba');
    expect(rootEl.textContent).toContain('0.63%');
    expect(rootEl.textContent).toContain('0.28%');
    expect(rootEl.textContent).toContain('Sin SAM ni revisión independiente.');
    http.verify();
  });
});