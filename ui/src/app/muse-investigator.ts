import {Component,OnChanges,OnDestroy,inject,input,output,signal} from '@angular/core';
import {HttpClient,HttpHeaders} from '@angular/common/http';
import {firstValueFrom} from 'rxjs';
import {LabConnection} from './lab-connection';
import {LabActivity} from './lab-activity';
import type {SamPeriodSource} from './sam-period';

interface Bilingual {es:string;en:string}
export interface FrozenMusePrediction {
  studyId:string;observationId:string;responseId:string;frozenAtUtc:string;payloadJson:string;sha256:string;
}
interface MuseState {
  observation:null|{id:string;analysisId:string;payloadJson:string;payloadSha256:string};
  evaluation:null|{model:string;responseId:string;rawResponseSha256:string;tokenCostEstimateUsd:number;
    answer:{decision:string;justification:Bilingual;limitations:Bilingual;protocol:Bilingual;
      hypothesis:Bilingual;falsification:Bilingual;nextLengthMeters:number|null;periodSeconds:number|null;tolerancePercent:number|null}};
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
    const serial=++this.serial;this.busy.set('loading');this.error.set('');this.state.set(null);
    try {
      const state=await firstValueFrom(this.http.get<MuseState>(this.url()));
      if(serial===this.serial)this.accept(state);
    } catch {if(serial===this.serial)this.error.set(this.es?'No se pudo consultar la bitácora de Muse.':'Could not load the Muse lab log.');}
    finally {if(serial===this.serial)this.busy.set(null);}
  }
  async prepare():Promise<void>{
    if(this.busy()||this.connection.busy()||this.state()?.prediction)return;
    const serial=++this.serial,source=this.source();
    this.busy.set('preparing');this.error.set('');
    try {
      const state=await firstValueFrom(this.http.post<MuseState>(this.url()+'/prepare',{
        videoSha256:source.originalVideoSha256,analysisId:source.analysisId,pivotRevisionId:source.pivotRevisionId
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
  decision():string {
    switch(this.state()?.evaluation?.answer.decision) {
      case 'PROPOSE_EXPERIMENT':return this.es?'Experimento propuesto':'Experiment proposed';
      case 'REPEAT_RECORDING':return this.es?'Repetir grabación':'Repeat recording';
      default:return this.es?'Inconcluso':'Inconclusive';
    }
  }
}
