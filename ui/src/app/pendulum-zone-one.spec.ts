import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PendulumZoneOne } from './pendulum-zone-one';

describe('Clean wide Pendulum Zone 1 for the hackathon jury', () => {
  const theoryUrl='http://127.0.0.1:5391/api/science/pendulum/mathematical-model';
  const studyUrl='http://127.0.0.1:5391/api/science/director/pendulum/sessions';

  async function verifyHttp(http:HttpTestingController):Promise<void>{
    await Promise.resolve();
    const pending=http.match(req=>req.method==='GET' &&
      (req.url.includes('/kinematics/') || req.url.includes('/sam/')));
    for(const request of pending)
      request.flush({error:'Synthetic unavailable result'},
        {status:404,statusText:'Not Found'});
    await Promise.resolve();
    http.verify();
  }

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

  async function init(chooseUpload=true){
    const fixture=TestBed.createComponent(PendulumZoneOne);
    fixture.detectChanges();
    const http=TestBed.inject(HttpTestingController);
    const req=http.expectOne(theoryUrl);
    req.flush(theory());
    await Promise.resolve();
    fixture.detectChanges();
    if(chooseUpload){fixture.componentInstance.newFocusedStudy();fixture.detectChanges();}
    return {fixture,http,component:fixture.componentInstance,
      root:fixture.nativeElement as HTMLElement};
  }

  it('shows only the requested configuration and three video view placeholders',async()=>{
    const {root,component,http}=await init(false);
    expect(root.textContent).toContain('Configura el péndulo');
    expect(root.textContent).toContain('Video y tres vistas sincronizadas');
    expect(root.querySelectorAll('.parameter-grid input').length).toBe(3);
    expect(root.querySelector('#focus-length')).not.toBeNull();
    expect(root.querySelector('#focus-angle')).not.toBeNull();
    expect(root.querySelector('#focus-mass')).not.toBeNull();
    expect(root.querySelectorAll('.placeholder-panel').length).toBe(3);
    expect(root.querySelectorAll('.video-toolbar .upload-new-video input[type=file]').length).toBe(0);
    expect(root.querySelector('.demo-video')?.textContent).toContain('Video de prueba');
    expect(root.textContent).toContain('Elige Video de prueba o Nueva prueba para empezar.');
    expect(root.querySelector('button.primary-button')).toBeNull();
    expect(root.querySelector('.model-charts')).toBeNull();
    expect(root.querySelector('.model-chart')).toBeNull();
    expect(root.querySelector('.technical-protocol')).toBeNull();
    expect(component.includeSecondTrial()).toBeFalse();
    expect(root.querySelector('app-pendulum-director')).toBeNull();
    await verifyHttp(http);
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
    expect(root.querySelectorAll('.video-toolbar .upload-new-video input[type=file]').length).toBe(1);
    expect(root.textContent).toContain('Sube tu primera grabación');
    expect((root.querySelector('#focus-length') as HTMLInputElement).disabled).toBeTrue();
    expect(root.querySelectorAll('.placeholder-panel').length).toBe(3);
    await verifyHttp(http);
  });

  it('wires the immediately visible file input to the upload action',async()=>{
    const {root,component,http}=await init();
    const uploader=root.querySelector('.video-toolbar .upload-new-video input[type=file]') as HTMLInputElement;
    expect(uploader).not.toBeNull();
    const trigger=spyOn(component,'uploadNewTrial').and.resolveTo();
    uploader.dispatchEvent(new Event('change',{bubbles:true}));
    expect(trigger).toHaveBeenCalled();
    expect(trigger.calls.mostRecent().args[0]).toBeTruthy();
    await verifyHttp(http);
  });

  it('uploads directly: freezes the current model before transferring the first video',async()=>{
    const {root,component,fixture,http}=await init();
    const actual=new File([new Uint8Array(15000)],'50CM_1.MOV',
      {type:'video/quicktime'});
    const input=root.querySelector('.video-toolbar .upload-new-video input[type=file]') as HTMLInputElement;
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
    expect(root.querySelectorAll('.video-toolbar .upload-new-video input[type=file]').length).toBe(0);
// The offline SAM JSON inspector has its own separate optional file input.
    expect(root.textContent).toContain('He revisado el video');
    http.expectNone(req=>req.url.includes('/api/meta/'));
    await verifyHttp(http);
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
    expect(root.querySelector('.video-toolbar .upload-new-video input[type=file]')).not.toBeNull();
    http.expectNone(studyUrl);
    http.expectNone(req=>req.url.includes('/video/baseline'));
    await verifyHttp(http);
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
    expect(root.querySelector('.video-toolbar .upload-new-video input[type=file]')).not.toBeNull();
    http.expectNone(r=>r.url.includes('/video/baseline'));
    await verifyHttp(http);
  });

  it('shows the video selector again immediately after New study',async()=>{
    const {root,component,fixture,http}=await init();
    component.session.set({...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'});
    fixture.detectChanges();
    component.choosingUpload.set(false);fixture.detectChanges();
    expect(root.querySelector('.upload-new-video')).toBeNull();
    component.onPreviewState('ready');component.newFocusedStudy();
    fixture.detectChanges();
    expect(component.session()).toBeNull();
    expect(root.querySelector('.video-toolbar .upload-new-video input[type=file]')).not.toBeNull();
    expect(root.textContent).toContain('Subir video');
    expect(root.querySelectorAll('.placeholder-panel').length).toBe(3);
    expect((root.querySelector('#focus-length') as HTMLInputElement).disabled).toBeFalse();
    expect(localStorage.getItem('prooflab-director-current')).toBeNull();
    await verifyHttp(http);
  });

  it('quotes cache automatically, locks playback until Process, and never POSTs for reuse',async()=>{
    const {root,component,fixture,http}=await init();
    component.session.set({...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'});
    fixture.detectChanges();await Promise.resolve();
    const controls=root.querySelector('app-sam-video-replay')!;
    const samUrl=studyUrl+'/'+session.id+'/sam/baseline';
    http.expectOne(samUrl+'/status?mode=api-meta-cache').flush({
      status:'CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW',
      sourceSha256:video.sha256,cacheKey:'c'.repeat(64),
      sourceFrames:1828,estimatedCostUsd:.3656,canRunLive:false,
      provenance:'META_SAM_3_1',mode:'api-meta-cache',
      cacheHit:true,storage:'SQLite',analysisId:'integration-cache-hit'
    });
    await Promise.resolve();
    http.expectOne(samUrl+'/preflight').flush({
      sourceSha256:video.sha256,sourceFrames:1828,estimatedCostUsd:.3656,
      durationSeconds:30.5,codec:'hevc',supportsSingleCall:true
    });
    await fixture.whenStable();fixture.detectChanges();
    const play=root.querySelector('.play-main') as HTMLButtonElement;
    expect(play.disabled).toBeTrue();
    expect(controls.textContent).toContain('US$ 0.00');
    expect(controls.textContent).toContain('US$ 0.3656');
    http.expectNone(samUrl+'/evidence');
    component.onPreviewState('ready');fixture.detectChanges();
    (controls.querySelector('.sam-submit') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(component.resultState()).toBe('loading');
    expect(root.querySelector('.viewer-loading')).not.toBeNull();
    expect((controls.querySelector('.sam-submit') as HTMLButtonElement).disabled).toBeTrue();
    const evidence=http.expectOne(samUrl+'/evidence');
    expect(evidence.request.method).toBe('GET');
    evidence.flush({
      state:{status:'CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW',
        sourceSha256:video.sha256,analysisId:'integration-cache-hit'},
      receipt:{model:'sam-3.1',concept:'metal pendulum bob',
        originalVideoSha256:video.sha256,analysisId:'integration-cache-hit',
        providerEvidenceStatus:'COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW'},
      rawResponseJson:JSON.stringify({model:'sam-3.1',status:'completed',
        output:[{content:[{type:'output_text',text:''}]}]})
    });
    http.expectOne(studyUrl+'/'+session.id+'/kinematics/baseline')
      .flush({}, {status:404,statusText:'Not Found'});
    await fixture.whenStable();
    for(let i=0;i<30 && !component.playbackReady();i++)
      await new Promise<void>(resolve=>setTimeout(resolve,10));
    fixture.detectChanges();
    expect(play.disabled).toBeFalse();
    expect(component.resultState()).toBe('ready');
    expect(component.connection.busy()).toBeFalse();
    expect(component.playbackReady()).toBeTrue();
    http.expectNone(req=>req.method==='POST' || req.url.endsWith('/readiness'));
    await verifyHttp(http);fixture.destroy();
  });

  it('fills the four-perspective viewer with one video and a default combined primary',async()=>{
    const {root,component,fixture,http}=await init();
    component.session.set({...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'});
    fixture.detectChanges();
    const player=root.querySelector('app-pendulum-master-player');
    expect(player).not.toBeNull();
    const manual=root.querySelector('app-sam-video-replay');
    expect(manual).not.toBeNull();
    expect(manual?.querySelector('input[type=password]')).toBeNull();
    expect((manual!.compareDocumentPosition(player!) &
      Node.DOCUMENT_POSITION_FOLLOWING)).toBeTruthy(); // manual controls above video
    expect(root.textContent).toContain('Abrir configuración');
    expect(root.textContent).toContain('Cuatro perspectivas sincronizadas');
    expect(root.querySelectorAll('.view-panel').length).toBe(4);
    expect(root.querySelector('[data-view="combined"]')?.classList.contains('is-primary')).toBeTrue();
    expect(root.querySelectorAll('video').length).toBe(1);
    expect(root.querySelectorAll('canvas').length).toBe(3);
    expect(root.querySelectorAll('.placeholder-panel').length).toBe(0);
    expect(player?.textContent).toContain('Rastro fantasma');
    expect(root.textContent).toContain('He revisado el video');
    expect(root.querySelector('.viewer-loading')?.textContent).toContain('Cargando video');
    expect((root.querySelector('.play-main') as HTMLButtonElement).disabled).toBeTrue();
    expect(root.querySelectorAll('.model-chart').length).toBe(0);
    await verifyHttp(http);
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
    await verifyHttp(http);
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
    expect(source.getAttribute('src')).toContain('/original/followup');
    expect(root.querySelectorAll('canvas').length).toBe(3);
    expect(root.querySelectorAll('.model-chart').length).toBe(0);
    await verifyHttp(http);
  });
  it('keeps the current trial selected when a new video is cancelled or invalid',async()=>{
    const {component,fixture,http}=await init();
    const original={...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'};
    component.session.set(original);fixture.detectChanges();component.onPreviewState('ready');await verifyHttp(http);
    await component.uploadNewTrial({target:{files:[],value:''}} as unknown as Event);
    expect(component.session()).toBe(original);
    const input={files:[new File([new Uint8Array(15000)],'invalid.exe')],value:'chosen'};
    await component.uploadNewTrial({target:input} as unknown as Event);
    expect(component.session()).toBe(original);expect(input.value).toBe('');
    expect(component.error()).toContain('Video inválido');
    http.expectNone(()=>true);http.verify();fixture.destroy();
  });

  it('uploads another video into a NEW trial without replacing the previous recording',async()=>{
    const {component,fixture,root,http}=await init();
    const original={...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'};
    component.session.set(original);fixture.detectChanges();component.onPreviewState('ready');await verifyHttp(http);
    component.choosingUpload.set(false);fixture.detectChanges();
    expect(root.querySelectorAll('.video-toolbar .new-trial').length).toBe(1);
    expect(root.querySelectorAll('.video-toolbar input[type=file]').length).toBe(0);
    const file=new File([new Uint8Array(15000)],'new-trial.mp4',{type:'video/mp4'});
    const input={files:[file],value:'chosen'};
    const nextId='f'.repeat(32);
    const transfer=component.uploadNewTrial({target:input} as unknown as Event);
    http.expectOne(theoryUrl).flush(theory());
    await Promise.resolve();await Promise.resolve();
    http.expectOne(studyUrl).flush({...session,id:nextId});
    await Promise.resolve();await Promise.resolve();
    const upload=http.expectOne(r=>r.url.includes('/'+nextId+'/video/baseline'));
    expect(upload.request.body).toBe(file);
    upload.flush({...session,id:nextId,baseline:{...video,originalFilename:'new-trial.mp4'}});
    await transfer;fixture.detectChanges();
    expect(component.session()?.id).toBe(nextId);
    expect(original.id).toBe(session.id);expect(original.baseline).toBe(video);
    expect(input.value).toBe('');
    http.expectNone(r=>r.method==='DELETE' || (r.method==='POST' && r.url.includes('/'+session.id+'/')));
    await verifyHttp(http);fixture.destroy();
  });

  it('enters empty despite a remembered study and loads the configured demo only on click',async()=>{
    localStorage.setItem('prooflab-director-current',session.id);
    const {component,fixture,root,http}=await init(false);
    http.expectNone(studyUrl+'/'+session.id);
    http.expectNone('demo-video.json');
    expect(component.session()).toBeNull();
    expect(root.querySelector('video')).toBeNull();
    expect((root.querySelector('.sam-submit') as HTMLButtonElement).disabled).toBeTrue();
    const loading=component.loadDemo();fixture.detectChanges();
    expect(component.demoLoading()).toBeTrue();
    expect(root.querySelector('.focus-video app-lab-activity')).not.toBeNull();
    expect(root.querySelector('.sam-empty app-lab-activity')).not.toBeNull();
    await component.loadDemo(); // second click cannot start another load
    http.expectOne('demo-video.json').flush({studyId:session.id});
    await Promise.resolve();
    http.expectOne(studyUrl+'/'+session.id).flush({...session,baseline:video});
    await loading;
    http.expectOne(theoryUrl).flush(theory());
    fixture.detectChanges();await Promise.resolve();
    // Metadata/quote runs while the browser preview is still loading.
    expect(component.previewState()).toBe('loading');
    expect(component.session()?.baseline).toEqual(video);
    expect(root.querySelector('.viewer-loading')).not.toBeNull();
    await verifyHttp(http);fixture.destroy();
  });

  it('recovers from demo configuration failure and allows choosing an upload',async()=>{
    const {component,fixture,root,http}=await init(false);
    const loading=component.loadDemo();
    http.expectOne('demo-video.json').flush({}, {status:404,statusText:'Not Found'});
    await loading;fixture.detectChanges();
    expect(component.videoActionsBusy()).toBeFalse();
    expect(component.error()).toContain('No se pudo cargar');
    component.newFocusedStudy();fixture.detectChanges();
    expect(root.querySelector('.video-toolbar .upload-new-video input[type=file]')).not.toBeNull();
    expect(component.session()).toBeNull();
    http.verify();fixture.destroy();
  });

  it('does not restart loading when selecting the already active trial',async()=>{
    const {component,http,fixture}=await init();
    component.previewState.set('ready');
    component.selectTrial('baseline');
    expect(component.previewState()).toBe('ready');
    expect(component.videoActionsBusy()).toBeFalse();
    http.verify();fixture.destroy();
  });

  it('loads the original even when no reduced browser proxy exists',async()=>{
    const {component,fixture,root,http}=await init();
    component.session.set({...session,baseline:{...video,previewAvailable:false}});
    fixture.detectChanges();
    expect(root.querySelector('video')?.getAttribute('src')).toBe(studyUrl+'/'+session.id+'/original/baseline');
    expect(root.querySelectorAll('video').length).toBe(1);
    expect(root.textContent).not.toContain('12 FPS');
    await verifyHttp(http);fixture.destroy();
  });

  it('replaces source actions with Upload and Cancel in the same card and restores on cancel',async()=>{
    const {component,fixture,root,http}=await init(false);
    (root.querySelector('.new-trial') as HTMLButtonElement).click();fixture.detectChanges();
    expect(root.querySelector('.video-toolbar .upload-new-video input[type=file]')).not.toBeNull();
    expect(root.querySelector('.cancel-trial')).not.toBeNull();
    expect(root.querySelector('.demo-video')).toBeNull();
    expect(root.querySelector('.new-trial')).toBeNull();
    expect(root.querySelector('.focus-video input[type=file]')).toBeNull();
    (root.querySelector('.cancel-trial') as HTMLButtonElement).click();fixture.detectChanges();
    expect(root.querySelector('.upload-new-video')).toBeNull();
    expect(root.querySelector('.demo-video')).not.toBeNull();
    expect(root.querySelector('.new-trial')).not.toBeNull();
    expect(component.session()).toBeNull();
    http.expectNone(r=>r.method==='POST');await verifyHttp(http);fixture.destroy();
  });

  it('cancels choosing another video without losing the existing session or result',async()=>{
    const {component,fixture,root,http}=await init(false);
    const original={...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'};
    component.session.set(original);fixture.detectChanges();component.onPreviewState('ready');await verifyHttp(http);
    component.playbackReady.set(true);component.resultState.set('ready');
    component.beginNewTrial();fixture.detectChanges();
    expect(root.querySelector('.new-trial')).toBeNull();
    component.cancelNewTrial();fixture.detectChanges();
    expect(component.session()).toBe(original);
    expect(component.playbackReady()).toBeTrue();
    expect(component.resultState()).toBe('ready');
    expect(root.querySelector('.demo-video')).not.toBeNull();
    http.expectNone(()=>true);http.verify();fixture.destroy();
  });
});
