import { DecimalPipe } from '@angular/common';
import { Component, input } from '@angular/core';

export interface UploadPhaseRow {
  key:string;
  label:string;
  status:string;
  elapsedMs:number|null;
}

@Component({
  selector:'app-director-upload-progress',
  standalone:true,
  imports:[DecimalPipe],
  styleUrl:'./director-upload-progress.scss',
  template:`
    <details class="upload-progress" [open]="loading()">
      <summary><strong role="status">{{label()}}</strong><span>{{es()?'Etapas de carga':'Upload stages'}}</span></summary>
      @if(percent()!==null){
        <div class="upload-transfer"><span>{{es()?'Archivo enviado':'File sent'}} · {{percent()! | number:'1.0-0'}}%</span>
          <progress max="100" [value]="percent()!" [attr.aria-label]="es()?'Archivo enviado':'File sent'"></progress>
        </div>
      }
      <ol class="upload-stage-list">
        @for(row of rows(); track row.key){
          <li [class.upload-stage-active]="row.status==='RUNNING' && (row.key==='playback' || !serverComplete() && sourcePending())"
              [class.upload-stage-failed]="row.status==='FAILED' || row.status==='CANCELLED'">
            <span>{{row.label}}</span><small>{{stateLabel(row.status)}}@if(row.elapsedMs!==null){ · {{row.elapsedMs!/1000 | number:'1.1-1'}} s}</small>
          </li>
        }
      </ol>
      @if(unavailable()){<p class="upload-note" role="status">{{es()?'No se pudieron actualizar todas las etapas de esta carga.':'Some stages of this upload could not be refreshed.'}}</p>}
    </details>
  `
})
export class DirectorUploadProgress {
  readonly es=input(true);
  readonly loading=input(false);
  readonly label=input('');
  readonly percent=input<number|null>(null);
  readonly rows=input<readonly UploadPhaseRow[]>([]);
  readonly serverComplete=input(false);
  readonly sourcePending=input(false);
  readonly unavailable=input(false);
  stateLabel(state:string):string {
    const labels:Record<string,[string,string]>={
      PENDING:['Pendiente','Pending'],RUNNING:['En curso','In progress'],
      COMPLETED:['Listo','Ready'],FAILED:['Falló','Failed'],CANCELLED:['Cancelado','Cancelled']
    };
    return labels[state]?.[this.es()?0:1]??(this.es()?'Sin confirmar':'Unconfirmed');
  }
}
