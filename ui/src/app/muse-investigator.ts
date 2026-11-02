import { apiOrigin } from './api-origin';
import {Component,OnChanges,OnDestroy,inject,input,output,signal,computed} from '@angular/core';
import {HttpClient,HttpHeaders} from '@angular/common/http';
import {firstValueFrom} from 'rxjs';
import {LabConnection} from './lab-connection';
import {LabActivity} from './lab-activity';
import type {SamPeriodSource} from './sam-period';
import {repeatabilityInterval95,compareRepeatability95} from './period-confidence';

interface Bilingual {es:string;en:string}
interface MusePhenomenon {
  classification:'PENDULUM_COMPATIBLE'|'OTHER_MOTION'|'INCONCLUSIVE';
  justification:Bilingual;
}
export interface FrozenMusePrediction {
  studyId:string;observationId:string;responseId:string;frozenAtUtc:string;payloadJson:string;sha256:string;
}
interface MuseState {
  observation:null|{id:string;studyId?:string;analysisId:string;videoSha256?:string;payloadJson:string;payloadSha256:string};
  evaluation:null|{model:string;responseId:string;observationId?:string;studyId?:string;rawResponseSha256:string;tokenCostEstimateUsd:number;
    answer:{decision:string;justification:Bilingual;limitations:Bilingual;protocol:Bilingual;
      hypothesis:Bilingual;falsification:Bilingual;phenomenon?:MusePhenomenon|null;nextLengthMeters:number|null;periodSeconds:number|null;tolerancePercent:number|null}};
  prediction:FrozenMusePrediction|null;attemptStatus:string|null;manualCallsPermitted:boolean;maximumEstimatedCostUsd:number;
}
type MuseJsonObject=Record<string,unknown>;
function museObject(value:unknown):MuseJsonObject|null {
  return value!==null && typeof value==='object' && !Array.isArray(value)
    ? value as MuseJsonObject : null;
}
function museText(value:unknown):value is string {
  return typeof value==='string' && value.trim().length>0;
}
function museObservationMatchesSource(observation:MuseState['observation'],source:SamPeriodSource):boolean {
  if(!observation || source.role!=='baseline' || source.providerModel!=='sam-3.1' ||
    ![source.studyId,source.originalVideoSha256,source.analysisId,source.providerResponseSha256,
      source.objectId,source.pivotProvenance,observation.id,observation.payloadSha256].every(museText) ||
    (source.pivotRevisionId!==null && !museText(source.pivotRevisionId)) ||
    (source.adaptiveBatchId!=null && !museText(source.adaptiveBatchId)) ||
    observation.studyId!==source.studyId || observation.videoSha256!==source.originalVideoSha256 ||
    observation.analysisId!==source.analysisId)return false;
  try {
    const payload=museObject(JSON.parse(observation.payloadJson));
    const identity=museObject(payload?.['identity']);
    const measurement=museObject(payload?.['measurement']);
    const measuredSource=museObject(measurement?.['source']);
    if(!identity || !measuredSource)return false;
    return identity['studyId']===source.studyId && identity['videoSha256']===source.originalVideoSha256 &&
      identity['analysisId']===source.analysisId && identity['responseSha256']===source.providerResponseSha256 &&
      identity['pivotRevisionId']===source.pivotRevisionId &&
      (identity['adaptiveBatchId']??null)===(source.adaptiveBatchId??null) &&
      measuredSource['studyId']===source.studyId && measuredSource['role']==='baseline' &&
      measuredSource['originalVideoSha256']===source.originalVideoSha256 &&
      measuredSource['analysisId']===source.analysisId &&
      measuredSource['providerResponseSha256']===source.providerResponseSha256 &&
      measuredSource['providerModel']===source.providerModel && measuredSource['objectId']===source.objectId &&
      measuredSource['pivotRevisionId']===source.pivotRevisionId &&
      measuredSource['pivotProvenance']===source.pivotProvenance;
  } catch {return false;}
}
@Component({
  selector:'app-muse-investigator',standalone:true,imports:[LabActivity],
  templateUrl:'./muse-investigator.html',styleUrl:'./muse-investigator.scss'
})
export class MuseInvestigator implements OnChanges,OnDestroy {
  readonly source=input.required<SamPeriodSource>();
  readonly language=input<'es'|'en'>('es');
  readonly view=input<'research'|'interpretation'>('research');
  readonly autoInterpretation=input(false);
  readonly interpretationOpened=signal(false);
  readonly interpretationTab=signal<'summary'|'evidence'|'limits'|'record'>('summary');
  readonly comparisonTab=signal<'summary'|'data'|'limits'|'record'>('summary');
  readonly reviewedPrediction=signal(false);
  readonly modelRunning=signal(false);
  readonly modelTime=signal(0);
  private modelTimer:ReturnType<typeof setInterval>|null=null;
  readonly evidenceSnapshot=computed(()=>{
    try{
      const raw=this.state()?.observation?.payloadJson;
      if(!raw)return null;
      const a=JSON.parse(raw)?.measurement?.assessment;
      if(!a)return null;
      return {
        period:typeof a.periodSeconds==='number'?a.periodSeconds:null,
        usedCycles:typeof a.usedCycles==='number'?a.usedCycles:null,
        completeCycles:typeof a.completeCycles==='number'?a.completeCycles:null,
        detectedFrames:typeof a.detectedFrames==='number'?a.detectedFrames:null,
        totalFrames:typeof a.totalFrames==='number'?a.totalFrames:null,
        coverage:typeof a.detectionCoverage==='number'?a.detectionCoverage:null
      };
    }catch{return null;}
  });
  readonly comparisonModel=computed(()=>{
    const s=this.state(),e=s?.evaluation,o=s?.observation;
    if(this.isFollowup || !e||!o||e.answer.decision!=='PROPOSE_EXPERIMENT' ||
       !this.evaluationMatchesSource())return null;
    try{
      const v=JSON.parse(o.payloadJson),l1=v.baselineLengthMeters,
        t1=v.measurement?.assessment?.periodSeconds,
        l2=e.answer.nextLengthMeters,t2=e.answer.periodSeconds,tol=e.answer.tolerancePercent;
      if(l2===null||t2===null||tol===null)return null;
      if([l1,l2,t1,t2,tol].some(x=>typeof x!=='number'||!Number.isFinite(x)||x<=0))return null;
      return {l1,l2,t1,t2,tol,lower:t2*(1-tol/100),upper:t2*(1+tol/100)};
    }catch{return null;}
  });
  readonly verifiedComparison=computed(()=>{
    if(!this.isFollowup || !this.contrastMatches())return null;
    const frozen=this.prediction(),evidence=this.contrast()?.evidence;
    if(!frozen || !evidence)return null;
    try{
      const observed=JSON.parse(evidence.payloadJson)?.measurement?.assessment?.periodSeconds;
      const expected=frozen.periodSeconds,tolerance=frozen.tolerancePercent;
      if(typeof observed!=='number'||!Number.isFinite(observed)||observed<=0||
         !Number.isFinite(expected)||expected<=0||!Number.isFinite(tolerance)||tolerance<=0)return null;
      const low=expected*(1-tolerance/100),high=expected*(1+tolerance/100);
      const min=Math.max(0,Math.min(low,observed)-Math.max(.05,expected*.06));
      const max=Math.max(high,observed)+Math.max(.05,expected*.06);
      const x=(v:number)=>42+476*(v-min)/(max-min);
      return {expected,observed,low,high,tolerance,differencePercent:100*(observed-expected)/expected,
        bandX:x(low),bandWidth:x(high)-x(low),expectedX:x(expected),observedX:x(observed),
        inBand:observed>=low&&observed<=high};
    }catch{return null;}
  });
  readonly modelCoordinates=computed(()=>{
    const m=this.comparisonModel(),t=this.modelTime();
    if(!m)return null;
    const a1=.27*Math.cos(2*Math.PI*t/m.t1),a2=.27*Math.cos(2*Math.PI*t/m.t2);
    const len1=132,len2=Math.max(78,Math.min(167,132*m.l2/m.l1));
    return {x1:115+len1*Math.sin(a1),y1:34+len1*Math.cos(a1),
      x2:333+len2*Math.sin(a2),y2:34+len2*Math.cos(a2)};
  });
  toggleModel():void{
    if(this.modelTimer!==null){clearInterval(this.modelTimer);this.modelTimer=null;
      this.modelRunning.set(false);return;}
    if(!this.comparisonModel())return;
    this.modelRunning.set(true);
    this.modelTimer=setInterval(()=>this.modelTime.update(t=>t+.05),50);
  }
  private interpretationContext='';
  readonly frozen=output<FrozenMusePrediction|null>();
  readonly connection=inject(LabConnection);
  private readonly http=inject(HttpClient);
  private serial=0;
  readonly state=signal<MuseState|null>(null);
  readonly contrast=signal<{evidence:null|{sha256:string;payloadJson:string};evaluation:null|{responseId:string;answer:{verdict:string;justification:Bilingual;limitations:Bilingual;nextStep:Bilingual}};attemptStatus:string|null}|null>(null);
  readonly followupLength=signal<number|null>(null);
  readonly statisticalSummary=computed(()=>{
    const s=this.state(),o=s?.observation,source=this.source();
    if(!o || (!this.isFollowup && o.analysisId!==source.analysisId) ||
      (this.isFollowup && s?.prediction?.observationId!==o.id))return null;
    try {
      const observation=JSON.parse(o.payloadJson),first=repeatabilityInterval95(observation.measurement?.assessment);
      const payload=this.contrastMatches()?JSON.parse(this.contrast()!.evidence!.payloadJson):null;
      const second=repeatabilityInterval95(payload?.measurement?.assessment);
      const prediction=s?.prediction?JSON.parse(s.prediction.payloadJson).prediction:null;
      return {confidenceLevel:.95,scope:'REPEATABILITY_ONLY',
        baselineObservationId:o.id,predictionSha256:s?.prediction?.sha256??null,
        contrastEvidenceSha256:this.contrastMatches()?this.contrast()?.evidence?.sha256??null:null,first,second,
        comparison:compareRepeatability95(first,second,prediction),
        globalPValue:null,globalDecision:'NOT_ASSESSED_MISSING_TOTAL_UNCERTAINTY',
        interpretation:'EXPLORATORY_INTERVALS_NOT_A_REPLACEMENT_FOR_THE_FROZEN_TOLERANCE_OR_A_HYPOTHESIS_ACCEPTANCE_PROBABILITY'};
    }catch{return null;}
  });
  number(value:number|null,digits=5):string {
    return value===null||!Number.isFinite(value)?'—':value.toLocaleString(this.es?'es-PE':'en-US',
      {minimumFractionDigits:digits,maximumFractionDigits:digits});
  }
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
  readonly downloading=signal(false);
  readonly downloadError=signal(false);
  async downloadReport(kind:'report'|'contrast'):Promise<void> {
    if(this.downloading())return;
    const serial=this.serial,source=this.source(),url=this.url()+'/'+kind,summary=this.statisticalSummary();
    this.downloading.set(true);this.downloadError.set(false);
    try {
      const data=await firstValueFrom(this.http.get(url,{responseType:'blob'}));
      if(serial!==this.serial)return;
      // Keep every original evidence payload/hash unchanged; append a separate display derivation.
      const saved=JSON.parse(await data.text());
      if(serial!==this.serial)return;
      const matches=kind==='report'
        ? saved.observation?.id===summary?.baselineObservationId && (saved.prediction?.sha256??null)===summary?.predictionSha256
        : saved.evidence?.sha256===summary?.contrastEvidenceSha256;
      const blob=new Blob([JSON.stringify({...saved,displayStatistics:matches?summary:null},null,2)],{type:'application/json'});
      const blobUrl=URL.createObjectURL(blob),link=document.createElement('a');
      link.href=blobUrl;link.download='prooflab-muse-'+kind+'-'+source.studyId+'.json';
      document.body.appendChild(link);
      try{link.click();}finally{link.remove();setTimeout(()=>URL.revokeObjectURL(blobUrl),1000);}
    }catch{if(serial===this.serial)this.downloadError.set(true);}
    finally{this.downloading.set(false);}
  }
  readonly headers=new HttpHeaders({'X-ProofLab-Manual-Action':'muse-research'});
  private readonly subscription=this.connection.changed.subscribe(()=>{
    this.resetInterpretationContext();
    if(!this.interpretationView())void this.load();
  });
  get es():boolean{return this.language()==='es';}
  text(value:Bilingual):string{return this.es?value.es:value.en;}
  url():string{return apiOrigin(this.connection.port())+'/api/science/director/pendulum/sessions/'+this.source().studyId+'/muse';}
  interpretationView():boolean{return this.view()==='interpretation';}
  private resetInterpretationContext():void {
    const key=JSON.stringify([this.connection.port(),this.source()]);
    if(key===this.interpretationContext)return;
    this.interpretationContext=key;this.serial++;
    this.interpretationOpened.set(false);
    this.state.set(null);this.contrast.set(null);this.followupLength.set(null);
    this.busy.set(null);this.error.set('');this.downloadError.set(false);
  }
  ngOnChanges():void {
    const previous=this.interpretationContext;
    this.resetInterpretationContext();
    if(this.interpretationView()) {
      // Only the Step 3 experience opts into automatic READ of the saved answer.
      // This GET is not a provider inference and spends no Meta credits.
      if(this.autoInterpretation() && (previous!==this.interpretationContext || !this.state())) {
        this.interpretationOpened.set(true);this.interpretationTab.set('summary');
        void this.load();
      }
    } else {
      this.reviewedPrediction.set(false);void this.load();
    }
  }
  async openInterpretation():Promise<void> {
    if(!this.interpretationView() || this.busy() || this.connection.busy())return;
    this.resetInterpretationContext();this.interpretationOpened.set(true);
    await this.load();
  }
  canConsultMuse():boolean {
    if(!this.interpretationView() || !!this.busy() || this.connection.busy() || !this.connection.hasToken())return false;
    const state=this.state();
    return !state?.evaluation && !state?.prediction && !state?.attemptStatus &&
      state?.manualCallsPermitted!==false;
  }
  async consultMuse():Promise<void> {
    // This explicit user action is the only entry to a paid call; merely mounting Understand remains read-free.
    if(!this.canConsultMuse())return;
    const context=JSON.stringify([this.connection.port(),this.source()]);
    if(!this.interpretationOpened())await this.openInterpretation();
    else if(!this.state())await this.load();
    // No auto-retry of previous attempts, no calls for an older video/analysis or frozen proposal.
    if(!this.canConsultMuse() || !this.interpretationOpened() || !this.state() ||
      JSON.stringify([this.connection.port(),this.source()])!==context)return;
    if(!this.interpretationObservationMatchesSource())await this.prepare();
    if(JSON.stringify([this.connection.port(),this.source()])!==context || !this.canEvaluate())return;
    await this.evaluate();
  }
  async freezePrediction():Promise<void> {
    const s=this.state(),source=this.source();
    if(this.isFollowup || this.busy() || this.connection.busy() ||
       !this.reviewedPrediction() || !s?.evaluation || !this.evaluationMatchesSource() ||
       s.prediction || s.evaluation.answer.decision!=='PROPOSE_EXPERIMENT' ||
       s.evaluation.answer.periodSeconds===null ||
       s.evaluation.answer.nextLengthMeters===null)return;
    const serial=++this.serial;this.busy.set('preparing');this.error.set('');
    try {
      const frozen=await firstValueFrom(this.http.post<MuseState>(this.url()+'/freeze',{
        observationId:s.evaluation.observationId,responseId:s.evaluation.responseId
      },{headers:this.headers}));
      if(serial===this.serial && this.source().studyId===source.studyId &&
         frozen.prediction?.observationId===s.evaluation.observationId){
        this.accept(frozen);
      }
    }catch{if(serial===this.serial)this.error.set(this.es?
      'No se pudo congelar la predicción. Confirma primero la revisión del seguimiento y la evidencia actual.':
      'Could not freeze the prediction. First confirm tracking review and current evidence.');}
    finally{if(serial===this.serial)this.busy.set(null);}
  }
  ngOnDestroy():void{
    if(this.modelTimer!==null)clearInterval(this.modelTimer);
    this.modelTimer=null;this.serial++;this.subscription.unsubscribe();
  }
  private accept(state:MuseState):void {
    this.state.set(state);
    // The interpretation view never unlocks prediction or bypasses the tracking review.
    if(!this.interpretationView())this.frozen.emit(state.prediction);
  }
  async load():Promise<void>{
    if(this.busy()==='evaluating' || (this.interpretationView() && !this.interpretationOpened()))return;
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
    if(this.busy()||this.connection.busy()||this.state()?.prediction ||
       (this.interpretationView() && !this.interpretationOpened()))return;
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
      (!this.interpretationView() || (this.interpretationOpened() && this.interpretationObservationMatchesSource()))&&
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
        try {
          const recovered=await firstValueFrom(this.http.get<MuseState>(url));
          if(serial===this.serial)this.accept(recovered);
        } catch {}
        if(serial===this.serial)this.error.set(this.es?'No se completó la evaluación. Revisa el intento guardado; no se reintentó automáticamente.':
          'Evaluation did not complete. Inspect the saved attempt; it was not automatically retried.');
      }
    }finally{this.connection.busy.set(false);if(serial===this.serial)this.busy.set(null);}
  }
  prediction():{periodSeconds:number;nextLengthMeters:number;tolerancePercent:number}|null {
    const p=this.state()?.prediction;
    if(!p)return null;
    try{return JSON.parse(p.payloadJson).prediction;}catch{return null;}
  }
  interpretationObservationMatchesSource():boolean {
    return museObservationMatchesSource(this.state()?.observation??null,this.source());
  }
  interpretationMatchesSource():boolean {
    const state=this.state(),observation=state?.observation,evaluation=state?.evaluation;
    return this.interpretationObservationMatchesSource() && !!observation && !!evaluation &&
      evaluation.observationId===observation.id && evaluation.studyId===this.source().studyId &&
      evaluation.model==='muse-spark-1.3' && museText(evaluation.responseId) &&
      museText(evaluation.rawResponseSha256);
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
