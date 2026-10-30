import {Component,OnChanges,OnDestroy,inject,input,output,signal} from '@angular/core';
import {HttpClient,HttpHeaders} from '@angular/common/http';
import {firstValueFrom} from 'rxjs';
import {LabConnection} from './lab-connection';
import {LabActivity} from './lab-activity';
import type {SamPeriodSource} from './sam-period';

interface Bilingual {es:string;en:string}
interface MusePhenomenon {
  classification:'PENDULUM_COMPATIBLE'|'OTHER_MOTION'|'INCONCLUSIVE';
  justification:Bilingual;
}
export interface FrozenMusePrediction {
  studyId:string;observationId:string;responseId:string;frozenAtUtc:string;payloadJson:string;sha256:string;
}
interface MuseState {
  observation:null|{id:string;analysisId:string;videoSha256?:string;payloadJson:string;payloadSha256:string};
  evaluation:null|{model:string;responseId:string;observationId?:string;studyId?:string;rawResponseSha256:string;tokenCostEstimateUsd:number;
    answer:{decision:string;justification:Bilingual;limitations:Bilingual;protocol:Bilingual;
      hypothesis:Bilingual;falsification:Bilingual;phenomenon?:MusePhenomenon|null;nextLengthMeters:number|null;periodSeconds:number|null;tolerancePercent:number|null}};
  prediction:FrozenMusePrediction|null;attemptStatus:string|null;manualCallsPermitted:boolean;maximumEstimatedCostUsd:number;
}
@Component({
  selector:'app-muse-investigator',standalone:true,imports:[LabActivity],
  templateUrl:'./muse-investigator.html',styleUrl:'./muse-investigator.scss'
})
export class MuseInvestigator implements OnChanges,OnDestroy {
  readonly source=input.required<SamPeriodSource>();
  readonly language=input<'es'|'en'>('es');
  readonly frozen=output<FrozenMusePrediction|null>();
  readonly connection=inject(LabConnection);
  private readonly http=inject(HttpClient);
  private serial=0;
  readonly state=signal<MuseState|null>(null);
  readonly contrast=signal<{evidence:null|{sha256:string;payloadJson:string};evaluation:null|{responseId:string;answer:{verdict:string;justification:Bilingual;limitations:Bilingual;nextStep:Bilingual}};attemptStatus:string|null}|null>(null);
  readonly followupLength=signal<number|null>(null);
  get isFollowup():boolean{return this.source().role==='followup';}
  contrastMatches():boolean {try{const c=this.contrast(),p=JSON.parse(c!.evidence!.payloadJson);return p.analysisId===this.source().analysisId&&p.sourceSha256===this.source().originalVideoSha256;}catch{return false;}}
  async prepareContrast():Promise<void>{
    if(this.busy()||this.connection.busy()||!this.followupLength())return;
    const serial=++this.serial,source=this.source();this.busy.set('preparing');this.error.set('');
    try{const c=await firstValueFrom(this.http.post<NonNullable<ReturnType<typeof this.contrast>>>(this.url()+'/contrast/prepare',{
      videoSha256:source.originalVideoSha256,analysisId:source.analysisId,pivotRevisionId:source.pivotRevisionId,
      adaptiveBatchId:source.adaptiveBatchId??null,followupLengthMeters:this.followupLength()
    },{headers:this.headers}));if(serial===this.serial)this.contrast.set(c);}
    catch{if(serial===this.serial)this.error.set(this.es?'No se pudo verificar el contraste. Revisa la predicción y el resultado SAM.':'Could not verify comparison. Check the prediction and SAM result.');}
    finally{if(serial===this.serial)this.busy.set(null);}
  }
  canContrast():boolean{return !this.busy()&&!this.connection.busy()&&this.connection.hasToken()&&!!this.state()?.manualCallsPermitted&&this.contrastMatches()&&!this.contrast()?.evaluation&&!this.contrast()?.attemptStatus;}
  async evaluateContrast():Promise<void>{
    if(!this.canContrast())return;const token=this.connection.tokenForManualRequest();if(!token)return;
    const serial=++this.serial,url=this.url();this.busy.set('evaluating');this.connection.busy.set(true);this.error.set('');
    try{const c=await firstValueFrom(this.http.post<NonNullable<ReturnType<typeof this.contrast>>>(url+'/contrast/evaluate',{
      evidenceSha256:this.contrast()!.evidence!.sha256,modelApiToken:token,confirmMeasuredDataTransfer:true,confirmPaidCall:true,maximumEstimatedCostUsd:.05
    },{headers:this.headers}));if(serial===this.serial)this.contrast.set(c);}
    catch{if(serial===this.serial){try{this.contrast.set(await firstValueFrom(this.http.get<NonNullable<ReturnType<typeof this.contrast>>>(url+'/contrast')));}catch{}this.error.set(this.es?'El contraste no se completó. Revisa el intento registrado.':'Comparison did not complete. Inspect the recorded attempt.');}}
    finally{this.connection.busy.set(false);if(serial===this.serial)this.busy.set(null);}
  }
  contrastVerdict():string {switch(this.contrast()?.evaluation?.answer.verdict){
    case 'WITHIN_PREDECLARED_BAND':return this.es?'Compatible con la predicción dentro del margen':'Consistent with the prediction within tolerance';
    case 'OUTSIDE_PREDECLARED_BAND':return this.es?'Fuera del margen de la predicción':'Outside the prediction tolerance';
    default:return this.es?'Contraste inconcluso':'Inconclusive comparison';}}

  readonly busy=signal<'loading'|'preparing'|'evaluating'|null>(null);
  readonly error=signal('');
  readonly headers=new HttpHeaders({'X-ProofLab-Manual-Action':'muse-research'});
  private readonly subscription=this.connection.changed.subscribe(()=>void this.load());
  get es():boolean{return this.language()==='es';}
  text(value:Bilingual):string{return this.es?value.es:value.en;}
  url():string{return 'http://127.0.0.1:'+this.connection.port()+'/api/science/director/pendulum/sessions/'+this.source().studyId+'/muse';}
  ngOnChanges():void{void this.load();}
  ngOnDestroy():void{this.serial++;this.subscription.unsubscribe();}
  private accept(state:MuseState):void {
    this.state.set(state);this.frozen.emit(state.prediction);
  }
  async load():Promise<void>{
    if(this.busy()==='evaluating')return;
    const serial=++this.serial,url=this.url(),followup=this.isFollowup;this.busy.set('loading');this.error.set('');this.state.set(null);this.contrast.set(null);this.followupLength.set(null);
    try {
      const state=await firstValueFrom(this.http.get<MuseState>(url));
      if(serial===this.serial){this.accept(state);this.followupLength.set(this.prediction()?.nextLengthMeters??null);}
      if(serial===this.serial&&followup){
        const c=await firstValueFrom(this.http.get<NonNullable<ReturnType<typeof this.contrast>>>(url+'/contrast'));
        if(serial===this.serial){this.contrast.set(c);if(c.evidence){try{this.followupLength.set(JSON.parse(c.evidence.payloadJson).followupLengthMeters);}catch{}}}
      }
    } catch {if(serial===this.serial)this.error.set(this.es?'No se pudo consultar la bitácora de Muse.':'Could not load the Muse lab log.');}
    finally {if(serial===this.serial)this.busy.set(null);}
  }
  async prepare():Promise<void>{
    if(this.busy()||this.connection.busy()||this.state()?.prediction)return;
    const serial=++this.serial,source=this.source();
    this.busy.set('preparing');this.error.set('');
    try {
      const state=await firstValueFrom(this.http.post<MuseState>(this.url()+'/prepare',{
        videoSha256:source.originalVideoSha256,analysisId:source.analysisId,pivotRevisionId:source.pivotRevisionId,adaptiveBatchId:source.adaptiveBatchId??null
      },{headers:this.headers}));
      if(serial===this.serial)this.accept(state);
    }catch{if(serial===this.serial)this.error.set(this.es
      ?'No se pudo preparar la evidencia. Verifica el resultado SAM y el pivote; el video 2 debe seguir pendiente.'
      :'Could not prepare evidence. Check the SAM result and pivot; video 2 must still be pending.');}
    finally{if(serial===this.serial)this.busy.set(null);}
  }
  canEvaluate():boolean {
    const s=this.state();
    return !this.busy()&&!this.connection.busy()&&!!s?.observation&&!s.evaluation&&!s.attemptStatus&&
      s.observation.analysisId===this.source().analysisId&&s.manualCallsPermitted&&this.connection.hasToken();
  }
  async evaluate():Promise<void> {
    if(!this.canEvaluate())return;
    const token=this.connection.tokenForManualRequest();if(!token)return;
    const serial=++this.serial,id=this.state()!.observation!.id,url=this.url();
    this.busy.set('evaluating');this.connection.busy.set(true);this.error.set('');
    try {
      const state=await firstValueFrom(this.http.post<MuseState>(url+'/evaluate',{
        observationId:id,modelApiToken:token,confirmMeasuredDataTransfer:true,
        confirmPaidCall:true,maximumEstimatedCostUsd:.05
      },{headers:this.headers}));
      if(serial===this.serial)this.accept(state);
    }catch {
      if(serial===this.serial) {
        try{this.accept(await firstValueFrom(this.http.get<MuseState>(url)));}catch{}
        this.error.set(this.es?'No se completó la evaluación. Revisa el intento guardado; no se reintentó automáticamente.':
          'Evaluation did not complete. Inspect the saved attempt; it was not automatically retried.');
      }
    }finally{this.connection.busy.set(false);if(serial===this.serial)this.busy.set(null);}
  }
  prediction():{periodSeconds:number;nextLengthMeters:number;tolerancePercent:number}|null {
    const p=this.state()?.prediction;
    if(!p)return null;
    try{return JSON.parse(p.payloadJson).prediction;}catch{return null;}
  }
  evaluationMatchesSource():boolean {
    const s=this.state(),o=s?.observation,e=s?.evaluation,source=this.source();
    return !!o && !!e && o.analysisId===source.analysisId &&
      (!o.videoSha256 || o.videoSha256===source.originalVideoSha256) &&
      (!e.observationId || e.observationId===o.id) && (!e.studyId || e.studyId===source.studyId);
  }
  phenomenon():MusePhenomenon|null {
    const e=this.state()?.evaluation,p=e?.answer.phenomenon;
    if(!this.evaluationMatchesSource() || e?.model!=='muse-spark-1.3' || !e.responseId?.trim() || !p ||
      !['PENDULUM_COMPATIBLE','OTHER_MOTION','INCONCLUSIVE'].includes(p.classification) ||
      !p.justification?.es?.trim() || !p.justification?.en?.trim())return null;
    return p;
  }
  phenomenonDecision():string|null {
    switch(this.phenomenon()?.classification) {
      case 'PENDULUM_COMPATIBLE':return this.es?'Muse Spark: compatible con un péndulo':'Muse Spark: consistent with a pendulum';
      case 'OTHER_MOTION':return this.es?'Muse Spark: otro tipo de movimiento':'Muse Spark: another type of motion';
      case 'INCONCLUSIVE':return this.es?'Muse Spark: clasificación inconclusa':'Muse Spark: inconclusive classification';
      default:return null;
    }
  }
  decision():string {
    if(this.state()?.evaluation && !this.evaluationMatchesSource())
      return this.es?'Evaluación de un análisis anterior':'Evaluation of an earlier analysis';
    switch(this.state()?.evaluation?.answer.decision) {
      case 'PROPOSE_EXPERIMENT':return this.es?'Experimento propuesto':'Experiment proposed';
      case 'REPEAT_RECORDING':return this.es?'Repetir grabación':'Repeat recording';
      default:return this.es?'Inconcluso':'Inconclusive';
    }
  }
}
