import { apiOrigin } from './api-origin';
import {Component,OnChanges,OnDestroy,SimpleChanges,inject,input,output,signal,computed,effect,untracked} from '@angular/core';
import {HttpClient,HttpHeaders} from '@angular/common/http';
import {firstValueFrom,timeout} from 'rxjs';
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
interface MuseContrastState {
  evidence:null|{studyId?:string;sha256:string;payloadJson:string};
  evaluation:null|{evidenceSha256?:string;model?:string;rawResponseSha256?:string;responseId:string;
    answer:{verdict:string;justification:Bilingual;limitations:Bilingual;nextStep:Bilingual}};
  attemptStatus:string|null;
}
// Stage navigation destroys the component, but a started HTTP request continues.
// Share only its promise for the exact evidence identity so a returning component
// does not race a second prepare against the server's study lock.
const contrastPreparations=new Map<string,Promise<MuseContrastState>>();
const contrastEvaluations=new Map<string,{evidenceSha256:string;promise:Promise<MuseContrastState>}>();
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
  readonly baselineLengthMeters=input<number|null>(null);
  readonly followupLengthMeters=input<number|null>(null);
  // Only the visible Comprobar step may start the already-authorized comparison.
  readonly autoContrast=input(false);
  readonly view=input<'research'|'interpretation'>('research');
  readonly chosenLengthCm=signal<number|null>(null);
  baselineLength():number|null {
    const configured=this.baselineLengthMeters();
    if(configured!==null && Number.isFinite(configured) && configured>0)return configured;
    try {const value=JSON.parse(this.state()?.observation?.payloadJson??'{}').baselineLengthMeters;
      return typeof value==='number' && Number.isFinite(value) && value>0?value:null;
    } catch {return null;}
  }
  predictionLengthError():string|null {
    const cm=this.chosenLengthCm();
    if(cm===null)return null;
    if(!Number.isFinite(cm)||cm<10||cm>200)
      return this.es?'Ingresa una longitud entre 10 y 200 cm.':'Enter a length from 10 to 200 cm.';
    const baseline=this.baselineLength();
    if(baseline!==null && Math.abs(cm/100-baseline)<=1e-6)
      return this.es?'Elige una longitud diferente a la primera prueba.':'Choose a different length from trial 1.';
    return null;
  }
  changePredictionLength(value:string):void {
    if(this.busy()||this.connection.busy()||this.state()?.prediction)return;
    this.chosenLengthCm.set(value.trim()===''?null:Number(value));
    this.reviewedPrediction.set(false);this.error.set('');
    this.modelRunning.set(false);this.modelTime.set(0);
    if(this.modelTimer!==null)clearInterval(this.modelTimer);
    this.modelTimer=null;
  }
  predictionObservationMatchesSource():boolean {
    const o=this.state()?.observation,cm=this.chosenLengthCm();
    if(cm===null || this.predictionLengthError() || !museObservationMatchesSource(o??null,this.source()))return false;
    try {const p=JSON.parse(o!.payloadJson);
      return p.identity?.purpose==='prediction' &&
        Math.abs(p.plannedFollowupLengthMeters-cm/100)<1e-8 &&
        Math.abs(p.identity.plannedFollowupLengthMeters-cm/100)<1e-8;
    } catch {return false;}
  }
  predictionEvaluationMatches():boolean {
    const s=this.state(),e=s?.evaluation;
    if(!e || !this.evaluationMatchesSource())return false;
    if(s?.prediction)return s.prediction.observationId===s.observation?.id &&
      s.prediction.responseId===e.responseId;
    return this.predictionObservationMatchesSource() &&
      (e.answer.decision!=='PROPOSE_EXPERIMENT' ||
        (typeof e.answer.nextLengthMeters==='number' &&
         Math.abs(e.answer.nextLengthMeters-this.chosenLengthCm()!/100)<1e-8));
  }
  canPredict():boolean {
    return !this.interpretationView() && !this.isFollowup && !!this.state() &&
      !this.busy() && !this.connection.busy() && !this.state()?.prediction &&
      this.chosenLengthCm()!==null && !this.predictionLengthError() &&
      this.connection.hasToken() && this.state()?.manualCallsPermitted!==false &&
      !this.predictionEvaluationMatches() &&
      !(this.predictionObservationMatchesSource() && !!this.state()?.attemptStatus);
  }
  async predictWithSpark():Promise<void> {
    if(!this.canPredict())return;
    const context=this.contextKey();
    await this.prepare();
    if(context!==this.contextKey() || this.error() || !this.predictionObservationMatchesSource())return;
    if(this.canEvaluate())await this.evaluate();
  }
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
       !this.predictionEvaluationMatches())return null;
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
      const payload=JSON.parse(evidence.payloadJson);
      const measuredLength=payload.followupLengthMeters;
      if(typeof measuredLength!=='number' || !Number.isFinite(measuredLength) ||
         Math.abs(measuredLength-frozen.nextLengthMeters)>1e-6)return null;
      const observed=payload.measurement?.assessment?.periodSeconds;
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
  private destroyed=false;
  readonly state=signal<MuseState|null>(null);
  readonly contrast=signal<MuseContrastState|null>(null);
  private readonly contrastLoaded=signal(false);
  private autoContrastRun='';
  readonly contrastSubmitted=signal(false);
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
  contrastMatches():boolean {
    try {
      const s=this.source(),e=this.contrast()?.evidence,f=this.state()?.prediction;
      if(!e || !f || s.role!=='followup' || s.providerModel!=='sam-3.1' ||
        ![s.studyId,s.analysisId,s.originalVideoSha256,s.providerResponseSha256,s.objectId,s.pivotProvenance,e.sha256,f.sha256].every(museText) ||
        f.studyId!==s.studyId || e.studyId!==s.studyId)return false;
      const p=JSON.parse(e.payloadJson),r=p.request,m=p.measurement?.source;
      return p.studyId===s.studyId && p.analysisId===s.analysisId && p.sourceSha256===s.originalVideoSha256 &&
        p.responseSha256===s.providerResponseSha256 && p.predictionSha256===f.sha256 &&
        p.followupLengthMeters===this.followupLength() &&
        r?.videoSha256===s.originalVideoSha256 && r.analysisId===s.analysisId &&
        r.pivotRevisionId===s.pivotRevisionId && (r.adaptiveBatchId??null)===(s.adaptiveBatchId??null) &&
        r.followupLengthMeters===p.followupLengthMeters &&
        m?.studyId===s.studyId && m.role==='followup' && m.originalVideoSha256===s.originalVideoSha256 &&
        m.analysisId===s.analysisId && m.providerResponseSha256===s.providerResponseSha256 &&
        m.providerModel===s.providerModel && m.objectId===s.objectId &&
        m.pivotRevisionId===s.pivotRevisionId && m.pivotProvenance===s.pivotProvenance;
    } catch {return false;}
  }
  contrastEvaluationMatches():boolean {
    const c=this.contrast(),e=c?.evaluation;
    return this.contrastMatches() && !!e && e.evidenceSha256===c?.evidence?.sha256 &&
      e.model==='muse-spark-1.3' && museText(e.responseId) && museText(e.rawResponseSha256);
  }
  private comparisonKey():string {return this.contextKey()+'|'+this.state()?.prediction?.sha256+'|'+this.followupLength();}
  private comparisonMarker():string {return 'prooflab.muse-contrast-submitted.'+this.comparisonKey();}
  private wasContrastSubmitted():boolean {
    if(this.contrastSubmitted())return true;
    try {return sessionStorage.getItem(this.comparisonMarker())==='submitted';}catch{return false;}
  }
  private markContrastSubmitted():void {
    this.contrastSubmitted.set(true);
    // Only a non-secret identity marker, never a token or scientific result. A reload
    // may reconcile a lost response, but must not send the same request again.
    try {sessionStorage.setItem(this.comparisonMarker(),'submitted');}catch{}
  }
  private readonly automaticComparison=effect(()=>{
    this.autoContrast();this.source();this.view();this.followupLengthMeters();
    this.state();this.contrast();this.contrastLoaded();this.busy();
    this.connection.hasToken();this.connection.busy();
    untracked(()=>{void this.runAutomaticContrast();});
  });
  private async runAutomaticContrast():Promise<void> {
    if(this.destroyed || !this.autoContrast() || this.interpretationView() || !this.isFollowup || !this.contrastLoaded() ||
      this.busy() || this.error() || !this.state()?.prediction || !this.followupLength())return;
    const key=this.comparisonKey();
    const running=contrastEvaluations.get(this.url()+'|'+key);
    if(running && this.contrastMatches() && running.evidenceSha256===this.contrast()?.evidence?.sha256){
      this.autoContrastRun=key;
      await this.receiveContrastEvaluation(running.promise);
      return;
    }
    if(this.connection.busy() || !this.connection.hasToken() || !this.state()?.manualCallsPermitted)return;
    if(this.autoContrastRun===key || this.wasContrastSubmitted() || this.contrast()?.attemptStatus ||
      this.contrast()?.evaluation || (this.contrast()?.evidence && !this.contrastMatches()))return;
    this.autoContrastRun=key;
    if(!this.contrast()?.evidence)await this.prepareContrast();
    if(this.destroyed || key!==this.comparisonKey() || this.error())return;
    if(!this.autoContrast() || this.connection.busy() || !this.connection.hasToken()){
      this.autoContrastRun='';return;
    }
    if(this.canContrast())await this.evaluateContrast();
  }
  comparisonStatus():string {
    if(this.contrastEvaluationMatches())return '';
    if(this.contrast()?.evidence && !this.contrastMatches())return this.es?
      'El contraste guardado pertenece a otra medición o predicción. No se enviará una nueva consulta automáticamente.':
      'The saved comparison belongs to another measurement or prediction. No new request will be sent automatically.';
    if(this.contrast()?.evaluation)return this.es?'No se pudo vincular la respuesta con la evidencia actual.':'The response could not be linked to the current evidence.';
    if((this.contrast()?.attemptStatus || this.wasContrastSubmitted()) && this.connection.busy())return this.es?
      'Muse está comparando las mediciones con tu predicción. La consulta ya está en curso.':
      'Muse is comparing measurements with your prediction. The request is already in progress.';
    if(this.contrast()?.attemptStatus || this.wasContrastSubmitted())return this.es?
      'La consulta ya fue enviada. Recupera su estado guardado; no se repetirá automáticamente.':
      'The request has already been sent. Check its saved status; it will not be repeated automatically.';
    if(!this.state()?.prediction)return this.es?'Falta una predicción congelada para comparar.':'A frozen prediction is required for comparison.';
    if(!this.connection.hasToken())return this.es?'Configura el token para completar automáticamente el contraste.':'Configure the token to complete the comparison automatically.';
    if(!this.state()?.manualCallsPermitted)return this.es?'El servidor no permite nuevas consultas de pago.':'The server does not permit new paid requests.';
    if(this.connection.busy())return this.es?'Esperando a que termine el procesamiento del video…':'Waiting for video processing to finish…';
    return this.es?'El contraste se prepara automáticamente con las mediciones de la segunda prueba y la predicción guardada.':
      'The comparison is prepared automatically using trial 2 measurements and the saved prediction.';
  }
  async refreshContrastStatus():Promise<void> {
    this.autoContrastRun=this.comparisonKey();
    await this.load();
  }
  async prepareContrast():Promise<void>{
    if(this.destroyed||!this.autoContrast()||!this.isFollowup||this.busy()||this.connection.busy()||!this.followupLength()||this.contrast()?.evidence)return;
    const serial=++this.serial,source=this.source(),url=this.url()+'/contrast/prepare',key=url+'|'+this.comparisonKey();
    this.busy.set('preparing');this.error.set('');
    try{
      let pending=contrastPreparations.get(key);
      if(!pending){
        pending=firstValueFrom(this.http.post<MuseContrastState>(url,{
          videoSha256:source.originalVideoSha256,analysisId:source.analysisId,pivotRevisionId:source.pivotRevisionId,
          adaptiveBatchId:source.adaptiveBatchId??null,followupLengthMeters:this.followupLength()
        },{headers:this.headers}).pipe(timeout(60000)));
        contrastPreparations.set(key,pending);
        const release=()=>{if(contrastPreparations.get(key)===pending)contrastPreparations.delete(key);};
        // Register both outcomes without creating an unhandled rejected finally promise.
        void pending.then(release,release);
      }
      const c=await pending;if(!this.destroyed && serial===this.serial)this.contrast.set(c);
    }
    catch{if(serial===this.serial)this.error.set(this.es?'No se pudo verificar el contraste. Revisa la predicción y el resultado SAM.':'Could not verify comparison. Check the prediction and SAM result.');}
    finally{if(serial===this.serial)this.busy.set(null);}
  }
  canContrast():boolean{return this.autoContrast()&&this.isFollowup&&!this.interpretationView()&&!this.busy()&&!this.connection.busy()&&this.connection.hasToken()&&!!this.state()?.manualCallsPermitted&&this.contrastMatches()&&!this.contrast()?.evaluation&&!this.contrast()?.attemptStatus&&!this.wasContrastSubmitted();}
  async evaluateContrast():Promise<void>{
    if(!this.canContrast())return;const token=this.connection.tokenForManualRequest();if(!token)return;
    const url=this.url(),key=url+'|'+this.comparisonKey(),evidenceSha256=this.contrast()!.evidence!.sha256;
    this.markContrastSubmitted();this.connection.busy.set(true);
    const pending=firstValueFrom(this.http.post<MuseContrastState>(url+'/contrast/evaluate',{
      evidenceSha256,modelApiToken:token,confirmMeasuredDataTransfer:true,confirmPaidCall:true,maximumEstimatedCostUsd:.05
    },{headers:this.headers}).pipe(timeout(195000))).catch(()=>
      firstValueFrom(this.http.get<MuseContrastState>(url+'/contrast').pipe(timeout(15000))));
    const shared={evidenceSha256,promise:pending};contrastEvaluations.set(key,shared);
    const release=()=>{if(contrastEvaluations.get(key)===shared)contrastEvaluations.delete(key);};
    void pending.then(release,release);
    try{await this.receiveContrastEvaluation(pending);}
    // Only the original operation owns this shared lock. A mounted observer never releases it.
    finally{this.connection.busy.set(false);}
  }
  private async receiveContrastEvaluation(pending:Promise<MuseContrastState>):Promise<void>{
    const serial=++this.serial;this.busy.set('evaluating');this.error.set('');
    try{
      const c=await pending;if(!this.destroyed && serial===this.serial)this.contrast.set(c);
    }catch{/* The shared operation already performed its single read-only reconciliation. */}
    finally{
      if(!this.destroyed && serial===this.serial){
        if(!this.contrastEvaluationMatches())this.error.set(this.es?
          'No se pudo confirmar el resultado. La consulta no se repetirá; puedes recuperar su estado guardado.':
          'Could not confirm the result. The request will not be repeated; you can check its saved status.');
        this.busy.set(null);
      }
    }
  }
  contrastVerdict():string {switch(this.contrast()?.evaluation?.answer.verdict){
    case 'WITHIN_PREDECLARED_BAND':return this.es?'Compatible con la predicción dentro del margen':'Consistent with the prediction within tolerance';
    case 'OUTSIDE_PREDECLARED_BAND':return this.es?'Fuera del margen de la predicción':'Outside the prediction tolerance';
    default:return this.es?'Contraste inconcluso':'Inconclusive comparison';}}

  readonly busy=signal<'loading'|'preparing'|'evaluating'|'freezing'|null>(null);
  readonly error=signal('');
  readonly downloading=signal(false);
  readonly downloadError=signal(false);
  async downloadReport(kind:'report'|'contrast'):Promise<void> {
    if(this.downloading())return;
    const serial=this.serial,source=this.source(),url=this.url()+'/'+kind,summary=this.statisticalSummary();
    this.downloading.set(true);this.downloadError.set(false);
    try {
      const data=await firstValueFrom(this.http.get(url+(kind==='report' && !this.interpretationView()?'?purpose=prediction':''),{responseType:'blob'}));
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
    if(!this.resetInterpretationContext())return;
    if(!this.interpretationView() || this.autoInterpretation()) {
      if(this.interpretationView())this.interpretationOpened.set(true);
      void this.load();
    }
  });
  get es():boolean{return this.language()==='es';}
  text(value:Bilingual):string{return this.es?value.es:value.en;}
  url():string{return apiOrigin(this.connection.port())+'/api/science/director/pendulum/sessions/'+this.source().studyId+'/muse';}
  stateUrl():string{return this.url()+(this.interpretationView()?'':'?purpose=prediction');}
  interpretationView():boolean{return this.view()==='interpretation';}
  private contextKey():string {
    const s=this.source();
    return JSON.stringify([this.interpretationView()?'interpretation':'prediction',this.connection.port(),s.studyId,s.role,s.originalVideoSha256,
      s.analysisId,s.providerResponseSha256,s.providerModel,s.objectId,
      s.pivotRevisionId,s.pivotProvenance,s.adaptiveBatchId??null]);
  }
  private resetInterpretationContext():boolean {
    const key=this.contextKey();
    if(key===this.interpretationContext)return false;
    this.interpretationContext=key;this.serial++;
    this.interpretationOpened.set(false);this.reviewedPrediction.set(false);
    this.state.set(null);this.contrast.set(null);this.followupLength.set(null);
    this.contrastLoaded.set(false);this.contrastSubmitted.set(false);this.autoContrastRun='';
    this.chosenLengthCm.set(null);
    this.busy.set(null);this.error.set('');this.downloadError.set(false);
    return true;
  }
  ngOnChanges(changes:SimpleChanges):void {
    const changed=this.resetInterpretationContext();
    if(this.isFollowup && changes['followupLengthMeters'])this.setComparisonLength();
    // Presentation changes do not change scientific evidence or cancel its pending save.
    if(this.interpretationView()) {
      if(this.state())this.interpretationOpened.set(true);
      if(this.autoInterpretation() && (changed || changes['view'] || changes['autoInterpretation'])) {
        this.interpretationOpened.set(true);
        if(!this.state())void this.load();
      }
    } else if(changed || changes['view']) {
      if(this.state())this.frozen.emit(this.state()!.prediction);
      else void this.load();
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
    // This explicit user action is the only entry to a paid call; mounting Understand only recovers stored evidence.
    if(!this.canConsultMuse())return;
    const context=this.contextKey();
    if(!this.interpretationOpened())await this.openInterpretation();
    else if(!this.state())await this.load();
    // No auto-retry of previous attempts, no calls for an older video/analysis or frozen proposal.
    if(!this.canConsultMuse() || !this.interpretationOpened() || !this.state() ||
      this.contextKey()!==context)return;
    if(!this.interpretationObservationMatchesSource())await this.prepare();
    if(this.contextKey()!==context || !this.canEvaluate())return;
    await this.evaluate();
  }
  async freezePrediction():Promise<void> {
    const s=this.state(),source=this.source();
    if(this.isFollowup || this.busy() || this.connection.busy() ||
       !this.reviewedPrediction() || !s?.evaluation || !this.predictionEvaluationMatches() ||
       s.prediction || s.evaluation.answer.decision!=='PROPOSE_EXPERIMENT' ||
       s.evaluation.answer.periodSeconds===null ||
       s.evaluation.answer.nextLengthMeters===null)return;
    const serial=++this.serial,url=this.url();this.busy.set('freezing');this.connection.busy.set(true);this.error.set('');
    try {
      const frozen=await firstValueFrom(this.http.post<MuseState>(url+'/freeze',{
        observationId:s.evaluation.observationId,responseId:s.evaluation.responseId
      },{headers:this.headers}));
      if(serial===this.serial && this.source().studyId===source.studyId &&
         frozen.prediction?.observationId===s.evaluation.observationId){
        this.accept(frozen);
      }
    }catch{
      // A lost response may still have committed the immutable prediction. Reconcile with
      // one read, never a second freeze or provider request.
      if(serial===this.serial) {
        try {
          const recovered=await firstValueFrom(this.http.get<MuseState>(url+'?purpose=prediction'));
          if(serial===this.serial && recovered.prediction?.studyId===source.studyId &&
             recovered.prediction.observationId===s.evaluation.observationId &&
             recovered.prediction.responseId===s.evaluation.responseId) {
            this.accept(recovered);return;
          }
        } catch {}
        if(serial===this.serial)this.error.set(this.es?
          'No se pudo confirmar el guardado de la predicción. Tus resultados siguen visibles; revisa el estado antes de continuar.':
          'Could not confirm that the prediction was saved. Your results remain visible; check the status before continuing.');
      }
    }
    finally{this.connection.busy.set(false);if(serial===this.serial)this.busy.set(null);}
  }
  ngOnDestroy():void{
    if(this.modelTimer!==null)clearInterval(this.modelTimer);
    this.modelTimer=null;this.destroyed=true;this.serial++;this.subscription.unsubscribe();
  }
  private accept(state:MuseState):void {
    this.state.set(state);
    if(!this.interpretationView() && !this.isFollowup &&
       (this.chosenLengthCm()===null || state.prediction)) {
      try {const payload=JSON.parse(state.prediction?.payloadJson??state.observation?.payloadJson??'{}');
        const length=state.prediction?payload.prediction?.nextLengthMeters:
          payload.identity?.purpose==='prediction'?payload.plannedFollowupLengthMeters:null;
        if(typeof length==='number' && Number.isFinite(length))this.chosenLengthCm.set(length*100);
      } catch { /* An invalid record never supplies the editable choice. */ }
    }
    // The interpretation view never unlocks prediction or bypasses the tracking review.
    if(!this.interpretationView())this.frozen.emit(state.prediction);
  }
  private setComparisonLength():void {
    const actual=this.followupLengthMeters(),frozen=this.prediction()?.nextLengthMeters;
    // Reuse the already declared length. A differing actual length stays different so
    // the backend can report INCONCLUSIVE; never replace it with the frozen target.
    this.followupLength.set(typeof actual==='number' && Number.isFinite(actual) && actual>0?actual:frozen??null);
  }
  async load():Promise<void>{
    if(this.busy() || (this.interpretationView() && !this.interpretationOpened()))return;
    const serial=++this.serial,url=this.url(),followup=this.isFollowup;
    this.busy.set('loading');this.error.set('');
    // Keep the current result visible while its server record is refreshed. Only a real
    // source change clears it; a slow or failed GET must not look like lost evidence.
    try {
      const state=await firstValueFrom(this.http.get<MuseState>(this.stateUrl()).pipe(timeout(15000)));
      if(serial===this.serial){this.accept(state);this.setComparisonLength();}
      if(serial===this.serial&&followup){
        const c=await firstValueFrom(this.http.get<MuseContrastState>(url+'/contrast').pipe(timeout(15000)));
        if(serial===this.serial){this.contrast.set(c);this.contrastLoaded.set(true);
          this.contrastSubmitted.set(this.wasContrastSubmitted());}
      }
    } catch {if(serial===this.serial)this.error.set(this.es?'No se pudo consultar la bitácora de Muse.':'Could not load the Muse lab log.');}
    finally {if(serial===this.serial)this.busy.set(null);}
  }
  async prepare():Promise<void>{
    if(this.busy()||this.connection.busy()||this.state()?.prediction ||
       (this.interpretationView() && !this.interpretationOpened()))return;
    if(!this.interpretationView() && (this.isFollowup || this.chosenLengthCm()===null || this.predictionLengthError()))return;
    const serial=++this.serial,source=this.source();
    this.busy.set('preparing');this.error.set('');
    try {
      const state=await firstValueFrom(this.http.post<MuseState>(this.url()+'/prepare',{
        videoSha256:source.originalVideoSha256,analysisId:source.analysisId,pivotRevisionId:source.pivotRevisionId,adaptiveBatchId:source.adaptiveBatchId??null,
        ...(!this.interpretationView()?{purpose:'prediction',plannedFollowupLengthMeters:this.chosenLengthCm()!/100}:{})
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
      (this.interpretationView()?(this.interpretationOpened() && this.interpretationObservationMatchesSource()):this.predictionObservationMatchesSource())&&
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
          const recovered=await firstValueFrom(this.http.get<MuseState>(this.stateUrl()));
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
