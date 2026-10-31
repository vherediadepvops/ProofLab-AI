import { apiOrigin, isLocalWorkspace } from './api-origin';
import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild, inject, input, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LabConnection } from './lab-connection';

interface ConnectionStatus {
  apiKeyConfigured:boolean; liveCallsPermitted:boolean; manualTokenCallsPermitted?:boolean;
  creditsConfirmedByOwner:boolean; ownerApprovedPaidRequests:boolean;
}

@Component({
  selector:'app-workspace-settings',
  standalone:true,
  templateUrl:'./workspace-settings.html',
  styleUrl:'./workspace-settings.scss'
})
export class WorkspaceSettings implements AfterViewInit, OnDestroy {
  readonly localWorkspace=isLocalWorkspace();
  get apiAddress():string {return apiOrigin(this.connection.port());}
  readonly connection=inject(LabConnection);
  private readonly http=inject(HttpClient);
  readonly language=input<'es'|'en'>('es');
  @ViewChild('tokenField',{static:true}) private tokenField!:ElementRef<HTMLInputElement>;
  readonly invalid=signal(false);
  readonly checking=signal(false);
  readonly status=signal<ConnectionStatus|null>(null);
  readonly unavailable=signal(false);
  private serial=0;
  get es():boolean{return this.language()==='es';}
  constructor(){
    this.connection.changed.pipe(takeUntilDestroyed()).subscribe(()=>{
      this.serial++;this.status.set(null);this.unavailable.set(false);
      this.checking.set(false);this.invalid.set(false);
    });
  }
  ngAfterViewInit():void {this.connection.register(this.tokenField.nativeElement);}
  ngOnDestroy():void {this.connection.unregister(this.tokenField.nativeElement);this.serial++;}
  inputChanged():void {this.connection.invalidateToken();this.invalid.set(false);}
  applyToken():void {this.invalid.set(!this.connection.confirmToken());}
  clearToken():void {if(!this.connection.busy()){this.connection.clearToken();this.invalid.set(false);}}
  changePort(event:Event):void {this.connection.setPort(Number((event.target as HTMLSelectElement).value));}
  async checkConnection():Promise<void>{
    if(this.checking() || this.connection.busy())return;
    const serial=++this.serial;
    this.checking.set(true);this.unavailable.set(false);this.status.set(null);
    try {
      const status=await firstValueFrom(this.http.get<ConnectionStatus>(
        apiOrigin(this.connection.port())+'/api/meta/readiness'));
      if(serial===this.serial)this.status.set(status);
    }catch{if(serial===this.serial)this.unavailable.set(true);}
    finally{if(serial===this.serial)this.checking.set(false);}
  }
}