import { AssemblyReview, type AssemblyProcessRequest } from './assembly-review';
import { CommonModule } from '@angular/common';
import { Component, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { LabActivity } from './lab-activity';
import { PendulumDirector } from './pendulum-director';
import { PendulumMasterPlayer } from './pendulum-master-player';
import { LabConnection } from './lab-connection';
import { SamVideoReplay, type SamProcessingSelection } from './sam-video-replay';

/**
 * Jury-facing Zone 1. Reuses the same validated Director session, upload,
 * model parameters and evidence endpoints; this class only changes the
 * presentation. The full/advanced Director remains preserved in the repo.
 */
@Component({
  selector: 'app-pendulum-zone-one',
  standalone: true,
  imports: [AssemblyReview, CommonModule, FormsModule, PendulumMasterPlayer, SamVideoReplay, LabActivity],
  templateUrl: './pendulum-zone-one.html',
  styleUrl: './pendulum-zone-one.scss'
})
export class PendulumZoneOne extends PendulumDirector {
  private readonly demoHttp=inject(HttpClient);
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
    if(this.videoActionsBusy())return;
    this.newFocusedStudy();
    this.sourceChoice.set('demo');this.choosingUpload.set(false);this.demoLoading.set(true);
    this.previewState.set('loading');
    try {
      // Local deployment configuration contains only a study ID, never video bytes or keys.
      const config=await firstValueFrom(this.demoHttp.get<{studyId:string}>('demo-video.json'));
      if(!/^[a-f0-9]{32}$/i.test(config.studyId))throw new Error('INVALID_DEMO');
      await this.restore(config.studyId);
      if(!this.session()?.baseline)throw new Error('DEMO_UNAVAILABLE');
      this.checkedVideo.set(false);
    } catch {
      this.session.set(null);this.previewState.set('error');
      this.error.set(this.es?'No se pudo cargar el video de prueba. Puedes reintentarlo o iniciar una nueva prueba.':
        'Could not load the test video. Try again or start a new trial.');
    } finally {this.demoLoading.set(false);}
  }

  sourcePending():boolean {
    return this.demoLoading() || this.registeringAndUploading() || !!this.stageBusy();
  }
  videoLoading():boolean {return this.sourcePending() || this.previewState()==='loading';}
  onPreviewState(state:'loading'|'ready'|'error'):void {this.previewState.set(state);}
  onProcessingStarted():void {this.resultState.set('loading');}
  onProcessingFinished(ready:boolean):void {
    this.resultState.set(ready?'ready':'error');this.onPlaybackReady(ready);
  }

  @ViewChild(AssemblyReview) private assemblyReview?:AssemblyReview;
  startAssembly(request:AssemblyProcessRequest):void {void this.assemblyReview?.startFromProcess(request);}
  readonly assemblyNonce=signal(0);
  onAssemblyConfirmed():void {this.clearProcessed();this.assemblyNonce.update(n=>n+1);}
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
  selectTrial(role:'baseline'|'followup'):void {
    if(role===this.activeTrial() || this.videoActionsBusy())return;
    this.clearProcessed();this.previewState.set('loading');this.activeTrial.set(role);
  }
  onPlaybackReady(ready:boolean):void {
    this.playbackReady.set(ready);
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

    this.sourceChoice.set('upload');
    const autoRegister = role === 'baseline' && !this.session();
    if (autoRegister) {
      // Preflight BEFORE creating a study to avoid orphan records on invalid
      // extension or size. The server performs its own stricter validation.
      if (file.size < 10000 || file.size > 240000000 ||
          !/\.(mov|mp4|m4v|webm|mkv|avi|3gp|3g2)$/i.test(file.name)) {
        this.error.set(this.es
          ? 'Video inválido. Selecciona MOV, MP4 u otro formato compatible de 10 KB a 240 MB.'
          : 'Invalid video. Select MOV, MP4 or another supported format (10 KB–240 MB).');
        control.value = '';
        return;
      }
      this.registeringAndUploading.set(true);
      this.error.set('');
    }

    this.previewState.set('loading');
    try {
      if (autoRegister) {
        // Always refresh from the currently visible length/angle/mass, so a
        // previous pending calculation cannot stamp stale parameters.
        await this.refreshTheory();
        if (!this.model()) {
          this.error.set(this.es
            ? 'No se pudo calcular la teoría. Corrige los parámetros antes de cargar el video.'
            : 'Could not calculate the model. Correct the parameters before upload.');
          return;
        }

        await this.start(); // persists model SHA-256 before media submission
        if (!this.session()) return; // no upload if registration failed
      }

      await this.upload(event, role);
      if((role==='baseline'?this.session()?.baseline:this.session()?.followup))this.choosingUpload.set(false);
      if (role === 'followup' && this.session()?.followup) {
        this.activeTrial.set('followup');
      }
    } finally {
      if (autoRegister) this.registeringAndUploading.set(false);
      const clip=role==='baseline'?this.session()?.baseline:this.session()?.followup;
      if(!clip)this.previewState.set(this.error()?'error':'empty');
      // Also clear after failed registration, to allow selecting same file.
      control.value = '';
    }
  }

  videoActionsBusy():boolean {
    return this.videoLoading() || this.busy() || this.resultState()==='loading' || this.connection.busy();
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
    this.newFocusedStudy(); // only clears the UI selection; stored studies are preserved
    await this.uploadFocused(event,'baseline');
  }

  newFocusedStudy():void {
    if(this.videoActionsBusy())return;
    this.clearProcessed();
    this.reset();
    this.activeTrial.set('baseline');
    this.sourceChoice.set('upload');this.choosingUpload.set(true);this.previewState.set('empty');
  }
}