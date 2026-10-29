import {Component,OnChanges,OnDestroy,SimpleChanges,ElementRef,inject,input,output,signal} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {firstValueFrom} from 'rxjs';
import {LabConnection} from './lab-connection';
import {LabActivity} from './lab-activity';

export interface SceneDefinition {
  key:string;videoSha256:string;width:number;height:number;arrangement:string;
  origin?:'SPARK_GROUNDING'|null;
  roles:{id:string;role:string}[];pivotX:number|null;pivotY:number|null;
}
interface Observation {frame:number;role:string;visible:boolean;x1:number;y1:number;x2:number;y2:number;confidence:number}
export interface AssemblyEvidence {
  cacheKey:string;videoSha256:string;model:string;protocol:string;
  frames:{index:number;timeSeconds:number;width:number;height:number;sha256:string}[];
  proposal:{arrangement:string;observations:Observation[];suspension:{status:string;x:number|null;y:number|null}};
}
export interface AssemblyProcessRequest {
  studyId:string;role:string;sha:string;port:number;cacheKey:string;maximumEstimatedCostUsd:number;
}
export interface AssemblyQuote {
  cacheKey:string;videoSha256:string;maximumEstimatedCostUsd:number;
  cacheHit:boolean;priorAttempt:boolean;canRun:boolean;evidence:AssemblyEvidence|null;approved:SceneDefinition|null;
}
@Component({
  selector:'app-assembly-review',standalone:true,imports:[LabActivity],
  template:`
    @if(studyId() && sha()) {
      <section class="assembly" [attr.aria-busy]="busy()">
        <header><div><span class="eyebrow">{{es?'RECONOCIMIENTO DEL MONTAJE':'ASSEMBLY RECOGNITION'}}</span>
          <h3>{{es?'Objeto, cuerda y suspensión':'Object, cord and suspension'}}</h3>
          <p>{{es?'Moneda, pesa u otro objeto · revisa sus posiciones antes de seguirlo con SAM.':'Coin, weight or another object · review its locations before tracking it with SAM.'}}</p></div>
          <button type="button" (click)="open()" [disabled]="busy() || connection.busy()">
            {{approved()?(es?'Ver montaje revisado':'View reviewed assembly'):(es?'Reconocer montaje':'Recognize assembly')}}
          </button>
        </header>
        @if(expanded()) {
          @if(busy()) {<app-lab-activity [label]="running()?(es?'Reconociendo los elementos…':'Recognizing the elements…'):(es?'Preparando revisión…':'Preparing review…')"
            [detail]="running()?(es?'Tres imágenes originales · esperando a Meta':'Three original images · waiting for Meta'):''"/>}
          @if(error()) {<p class="warning" role="alert">{{error()}}</p>}
          @if(quote();as q) {
            @if(!evidence() && !busy()) {
              <div class="quote"><strong>US$ {{q.maximumEstimatedCostUsd.toFixed(2)}}</strong>
                <span>{{es?'máximo estimado para reconocer · SAM se cotiza aparte abajo':'estimated recognition budget · SAM is quoted separately below'}}</span></div>
              <p>{{es?'Muse Spark propone qué es cada elemento. Después, SAM obtiene sus máscaras y posiciones en el video. Son dos acciones manuales; ninguna inicia la siguiente automáticamente.':'Muse Spark proposes each element’s identity. SAM then obtains its masks and positions in the video. These are two manual actions; neither starts the other automatically.'}}</p>
              @if(q.priorAttempt) {<p class="warning">{{es?'Hay un intento anterior pendiente de revisión.':'A previous attempt needs review.'}}</p>}
              @else {
                <label class="consent"><input type="checkbox" [checked]="consent()" (change)="consent.set($any($event.target).checked)"/>
                  {{es?'Autorizo enviar tres fotogramas a Meta con este presupuesto estimado.':'I authorize sending three frames to Meta with this estimated budget.'}}</label>
                <button type="button" class="primary" (click)="recognize()" [disabled]="!canRecognize()">{{es?'Detectar elementos':'Detect elements'}}</button>
                @if(!connection.hasToken()) {<button type="button" (click)="connection.openSettings()">{{es?'Configurar token':'Configure key'}}</button>}
                @if(!q.canRun) {<p class="warning">{{es?'No hay cupo autorizado disponible en el servidor.':'No authorized server allowance is available.'}}</p>}
              }
            }
          }
          @if(evidence();as e) {
            <p>{{es?'Comprueba las cajas y la cruz de suspensión en las tres imágenes. Desmarca cualquier elemento mal identificado. No confirmes si el objeto es incorrecto.':'Check the boxes and suspension cross in all three images. Uncheck any misidentified element. Do not confirm if the object is incorrect.'}}</p>
            <div class="roles">
              @for(role of roles;track role) {
                <label [style.color]="color(role)"><input type="checkbox" [checked]="selected().includes(role)"
                  [disabled]="!!approved() || !available(role) || busy()" (change)="toggle(role,$any($event.target).checked)"/>
                  {{label(role)}} <small>{{available(role)?'':(es?'no claro / ausente':'unclear / absent')}}</small></label>
              }
            </div>
            <div class="frames">
              @for(f of e.frames;track $index;let i=$index) {
                <figure><svg [attr.viewBox]="'0 0 '+f.width+' '+f.height" role="img" [attr.aria-label]="es?'Revisión de elementos del montaje':'Review assembly elements'">
                  <image [attr.href]="frameUrl(i)" [attr.width]="f.width" [attr.height]="f.height" (load)="imageReady(i)" (error)="imageFailed()"/>
                  @for(o of boxes(i);track o.role) {
                    <rect [attr.x]="o.x1*f.width/1000" [attr.y]="o.y1*f.height/1000"
                      [attr.width]="(o.x2-o.x1)*f.width/1000" [attr.height]="(o.y2-o.y1)*f.height/1000"
                      [attr.stroke]="color(o.role)" fill="none" stroke-width="1.5" vector-effect="non-scaling-stroke"/>
                    <text [attr.x]="o.x1*f.width/1000" [attr.y]="Math.max(f.height*.02,o.y1*f.height/1000-8)"
                      [attr.font-size]="f.width*.032" [attr.fill]="color(o.role)" stroke="#08111c" stroke-width="2" paint-order="stroke">{{label(o.role)}}</text>
                  }
                  @if(e.proposal.suspension.x!==null && e.proposal.suspension.y!==null) {
                    <path [attr.d]="cross(i)" stroke="#fff" stroke-width="2" fill="none" vector-effect="non-scaling-stroke"/>
                  }
                </svg><figcaption>{{f.timeSeconds.toFixed(2)}} s · {{f.width}} × {{f.height}}</figcaption></figure>
              }
            </div>
            @if(e.proposal.arrangement!=='simple_pendulum') {
              <p class="warning">{{es?'Montaje con polea o geometría incierta: se mostrarán posiciones y máscaras, sin aplicar el modelo de péndulo simple.':'Pulley system or uncertain geometry: positions and masks will be shown without applying the simple-pendulum model.'}}</p>
            }
            @if(e.proposal.suspension.x===null) {
              <p class="warning">{{es?'Salida de la cuerda no localizada con claridad. No se inventará un pivote a partir del centro del nudo o de la polea.':'The string exit is unclear. A pivot will not be invented from the knot or pulley center.'}}</p>
            }
            @if(approved()) {
              <p class="success">{{es?'Montaje revisado. Continúa con Procesar en SAM y revisa el costo. El token registrado sigue disponible en esta sesión.':'Assembly reviewed. Continue with Process in SAM and review the cost. The registered key remains available for this session.'}}</p>
            } @else {
              <label class="consent"><input type="checkbox" [checked]="reviewed()" (change)="reviewed.set($any($event.target).checked)"/>
                {{es?'He revisado las tres imágenes: las cajas seleccionadas identifican los elementos correctos y la cruz, si aparece, marca la salida de la cuerda.':'I reviewed all three images: selected boxes identify the correct elements and the cross, if present, marks the string exit.'}}</label>
              <button class="primary" type="button" (click)="confirm()" [disabled]="!canConfirm()">{{es?'Usar estos elementos en SAM':'Use these elements in SAM'}}</button>
            }
            <p class="muted">{{es?'El centro se calculará a partir de la máscara del objeto en cada fotograma: centro visible, no centro de masa certificado.':'The center will be calculated from the object mask in each frame: visible center, not a certified center of mass.'}}</p>
          }
          <button class="close" type="button" (click)="expanded.set(false)" [disabled]="busy()">{{es?'Cerrar revisión':'Close review'}}</button>
        }
      </section>
    }
  `,
  styles:[`
    :host{display:block;margin:12px 0;color:#cad8e7;font-size:13px}
    .assembly{padding:18px;border:1px solid #394653;border-radius:12px;background:#20262d}
    header{display:flex;align-items:center;justify-content:space-between;gap:16px}h3{font-size:16px;margin:5px 0}
    .eyebrow{font-size:10px;letter-spacing:.12em;color:#92b4d5}p{line-height:1.5;margin:8px 0;color:#afc3d5}
    button{padding:9px 13px;border:1px solid #486078;border-radius:8px;background:#253c50;color:#e4f3ff;font:inherit;cursor:pointer}
    button:disabled{opacity:.45;cursor:not-allowed}.primary{background:#276da4;margin:5px 0}.close{margin-top:12px;background:transparent}
    .quote{display:flex;align-items:center;gap:12px;margin:16px 0}.quote strong{font-size:24px}.quote span,.muted{font-size:12px;color:#97aec3}
    .consent{display:flex;align-items:flex-start;gap:8px;margin:15px 0;line-height:1.5}.warning{color:#ffd08b}.success{color:#96e8c4}
    .roles{display:flex;gap:15px;flex-wrap:wrap;margin:15px 0}.roles label{display:flex;align-items:center;gap:5px}.roles small{color:#9aa6b2}
    .frames{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}figure{margin:0;background:#0b1520;border-radius:9px;overflow:hidden}
    svg{width:100%;height:350px;display:block}figcaption{padding:8px;color:#a5bfd6}
    @media(max-width:700px){header{align-items:flex-start;flex-direction:column}.frames{grid-template-columns:1fr}svg{height:440px}}
  `]
})
export class AssemblyReview implements OnChanges,OnDestroy {
  private readonly http=inject(HttpClient);private readonly host=inject(ElementRef<HTMLElement>);
  readonly connection=inject(LabConnection);readonly refreshed=output<void>();
  readonly studyId=input.required<string>();readonly role=input.required<string>();readonly sha=input.required<string>();
  readonly port=input(5391);readonly language=input<'es'|'en'>('es');readonly confirmed=output<SceneDefinition>();
  readonly expanded=signal(false);readonly busy=signal(false);readonly running=signal(false);readonly error=signal('');
  readonly quote=signal<AssemblyQuote|null>(null);readonly evidence=signal<AssemblyEvidence|null>(null);
  readonly approved=signal<SceneDefinition|null>(null);readonly selected=signal<string[]>([]);
  readonly reviewed=signal(false);readonly consent=signal(false);readonly loaded=signal<number[]>([]);
  readonly roles=['object','cord','knot','pulley','support'];readonly Math=Math;
  private serial=0;private disposed=false;
  get es():boolean{return this.language()==='es';}
  label(r:string):string{return ({object:this.es?'Objeto':'Object',cord:this.es?'Cuerda':'Cord',knot:this.es?'Nudo':'Knot',pulley:this.es?'Polea':'Pulley',support:this.es?'Soporte':'Support'} as Record<string,string>)[r]??r;}
  color(r:string):string{return ({object:'#5cf4cb',cord:'#ffdd86',knot:'#d4a2ff',pulley:'#7ebcff',support:'#b5bfcd'} as Record<string,string>)[r]??'#fff';}
  private url():string{return 'http://127.0.0.1:'+this.port()+'/api/science/director/pendulum/sessions/'+this.studyId()+'/assembly/'+this.role();}
  ngOnChanges(c:SimpleChanges):void{
    if(!c['studyId']&&!c['role']&&!c['sha']&&!c['port'])return;
    this.serial++;this.quote.set(null);this.evidence.set(null);this.approved.set(null);this.loaded.set([]);
    this.selected.set([]);this.reviewed.set(false);this.consent.set(false);this.expanded.set(false);this.error.set('');
    if(!this.running())this.busy.set(false);
  }
  ngOnDestroy():void{this.disposed=true;this.serial++;}
  private show(e:AssemblyEvidence):void {
    if(e.videoSha256!==this.sha() || e.model!=='muse-spark-1.3' || e.protocol!=='ASSEMBLY_3_NATIVE_FRAMES_V1' || e.frames.length!==3)
      throw new Error('ASSEMBLY_MISMATCH');
    if(this.evidence()?.cacheKey!==e.cacheKey)this.loaded.set([]);
    this.evidence.set(e);this.selected.set(this.roles.filter(r=>this.available(r)));this.reviewed.set(false);
  }
  async open():Promise<void>{
    if(this.busy() || this.connection.busy())return;
    this.expanded.set(true);this.busy.set(true);this.error.set('');const serial=++this.serial;
    try{
      const q=await firstValueFrom(this.http.get<AssemblyQuote>(this.url()+'/preflight'));
      if(serial!==this.serial || this.disposed)return;
      if(q.videoSha256!==this.sha() || q.maximumEstimatedCostUsd!==.05)throw new Error('QUOTE_MISMATCH');
      this.quote.set(q);if(q.evidence)this.show(q.evidence);
      this.approved.set(q.approved);
      if(q.approved)this.selected.set(q.approved.roles.map(r=>r.role));
    }catch{if(serial===this.serial)this.error.set(this.es?'No se pudo consultar el montaje.':'Could not inspect the assembly.');}
    finally{if(serial===this.serial)this.busy.set(false);}
  }
  async startFromProcess(request:AssemblyProcessRequest):Promise<void> {
    const matches=()=>request.studyId===this.studyId() && request.role===this.role() &&
      request.sha===this.sha() && request.port===this.port() && !this.disposed;
    if(!matches() || this.busy() || this.connection.busy())return;
    await this.open();
    if(!matches() || this.error())return;
    this.host.nativeElement.scrollIntoView?.({behavior:'smooth',block:'start'});
    const q=this.quote();
    if(!q || q.cacheKey!==request.cacheKey)return;
    if(q.approved){this.confirmed.emit(q.approved);return;}
    if(q.cacheHit && q.evidence)return; // review without a paid call
    // Never upgrade a previously free click or accept a changed quote.
    if(request.maximumEstimatedCostUsd!==.05 || q.maximumEstimatedCostUsd!==.05)return;
    this.consent.set(true);
    await this.recognize();
  }
  available(role:string):boolean{return !!this.evidence()?.proposal.observations.some(o=>o.role===role&&o.visible&&o.confidence>=.8);}
  boxes(i:number):Observation[]{return this.evidence()?.proposal.observations.filter(o=>o.frame===i&&o.visible&&o.confidence>=.8&&this.selected().includes(o.role))??[];}
  toggle(role:string,on:boolean):void{if(this.approved()||this.busy())return;this.selected.update(v=>on?[...new Set([...v,role])]:v.filter(r=>r!==role));this.reviewed.set(false);}
  imageReady(i:number):void{this.loaded.update(v=>v.includes(i)?v:[...v,i]);}
  imageFailed():void{this.loaded.set([]);this.reviewed.set(false);this.error.set(this.es?'No se pudo cargar una imagen.':'An image could not be loaded.');}
  frameUrl(i:number):string{return this.url()+'/frame/'+i+'?key='+this.evidence()!.cacheKey;}
  cross(i:number):string {const e=this.evidence()!,f=e.frames[i],p=e.proposal.suspension;const x=p.x!*f.width/1000,y=p.y!*f.height/1000,r=f.width*.015;
    return 'M '+(x-r)+' '+y+' H '+(x+r)+' M '+x+' '+(y-r)+' V '+(y+r);}
  canRecognize():boolean{const q=this.quote();return !!q&&!q.cacheHit&&!q.priorAttempt&&q.canRun&&q.videoSha256===this.sha()&&
    this.consent()&&this.connection.hasToken()&&!this.busy()&&!this.connection.busy();}
  async recognize():Promise<void>{
    if(!this.canRecognize())return;const key=this.connection.tokenForManualRequest();if(!key)return;
    const serial=++this.serial;this.busy.set(true);this.running.set(true);this.connection.busy.set(true);this.error.set('');
    try {
      const e=await firstValueFrom(this.http.post<AssemblyEvidence>(this.url()+'/locate',{
        videoSha256:this.sha(),cacheKey:this.quote()!.cacheKey,modelApiToken:key,
        confirmImageTransfer:true,confirmPaidCall:true,maximumEstimatedCostUsd:.05
      },{headers:{'X-ProofLab-Manual-Action':'assembly'}}));
      if(serial===this.serial&&!this.disposed)this.show(e);
    }catch{if(serial===this.serial){this.quote.set(null);this.error.set(this.es?'La consulta no se completó. Puede haber consumo de créditos; vuelve a abrir para revisar el estado.':'The request did not complete. Credits may have been used; reopen to inspect status.');}}
    finally{this.connection.busy.set(false);this.running.set(false);this.busy.set(false);this.consent.set(false);if(serial===this.serial&&!this.disposed)this.refreshed.emit();}
  }
  canConfirm():boolean{return !!this.evidence()&&!this.approved()&&this.loaded().length===3&&this.reviewed()&&
    this.selected().includes('object')&&this.selected().every(r=>this.available(r))&&!this.busy()&&!this.connection.busy();}
  async confirm():Promise<void>{
    if(!this.canConfirm())return;const serial=++this.serial;this.busy.set(true);this.error.set('');
    try{
      const scene=await firstValueFrom(this.http.post<SceneDefinition>(this.url()+'/confirm',{
        videoSha256:this.sha(),cacheKey:this.evidence()!.cacheKey,confirmReviewed:true,roles:this.selected()
      },{headers:{'X-ProofLab-Manual-Action':'assembly'}}));
      if(serial!==this.serial||this.disposed)return;
      if(scene.videoSha256!==this.sha())throw new Error('ASSEMBLY_MISMATCH');
      this.approved.set(scene);this.confirmed.emit(scene);
    }catch{if(serial===this.serial)this.error.set(this.es?'No se pudo guardar la revisión.':'Could not save the review.');}
    finally{if(serial===this.serial)this.busy.set(false);}
  }
}
