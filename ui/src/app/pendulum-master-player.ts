import {
  AfterViewInit, Component, ElementRef, OnChanges, OnDestroy, SimpleChanges, ViewChild,
  inject, input, output, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { LabActivity } from './lab-activity';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  type KinematicRevision, type KinematicFrame, pivotFromClick,
  sampleAtTime, vectorAt, idealAngularSpeedAtAngle
} from './pendulum-kinematics';
import {
  type CandidatePixelPoint, formatMediaTime, motionAt,
  seekByFrame, trailingMotion
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
  readonly proxyFps = input(12);
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
    this.previewLoading.set(false);
    this.error.set(this.es?'El navegador no pudo decodificar la vista previa.':'Browser preview decode failed.');
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

  get es():boolean{return this.language()==='es';}
  get timecode():string{return formatMediaTime(this.currentTime());}
  get lengthcode():string{return formatMediaTime(this.duration());}
  get frameStepSeconds():number{return 1/Math.max(1,this.proxyFps());}

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
      this.previewStateChanged.emit('loading');
    }
    const key=this.studyId()+'|'+this.videoRole()+'|'+this.videoSha256()+'|'+
      this.samApiPort()+'|'+this.processingRequired()+'|'+this.processedAnalysisId();
    if(this.revisionKey===key && !changes['samRefreshNonce'])return;
    this.revisionKey=key;
    const serial=++this.revisionSerial;
    const samSerial=++this.samSerial;
    this.masterVideo?.nativeElement.pause();
    this.setVerified(false);
    this.pivotRevision.set(null);this.currentKinematics.set(null);
    this.pivotMode.set(false);this.pivotError.set('');this.pivotSaving.set(false);
    this.samTrack.set(null);this.samPathCache.clear();
    if(this.processingRequired() && !this.processedAnalysisId()) {
      this.samStatus.set('AWAITING_PROCESS');
      this.paintFrame(this.masterVideo?.nativeElement.currentTime??0);
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
        this.samTrack.set(null);this.setVerified(true);
        this.samStatus.set('SAM_3_1_COMPLETED_NO_MATCH');
        this.processingFinished.emit(true);
        this.paintFrame(this.masterVideo?.nativeElement.currentTime??0);
        return;
      }
      const track=verifiedSamPlaybackTrack(archive,result,this.videoSha256());
      this.samTrack.set(track);this.setVerified(true);
      this.samStatus.set('SAM_3_1_CACHED_MASKS_PENDING_HUMAN_BOB_REVIEW');
      this.processingFinished.emit(true);
      this.paintFrame(this.masterVideo?.nativeElement.currentTime??0);
    } catch(e:unknown) {
      if(serial!==this.samSerial || this.disposed)return;
      this.samTrack.set(null);this.setVerified(false);
      if((e as {status?:number})?.status===404){
        this.samStatus.set('NO_SAM_ARCHIVE_LOCAL_LEGACY_ONLY');
      }else{
        this.samStatus.set('SAM_CACHE_INVALID_OR_AMBIGUOUS_NO_FALLBACK');
      }
      this.processingFinished.emit(false);
      this.paintFrame(this.masterVideo?.nativeElement.currentTime??0);
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
      this.paintFrame(this.masterVideo?.nativeElement.currentTime??0);
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
      this.paintFrame(video.currentTime);
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
    this.setCanvasSizes();
    this.paintFrame(0);
  }

  ngOnDestroy():void{
    this.disposed=true;
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
    this.setCanvasSizes();
    this.paintFrame(video.currentTime);
  }

  onDecodedFrame():void{
    const video=this.masterVideo?.nativeElement;
    if(!video)return;
    if(this.previewLoading() && video.readyState>=2) {
      this.previewLoading.set(false);this.previewStateChanged.emit('ready');
    }
    this.paintFrame(video.currentTime);
  }

  onPlay():void{
    if(this.playbackLocked()){this.masterVideo?.nativeElement.pause();return;}
    this.playing.set(true);
    this.startFrameLoop();
  }

  onPause():void{
    this.playing.set(false);
    this.stopFrameLoop();
    const video=this.masterVideo?.nativeElement;
    if(video)this.paintFrame(video.currentTime);
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
    const next=seekByFrame(video.currentTime,step,this.proxyFps(),duration);
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
        if(this.disposed || video.paused)return;
        this.paintFrame(metadata.mediaTime);
        this.frameCallbackId=video.requestVideoFrameCallback(frame);
      };
      this.frameCallbackId=video.requestVideoFrameCallback(frame);
    }else{
      const frame=()=>{
        this.fallbackRafId=null;
        if(this.disposed || video.paused)return;
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
    const width=Math.min(1280,videoWidth);
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

  idealAtFrame():number|null {
    const revision=this.pivotRevision(),frame=this.currentKinematics();
    if(!revision || !frame || !frame.isValid)return null;
    const max=revision.analysis.theoreticalMaxAngularSpeedRadPerSec;
    return max===null?null:idealAngularSpeedAtAngle(
      frame.angleDegrees,this.initialAngleDegrees(),max);
  }

  private drawArrow(ctx:CanvasRenderingContext2D,
    x:number,y:number,dx:number,dy:number,color:string,scale:number):void{
    const size=Math.hypot(dx,dy);
    if(!Number.isFinite(size)||size<.01)return;
    const length=Math.min(96,Math.max(0,size*scale));
    if(length<2)return;
    const angle=Math.atan2(dy,dx);
    const endX=x+Math.cos(angle)*length,endY=y+Math.sin(angle)*length;
    ctx.strokeStyle=color;ctx.fillStyle=color;ctx.lineWidth=3;
    ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(endX,endY);ctx.stroke();
    const head=9;
    ctx.beginPath();ctx.moveTo(endX,endY);
    ctx.lineTo(endX-head*Math.cos(angle-.46),endY-head*Math.sin(angle-.46));
    ctx.lineTo(endX-head*Math.cos(angle+.46),endY-head*Math.sin(angle+.46));
    ctx.closePath();ctx.fill();
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
    ctx.fillStyle='rgba(10,244,183,.26)';
    ctx.strokeStyle='#43f1ce';
    ctx.lineWidth=2;
    ctx.fill(path);
    ctx.stroke(path);
    ctx.restore();
  }

  /** Draw candidate geometric measurements; never paint metric XY. */
  private paintAngular(ctx:CanvasRenderingContext2D,canvas:HTMLCanvasElement,
      frame:KinematicFrame|null,revision:KinematicRevision,showArrows:boolean):void {
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
          pivotYpx:revision.analysis.pivotYpx});
        if(v){
          this.drawArrow(ctx,bx,by,v.vx*fx,v.vy*fy,'#3cf2da',.22);
          this.drawArrow(ctx,bx,by,v.atx*fx,v.aty*fy,'#ffb34d',.035);
          this.drawArrow(ctx,bx,by,v.arx*fx,v.ary*fy,'#fb83ff',.035);
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
    try{
      for(let i=0;i<ctxs.length;i++){
        const ctx=ctxs[i]!,canvas=canvases[i]!;
        ctx.clearRect(0,0,canvas.width,canvas.height);
        if(frameReady){
          ctx.drawImage(video,0,0,canvas.width,canvas.height);
        }else{
          ctx.fillStyle='#0c1422';
          ctx.fillRect(0,0,canvas.width,canvas.height);
        }
      }
    }catch{
      this.error.set(this.es
        ? 'No es posible dibujar el video en los lienzos. Verifica la política CORS del servidor.'
        : 'Cannot draw the source video into overlays. Check server CORS.');
      return;
    }

    if(this.playbackLocked()){
      this.currentKinematics.set(null);
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
    const frame=revision ?
      (sam?samAngularFrameAt(sam,{
        pivotXpx:revision.analysis.pivotXpx,
        pivotYpx:revision.analysis.pivotYpx
      },time):sampleAtTime(revision.analysis.samples,time)) : null;
    this.currentKinematics.set(frame);
    const mask=sam?nearestSamMask(sam,time):null;
    if(sam && mask) {
      try{
        this.paintSamMask(ctxs[0]!,canvases[0]!,sam,mask);
        this.paintSamMask(ctxs[2]!,canvases[2]!,sam,mask);
      }catch{
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
      ctx.fillText('v '+detected.speedPixelsPerSecond.toFixed(1)+' px/s',
        14,Math.max(23,canvas.height-16));
      }
      }
      const trail=trailingMotion(samples,time);
      const ghostCanvas=canvases[1]!;
      const fx=ghostCanvas.width/480,fy=ghostCanvas.height/270;
      for(const ghostCtx of [ctxs[1]!,ctxs[2]!]){
      for(let i=0;i<trail.length;i++){
        const item=trail[i];
        const ratio=(i+1)/trail.length;
        ghostCtx.beginPath();
        ghostCtx.fillStyle='rgba(217,151,255,'+(.12+.56*ratio).toFixed(2)+')';
        ghostCtx.arc(item.x*fx,item.y*fy,Math.max(3,ghostCanvas.width/100),
          0,2*Math.PI);
        ghostCtx.fill();
      }
      ghostCtx.beginPath();
      ghostCtx.strokeStyle='#e5b1ff';ghostCtx.lineWidth=2;
      trail.forEach((item,i)=>{
        if(i===0)ghostCtx.moveTo(item.x*fx,item.y*fy);
        else ghostCtx.lineTo(item.x*fx,item.y*fy);
      });
      if(trail.length>1)ghostCtx.stroke();
      }
    }else{
      this.overlayStatus.set(sam
        ? 'SAM_NO_RELIABLE_MASK_DERIVATIVE_AT_THIS_PROXY_FRAME'
        : (!legacy ? this.samStatus() :
          revision?'NO_RELIABLE_LOCAL_TRACK_FOR_THIS_FRAME'
          : samples.length>0?'NO_RELIABLE_LOCAL_SAMPLE_AT_THIS_FRAME'
          :'NO_2D_TRACK_FOR_THIS_VIDEO'));
    }
    if(revision){
      this.paintAngular(ctxs[0]!,canvases[0]!,frame,revision,true);
      this.paintAngular(ctxs[1]!,canvases[1]!,frame,revision,false);
      this.paintAngular(ctxs[2]!,canvases[2]!,frame,revision,true);
    }
    const duration=this.duration();
    this.currentTime.set(Math.max(0,Math.min(duration||time,time)));
    this.lastPaintedTime.set(time);
    this.frameNumber.set(Math.round(time*this.proxyFps()));
    this.paintedViews.set(4); // one master video + three derived canvases
    this.didPaint=true;
  }
}