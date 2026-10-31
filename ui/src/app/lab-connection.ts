import { Injectable, signal } from '@angular/core';
import { Subject } from 'rxjs';

export type LocalApiPort = 5391 | 5393 | 5398 | 5410;

/** A single password field owns the secret. Never copy it to app state or storage. */
@Injectable({providedIn:'root'})
export class LabConnection {
  private tokenField?: HTMLInputElement;
  private readonly selectedPort=signal<LocalApiPort>(5391);
  readonly port=this.selectedPort.asReadonly();
  readonly hasToken=signal(false);
  readonly busy=signal(false);
  readonly changed=new Subject<void>();
  readonly settingsRequested=new Subject<void>();

  register(field:HTMLInputElement):void {
    if(this.tokenField && this.tokenField!==field)this.clearToken();
    this.tokenField=field;
  }
  unregister(field:HTMLInputElement):void {
    if(this.tokenField!==field)return;
    this.clearToken();this.tokenField=undefined;
  }
  invalidateToken():void { this.hasToken.set(false); }
  confirmToken():boolean {
    if(this.busy())return false;
    const value=this.tokenField?.value.trim() ?? '';
    const valid=this.validToken(value);
    this.hasToken.set(valid);
    return valid;
  }
  /** Move a modal draft to the session's password field; never persist the secret. */
  confirmTokenFrom(draft:HTMLInputElement):boolean {
    if(this.busy() || !this.tokenField)return false;
    const value=draft.value.trim();
    if(!this.validToken(value))return false;
    this.tokenField.value=value;
    this.hasToken.set(true);
    if(draft!==this.tokenField)draft.value='';
    return true;
  }
  private validToken(value:string):boolean {
    return value.startsWith('LLM_') && value.length>=24 &&
      value.length<=1024 && !/\s/.test(value);
  }
  clearToken():void {
    if(this.tokenField)this.tokenField.value='';
    this.hasToken.set(false);
  }
  /** Retained only in the registered password field, never persisted. */
  tokenForManualRequest():string {
    if(!this.hasToken() || this.busy())return '';
    return this.tokenField?.value.trim() ?? '';
  }
  setPort(value:number):boolean {
    if(this.busy() || (value!==5391 && value!==5393 && value!==5398 && value!==5410))return false;
    if(value===this.port())return true;
    this.clearToken();this.selectedPort.set(value);this.changed.next();
    return true;
  }
  openSettings():void { this.settingsRequested.next(); }
}