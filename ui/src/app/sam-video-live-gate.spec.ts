import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SamVideoReplay } from './sam-video-replay';

const sha='a'.repeat(64);
const id='b'.repeat(32);
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
    return {fixture,component:fixture.componentInstance,http};
  }
  const state=(status='NOT_ANALYZED_WITH_SAM',allowed=false)=>({
    status,sourceSha256:sha,cacheKey:'c'.repeat(64),
    sourceFrames:0,estimatedCostUsd:0,canRunLive:allowed,
    provenance:'NO_SAM_PROVIDER_EVIDENCE'
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
    http.expectOne(url+'/status').flush(state('NOT_ANALYZED_WITH_SAM',ready));
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
    http.expectNone(url+'/analyze');
    http.verify();fixture.destroy();
  });

  it('a cache hit emits replay without checking billing or issuing a POST',async()=>{
    const {fixture,component,http}=setup();
    const notification=jasmine.createSpy('cacheUpdated');
    component.cacheUpdated.subscribe(notification);
    const task=component.check();
    http.expectOne(url+'/status')
      .flush(state('CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW'));
    await task;fixture.detectChanges();
    expect(notification).toHaveBeenCalledTimes(1);
    expect(component.preflight()).toBeNull();
    expect(component.canOfferPaidRun()).toBeFalse();
    http.expectNone(url+'/preflight');
    http.expectNone(url+'/analyze');
    http.verify();fixture.destroy();
  });

  it('requires both explicit checkboxes and exactly one user-click POST when backend permits',async()=>{
    const {fixture,component,http}=setup();
    const task=component.check();
    await resolvePreflight(http,true);
    await task;
    expect(component.canOfferPaidRun()).toBeTrue();
    await component.analyze();
    http.expectNone(url+'/analyze'); // consent missing
    component.transferConsent.set(true);
    await component.analyze();
    http.expectNone(url+'/analyze'); // spending consent missing
    component.paidConsent.set(true);
    const input=fixture.nativeElement.querySelector('#sam-meta-token') as HTMLInputElement;
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
    http.expectOne(url+'/status')
      .flush(state('PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION',false));
    await Promise.resolve();
    http.expectOne(url+'/preflight').flush(preflight);
    await Promise.resolve();
    http.expectOne('http://127.0.0.1:5391/api/meta/readiness')
      .flush(readiness(true));
    await check;fixture.detectChanges();
    expect(component.canOfferPaidRun()).toBeTrue();
    const input=fixture.nativeElement.querySelector('#sam-meta-token') as HTMLInputElement;
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
});
