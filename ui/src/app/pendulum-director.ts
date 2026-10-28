import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

type Language = 'es' | 'en';
type Stage = 'baseline' | 'followup';
interface Point { timeSeconds: number; position: number }
interface Motion {
  framesAnalyzed: number; detectedFrames: number;
  observations: Point[];
}
interface DirectorVideo {
  originalFilename: string; sha256: string; posterAvailable: boolean;
  previewAvailable?: boolean;
  media: { codec: string; durationSeconds: number; width: number; height: number };
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
}
interface DirectorSession {
  id: string; baselineLengthMeters: number; createdAtUtc: string;
  step: string; modelStatus: string; baseline: DirectorVideo | null;
  humanReview: { reviewedAtUtc: string; bobConfirmed: boolean } | null;
  proposal: Proposal | null; followup: DirectorVideo | null;
  comparison: Comparison | null;
}

@Component({
  selector: 'app-pendulum-director',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './pendulum-director.html',
  styleUrl: './pendulum-director.scss'
})
export class PendulumDirector implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly api = 'http://127.0.0.1:5391/api/science/director/pendulum/sessions';
  readonly language = input<Language>('es');
  readonly baselineLength = signal(0.5);
  readonly session = signal<DirectorSession | null>(null);
  readonly checkedVideo = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly stageBusy = signal('');
  readonly advancedInformation = signal(false);
  get es(): boolean { return this.language() === 'es'; }

  ngOnInit(): void {
    const id = localStorage.getItem('prooflab-director-current');
    if (id && /^[a-f0-9]{32}$/i.test(id)) void this.restore(id);
  }

  async restore(id: string): Promise<void> {
    try {
      const item = await firstValueFrom(this.http.get<DirectorSession>(
        this.api + '/' + id));
      this.session.set(item);
      this.baselineLength.set(item.baselineLengthMeters);
      this.checkedVideo.set(!!item.humanReview);
    } catch {
      localStorage.removeItem('prooflab-director-current');
    }
  }

  async start(): Promise<void> {
    if(this.busy())return;
    this.busy.set(true);this.error.set('');
    try{
      const study=await firstValueFrom(this.http.post<DirectorSession>(
        this.api,{baselineLengthMeters:this.baselineLength()}));
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
    const s=this.session();return s?this.api+'/'+s.id+'/preview/'+role:'';
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
