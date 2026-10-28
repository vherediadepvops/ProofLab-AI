import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PendulumMasterPlayer } from './pendulum-master-player';
import type { CandidatePixelPoint } from './pendulum-master-sync';
import { firstValueFrom } from 'rxjs';
import { buildNormalizedVideoOverlay, type NormalizedOverlay } from './pendulum-overlay';

type Language = 'es' | 'en';
type Stage = 'baseline' | 'followup';
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
  tracking: Motion;
  spectral: { periodSeconds: number | null; fitRSquared: number; status: string };
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
interface DirectorSession {
  id: string; baselineLengthMeters: number; createdAtUtc: string;
  step: string; modelStatus: string; baseline: DirectorVideo | null;
  humanReview: { reviewedAtUtc: string; bobConfirmed: boolean } | null;
  proposal: Proposal | null; followup: DirectorVideo | null;
  comparison: Comparison | null;
  angleDegrees?: number; massGrams?: number; includeSecondTrial?: boolean;
  followUpMode?: 'recommended' | 'manual';
  selectedFollowUpLengthMeters?: number | null;
  singleComparison?: SingleComparison | null;
  modelSha256?: string | null;
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
  private readonly api = 'http://127.0.0.1:5391/api/science/director/pendulum/sessions';
  readonly language = input<Language>('es');
  readonly baselineLength = signal(0.5);
  readonly initialAngleDegrees = signal(15);
  readonly massGrams = signal(300);
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
  readonly advancedInformation = signal(false);
  get es(): boolean { return this.language() === 'es'; }

  get recommendedFollowUpLength(): number {
    const length=this.baselineLength();
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
    const followUp=this.chosenSecondLength;
    this.modelLoading.set(true);this.modelError.set('');
    // A request only to our deterministic .NET engine; never contacts Meta.
    try {
      const response=await firstValueFrom(this.http.post<MathematicalPrediction>(
        'http://127.0.0.1:5391/api/science/pendulum/mathematical-model',{
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
    if(!s || !m || Math.abs(m.baseline.lengthMeters-s.baselineLengthMeters)>.000001 ||
       Math.abs(m.baseline.initialAngleDegrees-(s.angleDegrees ?? 15))>.000001 ||
       Math.abs(m.baseline.massGrams-(s.massGrams ?? 300))>.000001)
      return empty;
    const baseline=s.baseline
      ? buildNormalizedVideoOverlay(m.baseline.samples,
          m.baseline.nonlinearPeriodSeconds,s.baseline.tracking.observations)
      : null;
    const secondLength=s.proposal?.theory.followUpLengthMeters;
    const followup=s.followup && m.followUp && secondLength!==undefined &&
      Math.abs(m.followUp.lengthMeters-secondLength)<.000001
      ? buildNormalizedVideoOverlay(m.followUp.samples,
          m.followUp.nonlinearPeriodSeconds,s.followup.tracking.observations)
      : null;
    return {baseline,followup};
  });

  private videoPlot(video:DirectorVideo|null):{
    position:string;velocity:string;phase:string;count:number
  }|null {
    if(!video || video.tracking.observations.length<12)return null;
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
    const observed=this.session()?.baseline?.spectral.periodSeconds;
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
    // The initial three graphs render from independent local physics, no tokens.
    void this.refreshTheory();
    const id = localStorage.getItem('prooflab-director-current');
    if (id && /^[a-f0-9]{32}$/i.test(id)) void this.restore(id);
  }

  async restore(id: string): Promise<void> {
    try {
      const item = await firstValueFrom(this.http.get<DirectorSession>(
        this.api + '/' + id));
      this.session.set(item);
      this.baselineLength.set(item.baselineLengthMeters);
      this.initialAngleDegrees.set(item.angleDegrees ?? 15);
      this.massGrams.set(item.massGrams ?? 300);
      this.includeSecondTrial.set(item.includeSecondTrial ?? true);
      this.followUpMode.set(item.followUpMode ?? 'recommended');
      this.manualFollowUpLength.set(item.selectedFollowUpLengthMeters ?? .8);
      this.checkedVideo.set(!!item.humanReview);
      void this.refreshTheory();
    } catch {
      localStorage.removeItem('prooflab-director-current');
    }
  }

  async start(): Promise<void> {
    if(this.busy() || !this.model() || this.modelLoading())return;
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
    localStorage.removeItem('prooflab-director-current');
    this.session.set(null);this.checkedVideo.set(false);this.error.set('');
  }

  async upload(event: Event, role: Stage):Promise<void>{
    const control=event.target as HTMLInputElement;
    const file=control.files?.[0];
    const s=this.session();
    if(!s || !file || this.stageBusy())return;
    if(file.size < 10000 || file.size > 240000000 ||
       !/\.(mov|mp4|m4v|webm|mkv|avi|3gp|3g2)$/i.test(file.name)){
      this.error.set(this.es
        ? 'Archivo incompatible. Usa MOV, MP4, WebM u otro formato permitido, hasta 240 MB.'
        : 'Unsupported media. Try MOV, MP4, WebM or another allowed format (up to 240 MB).');
      control.value='';return;
    }
    this.stageBusy.set(role);this.error.set('');
    try{
      const next=await firstValueFrom(this.http.post<DirectorSession>(
        this.api+'/'+s.id+'/video/'+role+'?filename='+encodeURIComponent(file.name),file));
      this.session.set(next);
      this.checkedVideo.set(false);
    }catch{this.error.set(this.es
      ? 'No se pudo analizar el video. Puede estar dañado o exceder el límite de procesamiento.'
      : 'Could not process the media. Check encoding and size limits.');}
    finally{this.stageBusy.set('');control.value='';}
  }

  async confirm():Promise<void>{
    await this.action('review-baseline',{bobConfirmed:this.checkedVideo()},
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
    this.busy.set(true);this.error.set('');
    try{
      const next=await firstValueFrom(this.http.post<DirectorSession>(
        this.api+'/'+s.id+'/'+path,payload));
      this.session.set(next);
    }catch{this.error.set(failure);}
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