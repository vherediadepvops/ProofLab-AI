import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SamVideoReplay } from './sam-video-replay';
import { LabConnection } from './lab-connection';

const sha='a'.repeat(64);
const id='b'.repeat(32);
const cacheMode='api-meta-cache';
const freshMode='api-meta';
const url='http://127.0.0.1:5391/api/science/director/pendulum/sessions/'+id+'/sam/baseline';

describe('SAM one-time paid inference gate and cached replay UI',()=>{
  beforeEach(async()=>{
    await TestBed.configureTestingModule({
      imports:[SamVideoReplay],
      providers:[provideHttpClient(),provideHttpClientTesting()]
    }).compileComponents();
  });
  function setup(){
    const fixture=TestBed.createComponent(SamVideoReplay);
    fixture.componentRef.setInput('studyId',id);
    fixture.componentRef.setInput('videoSha256',sha);
    fixture.detectChanges();
    const http=TestBed.inject(HttpTestingController);
    const field=document.createElement('input');
    field.type='password';
    const connection=TestBed.inject(LabConnection);
    connection.register(field);
    field.addEventListener('input',()=>connection.confirmToken());
    (fixture as any).settingsTokenField=field;
    return {fixture,component:fixture.componentInstance,http};
  }
  const state=(status='NOT_ANALYZED_WITH_SAM',allowed=false)=>({
    status,sourceSha256:sha,cacheKey:'c'.repeat(64),
    sourceFrames:0,estimatedCostUsd:0,canRunLive:allowed,
    provenance:'NO_SAM_PROVIDER_EVIDENCE',mode:cacheMode,
    cacheHit:status.startsWith('CACHED_META_'),storage:'SQLite',
    analysisId:status.startsWith('CACHED_META_')?'analysis-test-001':null
  });
  const preflight={
    sourceSha256:sha,sourceFrames:1828,durationSeconds:30.496667,
    estimatedCostUsd:0.3656,supportsSingleCall:true,
    codec:'hevc',timingSource:'FFPROBE',framePtsSha256:'d'.repeat(64)
  };
  const readiness=(ready=false)=>({
    apiKeyConfigured:ready,creditsConfirmedByOwner:ready,
    ownerApprovedPaidRequests:ready,liveCallsPermitted:ready,
    remainingRequests:ready?1:0,usedRequests:ready?0:2
  });
  async function resolvePreflight(http:HttpTestingController,ready=false){
    http.expectOne(url+'/status?mode='+cacheMode).flush(state('NOT_ANALYZED_WITH_SAM',ready));
    await Promise.resolve();
    http.expectOne(url+'/preflight').flush(preflight);
    await Promise.resolve();
    http.expectOne('http://127.0.0.1:5391/api/meta/readiness').flush(readiness(ready));
    await Promise.resolve();
  }

  it('uses only GET requests for preflight and refuses new SAM with Meta gates closed',async()=>{
    const {fixture,component,http}=setup();
    const task=component.check();
    await resolvePreflight(http,false);
    await task;fixture.detectChanges();
    expect(component.preflight()?.sourceFrames).toBe(1828);
    expect(component.canOfferPaidRun()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('bloqueado');
    expect(fixture.nativeElement.textContent).toContain('0.3656');
    component.transferConsent.set(true);
    component.paidConsent.set(true);
    await component.analyze(); // guard, no network
    http.expectNone(r=>r.method==='POST');
    http.verify();fixture.destroy();
  });

  it('a cache hit emits replay without checking billing or issuing a POST',async()=>{
    const {fixture,component,http}=setup();
    const notification=jasmine.createSpy('cacheUpdated');
    component.cacheUpdated.subscribe(notification);
    const task=component.check();
    http.expectOne(url+'/status?mode='+cacheMode)
      .flush(state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'));
    await task;fixture.detectChanges();
    expect(notification).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('HIT');
    expect(fixture.nativeElement.textContent).toContain('analysis-test-001');
    expect(component.tokenEntered()).toBeFalse();
    expect(component.readiness()).toBeNull();
    expect(component.preflight()).toBeNull();
    expect(component.canOfferPaidRun()).toBeFalse();
    http.expectNone(url+'/preflight');
    http.expectNone(r=>r.method==='POST');
    http.verify();fixture.destroy();
  });

  it('requires both explicit checkboxes and exactly one user-click POST when backend permits',async()=>{
    const {fixture,component,http}=setup();
    const task=component.check();
    await resolvePreflight(http,true);
    await task;
    expect(component.canOfferPaidRun()).toBeTrue();
    await component.analyze();
    http.expectNone(r=>r.method==='POST'); // consent missing
    component.transferConsent.set(true);
    await component.analyze();
    http.expectNone(r=>r.method==='POST'); // spending consent missing
    component.paidConsent.set(true);
    const input=(fixture as any).settingsTokenField as HTMLInputElement;
    const fakeKey='LLM_1234567890123456_TEST_ABCDEF1234567890';
    input.value=fakeKey;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const notification=jasmine.createSpy('cacheUpdated');
    component.cacheUpdated.subscribe(notification);
    const submit=component.analyze();
    const request=http.expectOne(url+'/manual-one-shot');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-ProofLab-Manual-Action')).toBe('sam-one-shot');
    expect(request.request.body.modelApiToken).toBe(fakeKey);
    expect(input.value).toBe('');
    expect(request.request.body.mode).toBe(cacheMode);
    expect(request.request.body.videoSha256).toBe(sha);
    expect(request.request.body.maximumEstimatedCostUsd).toBe(0.5);
    expect(request.request.body.confirmPrivateVideoTransfer).toBeTrue();
    expect(request.request.body.confirmOneBillableSamCall).toBeTrue();
    request.flush(state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'));
    await submit;fixture.detectChanges();
    expect(notification).toHaveBeenCalledTimes(1);
    expect(component.transferConsent()).toBeFalse();
    expect(component.paidConsent()).toBeFalse();
    expect(component.canOfferPaidRun()).toBeFalse();
    http.expectNone(url+'/manual-one-shot');
    expect(component.tokenEntered()).toBeFalse();
    expect(localStorage.getItem('sam-meta-token')).toBeNull();
    http.verify();fixture.destroy();
  });

  it('requires an extra explicit owner confirmation for the ONE reconciled retry',async()=>{
    const {fixture,component,http}=setup();
    const check=component.check();
    http.expectOne(url+'/status?mode='+cacheMode)
      .flush(state('PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION',false));
    await Promise.resolve();
    http.expectOne(url+'/preflight').flush(preflight);
    await Promise.resolve();
    http.expectOne('http://127.0.0.1:5391/api/meta/readiness')
      .flush(readiness(true));
    await check;fixture.detectChanges();
    expect(component.canOfferPaidRun()).toBeTrue();
    const input=(fixture as any).settingsTokenField as HTMLInputElement;
    input.value='LLM_1234567890123456_BBBBBBBBBBBBBBBBB';
    input.dispatchEvent(new Event('input'));
    component.transferConsent.set(true);
    component.paidConsent.set(true);
    await component.analyze();
    http.expectNone(url+'/manual-one-shot');
    component.retryConsent.set(true);
    const pending=component.analyze();
    const req=http.expectOne(url+'/manual-one-shot');
    expect(req.request.body.ownerApprovesOneReconciledRetry).toBeTrue();
    expect(req.request.body.previousAttemptBillingEvidence)
      .toContain('OWNER_APPROVED_ONE_MORE_SAM_ATTEMPT');
    expect(req.request.body.modelApiToken).toBe('LLM_1234567890123456_BBBBBBBBBBBBBBBBB');
    req.flush(state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'));
    await pending;
    expect(input.value).toBe('');
    http.verify();fixture.destroy();
  });
  function modeEvent(mode:string):Event {
    return {target:{value:mode}} as unknown as Event;
  }
  function enterToken(fixture:ReturnType<typeof setup>['fixture']):HTMLInputElement {
    const input=(fixture as any).settingsTokenField as HTMLInputElement;
    input.value='LLM_SYNTHETIC_TEST_KEY_1234567890';
    input.dispatchEvent(new Event('input'));
    return input;
  }
  async function resolveFresh(http:HttpTestingController,allowed=true):Promise<void> {
    http.expectOne(url+'/status?mode='+freshMode).flush({
      ...state('READY_FOR_FRESH_META_ANALYSIS',allowed),
      mode:freshMode,cacheHit:false,analysisId:null
    });
    await Promise.resolve();
    http.expectOne(url+'/preflight').flush(preflight);
    await Promise.resolve();
    http.expectOne('http://127.0.0.1:5391/api/meta/readiness').flush(readiness(allowed));
    await Promise.resolve();
  }

  it('offers exactly two primary modes, defaults to cache and links to centralized settings',()=>{
    const {fixture,component,http}=setup();
    const select=fixture.nativeElement.querySelector('select.sam-mode') as HTMLSelectElement;
    expect(Array.from(select.options).map(o=>o.value)).toEqual([freshMode,cacheMode]);
    expect(select.value).toBe(cacheMode);
    expect(component.localApiPort()).toBe(5391);
    expect(fixture.nativeElement.querySelector('details.sam-connection')).toBeNull();
    expect(fixture.nativeElement.querySelector('input[type="password"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Abrir configuración');
    fixture.componentRef.setInput('language','en');
    fixture.detectChanges();
    expect(select.options[1].textContent).toContain('API Meta + cache');
    http.verify();fixture.destroy();
  });

  it('clears consent and key when switching modes and does not start a request automatically',async()=>{
    const {fixture,component,http}=setup();
    const check=component.check();
    await resolvePreflight(http,true);
    await check;
    const input=enterToken(fixture);
    component.transferConsent.set(true);
    component.paidConsent.set(true);
    component.retryConsent.set(true);
    component.selectMode(modeEvent(freshMode));
    expect(component.mode()).toBe(freshMode);
    expect(component.cacheStatus()).toBeNull();
    expect(component.preflight()).toBeNull();
    expect(component.readiness()).toBeNull();
    expect(component.transferConsent()).toBeFalse();
    expect(component.paidConsent()).toBeFalse();
    expect(component.retryConsent()).toBeFalse();
    expect(component.tokenEntered()).toBeFalse();
    expect(input.value).toBe('');
    await component.analyze();
    http.expectNone(()=>true);
    http.verify();fixture.destroy();
  });

  it('requests one fresh analysis after explicit consent, blocks changes and ignores double clicks',async()=>{
    const {fixture,component,http}=setup();
    component.selectMode(modeEvent(freshMode));
    const check=component.check();
    await resolveFresh(http);
    await check;
    expect(component.canOfferPaidRun()).toBeTrue();
    enterToken(fixture);
    component.transferConsent.set(true);
    component.paidConsent.set(true);
    const submit=component.analyze();
    const req=http.expectOne(url+'/manual-one-shot');
    expect(req.request.body.mode).toBe(freshMode);
    component.selectMode(modeEvent(cacheMode));
    component.selectLocalPort(modeEvent('5393'));
    expect(component.mode()).toBe(freshMode);
    expect(component.localApiPort()).toBe(5391);
    await component.analyze();
    http.expectNone(r=>r.method==='POST');
    const offline={files:[new File([''],'empty.json')],value:'selected'};
    await component.onFile({target:offline} as unknown as Event);
    expect(component.sending()).toBeTrue();
    req.flush({...state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'),
      mode:freshMode,cacheHit:false,analysisId:'fresh-analysis-002'});
    await submit;fixture.detectChanges();
    expect(component.sending()).toBeFalse();
    expect(component.tokenEntered()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('Nuevo análisis SAM guardado');
    expect(fixture.nativeElement.textContent).toContain('fresh-analysis-002');
    expect(component.canOfferPaidRun()).toBeFalse();
    http.verify();fixture.destroy();
  });

  it('does not retry a failed POST or preserve its one-time consent or token',async()=>{
    const {fixture,component,http}=setup();
    const check=component.check();
    await resolvePreflight(http,true);
    await check;
    const input=enterToken(fixture);
    component.transferConsent.set(true);
    component.paidConsent.set(true);
    const submit=component.analyze();
    http.expectOne(url+'/manual-one-shot').flush({},{
      status:502,statusText:'Synthetic provider failure'
    });
    await submit;
    expect(component.sending()).toBeFalse();
    expect(component.cacheStatus()).toBeNull();
    expect(component.transferConsent()).toBeFalse();
    expect(component.paidConsent()).toBeFalse();
    expect(input.value).toBe('');
    expect(component.error()).toContain('facturada');
    await component.analyze();
    http.expectNone(()=>true);
    http.verify();fixture.destroy();
  });

  it('ignores an older check after a mode change without leaving the new check stuck',async()=>{
    const {fixture,component,http}=setup();
    const replay=jasmine.createSpy('replay');
    component.cacheUpdated.subscribe(replay);
    const oldCheck=component.check();
    const oldReq=http.expectOne(url+'/status?mode='+cacheMode);
    component.selectMode(modeEvent(freshMode));
    expect(component.checking()).toBeFalse();
    const newCheck=component.check();
    oldReq.flush(state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'));
    await oldCheck;
    expect(component.checking()).toBeTrue();
    expect(component.cacheStatus()).toBeNull();
    expect(replay).not.toHaveBeenCalled();
    await resolveFresh(http,false);
    await newCheck;
    expect(component.checking()).toBeFalse();
    expect(component.cacheStatus()?.status).toBe('READY_FOR_FRESH_META_ANALYSIS');
    expect(component.canOfferPaidRun()).toBeFalse();
    http.verify();fixture.destroy();
  });

  it('invalidates a check when the connection changes and can check the new port',async()=>{
    const {fixture,component,http}=setup();
    const oldCheck=component.check();
    const oldReq=http.expectOne(url+'/status?mode='+cacheMode);
    component.selectLocalPort(modeEvent('5393'));
    expect(component.checking()).toBeFalse();
    const newCheck=component.check();
    oldReq.flush(state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'));
    await oldCheck;
    expect(component.checking()).toBeTrue();
    http.expectOne(url.replace(':5391',':5393')+'/status?mode='+cacheMode)
      .flush(state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'));
    await newCheck;
    expect(component.checking()).toBeFalse();
    http.verify();fixture.destroy();
  });

  it('keeps offline JSON independent from an in-flight status check',async()=>{
    const {fixture,component,http}=setup();
    const check=component.check();
    const req=http.expectOne(url+'/status?mode='+cacheMode);
    const offline={files:[new File([''],'empty.json')],value:'selected'};
    await component.onFile({target:offline} as unknown as Event);
    expect(component.checking()).toBeTrue();
    req.flush(state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'));
    await check;
    expect(component.checking()).toBeFalse();
    expect(component.cacheStatus()?.cacheHit).toBeTrue();
    http.verify();fixture.destroy();
  });

  it('ignores a completed POST belonging to a previous video',async()=>{
    const {fixture,component,http}=setup();
    const check=component.check();
    await resolvePreflight(http,true);
    await check;
    enterToken(fixture);
    component.transferConsent.set(true);
    component.paidConsent.set(true);
    const replay=jasmine.createSpy('replay');
    component.cacheUpdated.subscribe(replay);
    const submit=component.analyze();
    const req=http.expectOne(url+'/manual-one-shot');
    fixture.componentRef.setInput('videoSha256','e'.repeat(64));
    fixture.detectChanges();
    req.flush(state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'));
    await submit;
    expect(component.sending()).toBeFalse();
    expect(component.cacheStatus()).toBeNull();
    expect(component.tokenEntered()).toBeFalse();
    expect(replay).not.toHaveBeenCalled();
    http.verify();fixture.destroy();
  });

  it('requires manual review for a failed new analysis without offering the legacy retry',async()=>{
    const {fixture,component,http}=setup();
    component.selectMode(modeEvent(freshMode));
    const check=component.check();
    http.expectOne(url+'/status?mode='+freshMode).flush({
      ...state('NEW_ANALYSIS_REQUIRES_MANUAL_RECONCILIATION',false),
      mode:freshMode,cacheHit:false
    });
    await Promise.resolve();
    http.expectOne(url+'/preflight').flush(preflight);
    await Promise.resolve();
    http.expectOne('http://127.0.0.1:5391/api/meta/readiness').flush(readiness(true));
    await check;fixture.detectChanges();
    expect(component.canOfferPaidRun()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('Se requiere revisión manual');
    expect(fixture.nativeElement.querySelector('.sam-consent')).toBeNull();
    enterToken(fixture);
    component.transferConsent.set(true);
    component.paidConsent.set(true);
    component.retryConsent.set(true);
    await component.analyze();
    http.expectNone(r=>r.method==='POST');
    fixture.componentRef.setInput('language','en');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Manual review is required');
    http.verify();fixture.destroy();
  });

  it('refuses a cached status belonging to a different video',async()=>{
    const {fixture,component,http}=setup();
    const replay=jasmine.createSpy('replay');
    component.cacheUpdated.subscribe(replay);
    const check=component.check();
    http.expectOne(url+'/status?mode='+cacheMode).flush({
      ...state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'),
      sourceSha256:'e'.repeat(64)
    });
    await check;
    expect(component.cacheStatus()).toBeNull();
    expect(component.checking()).toBeFalse();
    expect(component.error()).not.toBe('');
    expect(replay).not.toHaveBeenCalled();
    http.expectNone(r=>r.method==='POST');
    http.verify();fixture.destroy();
  });
});