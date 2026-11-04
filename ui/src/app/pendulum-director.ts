import { apiOrigin } from './api-origin';
import { LabConnection } from './lab-connection';
import { CommonModule } from '@angular/common';
import { HttpClient, HttpEventType, HttpResponse } from '@angular/common/http';
import { Component, computed, inject, input, OnInit, signal, DestroyRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PendulumMasterPlayer } from './pendulum-master-player';
import type { CandidatePixelPoint } from './pendulum-master-sync';
import { firstValueFrom, filter, map, tap, timeout } from 'rxjs';
import { buildNormalizedVideoOverlay, type NormalizedOverlay } from './pendulum-overlay';

type Language = 'es' | 'en';
type Stage = 'baseline' | 'followup';
export interface DirectorSamReviewEvidence {
  videoSha256:string;analysisId:string;adaptiveBatchId:string|null;pivotRevisionId:string|null;
}
interface Point { timeSeconds: number; position: number }
interface Motion {
  framesAnalyzed: number; detectedFrames: number;
  observations: Point[];
  pixelSamples?: CandidatePixelPoint[] | null;
}
interface DirectorVideo {
  originalFilename: string; sha256: string; posterAvailable: boolean;
  previewAvailable?: boolean;
  media: { codec: string; durationSeconds: number; width: number; height: number; nominalFramesPerSecond: number };
  tracking: Motion | null;
  spectral: { periodSeconds: number | null; fitRSquared: number; status: string } | null;
  localEvidenceStatus?: 'DEFERRED' | 'READY';
}
interface Proposal {
  source: string;
  reasonEs: string; reasonEn: string;
  registeredAtUtc: string; predictionSha256: string;
  theory: {
    baselineLengthMeters: number; followUpLengthMeters: number;
    expectedBaselinePeriodSeconds: number;
    expectedFollowUpPeriodSeconds: number;
    predictedIncreasePercent: number;
  }
}
interface Comparison {
  status: string;
  baselinePeriodSeconds: number; followUpPeriodSeconds: number;
  predictedIncreasePercent: number; observedIncreasePercent: number;
  changeGapPercentagePoints: number;
  limitationsEs: string; limitationsEn: string;
  predictedBaselinePeriodSeconds?: number | null;
  predictedFollowUpPeriodSeconds?: number | null;
  baselinePeriodDiscrepancyPercent?: number | null;
  followUpPeriodDiscrepancyPercent?: number | null;
}
interface ModelSample {
  timeSeconds: number; angleDegrees: number; horizontalCentimeters: number;
  elevationCentimeters: number; kineticJoules: number;
  potentialJoules: number; totalJoules: number;
}
interface ModelTrial {
  lengthMeters: number; initialAngleDegrees: number; massGrams: number;
  smallAnglePeriodSeconds: number; nonlinearPeriodSeconds: number;
  amplitudeCentimeters: number; initialPotentialJoules: number;
  maximumRelativeEnergyDriftPercent: number; samples: ModelSample[];
}
interface MathematicalPrediction {
  source: string; equation: string; assumptions: string;
  baseline: ModelTrial; followUp: ModelTrial | null;
  predictedPeriodIncreasePercent: number | null;
}
interface SingleComparison {
  predictedNonlinearPeriodSeconds: number;
  predictedSmallAnglePeriodSeconds: number;
  observedLocalPeriodSeconds: number;
  relativePeriodErrorPercent: number;
  status: string; limitationsEs: string; limitationsEn: string;
}
export type DirectorUploadState='RUNNING'|'COMPLETED'|'FAILED'|'CANCELLED';
export interface DirectorUploadProgress {
  uploadId:string;studyId:string;role:'baseline'|'followup';state:DirectorUploadState;
  phase:string;receivedBytes:number;totalBytes:number;startedAtUtc:string;updatedAtUtc:string;elapsedMs:number;
  phases:{key:string;status:DirectorUploadState|'PENDING';elapsedMs:number}[];
}
export interface LocalVideoPreparationStatus {
  sourceSha256:string;state:'QUEUED'|'RUNNING'|'READY'|'FAILED';phase:string;
  elapsedMs:number;referenceFrames:number;
}
export interface DirectorSession {
  preparationPending?:boolean;
  id: string; baselineLengthMeters: number | null; createdAtUtc: string;
  step: string; modelStatus: string; baseline: DirectorVideo | null;
  humanReview: { reviewedAtUtc: string; bobConfirmed: boolean } | null;
  proposal: Proposal | null; followup: DirectorVideo | null;
  comparison: Comparison | null;
  angleDegrees?: number | null; massGrams?: number | null; includeSecondTrial?: boolean;
  followUpMode?: 'recommended' | 'manual';
  selectedFollowUpLengthMeters?: number | null;
  singleComparison?: SingleComparison | null;
  modelSha256?: string | null;
  musePrediction?: import('./muse-investigator').FrozenMusePrediction | null;
}

@Component({
  selector: 'app-pendulum-director',
  standalone: true,
  imports: [CommonModule, FormsModule, PendulumMasterPlayer],
  templateUrl: './pendulum-director.html',
  styleUrl: './pendulum-director.scss'
})
export class PendulumDirector implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly directorConnection=inject(LabConnection);
  protected get api():string {return apiOrigin(this.directorConnection.port())+'/api/science/director/pendulum/sessions';}
  readonly language = input<Language>('es');
  readonly baselineLength = signal<number|null>(null);
  readonly initialAngleDegrees = signal<number|null>(null);
  readonly massGrams = signal<number|null>(null);
  readonly includeSecondTrial = signal(false);
  readonly followUpMode = signal<'recommended'|'manual'>('recommended');
  readonly manualFollowUpLength = signal(.8);
  readonly model = signal<MathematicalPrediction | null>(null);
  readonly modelLoading = signal(false);
  readonly modelError = signal('');
  private modelRequestCounter=0;
  readonly session = signal<DirectorSession | null>(null);
  readonly checkedVideo = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly stageBusy = signal('');
  readonly uploadProgress=signal<DirectorUploadProgress|null>(null);
  readonly uploadTransfer=signal<{uploadId:string;loaded:number;total:number|null}|null>(null);
  readonly uploadActiveId=signal('');
  readonly uploadProgressUnavailable=signal(false);
  readonly uploadServerComplete=signal(false);
  private readonly localPreparationSnapshot=signal<(LocalVideoPreparationStatus & {studyId:string;role:Stage})|null>(null);
  readonly localVideoPreparation=computed(()=>{
    const snapshot=this.localPreparationSnapshot(),session=this.session();
    return snapshot && session?.id===snapshot.studyId && session[snapshot.role]?.sha256===snapshot.sourceSha256
      ? snapshot : null;
  });
  private localPreparationSerial=0;
  private localPreparationPoll:ReturnType<typeof setTimeout>|null=null;
  private stopLocalPreparationPolling():void {
    this.localPreparationSerial++;
    if(this.localPreparationPoll!==null)clearTimeout(this.localPreparationPoll);
    this.localPreparationPoll=null;
  }
  private startLocalPreparationPolling(session:DirectorSession,role:Stage):void {
    this.stopLocalPreparationPolling();this.localPreparationSnapshot.set(null);
    const sha=session[role]?.sha256;if(!sha)return;
    const serial=this.localPreparationSerial,base=this.api,started=Date.now();
    let unavailable=0;
    const current=()=>serial===this.localPreparationSerial && this.api===base &&
      this.session()?.id===session.id && this.session()?.[role]?.sha256===sha;
    const poll=async():Promise<void>=>{
      this.localPreparationPoll=null;if(!current())return;
      try {
        const value=await firstValueFrom(this.http.get<LocalVideoPreparationStatus|null>(
          base+'/'+session.id+'/video/'+role+'/preparation').pipe(timeout(5000)));
        if(!current())return;
        if(value===null){unavailable++;}
        else {
          if(value.sourceSha256!==sha || !['QUEUED','RUNNING','READY','FAILED'].includes(value.state) ||
            !Number.isFinite(value.elapsedMs) || value.elapsedMs<0 ||
            !Number.isInteger(value.referenceFrames) || value.referenceFrames<0 || value.referenceFrames>50 ||
            value.state==='READY' && value.referenceFrames!==50)throw Error('LOCAL_PREPARATION_MISMATCH');
          unavailable=0;this.localPreparationSnapshot.set({...value,studyId:session.id,role});
          if(value.state==='READY' || value.state==='FAILED')return;
        }
      } catch {
        if(!current())return;
        unavailable++;
      }
      // Informational polling must never block Next/costs/Process or run forever.
      if(!current())return;
      if(unavailable>=3 || Date.now()-started>=10*60*1000){this.localPreparationSnapshot.set(null);return;}
      this.localPreparationPoll=setTimeout(()=>void poll(),2000);
    };
    void poll();
  }
  readonly uploadPercent=computed(()=>{
    const transfer=this.uploadTransfer(),progress=this.uploadProgress(),id=this.uploadActiveId();
    const sent=transfer?.uploadId===id?transfer:null;
    const received=progress?.uploadId===id?progress:null;
    const total=sent?.total??received?.totalBytes??null;
    // Both values are byte counts, not an estimate of server processing.
    const loaded=Math.max(sent?.loaded??0,received?.receivedBytes??0);
    return total!==null && total>0?Math.min(100,Math.max(0,100*loaded/total)):null;
  });
  private uploadSerial=0;
  private uploadPollHandle:ReturnType<typeof setTimeout>|null=null;
  constructor() {
    inject(DestroyRef).onDestroy(()=>{this.uploadSerial++;this.stopUploadPolling();this.stopLocalPreparationPolling();});
  }
  private stopUploadPolling():void {
    if(this.uploadPollHandle!==null)clearTimeout(this.uploadPollHandle);
    this.uploadPollHandle=null;
  }
  clearUploadProgress():void {
    this.uploadSerial++;this.stopUploadPolling();this.uploadActiveId.set('');
    this.stopLocalPreparationPolling();this.localPreparationSnapshot.set(null);
    this.uploadProgress.set(null);this.uploadTransfer.set(null);
    this.uploadProgressUnavailable.set(false);this.uploadServerComplete.set(false);
  }
  readonly advancedInformation = signal(false);
  get es(): boolean { return this.language() === 'es'; }

  readonly baselineCentimeters=computed(()=>{const length=this.baselineLength();return length===null?null:length*100;});
  optionalNumber(value:unknown, divisor=1):number|null {
    return value===null || value===undefined || value==='' ? null : Number(value)/divisor;
  }
  parameterState(value:number|null, min:number, max:number):'missing'|'valid'|'invalid' {
    return value===null?'missing':Number.isFinite(value)&&value>=min&&value<=max?'valid':'invalid';
  }
  parameterLabel(value:number|null,min:number,max:number,required=false):string {
    const state=this.parameterState(value,min,max);
    return state==='missing'?(required?(this.es?'Dato obligatorio':'Required value'):
      (this.es?'Sin dato · opcional':'Not provided · optional')):
      state==='valid'?(this.es?'Dato ingresado':'Value provided'):
      (this.es?'Revisa el rango permitido':'Check the allowed range');
  }
  readonly parametersInvalid=computed(()=>
    this.parameterState(this.baselineLength(),.1,5)==='invalid' ||
    this.parameterState(this.initialAngleDegrees(),0,85)==='invalid' ||
    this.parameterState(this.massGrams(),10,5000)==='invalid' ||
    (this.includeSecondTrial() && this.followUpMode()==='manual' &&
      this.parameterState(this.manualFollowUpLength(),.1,5)!=='valid'));
  readonly requiredParametersReady=computed(()=>
    this.parameterState(this.baselineLength(),.1,5)==='valid' &&
    this.parameterState(this.initialAngleDegrees(),0,85)==='valid' &&
    this.parameterState(this.massGrams(),10,5000)==='valid');
  requiredParametersSaved():boolean {
    const study=this.session();
    if(!study || !this.requiredParametersReady() ||
      this.parameterState(study.baselineLengthMeters,.1,5)!=='valid' ||
      this.parameterState(study.angleDegrees??null,0,85)!=='valid' ||
      this.parameterState(study.massGrams??null,10,5000)!=='valid')return false;
    return Math.abs(study.baselineLengthMeters!-this.baselineLength()!)<1e-9 &&
      Math.abs(study.angleDegrees!-this.initialAngleDegrees()!)<1e-9 &&
      Math.abs(study.massGrams!-this.massGrams()!)<1e-9;
  }
  readonly theoryInputsReady=computed(()=>this.baselineLength()!==null &&
    this.initialAngleDegrees()!==null && this.massGrams()!==null && !this.parametersInvalid());

  get recommendedFollowUpLength(): number|null {
    const length=this.baselineLength();
    if(length===null || this.parameterState(length,.1,5)!=='valid')return null;
    return Math.round((length*1.6>5 ? length*.7 : length*1.6)*1000)/1000;
  }

  get chosenSecondLength():number|null {
    if(!this.includeSecondTrial()) return null;
    return this.followUpMode()==='manual'
      ? this.manualFollowUpLength():this.recommendedFollowUpLength;
  }

  private readonly graphSize={left:36,top:15,width:608,height:160};
  readonly paths=computed(()=>{
    const prediction=this.model();
    if(!prediction)return null;
    const base=prediction.baseline,other=prediction.followUp;
    const trials=other ? [base,other] : [base];
    const g=this.graphSize;
    const tmax=20;
    const maxAmplitude=Math.max(1,...trials.map(t=>t.amplitudeCentimeters));
    const maxLength=Math.max(...trials.map(t=>t.lengthMeters))*100;
    const maxRise=Math.max(.5,...trials.map(t=>
      t.lengthMeters*100*(1-Math.cos(t.initialAngleDegrees*Math.PI/180))));
    const maxEnergy=Math.max(.0001,...trials.map(t=>t.initialPotentialJoules));
    const position=(s:ModelSample)=>({
      x:g.left+g.width*s.timeSeconds/tmax,
      y:g.top+g.height/2 - s.horizontalCentimeters/maxAmplitude*(g.height*.42)
    });
    const xy=(s:ModelSample)=>({
      x:g.left+g.width/2+s.horizontalCentimeters/maxLength*(g.width*.40),
      y:g.top+g.height-s.elevationCentimeters/maxRise*(g.height*.83)
    });
    const energy=(s:ModelSample,quantity:'kineticJoules'|'potentialJoules'|'totalJoules')=>({
      x:g.left+g.width*s.timeSeconds/tmax,
      y:g.top+g.height - s[quantity]/maxEnergy*(g.height*.84)
    });
    const points=(samples:ModelSample[],transform:(s:ModelSample)=>{x:number,y:number})=>
      samples.map(s=>{const v=transform(s);return v.x.toFixed(2)+','+v.y.toFixed(2)}).join(' ');
    return {
      motionA:points(base.samples,position),
      motionB:other?points(other.samples,position):'',
      xyA:points(base.samples,xy),
      xyB:other?points(other.samples,xy):'',
      kinetic:points(base.samples,s=>energy(s,'kineticJoules')),
      potential:points(base.samples,s=>energy(s,'potentialJoules')),
      total:points(base.samples,s=>energy(s,'totalJoules'))
    };
  });

  async refreshTheory():Promise<void> {
    const serial=++this.modelRequestCounter;
    if(!this.theoryInputsReady() || this.initialAngleDegrees()===0){
      this.model.set(null);this.modelLoading.set(false);this.modelError.set('');return;
    }
    const followUp=this.chosenSecondLength;
    this.modelLoading.set(true);this.modelError.set('');
    // A request only to our deterministic .NET engine; never contacts Meta.
    try {
      const response=await firstValueFrom(this.http.post<MathematicalPrediction>(
        apiOrigin(this.directorConnection.port())+'/api/science/pendulum/mathematical-model',{
          lengthMeters:this.baselineLength(),
          angleDegrees:this.initialAngleDegrees(),
          massGrams:this.massGrams(),
          followUpLengthMeters:followUp
        }));
      if(serial===this.modelRequestCounter) this.model.set(response);
    } catch {
      if(serial===this.modelRequestCounter) {
        this.model.set(null);
        this.modelError.set(this.es
          ? 'Valores inválidos: longitud 0,10–5 m, ángulo 1–85° y masa 10–5000 g.'
          : 'Invalid inputs: length 0.10–5 m, angle 1–85°, mass 10–5000 g.');
      }
    } finally {
      if(serial===this.modelRequestCounter) this.modelLoading.set(false);
    }
  }


  // Plots from raw locally tracked VIDEO positions only. These are NOT
  // mathematical simulations, real SAM masks or calibrated physical cm values.
  readonly evidencePlots=computed(()=>{
    const session=this.session();
    return {
      baseline:this.videoPlot(session?.baseline ?? null),
      followup:this.videoPlot(session?.followup ?? null)
    };
  });

  readonly normalizedOverlays=computed(():{
    baseline:NormalizedOverlay|null;
    followup:NormalizedOverlay|null;
  }=>{
    const s=this.session(),m=this.model();
    const empty={baseline:null,followup:null};
    if(!s || !m || s.baselineLengthMeters==null || s.angleDegrees==null || s.massGrams==null || Math.abs(m.baseline.lengthMeters-s.baselineLengthMeters)>.000001 ||
       Math.abs(m.baseline.initialAngleDegrees-s.angleDegrees)>.000001 ||
       Math.abs(m.baseline.massGrams-s.massGrams)>.000001)
      return empty;
    const baseline=s.baseline?.tracking
      ? buildNormalizedVideoOverlay(m.baseline.samples,
          m.baseline.nonlinearPeriodSeconds,s.baseline.tracking.observations)
      : null;
    const secondLength=s.proposal?.theory.followUpLengthMeters;
    const followup=s.followup?.tracking && m.followUp && secondLength!==undefined &&
      Math.abs(m.followUp.lengthMeters-secondLength)<.000001
      ? buildNormalizedVideoOverlay(m.followUp.samples,
          m.followUp.nonlinearPeriodSeconds,s.followup.tracking.observations)
      : null;
    return {baseline,followup};
  });

  private videoPlot(video:DirectorVideo|null):{
    position:string;velocity:string;phase:string;count:number
  }|null {
    if(!video?.tracking || video.tracking.observations.length<12)return null;
    const frames=video.tracking.observations.filter(x=>
      Number.isFinite(x.timeSeconds)&&Number.isFinite(x.position));
    if(frames.length<12)return null;
    const minT=frames[0].timeSeconds;
    const span=Math.max(.01,frames[frames.length-1].timeSeconds-minT);
    const xs=frames.map(x=>x.position);
    const sorted=[...xs].sort((a,b)=>a-b);
    const lo=sorted[Math.floor(sorted.length*.02)];
    const hi=sorted[Math.floor(sorted.length*.98)];
    const velocities=frames.map((q,i)=>{
      const before=frames[Math.max(0,i-1)],after=frames[Math.min(frames.length-1,i+1)];
      const dt=Math.max(.01,after.timeSeconds-before.timeSeconds);
      return (after.position-before.position)/dt;
    });
    const bySpeed=[...velocities].sort((a,b)=>a-b);
    const vlow=bySpeed[Math.floor(bySpeed.length*.02)];
    const vhigh=bySpeed[Math.floor(bySpeed.length*.98)];
    const px=(t:number)=>36+608*(t-minT)/span;
    const map=(value:number,low:number,high:number)=>
      175-160*(Math.max(low,Math.min(high,value))-low)/
        Math.max(.001,high-low);
    const toString=(points:Array<{x:number,y:number}>)=>points.map(
      o=>o.x.toFixed(2)+','+o.y.toFixed(2)).join(' ');
    return {
      count:frames.length,
      position:toString(frames.map(q=>({
        x:px(q.timeSeconds),y:map(q.position,lo,hi)
      }))),
      velocity:toString(frames.map((q,i)=>({
        x:px(q.timeSeconds),y:map(velocities[i],vlow,vhigh)
      }))),
      phase:toString(frames.map((q,i)=>({
        x:36+608*(Math.max(lo,Math.min(hi,q.position))-lo)/
          Math.max(.001,hi-lo),
        y:map(velocities[i],vlow,vhigh)
      })))
    };
  }

  get baselinePeriodDiscrepancyPercent():number|null {
    const observed=this.session()?.baseline?.spectral?.periodSeconds;
    const theory=this.model()?.baseline.nonlinearPeriodSeconds;
    return observed !== null && observed !== undefined && theory
      ? Math.abs(observed-theory)/theory*100 : null;
  }

  downloadTheoreticalCsv():void {
    const theory=this.model();if(!theory)return;
    const header='trial,time_s,angle_deg,x_cm,elevation_cm,kinetic_J,potential_J,total_J';
    const trials: Array<{label:string, trial:ModelTrial}> = [
      {label:'baseline',trial:theory.baseline}
    ];
    if(theory.followUp)trials.push({label:'followup',trial:theory.followUp});
    const rows=trials.flatMap(({label,trial})=>trial.samples.map(sample=>
        [label,sample.timeSeconds,sample.angleDegrees,
          sample.horizontalCentimeters,sample.elevationCentimeters,
          sample.kineticJoules,sample.potentialJoules,
          sample.totalJoules].join(',')));
    const blob=new Blob([[header,...rows].join('\n')],{type:'text/csv;charset=utf-8'});
    const url=URL.createObjectURL(blob);
    const link=document.createElement('a');
    link.href=url;link.download='prooflab-modelo-teorico.csv';
    link.click();URL.revokeObjectURL(url);
  }


  ngOnInit(): void {
    // Theory is calculated only after its physical inputs have been supplied; no provider calls.
    void this.refreshTheory();
    const id = localStorage.getItem('prooflab-director-current');
    if (id && /^[a-f0-9]{32}$/i.test(id)) void this.restore(id);
  }

  async restore(id: string): Promise<void> {
    try {
      const item = await firstValueFrom(this.http.get<DirectorSession>(
        this.api + '/' + id));
      this.applyRestoredSession(item);
    } catch {
      this.error.set(this.es ? 'No se pudo recuperar la investigación. Revisa el ID y la conexión.' : 'Could not restore the study. Check the ID and connection.');
    }
  }

  protected applyRestoredSession(item:DirectorSession):void {
      this.session.set(item);
      this.baselineLength.set(item.baselineLengthMeters ?? null);
      this.initialAngleDegrees.set(item.angleDegrees ?? null);
      this.massGrams.set(item.massGrams ?? null);
      this.includeSecondTrial.set(item.includeSecondTrial ?? true);
      this.followUpMode.set(item.followUpMode ?? 'recommended');
      this.manualFollowUpLength.set(item.selectedFollowUpLengthMeters ?? .8);
      this.checkedVideo.set(!!item.humanReview);
      void this.refreshTheory();
      localStorage.setItem('prooflab-director-current',item.id);
  }

  async start(): Promise<void> {
    if(this.busy() || this.parametersInvalid())return;
    this.busy.set(true);this.error.set('');
    try{
      const study=await firstValueFrom(this.http.post<DirectorSession>(
        this.api,{
          baselineLengthMeters:this.baselineLength(),
          angleDegrees:this.initialAngleDegrees(),
          massGrams:this.massGrams(),
          includeSecondTrial:this.includeSecondTrial(),
          followUpMode:this.followUpMode(),
          selectedFollowUpLengthMeters:this.includeSecondTrial() &&
            this.followUpMode()==='manual' ? this.manualFollowUpLength() : null
        }));
      this.session.set(study);
      localStorage.setItem('prooflab-director-current', study.id);
      this.checkedVideo.set(false);
    }catch{this.error.set(this.es
      ? 'No se pudo crear la investigación. Verifica la longitud y la API.'
      : 'Could not start research. Check length and API connection.');}
    finally{this.busy.set(false)}
  }

  reset():void {
    if(this.busy() || this.stageBusy())return;
    this.clearUploadProgress();
    localStorage.removeItem('prooflab-director-current');
    this.session.set(null);this.checkedVideo.set(false);this.error.set('');
    this.baselineLength.set(null);this.initialAngleDegrees.set(null);this.massGrams.set(null);
    this.includeSecondTrial.set(false);this.modelRequestCounter++;
    this.model.set(null);this.modelLoading.set(false);this.modelError.set('');
  }

  async upload(event:Event,role:Stage):Promise<void> {
    const control=event.target as HTMLInputElement,file=control.files?.[0],s=this.session();
    if(!s || !file || this.stageBusy())return;
    if(file.size<10000 || file.size>240000000 ||
       !/\.(mov|mp4|m4v|webm|mkv|avi|3gp|3g2)$/i.test(file.name)) {
      this.error.set(this.es?
        'Archivo incompatible. Usa MOV, MP4, WebM u otro formato permitido, hasta 240 MB.':
        'Unsupported media. Try MOV, MP4, WebM or another allowed format (up to 240 MB).');
      control.value='';return;
    }
    this.clearUploadProgress();
    const uploadId=crypto.randomUUID().replace(/-/g,''),serial=this.uploadSerial,base=this.api;
    const progressUrl=base+'/'+s.id+'/video/'+role+'/uploads/'+uploadId;
    this.uploadActiveId.set(uploadId);
    this.uploadTransfer.set({uploadId,loaded:0,total:file.size});
    this.stageBusy.set(role);this.error.set('');
    let finished=false;
    const current=()=>serial===this.uploadSerial && this.uploadActiveId()===uploadId &&
      this.session()?.id===s.id && this.api===base;
    const readProgress=async(final=false):Promise<void>=>{
      if(!current() || finished&&!final)return;
      try {
        const snapshot=await firstValueFrom(this.http.get<DirectorUploadProgress>(progressUrl).pipe(timeout(5000)));
        if(!current() || finished&&!final)return;
        if(snapshot.uploadId!==uploadId || snapshot.studyId!==s.id || snapshot.role!==role ||
          !['RUNNING','COMPLETED','FAILED','CANCELLED'].includes(snapshot.state) ||
          !Array.isArray(snapshot.phases))throw Error('UPLOAD_PROGRESS_MISMATCH');
        this.uploadProgress.set(snapshot);this.uploadProgressUnavailable.set(false);
      } catch(failure) {
        if(current() && (!finished || final) && (failure as {status?:number})?.status!==404)
          this.uploadProgressUnavailable.set(true);
      }
    };
    const poll=async():Promise<void>=>{
      this.uploadPollHandle=null;
      await readProgress();
      if(current() && !finished && (!this.uploadProgress() || this.uploadProgress()?.state==='RUNNING'))
        this.uploadPollHandle=setTimeout(()=>void poll(),750);
    };
    this.uploadPollHandle=setTimeout(()=>void poll(),250);
    try {
      const next=await firstValueFrom(this.http.post<DirectorSession>(
        base+'/'+s.id+'/video/'+role+'?filename='+encodeURIComponent(file.name)+'&uploadId='+uploadId,
        file,{observe:'events',reportProgress:true}).pipe(
        tap(item=>{
          if(!current() || item.type!==HttpEventType.UploadProgress)return;
          this.uploadTransfer.set({uploadId,loaded:item.loaded,total:item.total??null});
        }),
        filter((item):item is HttpResponse<DirectorSession>=>item.type===HttpEventType.Response),
        map(item=>item.body)
      ));
      if(!current())return;
      if(!next || next.id!==s.id || !(role==='baseline'?next.baseline:next.followup))
        throw Error('UPLOAD_RESULT_MISMATCH');
      this.uploadServerComplete.set(true);
      this.session.set(next);this.checkedVideo.set(false);
      this.startLocalPreparationPolling(next,role);
    } catch {
      if(current())this.error.set(this.es?
        'No se pudo preparar el video. Revisa las etapas y el archivo antes de volver a cargarlo.':
        'The video could not be prepared. Review the stages and file before uploading it again.');
    } finally {
      finished=true;
      if(serial===this.uploadSerial) {
        this.stopUploadPolling();this.stageBusy.set('');
        void readProgress(true);
      }
      control.value='';
    }
  }

  async confirm(samEvidence?:DirectorSamReviewEvidence):Promise<void>{
    await this.action('review-baseline',{bobConfirmed:this.checkedVideo(),...(samEvidence?{samEvidence}:{})},
      this.es?'No se pudo registrar la revisión del video.':'Could not confirm visual review.');
  }

  async propose():Promise<void>{
    await this.action('propose-local-control',{},
      this.es?'No se pudo registrar la propuesta de control.':'Could not register local control.');
  }

  async compare():Promise<void>{
    await this.action('compare',{},
      this.es?'La comparación aún no es posible.':'Comparison is not ready.');
  }

  async compareSingle():Promise<void> {
    await this.action('compare-single',{},
      this.es?'Falta confirmar una primera observación válida.':'One reviewed valid video is required.');
  }

  private async action(path:string, payload:unknown, failure:string):Promise<void>{
    const s=this.session();
    if(!s || this.busy())return;
    const api=this.api,sourceSha=s.baseline?.sha256;
    this.busy.set(true);this.error.set('');
    try{
      const next=await firstValueFrom(this.http.post<DirectorSession>(
        api+'/'+s.id+'/'+path,payload));
      if(this.session()?.id===s.id && this.api===api)this.session.set(next);
    }catch{
      if(this.session()?.id!==s.id || this.api!==api)return;
      this.error.set(failure);
      // Local evidence may have completed even when the observation was inconclusive.
      // Refresh the saved state without retrying the review or requesting a provider.
      if(path==='review-baseline' && s.baseline?.localEvidenceStatus==='DEFERRED') {
        try {
          const refreshed=await firstValueFrom(this.http.get<DirectorSession>(
            api+'/'+s.id).pipe(timeout(30000)));
          if(this.session()?.id===s.id && this.api===api && refreshed.id===s.id &&
             this.session()?.baseline?.sha256===sourceSha && refreshed.baseline?.sha256===sourceSha)
            this.session.set(refreshed);
        } catch { /* Keep the original review error; a later recovery can refresh the evidence. */ }
      }
    }
    finally{this.busy.set(false)}
  }

  reviewVideo(role:Stage):string {
    const s=this.session();return s?this.api+'/'+s.id+'/original/'+role:'';
  }

  poster(role:Stage):string {
    const s=this.session();return s?this.api+'/'+s.id+'/poster/'+role:'';
  }

  trajectory(p?:Point[]):string {
    if(!p || p.length<2)return '';
    const selected = p.filter(q=>Number.isFinite(q.timeSeconds)&&Number.isFinite(q.position));
    if(selected.length<2)return '';
    const t0=selected[0].timeSeconds, span=Math.max(.01,selected[selected.length-1].timeSeconds-t0);
    const xs=selected.map(x=>x.position),lo=Math.min(...xs),hi=Math.max(...xs);
    return selected.map(q=>((q.timeSeconds-t0)/span*370+15).toFixed(1)+','+
      (92-(q.position-lo)/Math.max(.01,hi-lo)*75).toFixed(1)).join(' ');
  }
}