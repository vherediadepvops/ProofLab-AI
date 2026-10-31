import {TestBed} from '@angular/core/testing';
import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController,provideHttpClientTesting} from '@angular/common/http/testing';
import {SamVideoReplay} from './sam-video-replay';
import {LabConnection} from './lab-connection';

const sha='a'.repeat(64),id='b'.repeat(32);
const fake='LLM_SYNTHETIC_LOCAL_TEST_KEY_1234567890';

describe('Inline session token dialog',()=>{
  beforeEach(async()=>{
    await TestBed.configureTestingModule({imports:[SamVideoReplay],
      providers:[provideHttpClient(),provideHttpClientTesting()]}).compileComponents();
  });
  afterEach(()=>TestBed.inject(HttpTestingController).verify());
  function setup(){
    const f=TestBed.createComponent(SamVideoReplay);
    f.componentRef.setInput('autoCheck',false);f.componentRef.setInput('guided',true);
    f.componentRef.setInput('studyId',id);f.componentRef.setInput('videoSha256',sha);
    f.detectChanges();
    const c=f.componentInstance,con=TestBed.inject(LabConnection);
    const field=document.createElement('input');field.type='password';con.register(field);
    c.preflight.set({sourceSha256:sha,sourceFrames:1938,durationSeconds:32.33,
      estimatedCostUsd:.39,supportsSingleCall:true,codec:'hevc',timingSource:'test',framePtsSha256:''});
    c.adaptiveQuote.set({sourceSha256:sha,existingSparkSceneVerified:true,
      requiresNewSparkCall:false,canRunWithManualToken:true,parts:3,simultaneousRequests:3,
      totalSamEstimatedUsd:.39} as any);
    c.readiness.set({manualTokenCallsPermitted:true,paidRequestsSwitchEnabled:true,
      creditsConfirmedByOwner:true,ownerApprovedPaidRequests:true,manualRequestsUnlimited:true} as any);
    f.detectChanges();
    const host=f.nativeElement as HTMLElement;
    return {f,c,con,field,host,dialog:host.querySelector('dialog')!,
      draft:host.querySelector<HTMLInputElement>('#sam-token-draft')!,
      submit:host.querySelector<HTMLButtonElement>('.sam-submit')!};
  }
  function open(x:ReturnType<typeof setup>){
    x.host.querySelector<HTMLButtonElement>('.sam-token-needed')!.click();x.f.detectChanges();
  }
  function accept(x:ReturnType<typeof setup>){
    x.dialog.querySelector('form')!.dispatchEvent(new Event('submit',{cancelable:true}));x.f.detectChanges();
  }
  it('opens the password dialog from the missing-token notice and registers without starting analysis',()=>{
    const x=setup(),process=spyOn(x.c,'process'),local=spyOn(localStorage,'setItem'),session=spyOn(sessionStorage,'setItem');
    expect(x.submit.disabled).toBeTrue();open(x);
    expect(x.dialog.open).toBeTrue();expect(document.activeElement).toBe(x.draft);
    expect(x.draft.type).toBe('password');x.draft.value=fake;accept(x);
    expect(x.dialog.open).toBeFalse();expect(x.draft.value).toBe('');
    expect(x.field.value).toBe(fake);expect(x.con.hasToken()).toBeTrue();
    expect(x.submit.disabled).toBeFalse();expect(x.host.querySelector('.sam-token-needed')).toBeNull();
    expect(x.host.textContent).not.toContain(fake);expect(process).not.toHaveBeenCalled();
    expect(local).not.toHaveBeenCalled();expect(session).not.toHaveBeenCalled();
    TestBed.inject(HttpTestingController).expectNone(()=>true);
  });
  it('keeps an invalid draft in the dialog without registering or enabling processing',()=>{
    const x=setup();open(x);x.draft.value='not-a-valid-key';accept(x);
    expect(x.dialog.open).toBeTrue();expect(x.con.hasToken()).toBeFalse();
    expect(x.submit.disabled).toBeTrue();expect(x.draft.getAttribute('aria-invalid')).toBe('true');
    expect(x.host.querySelector('[role="alert"]')?.textContent).toContain('LLM_');
  });
  it('clears drafts on Cancel and Escape without losing an existing session token',()=>{
    const x=setup();open(x);x.draft.value=fake;x.c.closeTokenDialog();x.f.detectChanges();
    expect(x.draft.value).toBe('');expect(x.con.hasToken()).toBeFalse();
    x.field.value=fake;x.con.confirmToken();x.c.openTokenDialog();x.draft.value='discarded';
    const cancel=new Event('cancel',{cancelable:true});x.dialog.dispatchEvent(cancel);x.f.detectChanges();
    expect(cancel.defaultPrevented).toBeTrue();expect(x.dialog.open).toBeFalse();
    expect(x.draft.value).toBe('');expect(x.field.value).toBe(fake);expect(x.con.hasToken()).toBeTrue();
  });
  it('does not enable processing before the quote, media and server are ready',()=>{
    const x=setup();open(x);x.draft.value=fake;x.c.checking.set(true);accept(x);
    expect(x.submit.disabled).toBeTrue();x.c.checking.set(false);
    x.f.componentRef.setInput('mediaReady',false);x.f.detectChanges();expect(x.submit.disabled).toBeTrue();
    x.f.componentRef.setInput('mediaReady',true);x.f.detectChanges();
    x.c.readiness.update(r=>({...r!,manualTokenCallsPermitted:false}));x.f.detectChanges();
    expect(x.submit.disabled).toBeTrue();
    x.c.readiness.update(r=>({...r!,manualTokenCallsPermitted:true}));x.f.detectChanges();
    expect(x.submit.disabled).toBeFalse();
  });
  it('closes and clears the draft when the connection changes or component is destroyed',()=>{
    const x=setup();open(x);x.draft.value=fake;x.con.setPort(5393);x.f.detectChanges();
    expect(x.dialog.open).toBeFalse();expect(x.draft.value).toBe('');
    x.c.openTokenDialog();x.draft.value=fake;x.f.destroy();expect(x.draft.value).toBe('');
  });
  it('offers the same flow in English',()=>{
    const x=setup();x.f.componentRef.setInput('language','en');x.f.detectChanges();
    expect(x.host.querySelector('.sam-token-needed')?.textContent).toContain('Meta token missing');
    open(x);expect(x.dialog.querySelector('h2')?.textContent).toBe('Configure Meta token');
    expect(x.dialog.querySelector('[type="submit"]')?.textContent).toBe('Accept');
  });
});
