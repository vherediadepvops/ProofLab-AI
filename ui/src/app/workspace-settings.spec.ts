import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WorkspaceSettings } from './workspace-settings';
import { LabConnection } from './lab-connection';

describe('Workspace settings and session credentials',()=>{
  beforeEach(async()=>{
    await TestBed.configureTestingModule({imports:[WorkspaceSettings],
      providers:[provideHttpClient(),provideHttpClientTesting()]}).compileComponents();
  });
  function setup(){
    const fixture=TestBed.createComponent(WorkspaceSettings);fixture.detectChanges();
    return {fixture,component:fixture.componentInstance,
      connection:TestBed.inject(LabConnection),http:TestBed.inject(HttpTestingController),
      input:fixture.nativeElement.querySelector('#settings-meta-token') as HTMLInputElement};
  }
  const fake='LLM_LOCAL_TEST_ONLY_12345678901234567890';
  it('configures only a password field and never sends or persists the key',()=>{
    const {fixture,component,connection,http,input}=setup();
    const local=spyOn(localStorage,'setItem'),session=spyOn(sessionStorage,'setItem');
    input.value=fake;input.dispatchEvent(new Event('input'));component.applyToken();fixture.detectChanges();
    expect(connection.hasToken()).toBeTrue();expect(input.type).toBe('password');
    expect(fixture.nativeElement.textContent).not.toContain(fake);
    expect(local).not.toHaveBeenCalled();expect(session).not.toHaveBeenCalled();
    http.expectNone(()=>true);
    expect(connection.tokenForManualRequest()).toBe(fake);expect(input.value).toBe(fake);
    component.clearToken();expect(connection.tokenForManualRequest()).toBe('');expect(connection.hasToken()).toBeFalse();
    http.verify();fixture.destroy();
  });
  it('clears a configured key on port change and rejects unsupported ports',()=>{
    const {fixture,component,connection,input,http}=setup();
    input.value=fake;component.applyToken();
    expect(connection.setPort(8080)).toBeFalse();expect(connection.hasToken()).toBeTrue();
    expect(connection.setPort(5393)).toBeTrue();expect(input.value).toBe('');
    expect(connection.hasToken()).toBeFalse();expect(connection.port()).toBe(5393);
    http.verify();fixture.destroy();
  });
  it('checks connection using GET without transmitting or consuming the key',async()=>{
    const {fixture,component,connection,input,http}=setup();
    input.value=fake;component.applyToken();
    const check=component.checkConnection();
    const req=http.expectOne('http://127.0.0.1:5391/api/meta/readiness');
    expect(req.request.method).toBe('GET');expect(req.request.body).toBeNull();
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({apiKeyConfigured:false,liveCallsPermitted:false});
    await check;expect(connection.hasToken()).toBeTrue();expect(input.value).toBe(fake);
    http.expectNone(r=>r.method==='POST');http.verify();fixture.destroy();
  });
  it('ignores status returned from the previously selected connection',async()=>{
    const {fixture,component,connection,http}=setup();
    const check=component.checkConnection();
    const req=http.expectOne('http://127.0.0.1:5391/api/meta/readiness');
    connection.setPort(5393);req.flush({apiKeyConfigured:true,liveCallsPermitted:true});
    await check;expect(component.status()).toBeNull();expect(component.checking()).toBeFalse();
    http.verify();fixture.destroy();
  });
  it('locks credential and server controls while a manual request is in flight',()=>{
    const {fixture,component,connection,input,http}=setup();
    input.value=fake;component.applyToken();connection.busy.set(true);fixture.detectChanges();
    expect(input.disabled).toBeTrue();expect(connection.setPort(5393)).toBeFalse();
    expect(connection.tokenForManualRequest()).toBe('');component.clearToken();expect(input.value).toBe(fake);
    connection.busy.set(false);fixture.destroy();expect(input.value).toBe('');
    http.verify();
  });
  it('rejects invalid keys and clears secrets when the component is destroyed',()=>{
    const {fixture,component,connection,input,http}=setup();
    input.value='invalid';component.applyToken();expect(component.invalid()).toBeTrue();
    expect(connection.hasToken()).toBeFalse();input.value=fake;component.applyToken();
    fixture.destroy();expect(input.value).toBe('');expect(connection.hasToken()).toBeFalse();
    http.verify();
  });
  it('retains a registered key for manual requests until cleared, without persisting it',()=>{
    const {fixture,component,connection,http,input}=setup();
    const local=spyOn(localStorage,'setItem'),session=spyOn(sessionStorage,'setItem');
    input.value=fake;component.applyToken();
    expect(connection.tokenForManualRequest()).toBe(fake);
    expect(connection.tokenForManualRequest()).toBe(fake);
    expect(connection.hasToken()).toBeTrue();expect(input.value).toBe(fake);
    connection.busy.set(true);expect(connection.tokenForManualRequest()).toBe('');
    connection.busy.set(false);component.clearToken();
    expect(connection.hasToken()).toBeFalse();expect(input.value).toBe('');
    expect(local).not.toHaveBeenCalled();expect(session).not.toHaveBeenCalled();
    http.expectNone(()=>true);fixture.destroy();
  });
});
