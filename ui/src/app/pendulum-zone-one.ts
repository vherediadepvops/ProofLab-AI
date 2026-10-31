import { apiOrigin, isLocalWorkspace } from './api-origin';
import type {FrozenMusePrediction} from './muse-investigator';
import { ExperimentSimulator } from './experiment-simulator';
import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal, input, effect, untracked, DestroyRef, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, timeout } from 'rxjs';
import { LabActivity } from './lab-activity';
import { PendulumDirector, type DirectorSession } from './pendulum-director';
import { PendulumMasterPlayer } from './pendulum-master-player';
import { LabConnection } from './lab-connection';
import { SamVideoReplay, type SamProcessingSelection } from './sam-video-replay';

/**
 * Jury-facing Zone 1. Reuses the same validated Director session, upload,
 * model parameters and evidence endpoints; this class only changes the
 * presentation. The full/advanced Director remains preserved in the repo.
 */
interface RecoverySnapshot {
  session:DirectorSession;
  operations:{id:string;role:'baseline'|'followup';stage:string;state:string;startedAtUtc:string;finishedAtUtc:string|null}[];
  selections:Omit<SamProcessingSelection,'port'>[];
  unavailable:string[];
}

@Component({
  selector: 'app-pendulum-zone-one',
  standalone: true,
  imports: [CommonModule, FormsModule, PendulumMasterPlayer, SamVideoReplay, LabActivity, ExperimentSimulator],
  templateUrl: './pendulum-zone-one.html',
  styleUrl: './pendulum-zone-one.scss'
})
export class PendulumZoneOne extends PendulumDirector {
  readonly localDemoAvailable=isLocalWorkspace();
  private readonly demoHttp=inject(HttpClient);
  @ViewChild(PendulumMasterPlayer) private currentPlayer?:PendulumMasterPlayer;
  // Presentation state only. Evidence, costs, recovery and predictions remain server-owned.
  readonly active=input(true);
  readonly experiment=signal<'pendulum'|'ramp'|'rocket'>('pendulum');
  readonly flowStep=signal<1|2|3|4|5>(1);
  readonly recoveryMenuOpen=signal(false);
  readonly recoveryWarning=signal(false);
  private readonly verifiedRoles=signal<Partial<Record<'baseline'|'followup',string>>>({});
  private advanceAfterProcessing=false;
  private recoveredDestination:1|2|3|4|5|null=null;
  readonly flowSteps=[
    {id:1 as const,es:'Preparar',en:'Prepare',subEs:'Tu experimento',subEn:'Your experiment'},
    {id:2 as const,es:'Analizar',en:'Analyze',subEs:'Muse Spark + SAM',subEn:'Muse Spark + SAM'},
    {id:3 as const,es:'Entender',en:'Understand',subEs:'Mediciones y evidencia',subEn:'Measurements and evidence'},
    {id:4 as const,es:'Predecir',en:'Predict',subEs:'Muse Spark propone',subEn:'Muse Spark proposes'},
    {id:5 as const,es:'Comprobar',en:'Compare',subEs:'Comparar resultados',subEn:'Compare results'}
  ];
  stepLabel(id:number):string {return id<this.flowStep()?'✓':'0'+id;}
  flowBusy():boolean {
    return this.recoveryControlsBusy() || this.resultState()==='loading';
  }
  simulationKind():'ramp'|'rocket' {return this.experiment()==='rocket'?'rocket':'ramp';}
  chooseExperiment(kind:'pendulum'|'ramp'|'rocket'):void {
    if(this.flowBusy())return;
    this.currentPlayer?.pausePlayback();this.experiment.set(kind);
  }
  private hasSavedSelection(role:'baseline'|'followup'):boolean {
    const selection=this.roleSelections[role],s=this.session(),clip=role==='baseline'?s?.baseline:s?.followup;
    return !!selection && selection.studyId===s?.id && selection.sourceSha256===clip?.sha256 &&
      selection.port===this.connection.port() && /^[a-f0-9]{32}$/.test(selection.analysisId);
  }
  hasVerifiedResult(role:'baseline'|'followup'):boolean {
    return this.hasSavedSelection(role) && this.verifiedRoles()[role]===this.roleSelections[role]?.analysisId;
  }
  canVisitStep(step:number):boolean {
    if(!this.session() || this.flowBusy())return false;
    if(step===1)return true;
    if(step===2)return !!this.session()?.baseline;
    // Navigation may reopen archived evidence; the player still verifies it before showing layers.
    if(step===3)return this.hasSavedSelection('baseline');
    if(step===4)return !!this.frozenForStudy() || (this.hasVerifiedResult('baseline') && !!this.session()?.humanReview);
    return step===5 && !!this.frozenForStudy() && this.hasVerifiedResult('followup');
  }
  setJourneyStep(step:1|2|3|4|5):void {
    if(!this.canVisitStep(step))return;
    const role=step===5 || step===4&&!!this.session()?.followup?'followup':'baseline';
    if(role!==this.activeTrial())this.selectTrial(role);
    if(role!==this.activeTrial())return;
    this.flowStep.set(step);
  }
  presentation():'source'|'understand'|'predict'|'compare' {
    if(this.flowStep()<=2 || this.flowStep()===4&&this.activeTrial()==='followup')return 'source';
    return this.flowStep()===3?'understand':this.flowStep()===4?'predict':'compare';
  }
  showProcessing():boolean {return this.flowStep()===2 || this.flowStep()===4&&this.activeTrial()==='followup'&&!!this.session()?.followup;}
  currentPhysicalLength():number|null {
    return this.activeTrial()==='baseline'?this.session()?.baselineLengthMeters??null:this.nextResearchLength();
  }
  stageTitle():string {
    const labels=this.es
      ? ['Prepara una pregunta que puedas comprobar.','Procesa el video, paso a paso.','Primero, entiende lo que mediste.','Registra una predicción antes de la siguiente prueba.','¿La evidencia coincide con la predicción?']
      : ['Prepare a question you can test.','Process the video, step by step.','First, understand what you measured.','Record a prediction before the next trial.','Does the evidence match the prediction?'];
    return labels[this.flowStep()-1];
  }
  stageDescription():string {
    const labels=this.es
      ? ['La medida física y tu grabación original son el punto de partida.','Revisa el costo antes de procesar. El video se conserva sin capas durante el análisis.','Las medidas vienen de posiciones y tiempos observados. Muse Spark interpreta la evidencia.','La predicción registrada se conserva antes de cargar el segundo video.','Compara las mediciones del segundo video con el margen fijado de antemano.']
      : ['Your physical measurement and original recording are the starting point.','Review the cost before processing. The video stays free of overlays during analysis.','Measurements come from observed positions and times. Muse Spark interprets the evidence.','The registered prediction is preserved before video 2 is uploaded.','Compare measurements from video 2 with the band declared in advance.'];
    return labels[this.flowStep()-1];
  }
  async continueToPrediction():Promise<void> {
    if(this.flowBusy() || !this.hasVerifiedResult('baseline'))return;
    if(!this.session()?.humanReview) {
      if(!this.checkedVideo())return;await this.confirm();
    }
    if(this.session()?.humanReview)this.setJourneyStep(4);
  }
  toggleRecoveryMenu():void {if(!this.recoveryControlsBusy())this.recoveryMenuOpen.update(v=>!v);}
  closeRecoveryMenu(event?:Event):void {
    if(this.recoveryLoading())return;
    this.recoveryMenuOpen.set(false);event?.preventDefault();
    document.getElementById('manage-recovery')?.focus();
  }
  async createNewResearch():Promise<void> {
    if(this.flowBusy())return;this.newFocusedStudy();await this.generateRecoveryCode();
  }
  closeResearch():void {
    if(this.flowBusy())return;this.newFocusedStudy();this.choosingUpload.set(false);this.recoveryMenuOpen.set(false);
    this.recoveryMessage.set(this.es?'Sesión cerrada. Puedes recuperarla con su código.':'Session closed. Recover it with its code.');
  }
  readonly resumeRequest=input<{id:string;nonce:number}|null>(null);
  readonly recoveryCode=signal('');
  readonly generatingCode=signal(false);
  readonly recoveryFailure=signal(false);
  readonly draftSaving=signal(false);
  readonly draftError=signal('');
  private draftQueue:Promise<boolean>=Promise.resolve(true);
  private draftWrites=0;
  readonly recoveryLoading=computed(()=>this.recovering() ||
    (this.recoveryMode && (this.previewState()==='loading' || this.resultState()==='loading')));
  recoveryControlsBusy():boolean {return this.generatingCode() || this.recoveryLoading() || this.draftSaving() || this.sourcePending() || this.busy() || this.connection.busy();}
  validRecoveryCode():boolean {return /^[a-f0-9]{32}$/i.test(this.recoveryCode().trim());}
  async generateRecoveryCode():Promise<void> {
    if(this.session() || this.recoveryControlsBusy())return;
    this.generatingCode.set(true);this.recoveryMessage.set('');this.recoveryFailure.set(false);this.recoveryWarning.set(false);
    try {
      await this.refreshTheory();await this.start();
      if(this.session()){
        this.copiedId.set(false);this.recoveryMode=false;this.draftError.set('');this.recoveryMenuOpen.set(false);this.activeTrial.set('baseline');this.flowStep.set(1);
        this.recoveryMessage.set(this.es?'Código generado. Cópialo y guárdalo antes de continuar.':'Code generated. Copy and save it before continuing.');
      } else {this.recoveryFailure.set(true);this.recoveryMessage.set(this.error() || this.modelError());}
    } finally {this.generatingCode.set(false);}
  }
  updateDraft():void {
    void this.refreshTheory();
    const current=this.session();if(!current || current.baseline)return;
    const payload={baselineLengthMeters:this.baselineLength(),angleDegrees:this.initialAngleDegrees(),massGrams:this.massGrams(),
      includeSecondTrial:this.includeSecondTrial(),followUpMode:this.followUpMode(),
      selectedFollowUpLengthMeters:this.includeSecondTrial() && this.followUpMode()==='manual'?this.manualFollowUpLength():null};
    const url=this.api+'/'+current.id+'/draft';
    this.draftWrites++;this.draftSaving.set(true);this.draftError.set('');
    this.draftQueue=this.draftQueue.then(async()=>{
      try {
        const saved=await firstValueFrom(this.demoHttp.put<DirectorSession>(url,payload).pipe(timeout(30000)));
        if(this.session()?.id===current.id)this.session.set(saved);
        this.draftError.set('');return true;
      } catch {
        this.draftError.set(this.es?'No se guardaron los parámetros. Revisa la conexión y pulsa Reintentar guardado.':'Parameters were not saved. Check the connection and retry saving.');return false;
      } finally {this.draftSaving.set(--this.draftWrites>0);}
    });
  }
  readonly recovering=signal(false);
  readonly recoveringOperation=signal('');
  readonly recoveryMessage=signal('');
  readonly copiedId=signal(false);
  private recoveryMode=false;
  private recoveryGeneration=0;
  private recoveryTimer:ReturnType<typeof setTimeout>|null=null;
  private roleSelections:Partial<Record<'baseline'|'followup',SamProcessingSelection>>={};
  constructor(){
    super();
    effect(()=>{const request=this.resumeRequest();if(request)untracked(()=>void this.resumeResearch(request.id));});
    inject(DestroyRef).onDestroy(()=>{this.recoveryGeneration++;if(this.recoveryTimer)clearTimeout(this.recoveryTimer);});
  }
  async copyStudyId():Promise<void> {
    const id=this.session()?.id;if(!id)return;
    try{await navigator.clipboard.writeText(id);this.copiedId.set(true);}catch{this.copiedId.set(false);const field=document.getElementById('active-recovery-code') as HTMLInputElement|null;field?.focus();field?.select();this.recoveryWarning.set(true);this.recoveryMessage.set(this.es?'Selecciona el código y cópialo con Ctrl+C o Cmd+C.':'Select the code and copy it with Ctrl+C or Cmd+C.');}
  }
  async resumeResearch(id:string):Promise<void> {
    id=id.trim().toLowerCase();
    if(this.recoveryControlsBusy())return;
    if(!/^[a-f0-9]{32}$/.test(id)){this.recoveryFailure.set(true);this.recoveryMessage.set(this.es?'Introduce un código de recuperación válido de 32 caracteres.':'Enter a valid 32-character recovery code.');return;}
    this.recoveryCode.set(id);this.recoveryFailure.set(false);this.recoveryWarning.set(false);this.draftError.set('');this.verifiedRoles.set({});this.recoveredDestination=null;
    const generation=++this.recoveryGeneration;
    if(this.recoveryTimer)clearTimeout(this.recoveryTimer);
    this.copiedId.set(false);this.recoveryMode=true;this.recovering.set(true);this.recoveryMessage.set('');this.error.set('');this.recoveringOperation.set('');
    this.roleSelections={};this.clearProcessed();this.session.set(null);this.previewState.set('empty');
    const port=this.connection.port();
    const poll=async(first:boolean):Promise<void>=>{
      try {
        const snapshot=await firstValueFrom(this.demoHttp.get<RecoverySnapshot>(
          apiOrigin(port)+'/api/science/director/pendulum/sessions/'+id+'/recovery').pipe(timeout(30000)));
        if(generation!==this.recoveryGeneration)return;
        if(port!==this.connection.port()){this.recovering.set(false);this.recoveringOperation.set('');return;}
        if(snapshot.session.id!==id)throw Error('STUDY_MISMATCH');
        if(first){
          this.applyRestoredSession(snapshot.session);this.musePrediction.set(snapshot.session.musePrediction??null);
          this.sourceChoice.set('upload');this.choosingUpload.set(false);this.previewState.set(snapshot.session.baseline?'loading':'empty');
          this.activeTrial.set(snapshot.session.followup?'followup':'baseline');
          this.flowStep.set(snapshot.session.followup||snapshot.session.musePrediction?4:snapshot.session.baseline?2:1);
        } else {
          this.session.set(snapshot.session);this.musePrediction.set(snapshot.session.musePrediction??null);
        }
        const running=snapshot.operations.filter(o=>o.state==='RUNNING');
        this.recoveringOperation.set(running.map(o=>o.stage).join(' · '));
        for(const result of snapshot.selections){
          const clip=result.videoRole==='followup'?snapshot.session.followup:snapshot.session.baseline;
          if(result.studyId===id && result.sourceSha256===clip?.sha256 && /^[a-f0-9]{32}$/.test(result.analysisId))
            this.roleSelections[result.videoRole]={...result,port};
        }
        const selected=this.roleSelections[this.activeTrial()];
        if(first && selected)this.recoveredDestination=this.activeTrial()==='followup'?(snapshot.session.musePrediction?5:4):snapshot.session.musePrediction?4:3;
        if(selected && selected.analysisId!==this.samSelection()?.analysisId){this.acceptProcessed(selected);this.resultState.set('loading');}
        const recent=snapshot.operations.some(o=>o.finishedAtUtc && Date.now()-Date.parse(o.finishedAtUtc)<6000);
        if(running.length || recent){
          this.recovering.set(true);
          this.recoveryMessage.set(this.es?'Solicitud guardada en curso. Recuperando su estado; no se envía otra petición.':'Saved request in progress. Recovering status without resending.');
          this.recoveryTimer=setTimeout(()=>void poll(false),2500);
        } else {
          this.recovering.set(false);this.recoveringOperation.set('');this.recoveryMenuOpen.set(false);
          const interrupted=snapshot.operations.some(o=>o.state==='INTERRUPTED' || o.state==='REVIEW_REQUIRED');
          this.recoveryWarning.set(interrupted || snapshot.unavailable.length>0);
          this.recoveryMessage.set(interrupted?(this.es?'Investigación recuperada. Hay intentos interrumpidos o pendientes de revisión; no se han reenviado.':'Study restored. Interrupted or unresolved attempts were not resent.'):
            snapshot.unavailable.length?(this.es?'Investigación recuperada. Parte de la evidencia guardada no pudo verificarse; no se ha sustituido ni reenviado.':'Study restored. Some saved evidence could not be verified; it was neither replaced nor resent.'):
            snapshot.selections.length?(this.es?'Investigación recuperada con sus resultados guardados. No se han realizado llamadas a Meta.':'Study restored with saved results. No Meta calls were made.'):
            (this.es?'Investigación recuperada. El análisis pendiente continúa únicamente al pulsar Procesar.':'Study restored. Pending analysis continues only when you press Process.'));
        }
      } catch (failure) {
        if(generation!==this.recoveryGeneration)return;
        this.recovering.set(false);this.recoveringOperation.set('');this.recoveryFailure.set(true);
        this.recoveryMessage.set(failure instanceof HttpErrorResponse && failure.status===404
          ? (this.es?'No se encontró ninguna sesión con ese código. Revisa el código e inténtalo de nuevo.':'No session was found with that code. Check it and try again.')
          : (this.es?'No se pudo recuperar la sesión. Comprueba la conexión con el servidor y vuelve a pulsar Buscar.':'Could not recover the session. Check the server connection and press Search again.'));
      }
    };
    await poll(true);
  }
  hasFollowupResult():boolean {return !!this.roleSelections.followup || this.samSelection()?.videoRole==='followup';}
  onProcessingReset():void {if(!this.recoveryMode)this.clearProcessed();}

  readonly musePrediction=signal<FrozenMusePrediction|null>(null);

  // An independent ideal-model illustration. Never writes to, fits or replaces
  // SAM measurements, the source video, or the registered research inputs.
  readonly theoryPositionPercent=signal(0);
  updateTheoryPosition(event:Event):void {
    const value=Number((event.target as HTMLInputElement).value);
    if(Number.isFinite(value))
      this.theoryPositionPercent.set(Math.max(-100,Math.min(100,value)));
  }
  readonly theoreticalPreview=computed(()=>{
    const model=this.model(),trial=model?.baseline;
    if(!trial || this.modelLoading() || this.modelError() ||
       model?.source!=='DETERMINISTIC_NONLINEAR_RK4_THEORY_NOT_META_NOT_SAM' ||
       Math.abs(trial.lengthMeters-this.baselineLength())>1e-6 ||
       Math.abs(trial.initialAngleDegrees-this.initialAngleDegrees())>1e-6 ||
       Math.abs(trial.massGrams-this.massGrams())>1e-6)return null;
    const length=trial.lengthMeters,amplitude=trial.initialAngleDegrees;
    const gravity=9.80665; // Matches ScientificModels.StandardGravity in the .NET ideal model.
    if(!Number.isFinite(length) || length<=0 || !Number.isFinite(amplitude) ||
       amplitude<=0 || amplitude>85 || !Number.isFinite(trial.nonlinearPeriodSeconds) ||
       trial.nonlinearPeriodSeconds<=0)return null;
    const radians=amplitude*Math.PI/180;
    const position=Math.max(-100,Math.min(100,this.theoryPositionPercent()));
    const angleDegrees=amplitude*position/100,angleRadians=angleDegrees*Math.PI/180;
    const maximumAngularSpeed=Math.sqrt(2*gravity/length*(1-Math.cos(radians)));
    const angularSpeed=Math.sqrt(Math.max(0,2*gravity/length*
      (Math.cos(angleRadians)-Math.cos(radians))));
    const radius=124,pivotX=160,pivotY=24;
    const x=pivotX+radius*Math.sin(angleRadians);
    const y=pivotY+radius*Math.cos(angleRadians);
    const leftX=pivotX-radius*Math.sin(radians);
    const rightX=pivotX+radius*Math.sin(radians);
    const extremeY=pivotY+radius*Math.cos(radians);
    // Cyan tangent illustrates motion from right to left, not the slider direction.
    const arrowLength=maximumAngularSpeed>0?42*angularSpeed/maximumAngularSpeed:0;
    return {
      amplitude,angleDegrees,angularSpeed,maximumAngularSpeed,
      maximumTangentialSpeed:length*maximumAngularSpeed,
      periodSeconds:trial.nonlinearPeriodSeconds,
      x,y,leftX,rightX,extremeY,pivotX,pivotY,
      arrowLength,arrowX:x-arrowLength*Math.cos(angleRadians),
      arrowY:y+arrowLength*Math.sin(angleRadians),
      positionKind:Math.abs(position)===100?'extreme':position===0?'bottom':'between'
    };
  });

  onResearchFrozen(prediction:FrozenMusePrediction|null):void {
    if(!prediction || prediction.studyId===this.session()?.id)this.musePrediction.set(prediction);
  }
  frozenForStudy():FrozenMusePrediction|null {
    const p=this.musePrediction();return p?.studyId===this.session()?.id?p:null;
  }
  nextResearchLength():number|null {
    const p=this.frozenForStudy();if(!p)return null;
    try{return JSON.parse(p.payloadJson).prediction.nextLengthMeters;}catch{return null;}
  }
  readonly sourceChoice=signal<'empty'|'demo'|'upload'>('empty');
  readonly demoLoading=signal(false);
  readonly choosingUpload=signal(false);
  beginNewTrial():void {
    if(this.videoActionsBusy())return;
    this.choosingUpload.set(true);this.error.set('');
  }
  cancelNewTrial():void {
    if(this.videoActionsBusy())return;
    this.choosingUpload.set(false);this.error.set('');
    if(!this.session()?.baseline)this.sourceChoice.set('empty');
  }
  readonly previewState=signal<'empty'|'loading'|'ready'|'error'>('empty');
  readonly resultState=signal<'idle'|'loading'|'ready'|'error'>('idle');

  override ngOnInit():void {
    // Enter empty even when a previous study is saved. Loading is an explicit action.
    void this.refreshTheory();
  }

  async loadDemo():Promise<void> {
    const current=this.session();if(!current || current.baseline || this.videoActionsBusy())return;
    this.sourceChoice.set('demo');this.choosingUpload.set(false);this.demoLoading.set(true);this.previewState.set('loading');this.error.set('');
    try {
      const config=await firstValueFrom(this.demoHttp.get<{studyId:string}>('demo-video.json'));
      if(!/^[a-f0-9]{32}$/.test(config.studyId))throw Error('INVALID_DEMO');
      const demo=await firstValueFrom(this.demoHttp.get<DirectorSession>(this.api+'/'+config.studyId));
      if(!demo.baseline)throw Error('MISSING_DEMO');
      const data=await firstValueFrom(this.demoHttp.get(this.api+'/'+config.studyId+'/original/baseline',{responseType:'blob'}));
      const file=new File([data],demo.baseline.originalFilename,{type:data.type});
      await this.upload({target:{files:[file],value:''}} as unknown as Event,'baseline');
      if(!this.session()?.baseline)throw Error('DEMO_UPLOAD_FAILED');
      this.checkedVideo.set(false);
    } catch {
      this.previewState.set('error');
      this.error.set(this.es?'No se pudo cargar el video de prueba. Tu código se conserva; vuelve a intentarlo o sube un video.':'Could not load the test video. Your code is preserved; retry or upload a video.');
    } finally {this.demoLoading.set(false);}
  }

  sourcePending():boolean {
    return this.demoLoading() || this.registeringAndUploading() || !!this.stageBusy();
  }
  videoLoading():boolean {return this.sourcePending() || this.previewState()==='loading';}
  onPreviewState(state:'loading'|'ready'|'error'):void {this.previewState.set(state);}
  onProcessingStarted():void {
    this.recoveryMode=false;this.recoveryMessage.set('');this.advanceAfterProcessing=true;
    delete this.roleSelections[this.activeTrial()];
    const verified={...this.verifiedRoles()};delete verified[this.activeTrial()];this.verifiedRoles.set(verified);
    this.clearProcessed();this.resultState.set('loading');
  }
  onProcessingFinished(ready:boolean):void {
    this.resultState.set(ready?'ready':'error');this.onPlaybackReady(ready);
    if(ready && this.advanceAfterProcessing){this.flowStep.set(this.activeTrial()==='followup'?5:3);this.advanceAfterProcessing=false;}
    if(ready && this.recoveredDestination){this.flowStep.set(this.recoveredDestination);this.recoveredDestination=null;}
    if(!ready){this.advanceAfterProcessing=false;this.recoveredDestination=null;}
  }

  readonly activeTrial = signal<'baseline'|'followup'>('baseline');
  readonly registeringAndUploading = signal(false);
  readonly samRefreshNonce = signal(0);
  readonly connection=inject(LabConnection);
  readonly samSelection=signal<SamProcessingSelection|null>(null);
  readonly playbackReady=signal(false);
  clearProcessed():void {
    this.samSelection.set(null);this.playbackReady.set(false);this.checkedVideo.set(false);this.resultState.set('idle');
  }
  acceptProcessed(selection:SamProcessingSelection):void {
    const clip=selection.videoRole==='followup'?this.session()?.followup:this.session()?.baseline;
    if(selection.studyId!==this.session()?.id || selection.sourceSha256!==clip?.sha256 ||
       selection.port!==this.connection.port())return;
    this.roleSelections[selection.videoRole]=selection;
    this.samSelection.set(selection);
    this.samRefreshNonce.update(value=>value+1);
  }
  processedAnalysis(role:'baseline'|'followup'):string {
    const selected=this.samSelection(),study=this.session();
    const clip=role==='followup'?study?.followup:study?.baseline;
    return selected?.studyId===study?.id && selected?.videoRole===role &&
      selected?.sourceSha256===clip?.sha256 && selected?.port===this.connection.port()
      ? selected.analysisId : '';
  }
  processedAdaptiveBatch(role:'baseline'|'followup'):string {
    return this.processedAnalysis(role)?this.samSelection()?.adaptiveBatchId??'':'';
  }
  selectTrial(role:'baseline'|'followup'):void {
    if(role===this.activeTrial() || this.videoActionsBusy())return;
    this.clearProcessed();this.previewState.set('loading');this.activeTrial.set(role);
    const saved=this.roleSelections[role];if(saved){this.recoveryMode=true;this.acceptProcessed(saved);this.resultState.set('loading');}
  }
  onPlaybackReady(ready:boolean):void {
    this.playbackReady.set(ready);
    const selected=this.samSelection();
    if(ready && selected)this.verifiedRoles.update(v=>({...v,[selected.videoRole]:selected.analysisId}));
    if(!ready)this.checkedVideo.set(false);
  }

  /**
   * Select the first recording immediately. The study's theoretical inputs
   * are recomputed and frozen on the server BEFORE any video bytes are sent.
   * Existing sessions and their original evidence are never overwritten.
   */
  async uploadFocused(event: Event, role:'baseline'|'followup'): Promise<void> {
    const control = event.target as HTMLInputElement;
    const file = control.files?.[0];
    if (!file || this.registeringAndUploading() ||
        this.busy() || !!this.stageBusy() || this.connection.busy()) return;

    if(!this.session() || this.draftSaving() || this.draftError())return;
    if(role==='followup' && (!this.frozenForStudy() || this.session()?.followup))return;
    this.sourceChoice.set('upload');this.previewState.set('loading');
    try {
      await this.upload(event, role);
      if((role==='baseline'?this.session()?.baseline:this.session()?.followup))this.choosingUpload.set(false);
      if(role==='followup' && this.session()?.followup){this.clearProcessed();this.activeTrial.set('followup');this.flowStep.set(4);}
      if(role==='baseline' && this.session()?.baseline)this.flowStep.set(1);
    } finally {
      const clip=role==='baseline'?this.session()?.baseline:this.session()?.followup;
      if(!clip)this.previewState.set(this.error()?'error':'empty');
      control.value='';
    }
  }

  videoActionsBusy():boolean {
    return !this.session() || this.generatingCode() || this.draftSaving() || !!this.draftError() || this.recoveryLoading() || this.videoLoading() || this.busy() || this.resultState()==='loading' || this.connection.busy();
  }

  async uploadNewTrial(event:Event):Promise<void> {
    const control=event.target as HTMLInputElement,file=control.files?.[0];
    if(!file || this.videoActionsBusy())return;
    // Cancellation and invalid files leave the current investigation selected.
    if(file.size<10000 || file.size>240000000 ||
       !/\.(mov|mp4|m4v|webm|mkv|avi|3gp|3g2)$/i.test(file.name)) {
      this.error.set(this.es?'Video inválido. Usa un formato compatible de 10 KB a 240 MB.':
        'Invalid video. Use a supported format from 10 KB to 240 MB.');
      control.value='';return;
    }
    if(this.session()?.baseline) {
      this.newFocusedStudy();
      await this.generateRecoveryCode();
      if(!this.session()){control.value='';return;}
    }
    await this.uploadFocused(event,'baseline');
  }

  newFocusedStudy():void {
    if(this.recoveryControlsBusy() || this.resultState()==='loading')return;
    this.recoveryGeneration++;this.recoveryMode=false;this.roleSelections={};this.recoveryMessage.set('');this.musePrediction.set(null);
    this.verifiedRoles.set({});this.flowStep.set(1);this.recoveryMenuOpen.set(false);this.recoveryWarning.set(false);this.recoveryFailure.set(false);this.advanceAfterProcessing=false;this.recoveredDestination=null;
    this.clearProcessed();
    this.reset();
    this.activeTrial.set('baseline');
    this.sourceChoice.set('upload');this.choosingUpload.set(true);this.previewState.set('empty');
  }
}