import { apiOrigin } from './api-origin';
import {Component,OnChanges,OnDestroy,SimpleChanges,inject,input,output,signal} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {firstValueFrom} from 'rxjs';
import {LabConnection} from './lab-connection';
import {LabActivity} from './lab-activity';
import {KinematicRevision} from './pendulum-kinematics';

export interface KnotEvidence {
  cacheKey:string;videoSha256:string;model:string;protocol:string;
  frames:{index:number;timeSeconds:number;width:number;height:number;sha256:string}[];
  proposal:{status:string;x:number|null;y:number|null;spreadPixels:number};
}
interface KnotQuote {
  cacheKey:string;videoSha256:string;maximumEstimatedCostUsd:number;
  cacheHit:boolean;priorAttempt:boolean;canRun:boolean;evidence:KnotEvidence|null;
}

@Component({
  selector:'app-knot-review',standalone:true,imports:[LabActivity],
  template:`
    <button type="button" class="knot-open" (click)="open()" [disabled]="connection.busy()">
      {{es?'Localizar nudo con Meta':'Locate knot with Meta'}}
    </button>
    @if(expanded()) {
      <section class="knot-panel" [attr.aria-busy]="busy()">
        <header><strong>{{es?'Punto de suspensión · Meta':'Suspension point · Meta'}}</strong>
          <button type="button" (click)="expanded.set(false)" [disabled]="busy()">{{es?'Cerrar':'Close'}}</button>
        </header>
        @if(busy()) {
          <app-lab-activity [label]="running()?(es?'Localizando el nudo…':'Locating the knot…'):(es?'Preparando revisión…':'Preparing review…')"
            [detail]="running()?(es?'Tres fotogramas del original · una consulta · sin reintentos automáticos':'Three original frames · one inference · no automatic retries'):''"/>
        }
        @if(error()) {<p class="warning" role="alert">{{error()}}</p>}
        @if(quote();as q) {
          @if(!evidence() && !busy()) {
            <p>{{es?'Muse Spark buscará dónde sale la cuerda del nudo en tres fotogramas del original.':'Muse Spark will locate where the string exits the knot in three original frames.'}}</p>
            <p class="budget"><strong>US$ {{q.maximumEstimatedCostUsd.toFixed(2)}}</strong>
              {{es?'presupuesto máximo estimado · no es una factura':'maximum estimated budget · not an invoice'}}</p>
            <p class="muted">{{es?'El servidor comprueba el tamaño de la solicitud antes de la inferencia. El cobro real depende de los tokens usados.':'The server checks request size before inference. Actual billing depends on token usage.'}}</p>
            @if(q.priorAttempt) {
              <p class="warning">{{es?'Hay un intento pendiente de revisión. No se enviará otra consulta automáticamente.':'A prior attempt needs review. No automatic repeat request will be sent.'}}</p>
            } @else {
              <label class="consent"><input type="checkbox" [checked]="consent()" (change)="consent.set($any($event.target).checked)"/>
                {{es?'Autorizo enviar estas tres imágenes a Meta y usar hasta este presupuesto estimado.':'I authorize sending these three images to Meta and using this estimated budget.'}}</label>
              <button type="button" class="primary" (click)="locate()" [disabled]="!canLocate()">{{es?'Detectar nudo':'Detect knot'}}</button>
              @if(!connection.hasToken()) {<button type="button" (click)="connection.openSettings()">{{es?'Configurar token':'Configure key'}}</button>}
              @if(!q.canRun) {<p class="warning">{{es?'El servidor no tiene cupo o autorización disponible.':'Server allowance or authorization is unavailable.'}}</p>}
            }
          }
        }
        @if(evidence();as e) {
          <p class="muted">{{es?'Propuesta visual de Meta · guardada en caché · no verificada científicamente':'Meta visual proposal · cached · not scientifically verified'}}</p>
          @if(candidate()) {
            <p>{{es?'Comprueba la cruz en los tres fotogramas. Haz clic para ajustar el punto exacto donde sale la cuerda.':'Check the cross in all three frames. Click to adjust the exact point where the string exits.'}}</p>
            <div class="frames">
              @for(frame of e.frames;track $index;let i=$index) {
                <figure>
                  <svg [attr.viewBox]="viewBox(i)" role="img" tabindex="0"
                    [attr.aria-label]="es?'Revisar nudo; ajuste fino con las flechas del teclado':'Review knot; fine adjustment with keyboard arrows'"
                    (click)="pick($event,i)" (keydown)="nudge($event,i)">
                    <image [attr.href]="frameUrl(i)" [attr.width]="frame.width" [attr.height]="frame.height"
                      (load)="imageReady(i)" (error)="imageFailed()"/>
                    <path [attr.d]="cross(i)" stroke="#ffdc79" fill="none" stroke-width="2" vector-effect="non-scaling-stroke"/>
                  </svg>
                  <figcaption>{{frame.timeSeconds.toFixed(2)}} s</figcaption>
                </figure>
              }
            </div>
            <label class="consent"><input type="checkbox" [checked]="reviewed()" (change)="reviewed.set($any($event.target).checked)"/>
              {{es?'He revisado que la cruz marca la salida de la cuerda.':'I checked that the cross marks the string exit.'}}</label>
            <button type="button" class="primary" (click)="confirm()" [disabled]="busy() || !reviewed() || loadedImages().length!==3">{{es?'Usar este punto':'Use this point'}}</button>
          } @else {
            <p class="warning">{{es?'Meta no encontró un punto suficientemente claro y estable. Conservamos el pivote actual.':'Meta did not find a sufficiently clear, stable point. The current pivot is preserved.'}}</p>
          }
          <button type="button" (click)="manual.emit();expanded.set(false)" [disabled]="busy()">{{es?'Marcar manualmente en el video':'Mark manually on video'}}</button>
        }
      </section>
    }
  `,
  styles:[`
    :host{display:block;margin-top:10px;font-size:12px;color:#c9d9e9}
    button{padding:7px 11px;border:1px solid #43566a;border-radius:7px;background:#202d3b;color:#d7eaff;cursor:pointer;font:inherit;margin:3px}
    button:disabled{opacity:.5;cursor:not-allowed}.primary{background:#276aa0;border-color:#428ac0;font-weight:650}
    .knot-panel{margin-top:9px;padding:16px;border:1px solid #425568;border-radius:12px;background:#19242f;max-width:1100px}
    header{display:flex;align-items:center;justify-content:space-between;gap:15px}p{line-height:1.5}.muted{color:#9ab1c7;font-size:11px}
    .budget strong{font-size:20px;margin-right:7px}.warning{color:#ffd085}
    .consent{display:flex;gap:8px;align-items:flex-start;margin:14px 0;line-height:1.5}
    .frames{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}
    figure{margin:0;min-width:0;background:#0d151e;border:1px solid #43566a;border-radius:9px;overflow:hidden}
    svg{display:block;width:100%;aspect-ratio:1;cursor:crosshair;touch-action:manipulation}figcaption{padding:6px 10px;color:#abc3d8}
    @media(max-width:620px){.frames{grid-template-columns:1fr}.knot-panel{padding:10px}}
  `]
})
export class KnotReview implements OnChanges,OnDestroy {
  private readonly http=inject(HttpClient);
  readonly connection=inject(LabConnection);
  readonly studyId=input.required<string>();readonly role=input.required<string>();
  readonly sha=input.required<string>();readonly port=input(5391);readonly language=input<'es'|'en'>('es');
  readonly confirmed=output<KinematicRevision>();readonly manual=output<void>();
  readonly expanded=signal(false);readonly busy=signal(false);readonly running=signal(false);
  readonly quote=signal<KnotQuote|null>(null);readonly evidence=signal<KnotEvidence|null>(null);
  readonly consent=signal(false);readonly reviewed=signal(false);readonly error=signal('');
  readonly point=signal<{x:number;y:number}|null>(null);
  readonly loadedImages=signal<number[]>([]);
  imageReady(i:number):void{this.loadedImages.update(v=>v.includes(i)?v:[...v,i]);}
  imageFailed():void{this.loadedImages.set([]);this.reviewed.set(false);this.error.set(this.es?'No se pudo cargar una imagen de revisión.':'A review image could not be loaded.');}
  private serial=0;private disposed=false;
  get es():boolean{return this.language()==='es';}
  private url():string{return apiOrigin(this.port())+'/api/science/director/pendulum/sessions/'+this.studyId()+'/knot/'+this.role();}
  ngOnChanges(changes:SimpleChanges):void{
    if(!changes['studyId'] && !changes['role'] && !changes['sha'] && !changes['port'])return;
    this.serial++;this.quote.set(null);this.evidence.set(null);this.point.set(null);
    this.consent.set(false);this.reviewed.set(false);this.expanded.set(false);this.loadedImages.set([]);
    if(!this.running())this.busy.set(false);
  }
  ngOnDestroy():void{this.disposed=true;this.serial++;}
  private matches(e:KnotEvidence):boolean {
    return e.videoSha256===this.sha() && e.model==='muse-spark-1.3' &&
      e.protocol==='ROPE_EXIT_3_NATIVE_FRAMES_V1' && e.frames.length===3;
  }
  private show(e:KnotEvidence):void {
    if(!this.matches(e))throw new Error('KNOT_VIDEO_MISMATCH');
    if(this.evidence()?.cacheKey!==e.cacheKey)this.loadedImages.set([]);
    this.evidence.set(e);
    const p=e.proposal;
    this.point.set(p.status==='CANDIDATE_REQUIRES_HUMAN_CONFIRMATION' &&
      p.x!==null && p.y!==null && Number.isFinite(p.x) && Number.isFinite(p.y)?{x:p.x,y:p.y}:null);
    this.reviewed.set(false);
  }
  candidate():boolean{return this.point()!==null;}
  async open():Promise<void>{
    if(this.busy() || this.connection.busy())return;
    this.expanded.set(true);this.error.set('');this.busy.set(true);
    const serial=++this.serial;
    try{
      const q=await firstValueFrom(this.http.get<KnotQuote>(this.url()+'/preflight'));
      if(serial!==this.serial || this.disposed)return;
      if(q.videoSha256!==this.sha() || q.maximumEstimatedCostUsd!==0.05)throw new Error('INVALID_QUOTE');
      this.quote.set(q);if(q.evidence)this.show(q.evidence);
    }catch{if(serial===this.serial)this.error.set(this.es?'No se pudo preparar la detección del nudo.':'Could not prepare knot detection.');}
    finally{if(serial===this.serial)this.busy.set(false);}
  }
  canLocate():boolean {
    const q=this.quote();
    return !!q && q.videoSha256===this.sha() && q.canRun && !q.priorAttempt &&
      !q.cacheHit && this.consent() && this.connection.hasToken() &&
      !this.busy() && !this.connection.busy();
  }
  async locate():Promise<void>{
    if(!this.canLocate())return;
    const key=this.connection.tokenForManualRequest();if(!key)return;
    const serial=++this.serial;this.busy.set(true);this.running.set(true);
    this.connection.busy.set(true);this.error.set('');
    try {
      const e=await firstValueFrom(this.http.post<KnotEvidence>(this.url()+'/locate',{
        videoSha256:this.sha(),cacheKey:this.quote()!.cacheKey,modelApiToken:key,
        confirmImageTransfer:true,confirmPaidCall:true,maximumEstimatedCostUsd:0.05
      },{headers:{'X-ProofLab-Manual-Action':'knot'}}));
      if(serial===this.serial && !this.disposed)this.show(e);
    }catch{if(serial===this.serial){this.quote.set(null);this.error.set(this.es?'La consulta no se completó. Puede haber consumo de créditos; vuelve a abrir para revisar el estado.':'The request did not complete. Credits may have been used; reopen to inspect status.');}}
    finally{this.connection.busy.set(false);this.running.set(false);this.busy.set(false);this.consent.set(false);}
  }
  frameUrl(i:number):string{return this.url()+'/frame/'+i+'?key='+this.evidence()!.cacheKey;}
  private crop(i:number):{x:number;y:number;size:number} {
    const e=this.evidence()!,f=e.frames[i],p=e.proposal;
    const size=Math.min(f.width,f.height)*.35;
    return {x:Math.max(0,Math.min(f.width-size,p.x!*f.width/1000-size/2)),
      y:Math.max(0,Math.min(f.height-size,p.y!*f.height/1000-size/2)),size};
  }
  viewBox(i:number):string {const c=this.crop(i);return [c.x,c.y,c.size,c.size].join(' ');}
  cross(i:number):string {
    const f=this.evidence()!.frames[i],p=this.point()!,c=this.crop(i);
    const x=p.x*f.width/1000,y=p.y*f.height/1000,r=c.size*.025;
    return 'M '+(x-r)+' '+y+' H '+(x+r)+' M '+x+' '+(y-r)+' V '+(y+r);
  }
  pick(event:MouseEvent,i:number):void {
    if(this.busy())return;
    const rect=(event.currentTarget as SVGElement).getBoundingClientRect(),c=this.crop(i),f=this.evidence()!.frames[i];
    if(rect.width<=0 || rect.height<=0)return;
    this.point.set({x:(c.x+(event.clientX-rect.left)/rect.width*c.size)*1000/f.width,
      y:(c.y+(event.clientY-rect.top)/rect.height*c.size)*1000/f.height});this.reviewed.set(false);
  }
  nudge(event:KeyboardEvent,i:number):void {
    const dirs:Record<string,[number,number]>={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]};
    const d=dirs[event.key],p=this.point(),f=this.evidence()?.frames[i];if(!d || !p || !f || this.busy())return;
    event.preventDefault();
    this.point.set({x:Math.max(0,Math.min(999.999,p.x+d[0]*1000/f.width)),
      y:Math.max(0,Math.min(999.999,p.y+d[1]*1000/f.height))});this.reviewed.set(false);
  }
  async confirm():Promise<void> {
    if(!this.reviewed() || !this.point() || !this.evidence() || this.busy() || this.loadedImages().length!==3)return;
    const serial=++this.serial;this.busy.set(true);this.error.set('');
    try {
      const revision=await firstValueFrom(this.http.post<KinematicRevision>(this.url()+'/confirm',{
        videoSha256:this.sha(),cacheKey:this.evidence()!.cacheKey,confirmRopeExit:true,...this.point()
      },{headers:{'X-ProofLab-Manual-Action':'knot'}}));
      if(serial===this.serial && !this.disposed){this.confirmed.emit(revision);this.expanded.set(false);}
    }catch{if(serial===this.serial)this.error.set(this.es?'No se pudo guardar el punto revisado.':'Could not save the reviewed point.');}
    finally{if(serial===this.serial)this.busy.set(false);}
  }
}