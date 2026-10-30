import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { SamVideoReplay } from './sam-video-replay';
import { PendulumMasterPlayer } from './pendulum-master-player';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PendulumZoneOne } from './pendulum-zone-one';

describe('Clean wide Pendulum Zone 1 for the hackathon jury', () => {
  it('restores both videos and the frozen prediction by ID using GET only',async()=>{
    const fixture=TestBed.createComponent(PendulumZoneOne),zone=fixture.componentInstance;
    const http=TestBed.inject(HttpTestingController),id='d'.repeat(32);
    const restored={...session,baseline:{...video,sha256:'a'.repeat(64)},followup:{...video,role:'followup',sha256:'b'.repeat(64)},
      musePrediction:{studyId:id,observationId:'o',responseId:'r',frozenAtUtc:'2026-10-30T00:00:00Z',payloadJson:'{}',sha256:'c'.repeat(64)}};
    const pending=zone.resumeResearch(id);
    const request=http.expectOne(studyUrl+'/'+id+'/recovery');expect(request.request.method).toBe('GET');
    request.flush({session:restored,operations:[],unavailable:[],selections:['baseline','followup'].map((videoRole,i)=>({
      studyId:id,videoRole,sourceSha256:(i?'b':'a').repeat(64),analysisId:(i?'f':'e').repeat(32),adaptiveBatchId:(i?'f':'e').repeat(32)}))});
    await pending;
    http.match(theoryUrl).forEach(r=>r.flush(theory(true)));
    expect(zone.session()?.id).toBe(id);expect(zone.activeTrial()).toBe('followup');
    expect(zone.processedAnalysis('followup')).toBe('f'.repeat(32));expect(zone.frozenForStudy()?.sha256).toBe('c'.repeat(64));
    zone.onPreviewState('ready');zone.onProcessingFinished(true);zone.selectTrial('baseline');
    expect(zone.processedAnalysis('baseline')).toBe('e'.repeat(32));
    expect(localStorage.getItem('prooflab-director-current')).toBe(id);http.expectNone(r=>r.method==='POST');http.verify();fixture.destroy();
  });
  it('keeps a recovered in-flight operation pending without submitting another request',async()=>{
    const fixture=TestBed.createComponent(PendulumZoneOne),zone=fixture.componentInstance;
    const http=TestBed.inject(HttpTestingController),id='d'.repeat(32);
    const pending=zone.resumeResearch(id);
    http.expectOne(studyUrl+'/'+id+'/recovery').flush({session:{...session,baseline:video},selections:[],unavailable:[],operations:[
      {id:'f'.repeat(32),role:'baseline',stage:'sam/manual-one-shot',state:'RUNNING',startedAtUtc:new Date().toISOString(),finishedAtUtc:null}]});
    await pending;http.match(theoryUrl).forEach(r=>r.flush(theory()));
    expect(zone.recovering()).toBeTrue();expect(zone.recoveringOperation()).toContain('sam');
    http.expectNone(r=>r.method==='POST');fixture.destroy();http.verify();
  });

  const theoryUrl='http://127.0.0.1:5391/api/science/pendulum/mathematical-model';
  const studyUrl='http://127.0.0.1:5391/api/science/director/pendulum/sessions';

  async function verifyHttp(http:HttpTestingController):Promise<void>{
    await Promise.resolve();
    const pending=http.match(req=>req.method==='GET' &&
      (req.url.includes('/kinematics/') || req.url.includes('/sam/') || req.url.includes('/grounding/') || req.url.endsWith('/readiness')));
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

  async function init(chooseUpload=true,generate=true){
    const fixture=TestBed.createComponent(PendulumZoneOne);
    fixture.detectChanges();
    const http=TestBed.inject(HttpTestingController);
    const req=http.expectOne(theoryUrl);
    req.flush(theory());
    await Promise.resolve();
    fixture.detectChanges();
    if(generate){
      const creating=fixture.componentInstance.generateRecoveryCode();
      http.expectOne(theoryUrl).flush(theory());await Promise.resolve();await Promise.resolve();
      http.expectOne(studyUrl).flush(session);await creating;fixture.detectChanges();
    }
    if(chooseUpload && generate){fixture.componentInstance.beginNewTrial();fixture.detectChanges();}
    return {fixture,http,component:fixture.componentInstance,
      root:fixture.nativeElement as HTMLElement};
  }

  it('illustrates the ideal maximum at the vertical and zero speed at both extremes without touching SAM',async()=>{
    const {fixture,root,component,http}=await init(false);
    const figure=root.querySelector('.theory-preview') as HTMLDetailsElement;
    const slider=root.querySelector('#theory-position-slider') as HTMLInputElement;
    expect(figure?.open).toBeFalse();
    expect(root.textContent).toContain('Predicción teórica del péndulo');
    expect(component.theoreticalPreview()?.angleDegrees).toBe(0);
    expect(component.theoreticalPreview()?.maximumAngularSpeed).toBeCloseTo(1.156,2);
    expect(component.theoreticalPreview()?.angularSpeed).toBeCloseTo(
      component.theoreticalPreview()!.maximumAngularSpeed,8);
    expect(component.theoreticalPreview()?.periodSeconds).toBeCloseTo(1.425,3);
    expect(component.theoreticalPreview()?.maximumTangentialSpeed).toBeCloseTo(.578,2);
    expect(root.textContent).toContain('Rapidez máxima en θ = 0°');

    slider.value='100';slider.dispatchEvent(new Event('input'));fixture.detectChanges();
    expect(component.theoreticalPreview()?.angleDegrees).toBe(15);
    expect(component.theoreticalPreview()?.angularSpeed).toBeCloseTo(0,8);
    expect(root.textContent).toContain('Rapidez cero en el extremo');
    slider.value='-100';slider.dispatchEvent(new Event('input'));fixture.detectChanges();
    expect(component.theoreticalPreview()?.angleDegrees).toBe(-15);
    expect(component.theoreticalPreview()?.angularSpeed).toBeCloseTo(0,8);
    expect(component.session()?.id).toBe(session.id);
    expect(component.theoryPositionPercent()).toBe(-100);
    http.expectNone(theoryUrl); // A position-only interaction has no requests.
    await verifyHttp(http);
  });

  it('recomputes the ideal preview for entered parameters without reusing stale theory',async()=>{
    const {fixture,root,component,http}=await init(false);
    const at50=component.theoreticalPreview()!.maximumAngularSpeed;
    component.baselineLength.set(.8);
    const pending=component.refreshTheory();
    fixture.detectChanges();
    expect(component.theoreticalPreview()).toBeNull();
    const request=http.expectOne(theoryUrl);
    expect(request.request.body.lengthMeters).toBe(.8);
    request.flush({...theory(),baseline:{
      ...theory().baseline,lengthMeters:.8,nonlinearPeriodSeconds:1.802
    }});
    await pending;fixture.detectChanges();
    expect(component.theoreticalPreview()!.maximumAngularSpeed).toBeLessThan(at50);
    expect(component.theoreticalPreview()!.periodSeconds).toBeCloseTo(1.802,3);
    expect(root.textContent).toContain('Predicción teórica del péndulo');
    await verifyHttp(http);
  });

  it('starts the guided layout with one clean source placeholder and direct upload',async()=>{
    const {root,component,http}=await init(false);
    expect(root.querySelectorAll('.journey-step').length).toBe(5);
    expect(root.querySelectorAll('.parameter-grid input').length).toBe(3);
    expect(root.querySelector('#focus-length')).not.toBeNull();
    expect(root.querySelector('#focus-angle')).not.toBeNull();
    expect(root.querySelector('#focus-mass')).not.toBeNull();
    expect(root.querySelectorAll('.empty-source').length).toBe(1);
    expect(root.querySelectorAll('.video-toolbar .upload-new-video input[type=file]').length).toBe(1);
    expect(root.querySelector('.demo-video')?.textContent).toContain('Usar video de ejemplo');
    expect(root.querySelector('#recovery-drawer')?.hasAttribute('hidden')).toBeTrue();
    expect(root.querySelector('.model-charts')).toBeNull();
    expect(component.includeSecondTrial()).toBeFalse();
    expect(component.flowStep()).toBe(1);
    expect(component.presentation()).toBe('source');
    expect(root.querySelector('app-pendulum-director')).toBeNull();
    await verifyHttp(http);
  });

  it('requires explicit generation before enabling the lab and prevents duplicate creation',async()=>{
    localStorage.setItem('prooflab-director-current',session.id);
    const {root,component,fixture,http}=await init(false,false);
    expect(component.session()).toBeNull();expect(root.querySelector('.focus-config')).toBeNull();
    expect(root.querySelector('.generate-recovery')).not.toBeNull();expect(root.querySelector('#lab-recovery-code')).not.toBeNull();
    await component.loadDemo();http.expectNone('demo-video.json');
    const creating=component.generateRecoveryCode();fixture.detectChanges();
    expect(root.querySelector('.recovery-entry [role=progressbar]')).not.toBeNull();
    await component.generateRecoveryCode();
    http.expectOne(theoryUrl).flush(theory());await Promise.resolve();await Promise.resolve();
    const request=http.expectOne(studyUrl);expect(request.request.method).toBe('POST');request.flush(session);await creating;
    fixture.detectChanges();expect(component.session()?.id).toBe(session.id);
    expect(root.querySelector('.generate-recovery')).toBeNull();expect((root.querySelector('#active-recovery-code') as HTMLInputElement)?.value).toBe(session.id);
    expect((root.querySelector('#focus-length') as HTMLInputElement).disabled).toBeFalse();
    expect(root.querySelector('.demo-video')).not.toBeNull();await verifyHttp(http);
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

  it('uploads the first recording under the generated recovery code',async()=>{
    const {component,fixture,root,http}=await init();
    const file=new File([new Uint8Array(15000)],'50CM_1.MOV',{type:'video/quicktime'});
    const control={files:[file],value:'chosen'};
    const pending=component.uploadFocused({target:control} as unknown as Event,'baseline');
    http.expectNone(studyUrl);http.expectNone(theoryUrl);
    const transfer=http.expectOne(r=>r.url.includes('/'+session.id+'/video/baseline'));
    expect(transfer.request.body).toBe(file);transfer.flush({...session,baseline:video});await pending;fixture.detectChanges();await Promise.resolve();await Promise.resolve();fixture.detectChanges();
    expect(component.session()?.id).toBe(session.id);expect(component.session()?.baseline?.sha256).toBe(video.sha256);
    expect((root.querySelector('#focus-length') as HTMLInputElement).disabled).toBeTrue();
    expect(control.value).toBe('');await verifyHttp(http);
  });

  it('rejects unsupported videos BEFORE creating any research record',async()=>{
    const {root,component,fixture,http}=await init();
    const invalid=new File([new Uint8Array(14000)],'pendulo.exe',
      {type:'application/octet-stream'});
    const control={files:[invalid],value:'file-selected'};
    await component.uploadFocused({target:control} as unknown as Event,'baseline');
    fixture.detectChanges();
    expect(control.value).toBe('');
    expect(component.session()?.id).toBe(session.id);
    expect(component.error()).toContain('Archivo incompatible');
    expect(root.querySelector('.video-toolbar .upload-new-video input[type=file]')).not.toBeNull();
    http.expectNone(studyUrl);
    http.expectNone(req=>req.url.includes('/video/baseline'));
    await verifyHttp(http);
  });

  it('keeps generation available when the server cannot create a code',async()=>{
    const {component,fixture,root,http}=await init(false,false);
    const pending=component.generateRecoveryCode();http.expectOne(theoryUrl).flush(theory());
    await Promise.resolve();await Promise.resolve();http.expectOne(studyUrl).flush({}, {status:503,statusText:'Unavailable'});await pending;
    fixture.detectChanges();expect(component.session()).toBeNull();expect(component.generatingCode()).toBeFalse();
    expect(root.querySelector('.recovery-error')?.textContent).toContain('No se pudo crear');
    expect(root.querySelector('.focus-config')).toBeNull();expect((root.querySelector('.generate-recovery') as HTMLButtonElement).disabled).toBeFalse();http.verify();
  });

  it('clears the active choice without deleting stored studies when starting over',async()=>{
    const {component,fixture,root,http}=await init();component.newFocusedStudy();fixture.detectChanges();
    expect(component.session()).toBeNull();expect(root.querySelector('.generate-recovery')).not.toBeNull();
    expect(root.querySelector('app-sam-video-replay')).toBeNull();expect(localStorage.getItem('prooflab-director-current')).toBeNull();http.verify();
  });

  it('quotes cache while preserving original playback and never POSTs for reuse',async()=>{
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
    http.expectOne('http://127.0.0.1:5391/api/meta/readiness').flush({manualTokenCallsPermitted:false,remainingRequests:0});
    http.expectOne(studyUrl+'/'+session.id+'/assembly/baseline/grounding/preflight?mode=api-meta-cache')
      .flush({}, {status:503,statusText:'Unavailable'});
    http.expectNone(req=>req.method==='POST' && req.url.includes('/assembly/'));
    expect(root.querySelector('app-assembly-review')).toBeNull();
    await fixture.whenStable();fixture.detectChanges();
    const play=root.querySelector('.play-main') as HTMLButtonElement;
    expect(play.disabled).toBeFalse();
    expect(controls.textContent).toContain('US$ 0.00');
    expect(controls.textContent).toContain('US$ 0.3656');
    http.expectNone(samUrl+'/evidence');
    component.onPreviewState('ready');component.setJourneyStep(2);fixture.detectChanges();
    (controls.querySelector('.sam-submit') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(component.resultState()).toBe('loading');
    expect(root.querySelector('app-sam-video-replay [aria-busy="true"]')).not.toBeNull();
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

  it('keeps one original decoder and hides analysis behind guided presentation',async()=>{
    const {root,component,fixture,http}=await init();
    component.session.set({...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'});
    fixture.detectChanges();
    const player=root.querySelector('app-pendulum-master-player');
    expect(player).not.toBeNull();
    const manual=root.querySelector('app-sam-video-replay');
    expect(manual).not.toBeNull();
    expect(manual?.querySelector('input[type=password]')).toBeNull();
    expect((manual!.compareDocumentPosition(player!) &
      Node.DOCUMENT_POSITION_PRECEDING)).toBeTruthy(); // original video precedes its cost card
    expect(root.textContent).toContain('Abrir configuración');
    expect(component.presentation()).toBe('source');
    expect(root.querySelectorAll('.view-panel').length).toBe(4);
    expect(root.querySelector('[data-view="combined"]')?.classList.contains('is-primary')).toBeTrue();
    expect(root.querySelectorAll('video').length).toBe(1);
    expect(root.querySelectorAll('canvas').length).toBe(3);
    expect(root.querySelectorAll('.placeholder-panel').length).toBe(0);
    expect(player?.textContent).toContain('Rastro fantasma');
    expect(root.textContent).toContain('Revisé el seguimiento');
    expect(root.querySelector('.viewer-loading')?.textContent).toContain('Cargando video');
    expect((root.querySelector('.play-main') as HTMLButtonElement).disabled).toBeFalse();
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
    expect(root.querySelectorAll('.journey-step').length).toBe(5);
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
    expect(root.querySelectorAll('.video-toolbar .new-trial').length).toBe(0);
    expect(root.querySelectorAll('.video-toolbar input[type=file]').length).toBe(1);
    const file=new File([new Uint8Array(15000)],'new-trial.mp4',{type:'video/mp4'});
    const input={files:[file],value:'chosen'};
    const nextId='f'.repeat(32);
    const transfer=component.uploadNewTrial({target:input} as unknown as Event);
    http.expectOne(theoryUrl).flush(theory());
    await Promise.resolve();await Promise.resolve();
    http.expectOne(studyUrl).flush({...session,id:nextId});
    for(let i=0;i<8;i++)await Promise.resolve();
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

  it('imports demo bytes into the generated session without adopting the demo ID',async()=>{
    const {component,fixture,root,http}=await init(false);
    const loading=component.loadDemo();fixture.detectChanges();expect(component.demoLoading()).toBeTrue();
    await component.loadDemo();http.expectOne('demo-video.json').flush({studyId:'e'.repeat(32)});await Promise.resolve();await Promise.resolve();
    http.expectOne(studyUrl+'/'+'e'.repeat(32)).flush({...session,id:'e'.repeat(32),baseline:video});await Promise.resolve();await Promise.resolve();
    http.expectOne(studyUrl+'/'+'e'.repeat(32)+'/original/baseline').flush(new Blob([new Uint8Array(15000)],{type:'video/quicktime'}));await Promise.resolve();await Promise.resolve();
    http.expectOne(r=>r.url.includes('/'+session.id+'/video/baseline')).flush({...session,baseline:video});await loading;fixture.detectChanges();
    expect(component.session()?.id).toBe(session.id);expect(component.previewState()).toBe('loading');
    expect(root.querySelector('.viewer-loading')).not.toBeNull();http.expectNone(studyUrl);await verifyHttp(http);fixture.destroy();
  });

  it('keeps the generated code when the demo cannot load',async()=>{
    const {component,fixture,root,http}=await init(false);const loading=component.loadDemo();
    http.expectOne('demo-video.json').flush({}, {status:404,statusText:'Not Found'});await loading;fixture.detectChanges();
    expect(component.session()?.id).toBe(session.id);expect(component.videoActionsBusy()).toBeFalse();
    component.beginNewTrial();fixture.detectChanges();expect(root.querySelector('.upload-new-video')).not.toBeNull();
    expect(component.error()).toBe('');http.verify();fixture.destroy();
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

  it('keeps direct upload available and preserves the study when the file picker is cancelled',async()=>{
    const {component,fixture,root,http}=await init(false);
    expect(root.querySelector('.video-toolbar .upload-new-video input[type=file]')).not.toBeNull();
    expect(root.querySelector('.demo-video')).not.toBeNull();
    expect(root.querySelector('.focus-video input[type=file]')).toBeNull();
    await component.uploadNewTrial({target:{files:[],value:''}} as unknown as Event);
    fixture.detectChanges();
    expect(component.session()?.id).toBe(session.id);
    expect(root.querySelector('.upload-new-video')).not.toBeNull();
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
  for(const failure of ['HTTP_ERROR','NO_INFERENCE','AMBIGUOUS','NOT_VISIBLE']) {
    it('closes the video overlay and preserves the error when Spark returns '+failure,async()=>{
      const {root,component,fixture,http}=await init();
      component.session.set({...session,baseline:video,step:'AWAITING_HUMAN_BOB_REVIEW'});
      fixture.detectChanges();await Promise.resolve();
      const samUrl=studyUrl+'/'+session.id+'/sam/baseline';
      const groundUrl=studyUrl+'/'+session.id+'/assembly/baseline/grounding';
      const source={sourceSha256:video.sha256,sourceFrames:1828,estimatedCostUsd:.3656,
        durationSeconds:30.5,codec:'hevc',supportsSingleCall:true};
      http.expectOne(samUrl+'/status?mode=api-meta-cache').flush({
        status:'NOT_ANALYZED_WITH_SAM',sourceSha256:video.sha256,cacheKey:'c'.repeat(64),
        cacheHit:false,analysisId:null,mode:'api-meta-cache',canRunLive:false});
      http.expectOne(samUrl+'/preflight').flush(source);
      http.expectOne('http://127.0.0.1:5391/api/meta/readiness').flush({
        apiKeyConfigured:false,liveCallsPermitted:false,manualTokenCallsPermitted:true,
        paidRequestsSwitchEnabled:true,creditsConfirmedByOwner:true,ownerApprovedPaidRequests:true,
        usedRequests:0,remainingRequests:0,manualRequestsUnlimited:true});
      const ground={cacheKey:'e'.repeat(64),videoSha256:video.sha256,
        maximumEstimatedCostUsd:.25,mode:'api-meta-cache',freshRequired:false,
        protocol:'ASSEMBLY_FULL_VIDEO_GROUNDING_V2',cacheHit:false,priorAttempt:false,canRun:true,
        status:'NEEDS_LOCALIZATION',scene:null};
      http.expectOne(groundUrl+'/preflight?mode=api-meta-cache').flush(ground);
      await fixture.whenStable();
      const replay=fixture.debugElement.query(By.directive(SamVideoReplay)).componentInstance as SamVideoReplay;
      const player=fixture.debugElement.query(By.directive(PendulumMasterPlayer)).componentInstance as PendulumMasterPlayer;
      // Browser preview readiness is independent of the network requests under test.
      player.previewLoading.set(false);component.onPreviewState('ready');
      const field=document.createElement('input');field.type='password';
      component.connection.register(field);field.value='LLM_SYNTHETIC_INTEGRATION_TEST_NEVER_SENT_123456';
      component.connection.confirmToken();fixture.detectChanges();
      expect(root.querySelector('.viewer-loading')).toBeNull();expect(replay.canProcess()).toBeTrue();
      const failureEvents=spyOn(replay.processingFailed,'emit').and.callThrough();
      const pending=replay.process();fixture.detectChanges();
      expect(component.resultState()).toBe('loading');
      expect(root.querySelector('app-sam-video-replay [aria-busy="true"]')).not.toBeNull();
      const request=http.expectOne(groundUrl);
      if(failure==='HTTP_ERROR')request.flush({}, {status:502,statusText:'Synthetic failure'});
      else if(failure==='NO_INFERENCE')request.flush({error:'GROUNDING_NO_INFERENCE_SENT_REVIEW_COST'},
        {status:409,statusText:'Conflict'});
      else request.flush({...ground,status:failure,cacheHit:true,canRun:false});
      await pending;fixture.detectChanges();
      expect(failureEvents).toHaveBeenCalledTimes(1);
      expect(component.resultState()).toBe('error');expect(component.playbackReady()).toBeFalse();
      expect(root.querySelector('.viewer-loading')).toBeNull();
      expect(component.videoActionsBusy()).toBeFalse();expect(component.connection.busy()).toBeFalse();
      expect(root.querySelector('.sam-message')?.textContent).toContain('SAM no se envió');
      if(failure==='NO_INFERENCE')expect(root.querySelector('.sam-message')?.textContent).not.toContain('facturada');
      // Existing behavior permits viewing the original after an unsuccessful analysis.
      expect((root.querySelector('.play-main') as HTMLButtonElement).disabled).toBeFalse();
      expect(player.samVerified()).toBeFalse();
      http.expectNone(r=>r.method==='POST'||r.url.endsWith('/evidence'));
      await verifyHttp(http);fixture.destroy();
    });
  }

  it('reports a missing session distinctly from an unavailable server and permits retry',async()=>{
    const {component,fixture,root,http}=await init(false,false);
    for(const status of [404,503]){
      component.recoveryCode.set(' '+session.id.toUpperCase()+' ');const pending=component.resumeResearch(component.recoveryCode());fixture.detectChanges();
      expect(root.querySelector('.recovery-entry [role=progressbar]')).not.toBeNull();
      await component.resumeResearch(session.id); // no duplicate request
      http.expectOne(studyUrl+'/'+session.id+'/recovery').flush({}, {status,statusText:'Test failure'});await pending;fixture.detectChanges();
      expect(root.querySelector('.recovery-status')?.textContent).toContain(status===404?'No se encontró ninguna sesión':'Comprueba la conexión');
      expect(component.recoveryLoading()).toBeFalse();expect(component.session()).toBeNull();
      expect((root.querySelector('.search-recovery') as HTMLButtonElement).disabled).toBeFalse();
    }
    http.verify();fixture.destroy();
  });
  it('restores an empty draft without waiting for nonexistent media',async()=>{
    const {component,fixture,root,http}=await init(false,false);const pending=component.resumeResearch(session.id);
    http.expectOne(studyUrl+'/'+session.id+'/recovery').flush({session,operations:[],selections:[],unavailable:[]});await pending;
    http.expectOne(theoryUrl).flush(theory());fixture.detectChanges();
    expect(component.recoveryLoading()).toBeFalse();expect(component.videoActionsBusy()).toBeFalse();expect(root.querySelector('.focus-config')).not.toBeNull();
    await verifyHttp(http);fixture.destroy();
  });
  it('shows recovery progress until both the original and saved graphics have loaded',async()=>{
    const {component,http,fixture}=await init(false,false);const pending=component.resumeResearch(session.id);
    http.expectOne(studyUrl+'/'+session.id+'/recovery').flush({session:{...session,baseline:video},operations:[],unavailable:[],selections:[{
      studyId:session.id,videoRole:'baseline',sourceSha256:video.sha256,analysisId:'e'.repeat(32)}]});await pending;
    http.expectOne(theoryUrl).flush(theory());expect(component.recovering()).toBeFalse();expect(component.recoveryLoading()).toBeTrue();
    component.onPreviewState('ready');expect(component.recoveryLoading()).toBeTrue();component.onProcessingFinished(true);expect(component.recoveryLoading()).toBeFalse();
    http.verify();fixture.destroy();
  });
  it('serializes draft saves under the same code and blocks uploads after a failed save',async()=>{
    const {component,http,fixture}=await init(false);
    component.baselineLength.set(.8);component.updateDraft();http.expectOne(theoryUrl).flush(theory());await Promise.resolve();
    const first=http.expectOne(studyUrl+'/'+session.id+'/draft');expect(first.request.method).toBe('PUT');expect(first.request.body.baselineLengthMeters).toBe(.8);
    component.massGrams.set(400);component.updateDraft();http.expectOne(theoryUrl).flush(theory());
    first.flush({...session,baselineLengthMeters:.8});for(let i=0;i<6;i++)await Promise.resolve();
    const second=http.expectOne(studyUrl+'/'+session.id+'/draft');expect(second.request.body.massGrams).toBe(400);
    second.flush({}, {status:503,statusText:'Unavailable'});for(let i=0;i<6;i++)await Promise.resolve();
    expect(component.draftSaving()).toBeFalse();expect(component.draftError()).toContain('No se guardaron');expect(component.videoActionsBusy()).toBeTrue();
    component.updateDraft();http.expectOne(theoryUrl).flush(theory());await Promise.resolve();http.expectOne(studyUrl+'/'+session.id+'/draft').flush({...session,baselineLengthMeters:.8,massGrams:400});
    for(let i=0;i<6;i++)await Promise.resolve();expect(component.draftError()).toBe('');expect(component.session()?.id).toBe(session.id);expect(component.videoActionsBusy()).toBeFalse();
    http.expectNone(studyUrl);http.verify();fixture.destroy();
  });

  it('gates the five stages on verified evidence and saved human review',async()=>{
    const {component,http,fixture}=await init(false);
    component.session.set({...session,baseline:video});component.onPreviewState('ready');
    expect(component.canVisitStep(2)).toBeTrue();expect(component.canVisitStep(3)).toBeFalse();
    component.setJourneyStep(3);expect(component.flowStep()).toBe(1);
    component.acceptProcessed({studyId:session.id,videoRole:'baseline',sourceSha256:video.sha256,analysisId:'f'.repeat(32),port:5391});
    component.onPlaybackReady(true);component.setJourneyStep(3);
    expect(component.flowStep()).toBe(3);expect(component.canVisitStep(4)).toBeFalse();
    component.checkedVideo.set(true);
    const next=component.continueToPrediction();
    http.expectOne(studyUrl+'/'+session.id+'/review-baseline').flush({...session,baseline:video,humanReview:{bobConfirmed:true,reviewedAtUtc:'2026-10-30T00:00:00Z'}});
    await next;expect(component.flowStep()).toBe(4);expect(component.canVisitStep(5)).toBeFalse();
    http.expectNone(r=>r.url.includes('/muse/evaluate')||r.url.includes('/sam/baseline/manual'));
    await verifyHttp(http);fixture.destroy();
  });
  it('does not upload video two before a registered prediction',async()=>{
    const {component,http,fixture}=await init(false);
    component.session.set({...session,baseline:video});component.onPreviewState('ready');
    const input=document.createElement('input');input.type='file';
    Object.defineProperty(input,'files',{value:[new File(['source'],'second.mp4',{type:'video/mp4'})]});
    await component.uploadFocused({target:input} as unknown as Event,'followup');
    expect(component.session()?.followup).toBeNull();expect(component.previewState()).toBe('ready');
    http.expectNone(r=>r.method==='POST');await verifyHttp(http);fixture.destroy();
  });
  it('retains recovery warnings outside a collapsed management drawer',async()=>{
    const {component,http,fixture,root}=await init(false,false);
    component.recoveryMenuOpen.set(true);const pending=component.resumeResearch(session.id);
    http.expectOne(studyUrl+'/'+session.id+'/recovery').flush({session,operations:[],selections:[],unavailable:['saved-evidence']});
    await pending;http.expectOne(theoryUrl).flush(theory());fixture.detectChanges();
    expect(component.recoveryMenuOpen()).toBeFalse();
    expect(root.querySelector('.recovery-warning')).not.toBeNull();
    expect(root.querySelector('.recovery-warning')?.classList.contains('visually-hidden')).toBeFalse();
    http.expectNone(r=>r.method==='POST');await verifyHttp(http);fixture.destroy();
  });

});
