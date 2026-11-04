import {Component,input,output,signal,OnChanges,SimpleChanges} from '@angular/core';

export interface SamInitialReference {
  sceneRunId:string;videoSha256:string;sceneKey:string;frame0Sha256:string;
  width:number;height:number;initialFrameUrl:string;clearFrameUrl?:string|null;clearFrameIndex?:number;
  suggestedPoint?:{frame:number;x:number;y:number}|null;confirmationId?:string|null;
}
export interface SamBodyPoint {x:number;y:number;}

@Component({selector:'app-sam-initial-reference-review',standalone:true,template:`
  <section class="initial-reference" aria-labelledby="initial-reference-title">
    <h3 id="initial-reference-title">{{es?'Marca el objeto antes de continuar':'Mark the object before continuing'}}</h3>
    <p>{{es?'La mano puede confundirse con el objeto. Marca un punto dentro de la parte visible del cuerpo que debe seguir SAM, sin tocar la mano, la uña, la cuerda ni la sombra.':'The hand can be mistaken for the object. Mark a point inside the visible body SAM should track, away from the hand, fingernail, cord and shadow.'}}</p>
    <div class="reference-images">
      <figure>
        <figcaption>{{es?'1. Primer fotograma: marca aquí':'1. First frame: mark here'}}</figcaption>
        <svg class="point-picker" [attr.viewBox]="viewBox()"
          tabindex="0" role="application" [attr.aria-label]="es?'Seleccionar punto del objeto en el primer fotograma':'Select an object point in the first frame'"
          aria-describedby="point-keyboard-help" (pointerdown)="select($event)" (keydown)="key($event)">
          <image [attr.href]="reference().initialFrameUrl" [attr.width]="reference().width" [attr.height]="reference().height"
            (load)="initialLoaded.set(true)" (error)="imageFailed(false)"/>
          @if(point(); as p){
            <circle [attr.cx]="p.x" [attr.cy]="p.y" [attr.r]="reference().width*.018/zoom()" fill="none" stroke="#6ee7c2" [attr.stroke-width]="reference().width*.004/zoom()"/>
            <path [attr.d]="cross(p)" stroke="#07121c" [attr.stroke-width]="reference().width*.009/zoom()"/>
            <path [attr.d]="cross(p)" stroke="#6ee7c2" [attr.stroke-width]="reference().width*.003/zoom()"/>
          }
        </svg>
        <div class="zoom-controls" [attr.aria-label]="es?'Ampliación para marcar':'Magnification for marking'">
          <button type="button" (click)="setZoom(1)" [disabled]="busy()" [attr.aria-pressed]="zoom()===1">1×</button>
          <button type="button" (click)="setZoom(2)" [disabled]="busy() || !point()" [attr.aria-pressed]="zoom()===2">2×</button>
          <button type="button" (click)="setZoom(4)" [disabled]="busy() || !point()" [attr.aria-pressed]="zoom()===4">4×</button>
        </div>
      </figure>
      @if(reference().clearFrameUrl){
        <figure><figcaption>{{es?'2. Referencia: el cuerpo libre':'2. Reference: the unobstructed body'}}</figcaption>
          <img [src]="reference().clearFrameUrl!" [alt]="es?'Objeto libre en otro fotograma del mismo video':'Unobstructed object in another frame of the same video'"
            (load)="clearLoaded.set(true)" (error)="imageFailed(true)"/>
        </figure>
      }
    </div>
    <small id="point-keyboard-help">{{es?'Toca la primera imagen; amplía para afinar el punto. Con teclado: enfoca la imagen y usa las flechas; Mayús mueve un píxel. Si el cuerpo no se distingue, cancela.':'Tap the first image; zoom in to refine the point. Keyboard: focus the image and use the arrows; Shift moves one pixel. Cancel if the body cannot be distinguished.'}}</small>
    <p class="point-status" role="status">{{imageError()?(es?'No se pudieron cargar las referencias. No se enviará SAM.':'The references could not be loaded. SAM will not be sent.'):point()?(es?'Punto marcado por ti. Comprueba que está sobre el cuerpo del objeto.':'You marked a point. Check that it is on the object body.'):(es?'Falta marcar el punto. Ningún punto de IA se acepta automáticamente.':'Mark a point to continue. No AI point is accepted automatically.')}}</p>
    <p>{{es?'Se reutiliza la revisión de Spark. SAM: hasta US$ ':'The Spark review is reused. SAM: up to US$ '}}{{samCost().toFixed(2)}}{{includeMuse()?(es?' + Muse hasta US$ 0,05.':' + Muse up to US$ 0.05.'):'.'}}</p>
    <div class="reference-actions">
      <button type="button" class="confirm-reference" (click)="submit()" [disabled]="!canConfirm()">{{busy()?(es?'Guardando punto…':'Saving point…'):(es?'Confirmar punto y continuar con SAM':'Confirm point and continue with SAM')}}</button>
      <button type="button" (click)="cancel.emit()" [disabled]="busy()">{{es?'Cancelar por ahora':'Cancel for now'}}</button>
    </div>
  </section>
`,styles:[`
  :host{display:block}.initial-reference{margin:16px 0;padding:16px;border:1px solid #69d9b9;border-radius:10px;background:#10282c;color:#e4f3ff}
  h3{margin:0 0 10px}p,small{line-height:1.6}p{margin:10px 0}.reference-images{display:grid;grid-template-columns:1fr 1fr;gap:12px}
  figure{margin:0;background:#07121c;border-radius:8px;overflow:hidden}figcaption{font-size:.8rem;padding:8px}
  svg,img{display:block;width:100%;height:360px;object-fit:contain}.point-picker{cursor:crosshair;touch-action:manipulation}.point-picker:focus{outline:3px solid #6ee7c2;outline-offset:-3px}
  .zoom-controls{display:flex;gap:8px;padding:8px}.zoom-controls button[aria-pressed="true"]{border-color:#6ee7c2;background:#335950}.reference-actions{display:flex;gap:10px;flex-wrap:wrap}button{border:1px solid #548179;border-radius:7px;padding:12px;background:#203c3d;color:#e4f3ff;font:inherit;cursor:pointer;min-height:44px}
  .confirm-reference{background:#6ee7c2;color:#06231b;font-weight:700}button:disabled{opacity:.5;cursor:not-allowed}
  @media(max-width:650px){.reference-images{grid-template-columns:1fr}svg,img{height:400px}.reference-actions button{width:100%}}
`]})
export class SamInitialReferenceReview implements OnChanges {
  readonly reference=input.required<SamInitialReference>();readonly language=input<'es'|'en'>('es');
  readonly busy=input(false);readonly continueAllowed=input(true);readonly samCost=input(0);readonly includeMuse=input(false);
  readonly confirm=output<SamBodyPoint>();readonly cancel=output<void>();
  readonly point=signal<SamBodyPoint|null>(null);readonly initialLoaded=signal(false);readonly clearLoaded=signal(false);readonly imageError=signal(false);
  readonly zoom=signal(1);private zoomCenter:SamBodyPoint|null=null;
  get es():boolean{return this.language()==='es';}
  ngOnChanges(changes:SimpleChanges):void{if(changes['reference']){this.zoom.set(1);this.zoomCenter=null;this.point.set(null);this.initialLoaded.set(false);this.clearLoaded.set(false);this.imageError.set(false);}}
  imageFailed(clear:boolean):void{if(clear)this.clearLoaded.set(false);else this.initialLoaded.set(false);this.imageError.set(true);this.point.set(null);}
  canConfirm():boolean{return this.continueAllowed() && !this.busy() && !this.imageError() && this.initialLoaded() && (!this.reference().clearFrameUrl||this.clearLoaded()) && !!this.point();}
  select(event:PointerEvent):void {
    if(this.busy()||!this.initialLoaded()||this.imageError())return;
    const svg=event.currentTarget as SVGSVGElement,matrix=svg.getScreenCTM();if(!matrix)return;
    const p=svg.createSVGPoint();p.x=event.clientX;p.y=event.clientY;const native=p.matrixTransform(matrix.inverse());
    const r=this.reference();if(native.x<0||native.y<0||native.x>=r.width||native.y>=r.height)return;
    this.point.set({x:Math.floor(native.x),y:Math.floor(native.y)});svg.focus();
  }
  key(event:KeyboardEvent):void {
    if(this.busy()||!this.initialLoaded()||this.imageError()||!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Enter',' '].includes(event.key))return;
    event.preventDefault();const r=this.reference(),p=this.point()??{x:Math.floor(r.width/2),y:Math.floor(r.height/2)};
    const step=event.shiftKey?1:Math.max(1,Math.round(Math.min(r.width,r.height)/200));
    this.point.set({x:Math.max(0,Math.min(r.width-1,p.x+(event.key==='ArrowRight'?step:event.key==='ArrowLeft'?-step:0))),
      y:Math.max(0,Math.min(r.height-1,p.y+(event.key==='ArrowDown'?step:event.key==='ArrowUp'?-step:0)))});
  }
  setZoom(value:number):void{if(this.busy() || value!==1&&!this.point())return;this.zoomCenter=this.point();this.zoom.set(value);}
  viewBox():string{const r=this.reference(),w=r.width/this.zoom(),h=r.height/this.zoom(),p=this.zoomCenter??{x:r.width/2,y:r.height/2};
    return [Math.max(0,Math.min(r.width-w,p.x-w/2)),Math.max(0,Math.min(r.height-h,p.y-h/2)),w,h].join(' ');}
  cross(p:SamBodyPoint):string {const d=this.reference().width*.025/this.zoom();return 'M'+(p.x-d)+' '+p.y+'H'+(p.x+d)+'M'+p.x+' '+(p.y-d)+'V'+(p.y+d);}
  submit():void{if(this.canConfirm())this.confirm.emit(this.point()!);}
}
