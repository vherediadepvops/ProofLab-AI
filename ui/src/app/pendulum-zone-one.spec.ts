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
    for(let i=0;i<6;i++)await Promise.resolve();
    const uploads=http.match(req=>req.method==='GET' &&
      /\/video\/(baseline|followup)\/uploads\/[a-f0-9]{32}$/.test(req.url));
    for(const request of uploads){
      if(request.cancelled)continue;
      const match=request.request.url.match(/\/sessions\/([a-f0-9]{32})\/video\/(baseline|followup)\/uploads\/([a-f0-9]{32})$/)!;
      const now=new Date().toISOString();
      request.flush({uploadId:match[3],studyId:match[1],role:match[2],state:'COMPLETED',phase:'complete',
        receivedBytes:15000,totalBytes:15000,startedAtUtc:now,updatedAtUtc:now,elapsedMs:1000,phases:[]});
    }
    const pending=http.match(req=>req.method==='GET' &&
      (req.url.includes('/kinematics/') || req.url.includes('/sam/') || req.url.includes('/grounding/') || req.url.endsWith('/readiness')));
    for(const request of pending)request.flush({error:'Synthetic unavailable result'},{status:404,statusText:'Not Found'});
    for(let i=0;i<6;i++)await Promise.resolve();
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

  it('starts with an editable empty draft and allows upload before preparation is complete',async()=>{
    const fixture=TestBed.createComponent(PendulumZoneOne),c=fixture.componentInstance;
    const http=TestBed.inject(HttpTestingController);fixture.detectChanges();
    expect(c.baselineLength()).toBeNull();expect(c.initialAngleDegrees()).toBeNull();expect(c.massGrams()).toBeNull();
    http.expectNone(theoryUrl);
    const blank={...session,baselineLengthMeters:null,angleDegrees:null,massGrams:null,preparationPending:true};
    const creating=c.generateRecoveryCode();await Promise.resolve();await Promise.resolve();
    const req=http.expectOne(studyUrl);expect(req.request.body.baselineLengthMeters).toBeNull();expect(req.request.body.angleDegrees).toBeNull();expect(req.request.body.massGrams).toBeNull();
    req.flush(blank);await creating;fixture.detectChanges();
    const el=fixture.nativeElement as HTMLElement;
    for(const id of ['focus-length','focus-angle','focus-mass']){
      const input=el.querySelector('#'+id) as HTMLInputElement;
      expect(input.value).toBe('');expect(input.dataset['state']).toBe('missing');
    }
    expect(c.videoActionsBusy()).toBeFalse();expect(c.uploadBlocked()).toBeFalse();expect(c.parametersInvalid()).toBeFalse();expect(c.theoreticalPreview()).toBeNull();
    expect((el.querySelector('.upload-new-video input') as HTMLInputElement).disabled).toBeFalse();
    expect((el.querySelector('#focus-length') as HTMLInputElement).required).toBeTrue();
    expect((el.querySelector('#focus-mass') as HTMLInputElement).required).toBeTrue();
    expect((el.querySelector('#focus-angle') as HTMLInputElement).required).toBeTrue();
    expect(el.querySelector('#length-input-state')?.textContent).toContain('Dato obligatorio');
    fixture.componentRef.setInput('language','en');fixture.detectChanges();
    expect(el.querySelector('#mass-input-state')?.textContent).toContain('Required value');
    fixture.componentRef.setInput('language','es');fixture.detectChanges();
    expect(el.textContent).toContain('no precisión garantizada');
    const restoring=c.restore(blank.id);http.expectOne(studyUrl+'/'+blank.id).flush(blank);await restoring;
    expect(c.baselineLength()).toBeNull();expect(c.initialAngleDegrees()).toBeNull();expect(c.massGrams()).toBeNull();
    http.expectNone(theoryUrl);await verifyHttp(http);
  });

  it('keeps saved results accessible for historical studies with missing physical inputs',async()=>{
    const {component:c,fixture,http}=await init(false);
    c.baselineLength.set(null);c.massGrams.set(null);c.initialAngleDegrees.set(null);
    c.session.set({...session,baselineLengthMeters:null,massGrams:null,angleDegrees:null,baseline:video});
    c.acceptProcessed({studyId:session.id,videoRole:'baseline',sourceSha256:video.sha256,analysisId:'f'.repeat(32),port:5391});
    c.onPreviewState('ready');c.onProcessingFinished(true);fixture.detectChanges();
    expect(c.canVisitStep(2)).toBeFalse();expect(c.canVisitStep(3)).toBeTrue();
    expect(fixture.nativeElement.querySelector('app-sam-video-replay')).toBeNull();
    c.setJourneyStep(3);expect(c.flowStep()).toBe(3);
    http.expectNone(r=>r.method==='POST');await verifyHttp(http);fixture.destroy();
  });

  it('distinguishes zero angle, invalid zero length, and cleared fields without changing measurements',async()=>{
    const {component:c,fixture,http}=await init(false);
    c.initialAngleDegrees.set(0);await c.refreshTheory();fixture.detectChanges();
    expect(c.parameterState(0,0,85)).toBe('valid');expect(c.parametersInvalid()).toBeFalse();
    expect(c.theoreticalPreview()).toBeNull();http.expectNone(theoryUrl);
    c.baselineLength.set(0);c.updateDraft();fixture.detectChanges();await Promise.resolve();
    expect(c.parametersInvalid()).toBeTrue();expect(c.canVisitStep(2)).toBeFalse();http.expectNone(studyUrl+'/'+session.id+'/draft');
    c.baselineLength.set(c.optionalNumber('',100));c.massGrams.set(c.optionalNumber(null));c.updateDraft();
    await Promise.resolve();await Promise.resolve();
    const save=http.expectOne(studyUrl+'/'+session.id+'/draft');
    expect(save.request.body.baselineLengthMeters).toBeNull();expect(save.request.body.massGrams).toBeNull();expect(save.request.body.angleDegrees).toBe(0);
    save.flush({...session,baselineLengthMeters:null,massGrams:null,angleDegrees:0});await Promise.resolve();await Promise.resolve();fixture.detectChanges();
    expect(c.parametersInvalid()).toBeFalse();expect(c.model()).toBeNull();
    c.reset();expect(c.baselineLength()).toBeNull();expect(c.initialAngleDegrees()).toBeNull();expect(c.massGrams()).toBeNull();
    await verifyHttp(http);
  });

  async function init(chooseUpload=true,generate=true){
    const fixture=TestBed.createComponent(PendulumZoneOne);
    fixture.componentInstance.baselineLength.set(.5); fixture.componentInstance.initialAngleDegrees.set(15); fixture.componentInstance.massGrams.set(300);
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
    for(let i=0;i<6;i++)await Promise.resolve();
    http.expectNone(studyUrl);http.expectNone(theoryUrl);
    const transfer=http.expectOne(r=>r.method==='POST' && r.url.includes('/'+session.id+'/video/baseline'));
    expect(transfer.request.body).toBe(file);transfer.flush({...session,baseline:video,preparationPending:true});await pending;fixture.detectChanges();await Promise.resolve();await Promise.resolve();fixture.detectChanges();
    expect(component.session()?.id).toBe(session.id);expect(component.session()?.baseline?.sha256).toBe(video.sha256);
    expect((root.querySelector('#focus-length') as HTMLInputElement).disabled).toBeFalse();
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
    fixture.detectChanges();
    const replay=fixture.debugElement.query(By.directive(SamVideoReplay)).componentInstance as SamVideoReplay;
    const player=fixture.debugElement.query(By.directive(PendulumMasterPlayer)).componentInstance as PendulumMasterPlayer;
    replay.selectSource('cache',true);
    await Promise.resolve();
    const controls=root.querySelector('app-sam-video-replay')!,samUrl=studyUrl+'/'+session.id+'/sam/baseline';
    const analysisId='f'.repeat(32),evidenceUrl=samUrl+'/evidence?analysisId='+analysisId;
    http.expectOne(samUrl+'/reusable').flush({sourceSha256:video.sha256,cacheHit:true,analysisId,
      adaptiveBatchId:null,excludedResults:0,quality:{usable:true,reason:'MEASUREMENTS_AVAILABLE'},storage:'sqlite'});
    http.expectOne(samUrl+'/preflight').flush({sourceSha256:video.sha256,sourceFrames:1828,estimatedCostUsd:.3656,
      durationSeconds:30.5,codec:'hevc',supportsSingleCall:true});
    await fixture.whenStable();fixture.detectChanges();
    expect(replay.displayedCost()).toBe(0);expect(controls.textContent).toContain('US$ 0.00');
    http.expectNone(req=>req.method==='POST' || req.url.endsWith('/readiness') ||
      req.url.includes('/spark-quote') || req.url.includes('/grounding/preflight'));
    http.expectNone(evidenceUrl);
    player.previewLoading.set(false);component.onPreviewState('ready');component.setJourneyStep(2);fixture.detectChanges();
    const play=root.querySelector('.play-main') as HTMLButtonElement;expect(play.disabled).toBeFalse();
    (controls.querySelector('.sam-submit') as HTMLButtonElement).click();fixture.detectChanges();
    expect(component.resultState()).toBe('loading');
    expect(root.querySelector('app-sam-video-replay [aria-busy="true"]')).not.toBeNull();
    expect((controls.querySelector('.sam-submit') as HTMLButtonElement).disabled).toBeTrue();
    const evidence=http.expectOne(evidenceUrl);expect(evidence.request.method).toBe('GET');
    // Synthetic parser-conformance mask, never a live provider observation.
    const mask="<|mask;x=0;y=0;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7`e.ioeAf6-k/#Xd8%dX9x(|>";
    const wire=Array.from({length:8},(_,i)=>'<'+(i*4)+'f>0<|box;x1=211;y1=228;x2=270;y2=254;w=1920;h=1080|>'+mask).join('\n');
    evidence.flush({
      state:{status:'CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW',sourceSha256:video.sha256,
        analysisId,provenance:'META_SAM_3_1_CACHED_PROVIDER_OUTPUT'},
      receipt:{model:'sam-3.1',concept:'metal pendulum bob',originalVideoSha256:video.sha256,analysisId,
        providerEvidenceStatus:'COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW',
        framePtsSeconds:Array.from({length:1828},(_,i)=>i/60),sourceFrameCount:1828},
      rawResponseJson:JSON.stringify({model:'sam-3.1',status:'completed',output:[{content:[{type:'output_text',text:wire}]}]})
    });
    http.expectOne(studyUrl+'/'+session.id+'/kinematics/baseline').flush({},{status:404,statusText:'Not Found'});
    for(let i=0;i<8;i++)await Promise.resolve();
    for(let i=0;i<30&&!component.playbackReady();i++)await new Promise<void>(resolve=>setTimeout(resolve,10));
    fixture.detectChanges();
    expect(play.disabled).toBeFalse();expect(component.resultState()).toBe('ready');
    expect(component.connection.busy()).toBeFalse();expect(component.playbackReady()).toBeTrue();
    expect(player.samVerified()).toBeTrue();expect(player.samTrack()?.masks.length).toBe(8);
    expect(component.processedAnalysis('baseline')).toBe(analysisId);
    http.expectNone(req=>req.method==='POST'||req.url.endsWith('/readiness'));
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
    const tokenDialog=manual?.querySelector('dialog.sam-token-dialog') as HTMLDialogElement;
    expect(tokenDialog).not.toBeNull();expect(tokenDialog.open).toBeFalse();
    const passwordFields=manual!.querySelectorAll('input[type=password]');
    expect(passwordFields.length).toBe(1);expect(passwordFields[0].closest('dialog')).toBe(tokenDialog);
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

  it('opens a new empty setup before another recording can be uploaded',async()=>{
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
    http.expectNone(theoryUrl);
    await Promise.resolve();await Promise.resolve();
    const create=http.expectOne(studyUrl);
    expect(create.request.body.baselineLengthMeters).toBeNull();
    expect(create.request.body.angleDegrees).toBeNull();
    expect(create.request.body.massGrams).toBeNull();
    create.flush({...session,id:nextId,baselineLengthMeters:null,angleDegrees:null,massGrams:null,preparationPending:true});
    for(let i=0;i<8;i++)await Promise.resolve();
    http.expectNone(r=>r.url.includes('/video/'));
    await transfer;fixture.detectChanges();
    expect(component.session()?.baseline).toBeNull();expect(component.uploadBlocked()).toBeFalse();
    expect((root.querySelector('.upload-new-video input') as HTMLInputElement).disabled).toBeFalse();
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
    http.expectOne(studyUrl+'/'+'e'.repeat(32)+'/original/baseline').flush(new Blob([new Uint8Array(15000)],{type:'video/quicktime'}));for(let i=0;i<8;i++)await Promise.resolve();
    http.expectOne(r=>r.method==='POST' && r.url.includes('/'+session.id+'/video/baseline')).flush({...session,baseline:video,preparationPending:true});await loading;fixture.detectChanges();
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
    expect(root.querySelector('.new-trial')).not.toBeNull();
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
      const samUrl=studyUrl+'/'+session.id+'/sam/baseline',adaptiveUrl=studyUrl+'/'+session.id+'/sam-adaptive/baseline';
      http.expectOne(samUrl+'/preflight').flush({sourceSha256:video.sha256,sourceFrames:1828,
        estimatedCostUsd:.3656,durationSeconds:30.5,codec:'hevc',supportsSingleCall:true});
      http.expectNone(samUrl+'/reusable');for(let i=0;i<8;i++)await Promise.resolve();
      http.expectOne(adaptiveUrl+'/preflight?targetCoreFrames=53&mode=api-meta').flush({
        mode:'api-meta',sceneRunId:null,sourceSha256:video.sha256,sparkSceneKey:'',
        sourcePtsSha256:'a'.repeat(64),planSha256:'c'.repeat(64),suggestedBatchId:'f'.repeat(32),
        sourceFrames:1828,parts:3,simultaneousRequests:3,submittedFrames:1848,billableFrames:1848,
        totalSamEstimatedUsd:.3696,maximumAllowedSamUsd:.50,continuousRetryEstimatedUsd:.3656,
        maximumSamWithRetryEstimatedUsd:.7352,remainingTrialSlots:null,canRunWithManualToken:true,
        existingSparkSceneVerified:false,requiresNewSparkCall:true,idealPartsWithoutSparkCoverage:3,
        verifiedSparkSeedCount:0,reducedForSparseSparkSeeds:false});
      http.expectOne('http://127.0.0.1:5391/api/meta/readiness').flush({
        apiKeyConfigured:false,liveCallsPermitted:false,manualTokenCallsPermitted:true,
        paidRequestsSwitchEnabled:true,creditsConfirmedByOwner:true,ownerApprovedPaidRequests:true,
        usedRequests:0,remainingRequests:0,manualRequestsUnlimited:true});
      const ground={cacheKey:'e'.repeat(64),videoSha256:video.sha256,maximumEstimatedCostUsd:.25,
        mode:'api-meta',freshRequired:true,protocol:'ASSEMBLY_FULL_VIDEO_GROUNDING_V2',
        cacheHit:false,priorAttempt:false,canRun:true,status:'NEEDS_LOCALIZATION',scene:null};
      http.expectOne(adaptiveUrl+'/spark-quote?mode=api-meta').flush(ground);await fixture.whenStable();
      const replay=fixture.debugElement.query(By.directive(SamVideoReplay)).componentInstance as SamVideoReplay;
      const player=fixture.debugElement.query(By.directive(PendulumMasterPlayer)).componentInstance as PendulumMasterPlayer;
      player.previewLoading.set(false);component.onPreviewState('ready');component.setJourneyStep(2);
      const field=document.createElement('input');field.type='password';component.connection.register(field);
      field.value='LLM_SYNTHETIC_INTEGRATION_TEST_NEVER_SENT_123456';component.connection.confirmToken();fixture.detectChanges();
      expect(replay.mode()).toBe('api-meta');expect(root.querySelector('.viewer-loading')).toBeNull();
      expect(replay.canProcess()).toBeTrue();
      const failureEvents=spyOn(replay.processingFailed,'emit').and.callThrough();
      const pending=replay.process();fixture.detectChanges();
      expect(component.resultState()).toBe('loading');
      expect(root.querySelector('app-sam-video-replay [aria-busy="true"]')).not.toBeNull();
      const request=http.expectOne(adaptiveUrl+'/spark-once');
      expect(request.request.method).toBe('POST');expect(request.request.body.mode).toBe('api-meta');
      if(failure==='HTTP_ERROR')request.flush({},{status:502,statusText:'Synthetic failure'});
      else if(failure==='NO_INFERENCE')request.flush({error:'GROUNDING_NO_INFERENCE_SENT_REVIEW_COST'},{status:409,statusText:'Conflict'});
      else request.flush({...ground,status:failure,canRun:false,scene:null});
      await pending;fixture.detectChanges();
      expect(failureEvents).toHaveBeenCalledTimes(1);expect(component.resultState()).toBe('error');
      expect(component.playbackReady()).toBeFalse();expect(root.querySelector('.viewer-loading')).toBeNull();
      expect(component.videoActionsBusy()).toBeFalse();expect(component.connection.busy()).toBeFalse();
      expect(root.querySelector('.sam-message')?.textContent).toContain('SAM no se envió');
      if(failure==='NO_INFERENCE')expect(root.querySelector('.sam-message')?.textContent).not.toContain('facturada');
      expect((root.querySelector('.play-main') as HTMLButtonElement).disabled).toBeFalse();
      expect(player.samVerified()).toBeFalse();
      http.expectNone(r=>r.method==='POST'||r.url.includes('/evidence'));
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
  it('serializes draft saves under the same code and cannot advance with an unsaved snapshot',async()=>{
    const {component,http,fixture}=await init(false);
    component.baselineLength.set(.8);component.updateDraft();http.expectOne(theoryUrl).flush(theory());await Promise.resolve();
    const first=http.expectOne(studyUrl+'/'+session.id+'/draft');expect(first.request.method).toBe('PUT');expect(first.request.body.baselineLengthMeters).toBe(.8);
    component.massGrams.set(400);component.updateDraft();http.expectOne(theoryUrl).flush(theory());
    first.flush({...session,baselineLengthMeters:.8});for(let i=0;i<6;i++)await Promise.resolve();
    const second=http.expectOne(studyUrl+'/'+session.id+'/draft');expect(second.request.body.massGrams).toBe(400);
    second.flush({}, {status:503,statusText:'Unavailable'});for(let i=0;i<6;i++)await Promise.resolve();
    expect(component.draftSaving()).toBeFalse();expect(component.draftError()).toContain('No se guardaron');expect(component.requiredParametersSaved()).toBeFalse();expect(component.canVisitStep(2)).toBeFalse();
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
