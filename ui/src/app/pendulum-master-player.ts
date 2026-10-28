import {
  AfterViewInit, Component, ElementRef, OnDestroy, ViewChild,
  input, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  type CandidatePixelPoint, formatMediaTime, motionAt,
  seekByFrame, trailingMotion
} from './pendulum-master-sync';

/**
 * ONE HTMLVideoElement is the only media clock and decoder.
 * Views 2/3 drawImage(the SAME decoded video frame) into canvases
 * on requestVideoFrameCallback, including pause and seeked.
 * No separate video players, model calls, or assumed SAM masks.
 */
@Component({
  selector: 'app-pendulum-master-player',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './pendulum-master-player.html',
  styleUrl: './pendulum-master-player.scss'
})
export class PendulumMasterPlayer implements AfterViewInit, OnDestroy {
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

  toggleZoom():void { this.zoomed.update(value=>!value); }

  ngAfterViewInit():void{
    // Angular may create the video after a study response. This component
    // mounts only when a server-side preview is available.
    this.setCanvasSizes();
    this.paintFrame(0);
  }

  ngOnDestroy():void{
    this.disposed=true;
    this.stopFrameLoop();
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
    this.paintFrame(video.currentTime);
  }

  onPlay():void{
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

    const samples=this.points()??[];
    const detected=motionAt(samples,time);
    if(detected){
      this.overlayStatus.set('LOCAL_PIXEL_CANDIDATE_NOT_SAM');
      const canvas=canvases[0]!;
      const factorX=canvas.width/480, factorY=canvas.height/270;
      const x=detected.x*factorX,y=detected.y*factorY;
      const vx=detected.vxPixelsPerSecond*factorX;
      const vy=detected.vyPixelsPerSecond*factorY;
      const magnitude=Math.hypot(vx,vy);
      const length=magnitude<3?0:Math.min(94,magnitude*.12);
      const angle=Math.atan2(vy,vx);
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
      this.overlayStatus.set(samples.length>0
        ? 'NO_RELIABLE_LOCAL_SAMPLE_AT_THIS_FRAME'
        : 'NO_2D_TRACK_FOR_THIS_VIDEO');
    }
    const duration=this.duration();
    this.currentTime.set(Math.max(0,Math.min(duration||time,time)));
    this.lastPaintedTime.set(time);
    this.frameNumber.set(Math.round(time*this.proxyFps()));
    this.paintedViews.set(4); // one master video + three derived canvases
    this.didPaint=true;
  }
}
