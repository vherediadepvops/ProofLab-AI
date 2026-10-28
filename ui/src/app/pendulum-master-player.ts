import {
  AfterViewInit, Component, ElementRef, OnChanges, OnDestroy, SimpleChanges, ViewChild,
  inject, input, output, signal, computed
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { LabActivity } from './lab-activity';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  type KinematicRevision, type KinematicFrame, type ImageSize, pivotFromClick,
  sampleAtTime, vectorAt, idealAngularSpeedAtAngle, measureCordAngle, type CordAngleMeasurement
} from './pendulum-kinematics';
import {
  type CandidatePixelPoint, formatMediaTime, motionAt,
  seekSourceFrame, trailingMotion
} from './pendulum-master-sync';
import {
  verifiedSamPlaybackTrack, nearestSamMask, samAngularFrameAt,
  type AuthenticatedSamVideoArchive, type SamPlaybackTrack, type SamPlaybackMask
} from './sam-playback';

/**
 * ONE HTMLVideoElement is the only media clock and decoder.
 * Views 2/3 drawImage(the SAME decoded video frame) into canvases
 * on requestVideoFrameCallback, including pause and seeked.
 * No separate video players, model calls, or assumed SAM masks.
 */
@Component({
  selector: 'app-pendulum-master-player',
  standalone: true,
  imports: [LabActivity,CommonModule],
  templateUrl: './pendulum-master-player.html',
  styleUrl: './pendulum-master-player.scss'
})
export class PendulumMasterPlayer implements AfterViewInit, OnChanges, OnDestroy {
  private readonly http=inject(HttpClient,{optional:true});
  private readonly api='http://127.0.0.1:5391/api/science/director/pendulum/sessions';
  private revisionSerial=0;
  private revisionKey='';
  private samSerial=0;
  private readonly samPathCache=new Map<number,Path2D>();
  readonly samTrack=signal<SamPlaybackTrack|null>(null);
  readonly samStatus=signal('CHECKING_SAM_CACHE');
  @ViewChild('masterVideo') masterVideo?: ElementRef<HTMLVideoElement>;
  @ViewChild('vectorCanvas') vectorCanvas?: ElementRef<HTMLCanvasElement>;
  @ViewChild('ghostCanvas') ghostCanvas?: ElementRef<HTMLCanvasElement>;
  @ViewChild('combinedCanvas') combinedCanvas?: ElementRef<HTMLCanvasElement>;
  readonly videoUrl = input.required<string>();
  readonly name = input('pendulum.mp4');
  readonly originalFps = input(60);
  readonly originalWidth = input(1920);
  readonly originalHeight = input(1080);
  readonly originalCodec=input('');
  readonly decodedSize=signal<{width:number;height:number}|null>(null);
  readonly droppedFrames=signal(0);
  private decodedMediaTime:number|null=null;
  private pendingDecodedPaint=false;
  private sourceFramePts:number[]=[];
  sourceFps():number {
    const fps=this.originalFps();
    return Number.isFinite(fps) && fps>0?fps:60;
  }
  private frameTime():number {
    return this.decodedMediaTime ?? this.masterVideo?.nativeElement.currentTime ?? 0;
  }
  readonly points = input<CandidatePixelPoint[] | null>(null);
  readonly language = input<'es'|'en'>('es');
  readonly studyId = input('');
  readonly videoSha256 = input('');
  readonly videoRole = input<'baseline'|'followup'>('baseline');
  readonly initialAngleDegrees = input(15);
  readonly samRefreshNonce = input(0);
  readonly processingRequired=input(false);
  readonly processedAnalysisId=input('');
  readonly samApiPort=input<5391|5393|5398>(5391);
  readonly processingReady=output<boolean>();
  readonly processingFinished=output<boolean>();
  readonly processingPending=input(false);
  readonly previewStateChanged=output<'loading'|'ready'|'error'>();
  readonly previewLoading=signal(true);
  onPreviewError():void {
    this.currentCordAngle.set(null);
    this.previewLoading.set(false);
    const codec=this.originalCodec() || this.name().split('.').pop() || '';
    this.error.set(this.es?
      'No se pudo reproducir el original ('+codec+'). El navegador puede no admitir su formato o no haber podido cargarlo. No se ha sustituido por una copia reducida. Puedes descargar el original.':
      'Could not play the original ('+codec+'). Its format may be unsupported or loading may have failed. No reduced copy was substituted. You can download the original.');
    this.previewStateChanged.emit('error');
  }
  readonly samVerified=signal(false);
  playbackLocked():boolean {return this.processingRequired() && !this.samVerified();}
  private setVerified(value:boolean):void {
    this.samVerified.set(value);this.processingReady.emit(value);
  }
  readonly pivotMode = signal(false);
  readonly pivotSaving = signal(false);
  readonly pivotError = signal('');
  readonly pivotRevision = signal<KinematicRevision|null>(null);
  readonly currentKinematics = signal<KinematicFrame|null>(null);
  readonly currentCordAngle = signal<CordAngleMeasurement|null>(null);

  // One scale per track/pivot/native geometry, never a per-frame clamp or radius.
  private readonly maximumObservedAngularSpeed=computed(()=>{
    const track=this.samTrack(),revision=this.pivotRevision(),image=this.decodedSize();
    if(!track || !revision || !image)return 0;
    let peak=0;
    for(const point of track.points){
      const frame=samAngularFrameAt(track,revision.analysis,point.timeSeconds,image);
      const omega=frame?.angularVelocityRadiansPerSecond;
      if(frame?.isValid && omega!=null && Number.isFinite(omega))
        peak=Math.max(peak,Math.abs(omega));
    }
    return peak;
  });

  readonly playing = signal(false);
  readonly currentTime = signal(0);
  readonly duration = signal(0);
  readonly playbackRate = signal(1);
  readonly error = signal('');
  readonly lastPaintedTime = signal(0);
  readonly frameNumber = signal(0);
  readonly paintedViews = signal(0);
  readonly overlayStatus = signal('LOCAL_NOT_SAM');
  readonly activeView = signal<'combined'|'original'|'vectors'|'ghost'>('combined');
  readonly viewSlots = signal<('combined'|'original'|'vectors'|'ghost')[]>(
    ['combined','original','vectors','ghost']);
  readonly zoomed = signal(false);
  readonly panOffset = signal({x:0,y:0});
  readonly dragging = signal(false);
  private activeDrag:{pointerId:number,startX:number,startY:number,
    originalX:number,originalY:number,width:number,height:number,moved:boolean}|null=null;
  private suppressSurfaceClick=false;
  private frameCallbackId:number|null=null;
  private fallbackRafId:number|null=null;
  private disposed=false;
  private didPaint=false;
  private readoutResize?:ResizeObserver;
  private readonly readoutBounds=new WeakMap<HTMLCanvasElement,{width:number;height:number}>();

  get es():boolean{return this.language()==='es';}
  get timecode():string{return formatMediaTime(this.currentTime());}
  get lengthcode():string{return formatMediaTime(this.duration());}
  get frameStepSeconds():number{return 1/Math.max(1,this.sourceFps());}

  gridArea(view:'combined'|'original'|'vectors'|'ghost'):string {
    const position=this.viewSlots().indexOf(view);
    return position===0?'main':'side'+position;
  }

  selectView(view:'combined'|'original'|'vectors'|'ghost'):void {
    const existing=this.viewSlots(),slot=existing.indexOf(view);
    if(slot<=0)return;
    const next=[...existing];
    // Exact swap: old main occupies the clicked thumbnail's former slot.
    [next[0],next[slot]]=[next[slot],next[0]];
    this.viewSlots.set(next);
    this.activeView.set(view);
    // The HTMLVideoElement remains in the DOM with its original clock.
  }

  toggleZoom():void {
    this.zoomed.update(value=>!value);
    this.panOffset.set({x:0,y:0});
    this.activeDrag=null;
    this.dragging.set(false);
  }

  imageTransform():string {
    if(!this.zoomed())return 'none';
    const offset=this.panOffset();
    return 'translate('+offset.x+'%, '+offset.y+'%) scale(1.6)';
  }

  onSurfaceClick(view:'combined'|'original'|'vectors'|'ghost',
      event:MouseEvent):void {
    if(this.suppressSurfaceClick){this.suppressSurfaceClick=false;return;}
    if(this.pivotMode()){
      if(view==='original')void this.selectPivot(event);
      return;
    }
    this.selectView(view);
  }

  onPanStart(event:PointerEvent):void {
    this.suppressSurfaceClick=false;
    if(!this.zoomed() || event.button!==0)return;
    const surface=event.currentTarget as HTMLElement;
    const rect=surface.getBoundingClientRect();
    if(rect.width<=0 || rect.height<=0)return;
    const current=this.panOffset();
    this.activeDrag={pointerId:event.pointerId,startX:event.clientX,startY:event.clientY,
      originalX:current.x,originalY:current.y,width:rect.width,height:rect.height,moved:false};
    this.dragging.set(true);
    surface.setPointerCapture(event.pointerId);
    event.preventDefault();
  }

  onPanMove(event:PointerEvent):void {
    const drag=this.activeDrag;
    if(!drag || drag.pointerId!==event.pointerId)return;
    const dx=event.clientX-drag.startX,dy=event.clientY-drag.startY;
    drag.moved=drag.moved||Math.abs(dx)>4||Math.abs(dy)>4;
    if(!drag.moved)return;
    // 1.6x zoom exposes 30% scrollable image extent each way.
    const clamp=(v:number)=>Math.max(-30,Math.min(30,v));
    this.panOffset.set({
      x:clamp(drag.originalX+100*dx/drag.width),
      y:clamp(drag.originalY+100*dy/drag.height)
    });
    event.preventDefault();
  }

  onPanEnd(event:PointerEvent):void {
    const drag=this.activeDrag;
    if(!drag || drag.pointerId!==event.pointerId)return;
    this.suppressSurfaceClick=drag.moved;
    this.activeDrag=null;
    this.dragging.set(false);
    const surface=event.currentTarget as HTMLElement;
    if(surface.hasPointerCapture(event.pointerId))
      surface.releasePointerCapture(event.pointerId);
  }

  private pivotUrl():string{
    return this.api+'/'+this.studyId()+'/kinematics/'+this.videoRole();
  }

  private matchingRevision(value:KinematicRevision):boolean {
    return value.studyId===this.studyId() &&
      value.role===this.videoRole() &&
      value.videoSha256?.toLowerCase()===this.videoSha256().toLowerCase() &&
      value.analysis?.method==='LOCAL_2D_PIVOT_FINITE_DIFFERENCE_V1_NOT_SAM' &&
      value.analysis?.physicalUnitsEnabled===false &&
      value.analysis?.sourceWidth===480 && value.analysis?.sourceHeight===270 &&
      Array.isArray(value.analysis?.samples);
  }

  ngOnChanges(changes:SimpleChanges):void{
    if(changes['videoUrl']) {
      this.previewLoading.set(true);this.error.set('');
      this.decodedMediaTime=null;this.pendingDecodedPaint=false;this.decodedSize.set(null);this.droppedFrames.set(0);
      this.previewStateChanged.emit('loading');
    }
    const key=this.studyId()+'|'+this.videoRole()+'|'+this.videoSha256()+'|'+
      this.samApiPort()+'|'+this.processingRequired()+'|'+this.processedAnalysisId();
    if(this.revisionKey===key && !changes['samRefreshNonce']){
      if(changes['language'])this.paintFrame(this.frameTime());
      return;
    }
    this.revisionKey=key;
    const serial=++this.revisionSerial;
    const samSerial=++this.samSerial;
    this.masterVideo?.nativeElement.pause();
    this.setVerified(false);
    this.pivotRevision.set(null);this.currentKinematics.set(null);this.currentCordAngle.set(null);
    this.pivotMode.set(false);this.pivotError.set('');this.pivotSaving.set(false);
    this.samTrack.set(null);this.samPathCache.clear();this.sourceFramePts=[];
    if(this.processingRequired() && !this.processedAnalysisId()) {
      this.samStatus.set('AWAITING_PROCESS');
      this.paintFrame(this.frameTime());
      return;
    }
    this.samStatus.set('CHECKING_SAM_CACHE');
    if(this.http && /^[a-f0-9]{32}$/i.test(this.studyId()) &&
       /^[a-f0-9]{64}$/i.test(this.videoSha256())) {
      void this.loadPivot(serial);
      void this.loadSam(samSerial);
    } else {
      this.samStatus.set('NO_SAM_ARCHIVE_LOCAL_LEGACY_ONLY');
    }
  }

  /**
   * Only the server's SHA-checked provider archive is eligible to drive
   * the three observation views. The offline JSON inspector is never wired
   * into these scientific overlays.
   */
  private async loadSam(serial:number):Promise<void>{
    try {
      const endpoint='http://127.0.0.1:'+this.samApiPort()+
        '/api/science/director/pendulum/sessions/'+this.studyId()+'/sam/'+this.videoRole()+'/evidence';
      const archive=await firstValueFrom(
        this.http!.get<AuthenticatedSamVideoArchive>(endpoint));
      if(serial!==this.samSerial || this.disposed)return;
      if(this.processingRequired() && (
        archive.state.analysisId!==this.processedAnalysisId() ||
        archive.receipt.analysisId!==this.processedAnalysisId()))
        throw new Error('SAM_ANALYSIS_CHANGED_PROCESS_AGAIN');
      const {decodeSamVideoResponse}=await import('./sam-video-evidence');
      const result=await decodeSamVideoResponse(archive.rawResponseJson);
      if(serial!==this.samSerial || this.disposed)return;
      // Meta explicitly permits completed streams with zero matches.
      // A valid empty observation is NOT a corrupt cache, and never
      // generates imaginary SAM masks, points, vectors or ghost trails.
      if(result.result==='NO_MATCH'){
        if(archive.state.sourceSha256!==this.videoSha256() ||
           archive.receipt.originalVideoSha256!==this.videoSha256() ||
           archive.state.status!=='CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW' ||
           archive.receipt.providerEvidenceStatus!=='COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW' ||
           archive.receipt.model!=='sam-3.1' || archive.receipt.concept!=='metal pendulum bob')
          throw new Error('SAM_NO_MATCH_PROVENANCE_INVALID');
        this.rememberFramePts(archive.receipt.framePtsSeconds);
        this.samTrack.set(null);this.setVerified(true);
        this.samStatus.set('SAM_3_1_COMPLETED_NO_MATCH');
        this.processingFinished.emit(true);
        this.paintFrame(this.frameTime());
        return;
      }
      const track=verifiedSamPlaybackTrack(archive,result,this.videoSha256());
      this.rememberFramePts(archive.receipt.framePtsSeconds);
      this.samTrack.set(track);this.setVerified(true);
      this.samStatus.set('SAM_3_1_CACHED_MASKS_PENDING_HUMAN_BOB_REVIEW');
      this.processingFinished.emit(true);
      this.paintFrame(this.frameTime());
    } catch(e:unknown) {
      if(serial!==this.samSerial || this.disposed)return;
      this.samTrack.set(null);this.setVerified(false);
      if((e as {status?:number})?.status===404){
        this.samStatus.set('NO_SAM_ARCHIVE_LOCAL_LEGACY_ONLY');
      }else{
        this.samStatus.set('SAM_CACHE_INVALID_OR_AMBIGUOUS_NO_FALLBACK');
      }
      this.processingFinished.emit(false);
      this.paintFrame(this.frameTime());
    }
  }

  private async loadPivot(serial:number):Promise<void>{
    try{
      const result=await firstValueFrom(this.http!.get<KinematicRevision>(this.pivotUrl()));
      if(serial!==this.revisionSerial || this.disposed)return;
      if(!this.matchingRevision(result)){
        this.pivotError.set(this.es
          ? 'La revisión guardada no corresponde a este video.'
          : 'Stored pivot revision does not match this video.');
        return;
      }
      this.pivotRevision.set(result);
      this.paintFrame(this.frameTime());
    }catch(error:unknown){
      if(serial!==this.revisionSerial || this.disposed)return;
      if((error as {status?:number})?.status!==404)
        this.pivotError.set(this.es
          ? 'No se pudo recuperar el pivote guardado.'
          : 'Could not restore the saved pivot.');
    }
  }

  beginPivotSelection():void{
    if(this.playbackLocked() || !this.http || !/^[a-f0-9]{32}$/i.test(this.studyId()) ||
       !/^[a-f0-9]{64}$/i.test(this.videoSha256()) ||
       (this.points()?.length??0)<5 || this.pivotSaving())return;
    this.masterVideo?.nativeElement.pause();
    this.pivotMode.set(true);
    this.pivotError.set('');
    this.zoomed.set(false);
    this.panOffset.set({x:0,y:0});
    this.activeDrag=null;this.dragging.set(false);
    this.selectView('original');
  }

  cancelPivotSelection():void{
    if(!this.pivotSaving())this.pivotMode.set(false);
  }

  private async selectPivot(event:MouseEvent):Promise<void>{
    const video=this.masterVideo?.nativeElement;
    if(!video || !this.http || !this.pivotMode() || this.pivotSaving())return;
    const position=pivotFromClick(event.clientX,event.clientY,
      video.getBoundingClientRect(),video.videoWidth,video.videoHeight);
    if(!position){
      this.pivotError.set(this.es
        ? 'Elige un punto dentro de la imagen, no sobre la banda negra.'
        : 'Choose a point inside the image, not a black bar.');
      return;
    }
    const serial=++this.revisionSerial;
    this.pivotSaving.set(true);
    this.pivotError.set('');
    try{
      const result=await firstValueFrom(this.http.post<KinematicRevision>(
        this.pivotUrl(),{pivotXpx:position.pivotXpx,pivotYpx:position.pivotYpx,
          videoSha256:this.videoSha256()}));
      if(serial!==this.revisionSerial || this.disposed)return;
      if(!this.matchingRevision(result)){
        this.pivotError.set(this.es
          ? 'Los resultados no corresponden a este video.'
          : 'Analysis response does not match this video.');
        return;
      }
      this.pivotRevision.set(result);
      this.pivotMode.set(false);
      this.paintFrame(this.frameTime());
    }catch{
      if(serial===this.revisionSerial && !this.disposed)
        this.pivotError.set(this.es
          ? 'No se pudo registrar el pivote. Revisa el punto e inténtalo otra vez.'
          : 'Could not save the pivot. Check the point and retry.');
    }finally{
      if(serial===this.revisionSerial)this.pivotSaving.set(false);
    }
  }

  ngAfterViewInit():void{
    this.readoutResize=new ResizeObserver(entries=>{
      for(const entry of entries)this.readoutBounds.set(entry.target as HTMLCanvasElement,
        {width:entry.contentRect.width,height:entry.contentRect.height});
      if(!this.disposed && !this.playing())this.paintFrame(this.frameTime());
    });
    for(const canvas of [this.vectorCanvas?.nativeElement,this.combinedCanvas?.nativeElement])
      if(canvas)this.readoutResize.observe(canvas);
    this.setCanvasSizes();
    this.paintFrame(0);
    this.startFrameLoop();
  }

  private rememberFramePts(pts:number[]|undefined):void {
    this.sourceFramePts=Array.isArray(pts) && pts.every((t,i)=>
      Number.isFinite(t) && t>=0 && (i===0 || t>pts[i-1])) ? pts : [];
  }

  ngOnDestroy():void{
    this.disposed=true;
    this.readoutResize?.disconnect();
    this.stopFrameLoop();
    ++this.revisionSerial;
    ++this.samSerial;
    this.samPathCache.clear();
    this.masterVideo?.nativeElement.pause();
  }

  onLoadedMetadata():void{
    const video=this.masterVideo?.nativeElement;
    if(!video)return;
    this.duration.set(Number.isFinite(video.duration)?video.duration:0);
    this.decodedSize.set({width:video.videoWidth,height:video.videoHeight});
    this.setCanvasSizes();
    this.startFrameLoop();
    if(typeof video.requestVideoFrameCallback!=='function')this.paintFrame(video.currentTime);
  }

  onDecodedFrame():void{
    const video=this.masterVideo?.nativeElement;
    if(!video)return;
    if(this.previewLoading() && video.readyState>=2) {
      this.previewLoading.set(false);this.previewStateChanged.emit('ready');
    }
    // currentTime is a clock, not necessarily the decoded frame's timestamp.
    // With rVFC only its mediaTime may choose masks, including paused seeks.
    if(typeof video.requestVideoFrameCallback!=='function') {
      this.decodedMediaTime=video.currentTime;
      this.paintFrame(video.currentTime);
    } else if(this.pendingDecodedPaint && !video.seeking && video.readyState>=2) {
      this.pendingDecodedPaint=false;
      this.paintFrame(this.frameTime());
    }
  }

  onPlay():void{
    if(this.playbackLocked()){this.masterVideo?.nativeElement.pause();return;}
    this.playing.set(true);
    this.startFrameLoop();
  }

  onPause():void{
    this.playing.set(false);
    const video=this.masterVideo?.nativeElement;
    // Keep rVFC armed while paused: a seek presents a new decoded frame.
    if(video && typeof video.requestVideoFrameCallback!=='function') {
      this.stopFrameLoop();this.onDecodedFrame();
    }
  }

  onEnded():void{
    this.onPause();
  }

  togglePlayback():void{
    if(this.playbackLocked())return;
    const video=this.masterVideo?.nativeElement;
    if(!video)return;
    if(video.paused){
      void video.play().catch(()=>{
        this.error.set(this.es
          ? 'El navegador no pudo iniciar la reproducción del archivo.'
          : 'The browser could not start this video.');
      });
    }else video.pause();
  }

  setSpeed(speed:number):void{
    if(!Number.isFinite(speed) || speed<.25 || speed>2)return;
    const video=this.masterVideo?.nativeElement;
    if(video)video.playbackRate=speed;
    this.playbackRate.set(speed);
  }

  stepFrame(step:-1|1):void{
    if(this.playbackLocked())return;
    const video=this.masterVideo?.nativeElement;
    if(!video)return;
    video.pause();
    const duration=Number.isFinite(video.duration)?video.duration:this.duration();
    const next=seekSourceFrame(this.sourceFramePts,this.frameTime(),step,this.sourceFps(),duration);
    video.currentTime=next;
    this.currentTime.set(next);
    // Actual canvases refresh from the decoded frame on seeked.
  }

  scrub(event:Event):void{
    if(this.playbackLocked())return;
    const video=this.masterVideo?.nativeElement;
    if(!video)return;
    const input=event.target as HTMLInputElement;
    const time=Number(input.value);
    if(!Number.isFinite(time))return;
    video.pause();
    const safe=Math.max(0,Math.min(time,Number.isFinite(video.duration)
      ?video.duration:0));
    video.currentTime=safe;
    this.currentTime.set(safe);
  }

  private startFrameLoop():void{
    this.stopFrameLoop();
    const video=this.masterVideo?.nativeElement;
    if(!video || this.disposed)return;
    if(typeof video.requestVideoFrameCallback==='function'){
      const frame=(_:DOMHighResTimeStamp,metadata:VideoFrameCallbackMetadata)=>{
        this.frameCallbackId=null;
        if(this.disposed)return;
        this.decodedMediaTime=metadata.mediaTime;
        this.pendingDecodedPaint=video.readyState<HTMLMediaElement.HAVE_CURRENT_DATA;
        if(!this.pendingDecodedPaint)this.paintFrame(metadata.mediaTime);
        this.droppedFrames.set(video.getVideoPlaybackQuality?.().droppedVideoFrames ?? 0);
        this.frameCallbackId=video.requestVideoFrameCallback(frame);
      };
      this.frameCallbackId=video.requestVideoFrameCallback(frame);
    }else{
      if(video.paused)return;
      const frame=()=>{
        this.fallbackRafId=null;
        if(this.disposed || video.paused)return;
        this.decodedMediaTime=video.currentTime;
        this.paintFrame(video.currentTime);
        this.fallbackRafId=requestAnimationFrame(frame);
      };
      this.fallbackRafId=requestAnimationFrame(frame);
    }
  }

  private stopFrameLoop():void{
    const video=this.masterVideo?.nativeElement;
    if(this.frameCallbackId!==null &&
      video && typeof video.cancelVideoFrameCallback==='function')
      video.cancelVideoFrameCallback(this.frameCallbackId);
    if(this.fallbackRafId!==null)cancelAnimationFrame(this.fallbackRafId);
    this.frameCallbackId=null;
    this.fallbackRafId=null;
  }

  private setCanvasSizes():void{
    const source=this.masterVideo?.nativeElement;
    const videoWidth=source?.videoWidth||640,videoHeight=source?.videoHeight||360;
    const width=videoWidth;
    const height=Math.max(1,Math.round(width*videoHeight/videoWidth));
    for(const canvas of [this.vectorCanvas?.nativeElement,
       this.ghostCanvas?.nativeElement,this.combinedCanvas?.nativeElement]){
      if(!canvas)continue;
      canvas.width=width;
      canvas.height=height;
    }
  }

  formatMetric(value:number|null|undefined,digits=2):string {
    return value!=null && Number.isFinite(value)?value.toFixed(digits):'—';
  }

  cordAngleText():string {
    const value=this.currentCordAngle()?.angleDegrees;
    if(value==null)return '—';
    const rounded=Math.round(value*10)/10;
    return (rounded>0?'+':rounded<0?'−':'')+
      Math.abs(rounded).toFixed(1).replace('.',this.es?',':'.')+'°';
  }

  cordAngleDirection():string {
    const value=this.currentCordAngle()?.angleDegrees;
    if(value==null)return this.es?'Sin medición':'No measurement';
    if(Math.abs(value)<.05)return this.es?'En la vertical':'On the vertical';
    return value>0?(this.es?'Derecha':'Right'):(this.es?'Izquierda':'Left');
  }

  cordAngleHint():string {
    if(!this.samTrack())return this.es?'Se necesitan detecciones de la plomada para medir el ángulo.':'Bob detections are required to measure the angle.';
    if(!this.pivotRevision())return this.es?'Marca el punto de suspensión para medir el ángulo.':'Select the suspension point to measure the angle.';
    if(!this.currentCordAngle())return this.es?'Sin detección válida en este fotograma.':'No valid detection in this frame.';
    return this.es?'Estimado entre la suspensión y el centro de la plomada.':'Estimated between the suspension point and the bob centre.';
  }

  angularVelocityAtFrame():number|null {
    const frame=this.currentKinematics(),angle=this.currentCordAngle();
    // The visible angle and derivative must refer to the same SAM observation.
    // Never substitute local tracking, ideal speed or a stale neighbouring frame.
    if(this.playbackLocked() || !this.samTrack() || !angle || !frame?.isValid ||
        Math.abs(frame.bobXpx-angle.bobXpx)>1e-6 ||
        Math.abs(frame.bobYpx-angle.bobYpx)>1e-6)return null;
    const velocity=frame.angularVelocityRadiansPerSecond;
    return velocity!=null && Number.isFinite(velocity)?velocity:null;
  }

  angularVelocityText(degrees=false):string {
    const value=this.angularVelocityAtFrame();
    if(value==null)return '—';
    const digits=degrees?1:3,factor=10**digits;
    const rounded=Math.round(value*(degrees?180/Math.PI:1)*factor)/factor;
    return (rounded>0?'+':rounded<0?'−':'')+
      Math.abs(rounded).toFixed(digits).replace('.',this.es?',':'.');
  }

  angularVelocityDirection():string {
    const value=this.angularVelocityAtFrame();
    if(value==null)return this.es?'Sin medición':'No measurement';
    if(Math.abs(value)<.0005)return this.es?'Cambio angular ≈ 0':'Angular change ≈ 0';
    return value>0?(this.es?'Hacia la derecha':'Moving right'):
      (this.es?'Hacia la izquierda':'Moving left');
  }

  angularVelocityHint():string {
    if(!this.currentCordAngle())return this.cordAngleHint();
    if(this.angularVelocityAtFrame()==null)return this.es?
      'Faltan detecciones cercanas válidas para calcular la velocidad angular.':
      'Not enough valid nearby detections to calculate angular velocity.';
    return this.es?'ω = dθ/dt · Calculada con los tiempos originales del video.':
      'ω = dθ/dt · Calculated using the original video timestamps.';
  }

  idealAtFrame():number|null {
    const revision=this.pivotRevision(),frame=this.currentKinematics();
    if(!revision || !frame || !frame.isValid)return null;
    const max=revision.analysis.theoreticalMaxAngularSpeedRadPerSec;
    return max===null?null:idealAngularSpeedAtAngle(
      frame.angleDegrees,this.initialAngleDegrees(),max);
  }

  private drawArrow(ctx:CanvasRenderingContext2D,
    x:number,y:number,dx:number,dy:number,color:string,scale:number,
    unit=1,exactLength?:number):void{
    const size=Math.hypot(dx,dy);
    if(!Number.isFinite(size)||size===0)return;
    // The cyan arrow has a linear angular-speed length; other vectors retain their scale.
    const length=exactLength ?? Math.min(170*unit,Math.max(0,size*scale*3));
    if(!Number.isFinite(length)||length<=0)return;
    if(exactLength===undefined && (size<.01 || length<2*unit))return;
    // Shrink the shaft and head to zero too; no residual dot near a reversal.
    const strokeUnit=exactLength===undefined?unit:Math.min(unit,length/8);
    const angle=Math.atan2(dy,dx);
    const endX=x+Math.cos(angle)*length,endY=y+Math.sin(angle)*length;
    const head=Math.min(16*unit,length*.4);
    ctx.save();
    ctx.lineCap=exactLength===undefined?'round':'butt';ctx.lineJoin='round';ctx.globalAlpha=.72;
    ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(endX,endY);
    // Fine translucent strokes preserve the video beneath the vector.
    ctx.strokeStyle='rgba(4,12,21,.45)';ctx.lineWidth=3*strokeUnit;ctx.stroke();
    ctx.strokeStyle=color;ctx.lineWidth=1.6*strokeUnit;ctx.stroke();
    ctx.beginPath();ctx.moveTo(endX,endY);
    ctx.lineTo(endX-head*Math.cos(angle-.5),endY-head*Math.sin(angle-.5));
    ctx.lineTo(endX-head*Math.cos(angle+.5),endY-head*Math.sin(angle+.5));
    ctx.closePath();
    ctx.strokeStyle='rgba(4,12,21,.45)';ctx.lineWidth=strokeUnit;ctx.stroke();
    ctx.fillStyle=color;ctx.fill();
    ctx.restore();
  }

  private paintSamMask(
    ctx:CanvasRenderingContext2D,canvas:HTMLCanvasElement,
    track:SamPlaybackTrack,mark:SamPlaybackMask):void {
    let path=this.samPathCache.get(mark.frameIndex);
    if(!path) {
      path=new Path2D(mark.maskPath);
      this.samPathCache.set(mark.frameIndex,path);
      if(this.samPathCache.size>96)
        this.samPathCache.delete(this.samPathCache.keys().next().value!);
    }
    ctx.save();
    ctx.scale(canvas.width/track.frameWidth,canvas.height/track.frameHeight);
    ctx.translate(mark.left,mark.top);
    ctx.fillStyle='rgba(10,244,183,.12)';
    ctx.strokeStyle='rgba(67,241,206,.65)';
    ctx.lineWidth=2;
    ctx.fill(path);
    ctx.stroke(path);
    ctx.restore();
  }

  /** Reference and arc share the native image geometry and decoded media clock. */
  private paintCordAngle(ctx:CanvasRenderingContext2D,canvas:HTMLCanvasElement,
      measurement:CordAngleMeasurement,revision:KinematicRevision):void {
    const unit=Math.min(canvas.width,canvas.height)/480;
    const px=revision.analysis.pivotXpx*canvas.width/480;
    const py=revision.analysis.pivotYpx*canvas.height/270;
    const bx=measurement.bobXpx*canvas.width/480;
    const by=measurement.bobYpx*canvas.height/270;
    const length=Math.hypot(bx-px,by-py);
    const radius=Math.min(length*.28,60*unit);
    const theta=measurement.angleDegrees*Math.PI/180;
    ctx.save();
    ctx.lineWidth=.8*unit;ctx.strokeStyle='rgba(181,199,219,.45)';
    ctx.setLineDash([5*unit,5*unit]);
    ctx.beginPath();ctx.moveTo(px,py);
    ctx.lineTo(px,Math.min(canvas.height,py+length));ctx.stroke();
    ctx.setLineDash([]);
    ctx.lineWidth=unit;ctx.strokeStyle='rgba(255,207,112,.55)';
    ctx.beginPath();ctx.moveTo(px,py);ctx.lineTo(bx,by);ctx.stroke();
    // Canvas y points down; a positive angle moves from vertical towards right.
    ctx.beginPath();ctx.arc(px,py,radius,Math.PI/2,Math.PI/2-theta,theta>0);ctx.stroke();
    ctx.restore();
  }

  /** Micro readout follows the current SAM centroid on the decoded frame. */
  private paintFrameReadout(ctx:CanvasRenderingContext2D,canvas:HTMLCanvasElement):void {
    const bob=this.currentCordAngle();
    if(!bob)return; // No detection means no floating label at an invented position.
    const bounds=this.readoutBounds.get(canvas);
    const fit=bounds?Math.min(bounds.width/canvas.width,bounds.height/canvas.height):0;
    // About 12 CSS px, with 20 CSS px clearance above the bob; fit small previews.
    const unit=Math.min(canvas.width/150,
      fit>0?1/fit:Math.min(canvas.width,canvas.height)/480);
    const bx=bob.bobXpx*canvas.width/480,by=bob.bobYpx*canvas.height/270;
    const lines=[
      {text:'θ: '+this.cordAngleText(),color:'#ffe2a8'},
      {text:'ω: '+this.angularVelocityText()+' rad/s',color:'#a6efed'}
    ];
    ctx.save();
    ctx.font='600 '+(12*unit)+'px system-ui';
    ctx.textBaseline='bottom';ctx.globalAlpha=.85;
    const width=Math.max(...lines.map(line=>ctx.measureText(line.text).width));
    const x=Math.max(4*unit,Math.min(canvas.width-width-4*unit,bx-width/2));
    const bottom=Math.max(31*unit,Math.min(canvas.height-4*unit,by-20*unit));
    ctx.lineJoin='round';ctx.lineWidth=2*unit;ctx.strokeStyle='rgba(4,12,21,.8)';
    lines.forEach((line,index)=>{
      const y=bottom-(1-index)*15*unit;
      ctx.strokeText(line.text,x,y);
      ctx.fillStyle=line.color;ctx.fillText(line.text,x,y);
    });
    ctx.restore();
  }

  /** Draw candidate geometric measurements; never paint metric XY. */
  private paintAngular(ctx:CanvasRenderingContext2D,canvas:HTMLCanvasElement,
      frame:KinematicFrame|null,revision:KinematicRevision,showArrows:boolean,
      geometry:ImageSize):void {
    const fx=canvas.width/480,fy=canvas.height/270;
    const pivotX=revision.analysis.pivotXpx*fx;
    const pivotY=revision.analysis.pivotYpx*fy;
    ctx.save();
    ctx.lineWidth=2.5;ctx.strokeStyle='#ffcc55';
    ctx.beginPath();ctx.arc(pivotX,pivotY,6,0,2*Math.PI);ctx.stroke();
    ctx.beginPath();ctx.moveTo(pivotX-11,pivotY);
    ctx.lineTo(pivotX+11,pivotY);ctx.moveTo(pivotX,pivotY-11);
    ctx.lineTo(pivotX,pivotY+11);ctx.stroke();
    if(frame && Number.isFinite(frame.bobXpx) && Number.isFinite(frame.bobYpx)){
      const bx=frame.bobXpx*fx,by=frame.bobYpx*fy;
      ctx.strokeStyle='#ffcc5580';ctx.lineWidth=1.6;
      ctx.beginPath();ctx.moveTo(pivotX,pivotY);ctx.lineTo(bx,by);ctx.stroke();
      ctx.beginPath();ctx.arc(bx,by,7,0,2*Math.PI);
      ctx.strokeStyle='#ffe6a3';ctx.lineWidth=2;ctx.stroke();
      if(showArrows){
        const v=vectorAt(frame,{pivotXpx:revision.analysis.pivotXpx,
          pivotYpx:revision.analysis.pivotYpx},geometry);
        if(v){
          const sx=canvas.width/geometry.width,sy=canvas.height/geometry.height;
          const arrowUnit=Math.min(canvas.width,canvas.height)/480;
          const peak=this.maximumObservedAngularSpeed();
          const omega=Math.abs(frame.angularVelocityRadiansPerSecond!);
          // The largest valid measured |ω| spans 90 drawing units (18.75% of the short side).
          // A fixed scale keeps half-speed at half-length, regardless of radius or frame order.
          const velocityLength=this.samTrack()?(peak>0?90*arrowUnit*omega/peak:0):undefined;
          this.drawArrow(ctx,bx,by,v.vx*sx,v.vy*sy,'#3cf2da',.22,arrowUnit,velocityLength);
          this.drawArrow(ctx,bx,by,v.atx*sx,v.aty*sy,'#ffb34d',.035,arrowUnit);
          this.drawArrow(ctx,bx,by,v.arx*sx,v.ary*sy,'#fb83ff',.035,arrowUnit);
        }
      }
    }
    ctx.restore();
  }

  /**
   * All overlays use one media timestamp; no incremental state clock.
   * Canvas content is just visualization: source tracker may be incorrect.
   */
  paintFrame(mediaTime:number):void{
    const video=this.masterVideo?.nativeElement;
    const canvases=[this.vectorCanvas?.nativeElement,
                    this.ghostCanvas?.nativeElement,
                    this.combinedCanvas?.nativeElement];
    if(!video || canvases.some(x=>!x))return;
    const time=Number.isFinite(mediaTime)?mediaTime:video.currentTime;
    if(!Number.isFinite(time))return;

    const ctxs=canvases.map(canvas=>canvas!.getContext('2d'));
    if(ctxs.some(x=>x===null))return;
    const frameReady=video.readyState>=HTMLMediaElement.HAVE_CURRENT_DATA;
    if(!frameReady)return; // Keep the last complete frame during a seek.
    try{
      for(let i=0;i<ctxs.length;i++){
        const ctx=ctxs[i]!,canvas=canvases[i]!;
        ctx.clearRect(0,0,canvas.width,canvas.height);
        ctx.drawImage(video,0,0,canvas.width,canvas.height);
      }
    }catch{
      this.error.set(this.es
        ? 'No es posible dibujar el video en los lienzos. Verifica la política CORS del servidor.'
        : 'Cannot draw the source video into overlays. Check server CORS.');
      return;
    }

    if(this.playbackLocked()){
      this.currentKinematics.set(null);this.currentCordAngle.set(null);
      this.overlayStatus.set(this.samStatus());
      this.paintedViews.set(4);
      return;
    }
    const sam=this.samTrack();
    // SAM authenticated cache takes priority. If an archive is invalid or
    // ambiguous, do NOT silently replace its observations with local data.
    const legacy=!this.processingRequired() && this.samStatus()==='NO_SAM_ARCHIVE_LOCAL_LEGACY_ONLY';
    const samples=sam?.points ?? (legacy?(this.points()??[]):[]);
    const revision=(sam || !this.processingRequired())?this.pivotRevision():null;
    const geometry:ImageSize=sam
      ? {width:video.videoWidth,height:video.videoHeight}
      : {width:480,height:270}; // Legacy saved derivatives retain their own coordinate system.
    const frame=revision ?
      (sam?samAngularFrameAt(sam,{
        pivotXpx:revision.analysis.pivotXpx,
        pivotYpx:revision.analysis.pivotYpx
      },time,geometry):sampleAtTime(revision.analysis.samples,time)) : null;
    this.currentKinematics.set(frame);
    const mask=sam?nearestSamMask(sam,time):null;
    // The angle needs one actual observation, not valid velocity/acceleration.
    // A missing source-frame detection must never retain the last angle.
    const angleMask=sam?nearestSamMask(sam,time,Math.min(.055,.5/this.sourceFps())):null;
    this.currentCordAngle.set(sam && angleMask && revision
      ? measureCordAngle(angleMask.centroidXpx/sam.frameWidth*480,
          angleMask.centroidYpx/sam.frameHeight*270,revision.analysis,geometry)
      : null);
    if(sam && mask) {
      try{
        this.paintSamMask(ctxs[0]!,canvases[0]!,sam,mask);
        this.paintSamMask(ctxs[2]!,canvases[2]!,sam,mask);
      }catch{
        this.currentCordAngle.set(null);
        this.overlayStatus.set('SAM_MASK_PATH_INVALID_NO_FABRICATED_OVERLAY');
        return;
      }
    }
    // A SAM frame with no nearby authentic mask is NOT an observation:
    // do not reuse a distant centroid or silently show FFmpeg vectors.
    const detected=sam && !mask ? null : motionAt(samples,time);
    if(detected){
      this.overlayStatus.set(sam
        ? (revision
          ? (frame?.isValid?'SAM_3_1_CACHED_PX_KINEMATICS_REVIEW_PENDING'
            :'SAM_MASK_VALID_ANGULAR_DERIVATIVE_MISSING')
          :'SAM_3_1_CACHED_PX_TRACK_REVIEW_PENDING')
        : (revision
          ? (frame?.isValid?'LOCAL_ANGULAR_CANDIDATE_NOT_SAM'
            :'ANGULAR_DERIVATIVE_UNAVAILABLE')
          :'LOCAL_PIXEL_CANDIDATE_NOT_SAM'));
      const canvas=canvases[0]!;
      const factorX=canvas.width/480, factorY=canvas.height/270;
      const x=detected.x*factorX,y=detected.y*factorY;
      const vx=detected.vxPixelsPerSecond*factorX;
      const vy=detected.vyPixelsPerSecond*factorY;
      const magnitude=Math.hypot(vx,vy);
      const length=magnitude<3?0:Math.min(94,magnitude*.12);
      const angle=Math.atan2(vy,vx);
      if(!revision){
      for(const ctx of [ctxs[0]!,ctxs[2]!]){
      ctx.lineWidth=Math.max(2,canvas.width/275);
      ctx.strokeStyle='#46e0e2';
      ctx.fillStyle='#46e0e2';
      ctx.beginPath();ctx.arc(x,y,Math.max(5,canvas.width/120),0,2*Math.PI);ctx.stroke();
      if(length>0){
        const endX=x+Math.cos(angle)*length,endY=y+Math.sin(angle)*length;
        ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(endX,endY);ctx.stroke();
        const head=10;
        ctx.beginPath();ctx.moveTo(endX,endY);
        ctx.lineTo(endX-head*Math.cos(angle-.43),endY-head*Math.sin(angle-.43));
        ctx.lineTo(endX-head*Math.cos(angle+.43),endY-head*Math.sin(angle+.43));
        ctx.closePath();ctx.fill();
      }
      ctx.font='600 13px system-ui';ctx.fillStyle='#46e0e2';
      ctx.fillText('v '+(sam?magnitude:detected.speedPixelsPerSecond).toFixed(1)+' px/s',
        14,Math.max(23,canvas.height-16));
      }
      }
      const trail=trailingMotion(samples,time);
      const ghostCanvas=canvases[1]!;
      const fx=ghostCanvas.width/480,fy=ghostCanvas.height/270;
      const trailUnit=Math.min(ghostCanvas.width,ghostCanvas.height)/480;
      for(const ghostCtx of [ctxs[1]!,ctxs[2]!]){
      for(let i=0;i<trail.length;i++){
        const item=trail[i];
        const ratio=(i+1)/trail.length;
        ghostCtx.beginPath();
        ghostCtx.fillStyle='rgba(217,151,255,'+(.04+.18*ratio).toFixed(2)+')';
        ghostCtx.arc(item.x*fx,item.y*fy,1.4*trailUnit,
          0,2*Math.PI);
        ghostCtx.fill();
      }
      ghostCtx.beginPath();
      ghostCtx.strokeStyle='rgba(229,177,255,.45)';ghostCtx.lineWidth=trailUnit;
      trail.forEach((item,i)=>{
        if(i===0)ghostCtx.moveTo(item.x*fx,item.y*fy);
        else ghostCtx.lineTo(item.x*fx,item.y*fy);
      });
      if(trail.length>1)ghostCtx.stroke();
      }
    }else{
      this.overlayStatus.set(sam
        ? 'SAM_NO_RELIABLE_MASK_DERIVATIVE_AT_THIS_SOURCE_FRAME'
        : (!legacy ? this.samStatus() :
          revision?'NO_RELIABLE_LOCAL_TRACK_FOR_THIS_FRAME'
          : samples.length>0?'NO_RELIABLE_LOCAL_SAMPLE_AT_THIS_FRAME'
          :'NO_2D_TRACK_FOR_THIS_VIDEO'));
    }
    if(revision){
      this.paintAngular(ctxs[0]!,canvases[0]!,frame,revision,true,geometry);
      this.paintAngular(ctxs[1]!,canvases[1]!,frame,revision,false,geometry);
      this.paintAngular(ctxs[2]!,canvases[2]!,frame,revision,true,geometry);
    }
    const cordAngle=this.currentCordAngle();
    if(revision && cordAngle)this.paintCordAngle(ctxs[2]!,canvases[2]!,cordAngle,revision);
    if(sam){
      this.paintFrameReadout(ctxs[0]!,canvases[0]!);
      this.paintFrameReadout(ctxs[2]!,canvases[2]!);
    }
    const duration=this.duration();
    this.currentTime.set(Math.max(0,Math.min(duration||time,time)));
    this.lastPaintedTime.set(time);
    this.frameNumber.set(Math.round(time*this.sourceFps()));
    this.paintedViews.set(4); // one master video + three derived canvases
    this.didPaint=true;
  }
}
