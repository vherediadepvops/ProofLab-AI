import { NO_ERRORS_SCHEMA } from '@angular/core';
import { TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PendulumZoneOne } from './pendulum-zone-one';
import { PendulumMasterPlayer } from './pendulum-master-player';
import { SamVideoReplay } from './sam-video-replay';
import type { DirectorSession } from './pendulum-director';

const id='d'.repeat(32),api='http://127.0.0.1:5391/api/science/director/pendulum/sessions';
const study=api+'/'+id,prepareUrl=study+'/prepare-analysis',draftUrl=study+'/draft';
const video={
  role:'baseline',videoId:'e'.repeat(32),originalFilename:'first.mp4',bytes:15000,sha256:'b'.repeat(64),
  uploadedAtUtc:'2026-11-01T15:00:00Z',posterAvailable:true,previewAvailable:false,
  media:{codec:'h264',durationSeconds:6,width:640,height:480,nominalFramesPerSecond:30},
  tracking:{framesAnalyzed:72,detectedFrames:60,observations:[{timeSeconds:0,position:150}],
    pixelSamples:[{timeSeconds:0,x:100,y:180,approximateRadiusPixels:10}]},
  spectral:{periodSeconds:1.4,fitRSquared:.97,status:'PERIOD_CANDIDATE_REQUIRES_MANUAL_BOB_VERIFICATION'}
};
const blank:DirectorSession={
  id,baselineLengthMeters:null,angleDegrees:null,massGrams:null,includeSecondTrial:false,followUpMode:'recommended',
  selectedFollowUpLengthMeters:null,createdAtUtc:'2026-11-01T14:00:00Z',step:'AWAITING_FIRST_VIDEO',
  modelStatus:'LOCAL_PROTOCOL_NOT_META_AI_NOT_SAM',preparationPending:true,
  baseline:null,humanReview:null,proposal:null,followup:null,comparison:null,singleComparison:null,modelSha256:'1'.repeat(64)
};
function saved(mass=300):DirectorSession {
  return {...blank,baselineLengthMeters:.5,angleDegrees:0,massGrams:mass,baseline:video};
}
function progress(uploadId:string,phase='local_tracking',receivedBytes=15000) {
  const now=new Date().toISOString();
  return {uploadId,studyId:id,role:'baseline',state:'RUNNING',phase,receivedBytes,totalBytes:15000,
    startedAtUtc:now,updatedAtUtc:now,elapsedMs:1000,phases:[]};
}

describe('Preparation, native upload and explicit analysis admission',()=>{
  beforeEach(async()=>{
    localStorage.removeItem('prooflab-director-current');
    TestBed.configureTestingModule({
      imports:[PendulumZoneOne],providers:[provideHttpClient(),provideHttpClientTesting()]
    });
    // Keep the actual preparation controls; isolate unrelated child requests.
    TestBed.overrideComponent(PendulumZoneOne,{
      remove:{imports:[PendulumMasterPlayer,SamVideoReplay]},add:{schemas:[NO_ERRORS_SCHEMA]}
    });
    await TestBed.compileComponents();
  });
  afterEach(()=>TestBed.inject(HttpTestingController).verify());

  function setup(withSavedVideo=false) {
    const f=TestBed.createComponent(PendulumZoneOne),c=f.componentInstance,h=TestBed.inject(HttpTestingController);
    c.session.set(withSavedVideo?saved():{...blank});
    if(withSavedVideo){
      c.baselineLength.set(.5);c.initialAngleDegrees.set(0);c.massGrams.set(300);c.onPreviewState('ready');
    }
    f.detectChanges();flushMicrotasks();return {f,c,h};
  }
  type Context=ReturnType<typeof setup>;
  function finish(x:Context):void {
    x.h.expectNone(r=>/\/(?:sam(?:-adaptive)?|assembly|muse)\//.test(r.url)||r.url.endsWith('/api/meta/readiness'));
    x.f.destroy();flushMicrotasks();
  }
  function startUpload(x:Context) {
    const file=new File([new Uint8Array(15000)],'first.mp4',{type:'video/mp4'});
    const control={files:[file],value:'selected'};
    const pending=x.c.uploadFocused({target:control} as unknown as Event,'baseline');
    flushMicrotasks(); // uploadFocused waits for previously queued draft saves.
    const request=x.h.expectOne(r=>r.method==='POST'&&r.url.includes('/'+id+'/video/baseline'));
    const uploadId=new URL(request.request.urlWithParams).searchParams.get('uploadId');
    expect(uploadId).toMatch(/^[a-f0-9]{32}$/);
    expect(request.request.body).toBe(file);expect(request.request.reportProgress).toBeTrue();
    return {pending,request,control,uploadId:uploadId!,progressUrl:study+'/video/baseline/uploads/'+uploadId};
  }
  function flushUploadFinal(x:Context,upload:ReturnType<typeof startUpload>,state:'COMPLETED'|'FAILED'):void {
    flushMicrotasks();
    const reads=x.h.match(r=>r.method==='GET'&&r.url===upload.progressUrl);
    expect(reads.length).withContext('The upload requests its final server progress').toBeGreaterThan(0);
    const snapshot={...progress(upload.uploadId,state==='COMPLETED'?'complete':'receive_and_hash',
      state==='COMPLETED'?15000:0),state};
    for(const read of reads)if(!read.cancelled)read.flush(snapshot);
    flushMicrotasks();
  }

  it('uploads with empty physical values and keeps them editable',fakeAsync(()=>{
    const x=setup();
    expect(x.c.uploadBlocked()).toBeFalse();
    expect(x.c.baselineLength()).toBeNull();expect(x.c.initialAngleDegrees()).toBeNull();expect(x.c.massGrams()).toBeNull();
    const u=startUpload(x);u.request.event({type:HttpEventType.Sent});flushMicrotasks();
    expect(x.c.session()?.baseline).toBeNull();expect(x.c.stageBusy()).toBe('baseline');
    u.request.flush({...blank,baseline:video});flushUploadFinal(x,u,'COMPLETED');
    // Server byte telemetry also works when the browser supplies no upload events.
    expect(x.c.uploadPercent()).toBe(100);
    x.c.onPreviewState('ready');x.f.detectChanges();
    expect(x.c.session()?.id).toBe(id);expect(x.c.session()?.baseline?.sha256).toBe(video.sha256);
    expect(x.c.session()?.preparationPending).toBeTrue();
    expect(x.c.baselineLength()).toBeNull();expect(x.c.initialAngleDegrees()).toBeNull();expect(x.c.massGrams()).toBeNull();
    expect(x.c.flowStep()).toBe(1);expect(u.control.value).toBe('');
    for(const inputId of ['focus-length','focus-angle','focus-mass']){
      const input=x.f.nativeElement.querySelector('#'+inputId) as HTMLInputElement;
      expect(input).not.toBeNull();expect(input.disabled).toBeFalse();
    }
    x.h.expectNone(api);x.h.expectNone(prepareUrl);finish(x);
  }));

  it('keeps upload 100 percent separate from server and player readiness',fakeAsync(()=>{
    const x=setup(),u=startUpload(x);
    u.request.event({type:HttpEventType.Sent});
    u.request.event({type:HttpEventType.UploadProgress,loaded:7500,total:15000});flushMicrotasks();
    expect(x.c.uploadTransfer()?.loaded).toBe(7500);expect(x.c.uploadTransfer()?.total).toBe(15000);
    expect(x.c.stageBusy()).toBe('baseline');expect(x.c.session()?.baseline).toBeNull();
    u.request.event({type:HttpEventType.UploadProgress,loaded:15000,total:15000});flushMicrotasks();
    expect(x.c.uploadTransfer()?.loaded).toBe(15000);expect(x.c.uploadServerComplete()).toBeFalse();
    expect(x.c.stageBusy()).toBe('baseline');expect(x.c.canVisitStep(2)).toBeFalse();
    tick(1000);
    const poll=x.h.expectOne(u.progressUrl);expect(poll.request.method).toBe('GET');
    poll.flush(progress(u.uploadId));flushMicrotasks();
    expect(x.c.uploadProgress()?.phase).toBe('local_tracking');expect(x.c.uploadProgress()?.state).toBe('RUNNING');
    expect(x.c.session()?.baseline).toBeNull();
    u.request.flush({...blank,baseline:video});flushUploadFinal(x,u,'COMPLETED');
    expect(x.c.uploadServerComplete()).toBeTrue();expect(x.c.stageBusy()).toBe('');
    expect(x.c.session()?.baseline?.sha256).toBe(video.sha256);
    expect(x.c.previewState()).toBe('loading');expect(x.c.flowStep()).toBe(1);expect(x.c.canVisitStep(2)).toBeFalse();
    tick(2000);x.h.expectNone(u.progressUrl);finish(x);
  }));

  it('rejects missing or out-of-range values on Next',fakeAsync(()=>{
    const x=setup(true);
    const cases:Array<[number|null,number|null,number|null]>=[
      [null,15,300],[.5,null,300],[.5,15,null],[0,15,300],[.5,86,300]
    ];
    for(const [length,angle,mass]of cases){
      x.c.baselineLength.set(length);x.c.initialAngleDegrees.set(angle);x.c.massGrams.set(mass);
      void x.c.continueFromPreparation();flushMicrotasks();x.f.detectChanges();
      expect(x.c.flowStep()).toBe(1);expect(x.c.preparationMessage().length).toBeGreaterThan(0);
      expect(x.c.preparingAnalysis()).toBeFalse();x.h.expectNone(prepareUrl);
    }
    finish(x);
  }));

  it('accepts saved zero degrees and freezes the exact preparation',fakeAsync(()=>{
    const x=setup(true);
    expect(x.c.initialAngleDegrees()).toBe(0);expect(x.c.requiredParametersReady()).toBeTrue();
    expect(x.c.requiredParametersSaved()).toBeTrue();expect(x.c.model()).toBeNull();
    void x.c.continueFromPreparation();flushMicrotasks();
    const freeze=x.h.expectOne(prepareUrl);expect(freeze.request.method).toBe('POST');
    expect(freeze.request.body).toEqual(jasmine.objectContaining({baselineLengthMeters:.5,angleDegrees:0,massGrams:300}));
    expect(x.c.flowStep()).toBe(1);expect(x.c.preparingAnalysis()).toBeTrue();
    void x.c.continueFromPreparation();flushMicrotasks();x.h.expectNone(prepareUrl);
    freeze.flush({...saved(),preparationPending:false,modelSha256:'2'.repeat(64)});
    flushMicrotasks();x.f.detectChanges();
    expect(x.c.session()?.preparationPending).toBeFalse();expect(x.c.session()?.modelSha256).toBe('2'.repeat(64));
    expect(x.c.session()?.baseline?.sha256).toBe(video.sha256);expect(x.c.flowStep()).toBe(2);
    expect(x.c.preparingAnalysis()).toBeFalse();expect(x.c.model()).toBeNull();finish(x);
  }));

  it('waits for the last draft and never freezes a stale or failed save',fakeAsync(()=>{
    const x=setup(true);x.c.massGrams.set(350);x.c.updateDraft();flushMicrotasks();
    const first=x.h.expectOne(draftUrl);expect(first.request.body.massGrams).toBe(350);
    x.c.massGrams.set(400);x.c.updateDraft();void x.c.continueFromPreparation();flushMicrotasks();
    expect(x.c.requiredParametersSaved()).toBeFalse();expect(x.c.flowStep()).toBe(1);x.h.expectNone(prepareUrl);
    first.flush({...saved(350),modelSha256:'2'.repeat(64)});flushMicrotasks();
    const last=x.h.expectOne(draftUrl);expect(last.request.body.massGrams).toBe(400);
    expect(x.c.requiredParametersSaved()).toBeFalse();x.h.expectNone(prepareUrl);
    last.flush({},{status:503,statusText:'Unavailable'});flushMicrotasks();
    expect(x.c.draftError().length).toBeGreaterThan(0);expect(x.c.preparationMessage().length).toBeGreaterThan(0);
    expect(x.c.flowStep()).toBe(1);expect(x.c.session()?.massGrams).toBe(350);expect(x.c.massGrams()).toBe(400);
    x.h.expectNone(prepareUrl);
    x.c.updateDraft();flushMicrotasks();
    x.h.expectOne(draftUrl).flush({...saved(400),modelSha256:'3'.repeat(64)});flushMicrotasks();
    expect(x.c.requiredParametersSaved()).toBeTrue();expect(x.c.draftError()).toBe('');
    void x.c.continueFromPreparation();flushMicrotasks();
    const freeze=x.h.expectOne(prepareUrl);expect(freeze.request.body.massGrams).toBe(400);
    freeze.flush({...saved(400),preparationPending:false,modelSha256:'3'.repeat(64)});flushMicrotasks();
    expect(x.c.flowStep()).toBe(2);expect(x.c.session()?.id).toBe(id);
    expect(x.c.session()?.baseline?.sha256).toBe(video.sha256);expect(x.c.session()?.modelSha256).toBe('3'.repeat(64));
    finish(x);
  }));

  it('requires an existing and loaded original before freezing preparation',fakeAsync(()=>{
    const x=setup(true);
    for(const state of ['empty','loading','error'] as const){
      x.c.session.set(state==='empty'?{...saved(),baseline:null}:saved());
      x.c.previewState.set(state);void x.c.continueFromPreparation();flushMicrotasks();
      expect(x.c.flowStep()).toBe(1);expect(x.c.preparationMessage().length).toBeGreaterThan(0);
      expect(x.c.preparingAnalysis()).toBeFalse();x.h.expectNone(prepareUrl);
    }
    finish(x);
  }));

  it('stops a failed upload and ignores its late progress during the next one',fakeAsync(()=>{
    const x=setup(),first=startUpload(x);tick(1000);
    const oldPoll=x.h.expectOne(first.progressUrl);
    first.request.flush({},{status:500,statusText:'Synthetic upload failure'});flushUploadFinal(x,first,'FAILED');
    expect(x.c.stageBusy()).toBe('');expect(x.c.uploadServerComplete()).toBeFalse();
    expect(x.c.session()?.baseline).toBeNull();expect(x.c.error().length).toBeGreaterThan(0);expect(x.c.flowStep()).toBe(1);
    const second=startUpload(x);
    expect(second.uploadId).not.toBe(first.uploadId);expect(x.c.uploadActiveId()).toBe(second.uploadId);
    if(!oldPoll.cancelled){oldPoll.flush(progress(first.uploadId,'receive_and_hash',7500));flushMicrotasks();}
    expect(x.c.uploadActiveId()).toBe(second.uploadId);expect(x.c.uploadProgress()?.uploadId).not.toBe(first.uploadId);
    expect(x.c.stageBusy()).toBe('baseline');
    tick(1000);x.h.expectOne(second.progressUrl).flush(progress(second.uploadId));flushMicrotasks();
    expect(x.c.uploadProgress()?.uploadId).toBe(second.uploadId);expect(x.c.uploadProgress()?.phase).toBe('local_tracking');
    second.request.flush({...blank,baseline:video});flushUploadFinal(x,second,'COMPLETED');
    expect(x.c.stageBusy()).toBe('');expect(x.c.session()?.baseline?.sha256).toBe(video.sha256);
    tick(2000);x.h.expectNone(first.progressUrl);x.h.expectNone(second.progressUrl);finish(x);
  }));

  it('defers upload-time edits and saves only the latest values without losing the video',fakeAsync(()=>{
    const x=setup(),u=startUpload(x);u.request.event({type:HttpEventType.Sent});
    u.request.event({type:HttpEventType.UploadProgress,loaded:7500,total:15000});flushMicrotasks();x.f.detectChanges();
    expect(x.c.stageBusy()).toBe('baseline');
    for(const inputId of ['focus-length','focus-angle','focus-mass']){
      const input=x.f.nativeElement.querySelector('#'+inputId) as HTMLInputElement;
      expect(input).not.toBeNull();expect(input.disabled).toBeFalse();
    }
    x.c.baselineLength.set(.5);x.c.initialAngleDegrees.set(0);x.c.massGrams.set(350);x.c.updateDraft();
    x.c.massGrams.set(400);x.c.updateDraft();flushMicrotasks();
    expect(x.c.massGrams()).toBe(400);expect(x.c.session()?.massGrams).toBeNull();expect(x.c.draftSaving()).toBeTrue();
    x.h.expectNone(draftUrl);x.h.expectNone(prepareUrl);
    u.request.flush({...blank,baseline:video});flushUploadFinal(x,u,'COMPLETED');
    expect(x.c.baselineLength()).toBe(.5);expect(x.c.initialAngleDegrees()).toBe(0);expect(x.c.massGrams()).toBe(400);
    expect(x.c.session()?.baseline?.sha256).toBe(video.sha256);expect(x.c.flowStep()).toBe(1);
    const deferred=x.h.expectOne(draftUrl);expect(deferred.request.method).toBe('PUT');
    expect(deferred.request.body).toEqual(jasmine.objectContaining({baselineLengthMeters:.5,angleDegrees:0,massGrams:400}));
    x.h.expectNone(draftUrl);x.h.expectNone(prepareUrl);
    deferred.flush({...saved(400),preparationPending:true,modelSha256:'3'.repeat(64)});flushMicrotasks();
    expect(x.c.draftSaving()).toBeFalse();expect(x.c.draftError()).toBe('');
    expect(x.c.requiredParametersSaved()).toBeTrue();expect(x.c.session()?.id).toBe(id);
    expect(x.c.session()?.baseline).toEqual(video);expect(x.c.session()?.massGrams).toBe(400);
    expect(x.c.session()?.modelSha256).toBe('3'.repeat(64));expect(x.c.flowStep()).toBe(1);
    tick(2000);x.h.expectNone(draftUrl);x.h.expectNone(u.progressUrl);x.h.expectNone(prepareUrl);finish(x);
  }));
});
