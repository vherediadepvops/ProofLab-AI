export type VideoExportErrorCode = 'EXPORT_UNSUPPORTED'|'EXPORT_NOT_READY'|'EXPORT_HIDDEN'|
  'EXPORT_CANCELLED'|'EXPORT_SOURCE_CHANGED'|'EXPORT_STALLED'|'EXPORT_INTERRUPTED'|'EXPORT_FAILED';

export class VideoExportError extends Error {
  constructor(readonly code:VideoExportErrorCode) {super(code);this.name='VideoExportError';}
}

export interface AnnotatedVideoExport {
  blob:Blob;
  extension:'mp4'|'webm';
  mimeType:string;
}

/** Small browser seam; real rendering and media decoding remain owned by the player. */
export interface VideoExportRuntime {
  isTypeSupported:(mime:string)=>boolean;
  createRecorder:(stream:MediaStream,options:MediaRecorderOptions)=>MediaRecorder;
  visibility:Pick<Document,'hidden'|'addEventListener'|'removeEventListener'>;
  now:()=>number;
  setTimer:(callback:()=>void,ms:number)=>ReturnType<typeof setTimeout>;
  clearTimer:(timer:ReturnType<typeof setTimeout>)=>void;
}

export interface VideoExportOptions {
  video:HTMLVideoElement;
  canvas:HTMLCanvasElement;
  renderFrame:(mediaTime:number)=>void;
  onProgress?:(fraction:number)=>void;
  signal?:AbortSignal;
  runtime?:VideoExportRuntime;
}

const mimeCandidates=['video/mp4','video/mp4;codecs=avc1.42E01E',
  'video/webm;codecs=vp9','video/webm;codecs=vp8','video/webm'];

export function selectVideoExportMime(supported:(type:string)=>boolean):string|null {
  return mimeCandidates.find(type=>{try{return supported(type);}catch{return false;}})??null;
}

export function videoExportSupported():boolean {
  return typeof MediaRecorder!=='undefined' && typeof HTMLCanvasElement!=='undefined' &&
    typeof HTMLCanvasElement.prototype.captureStream==='function' &&
    typeof HTMLVideoElement!=='undefined' &&
    typeof HTMLVideoElement.prototype.requestVideoFrameCallback==='function' &&
    !!selectVideoExportMime(type=>MediaRecorder.isTypeSupported(type));
}

function browserRuntime():VideoExportRuntime {
  return {
    isTypeSupported:type=>MediaRecorder.isTypeSupported(type),
    createRecorder:(stream,options)=>new MediaRecorder(stream,options),
    visibility:document,now:()=>performance.now(),
    setTimer:(fn,ms)=>setTimeout(fn,ms),clearTimer:timer=>clearTimeout(timer)
  };
}

/**
 * Silent, real-time visual copy of the complete source, at 1× and native canvas size.
 * Only decoded frame timestamps select overlays. Nothing is sent to a provider.
 * The caller must suspend its own frame loop and editing for the duration.
 */
export async function exportAnnotatedVideo(options:VideoExportOptions):Promise<AnnotatedVideoExport> {
  if(!options.runtime && !videoExportSupported())throw new VideoExportError('EXPORT_UNSUPPORTED');
  const runtime=options.runtime??browserRuntime(),{video,canvas,signal}=options;
  const mime=selectVideoExportMime(runtime.isTypeSupported);
  if(!mime || typeof video.requestVideoFrameCallback!=='function' || typeof canvas.captureStream!=='function')
    throw new VideoExportError('EXPORT_UNSUPPORTED');
  if(signal?.aborted)throw new VideoExportError('EXPORT_CANCELLED');
  if(runtime.visibility.hidden)throw new VideoExportError('EXPORT_HIDDEN');
  if(!Number.isFinite(video.duration)||video.duration<=0||video.readyState<2||
      video.videoWidth<=0||video.videoHeight<=0||canvas.width<=0||canvas.height<=0)
    throw new VideoExportError('EXPORT_NOT_READY');

  const source=video.currentSrc||video.src;
  const saved={time:video.currentTime,rate:video.playbackRate,paused:video.paused,muted:video.muted};
  let recorder:MediaRecorder|null=null,stream:MediaStream|null=null;
  let frameId:number|null=null,active=true,recording=false,finishing=false;
  let lastFrameAt=runtime.now(),lastMediaTime=-1,frames=0;
  const timers=new Set<ReturnType<typeof setTimeout>>();
  const cleanups:(()=>void)[]=[];
  const chunks:Blob[]=[];
  let rejectFatal:(reason:unknown)=>void=()=>{};
  const fatal=new Promise<never>((_,reject)=>{rejectFatal=reject;});
  void fatal.catch(()=>{});
  const fail=(code:VideoExportErrorCode)=>{if(active)rejectFatal(new VideoExportError(code));};
  const sameSource=()=>source===(video.currentSrc||video.src);
  const timer=(fn:()=>void,ms:number)=>{
    const id=runtime.setTimer(()=>{timers.delete(id);fn();},ms);timers.add(id);return id;
  };
  const untimer=(id:ReturnType<typeof setTimeout>)=>{runtime.clearTimer(id);timers.delete(id);};
  const listen=(target:EventTarget,event:string,listener:EventListener)=>{
    target.addEventListener(event,listener);cleanups.push(()=>target.removeEventListener(event,listener));
  };
  const abort=()=>fail('EXPORT_CANCELLED');
  signal?.addEventListener('abort',abort,{once:true});
  cleanups.push(()=>signal?.removeEventListener('abort',abort));
  const visibility=()=>{if(runtime.visibility.hidden)fail('EXPORT_HIDDEN');};
  runtime.visibility.addEventListener('visibilitychange',visibility);
  cleanups.push(()=>runtime.visibility.removeEventListener('visibilitychange',visibility));
  listen(video,'error',()=>fail('EXPORT_FAILED'));
  listen(video,'emptied',()=>fail('EXPORT_SOURCE_CHANGED'));

  // Every wait is bounded and competes with cancellation/error, including seek and recorder flush.
  const bounded=<T>(work:Promise<T>,ms=15000):Promise<T>=>{
    let timeout:ReturnType<typeof setTimeout>;
    const expired=new Promise<never>((_,reject)=>{timeout=timer(()=>reject(new VideoExportError('EXPORT_STALLED')),ms);});
    return Promise.race([work,fatal,expired]).finally(()=>untimer(timeout!));
  };
  const cancelFrame=()=>{if(frameId!==null)video.cancelVideoFrameCallback(frameId);frameId=null;};
  const seekDecoded=(time:number):Promise<number>=>{
    cancelFrame();
    return new Promise((resolve,reject)=>{
      let decoded:number|null=null,completed=false;
      const complete=()=>{
        if(completed||decoded===null||video.seeking||video.readyState<2)return;
        completed=true;
        video.removeEventListener('seeked',complete);video.removeEventListener('loadeddata',complete);
        try{options.renderFrame(decoded);resolve(decoded);}catch{reject(new VideoExportError('EXPORT_FAILED'));}
      };
      video.addEventListener('seeked',complete);video.addEventListener('loadeddata',complete);
      cleanups.push(()=>{
        video.removeEventListener('seeked',complete);video.removeEventListener('loadeddata',complete);
      });
      const inspect=(_:DOMHighResTimeStamp,metadata:VideoFrameCallbackMetadata)=>{
        frameId=null;
        if(!sameSource()){reject(new VideoExportError('EXPORT_SOURCE_CHANGED'));return;}
        if(Math.abs(metadata.mediaTime-time)>(time===0?.04:.25)){
          frameId=video.requestVideoFrameCallback(inspect);return;
        }
        // A cached seek can report its decoded timestamp before readyState/seeked update.
        // Keep that exact timestamp; a paused video need not deliver another callback.
        decoded=metadata.mediaTime;complete();
      };
      frameId=video.requestVideoFrameCallback(inspect);
      try{video.currentTime=time;}catch{cancelFrame();reject(new VideoExportError('EXPORT_FAILED'));}
    });
  };

  try {
    video.pause();video.playbackRate=1;video.muted=true;
    // An identical seek need not present a new decoded frame. Force a distinct position first.
    if(video.currentTime<.001)await bounded(seekDecoded(Math.min(.1,video.duration/2)));
    const firstTime=await bounded(seekDecoded(0));
    if(firstTime>.04)throw new VideoExportError('EXPORT_NOT_READY');
    // Detect tainted canvases before recording; captureStream alone may otherwise stay silent.
    canvas.getContext('2d')?.getImageData(0,0,1,1);
    stream=canvas.captureStream(0);
    const track=stream.getVideoTracks()[0] as CanvasCaptureMediaStreamTrack|undefined;
    if(!track||typeof track.requestFrame!=='function')throw new VideoExportError('EXPORT_UNSUPPORTED');
    recorder=runtime.createRecorder(stream,{mimeType:mime,videoBitsPerSecond:
      Math.min(24000000,Math.max(4000000,canvas.width*canvas.height*6))});
    const actualMime=recorder.mimeType||mime;
    const extension=actualMime.startsWith('video/mp4')?'mp4':actualMime.startsWith('video/webm')?'webm':null;
    if(!extension)throw new VideoExportError('EXPORT_UNSUPPORTED');
    listen(recorder,'dataavailable',event=>{const data=(event as BlobEvent).data;if(data?.size)chunks.push(data);});
    listen(recorder,'error',()=>fail('EXPORT_FAILED'));
    let resolveStopped:()=>void=()=>{};
    const stopped=new Promise<void>(resolve=>{resolveStopped=resolve;});
    listen(recorder,'stop',()=>{if(!finishing)fail('EXPORT_INTERRUPTED');resolveStopped();});
    let resolveEnded:()=>void=()=>{};
    const ended=new Promise<void>(resolve=>{resolveEnded=resolve;});
    listen(video,'ended',()=>{
      if(video.currentTime<video.duration-.25 || lastMediaTime<video.duration-.5 || frames<2){
        fail('EXPORT_INTERRUPTED');return;
      }
      resolveEnded();
    });
    listen(video,'pause',()=>{if(recording&&!video.ended)fail('EXPORT_INTERRUPTED');});
    listen(video,'seeking',()=>{if(recording)fail('EXPORT_INTERRUPTED');});
    // Remove buffering wall time from the copy, instead of stretching a frozen image.
    listen(video,'waiting',()=>{if(recording&&recorder?.state==='recording')recorder.pause();});
    listen(video,'playing',()=>{if(recording&&recorder?.state==='paused')recorder.resume();});

    const capture=(_:DOMHighResTimeStamp,metadata:VideoFrameCallbackMetadata)=>{
      frameId=null;
      if(!active||!recording)return;
      if(!sameSource()){fail('EXPORT_SOURCE_CHANGED');return;}
      if(video.videoWidth!==canvas.width||video.videoHeight!==canvas.height){fail('EXPORT_INTERRUPTED');return;}
      try {
        options.renderFrame(metadata.mediaTime);
        track.requestFrame();
        frames++;lastMediaTime=metadata.mediaTime;lastFrameAt=runtime.now();
        options.onProgress?.(Math.min(.99,Math.max(0,metadata.mediaTime/video.duration)));
      }catch{fail('EXPORT_FAILED');return;}
      frameId=video.requestVideoFrameCallback(capture);
    };
    const watch=()=>{
      if(!active||!recording)return;
      if(!sameSource()){fail('EXPORT_SOURCE_CHANGED');return;}
      if(runtime.now()-lastFrameAt>15000){fail('EXPORT_STALLED');return;}
      timer(watch,1000);
    };
    recorder.start(1000);recording=true;
    track.requestFrame();frames=1;lastMediaTime=firstTime;lastFrameAt=runtime.now();
    options.onProgress?.(0);
    frameId=video.requestVideoFrameCallback(capture);
    timer(watch,1000);
    await bounded(video.play());
    await bounded(ended,Math.max(45000,video.duration*2000+30000));
    recording=false;cancelFrame();
    // Let the final fully painted frame enter the recorder before requesting its final chunk.
    await bounded(new Promise<void>(resolve=>timer(resolve,100)),1000);
    finishing=true;
    if(recorder.state==='inactive')throw new VideoExportError('EXPORT_INTERRUPTED');
    recorder.stop();
    await bounded(stopped);
    const blob=new Blob(chunks,{type:actualMime});
    if(!blob.size)throw new VideoExportError('EXPORT_FAILED');
    options.onProgress?.(1);
    return {blob,extension,mimeType:actualMime};
  }catch(error){
    if(error instanceof VideoExportError)throw error;
    throw new VideoExportError('EXPORT_FAILED');
  }finally{
    active=false;recording=false;finishing=true;cancelFrame();
    cleanups.forEach(cleanup=>cleanup());
    for(const id of timers)runtime.clearTimer(id);
    timers.clear();
    if(recorder&&recorder.state!=='inactive')try{recorder.stop();}catch{/* Already stopped. */}
    stream?.getTracks().forEach(track=>track.stop());
    const destroyed=signal?.reason==='destroyed';
    const obsolete=signal?.reason==='source-changed'||destroyed;
    // Mute/rate belong to the reused element; replacing its URL must not retain export settings.
    if(!destroyed){video.playbackRate=saved.rate;video.muted=saved.muted;}
    if(sameSource()&&!obsolete){
      video.pause();
      // Restoring is best effort and must never leave cancellation waiting for decoding.
      try{video.currentTime=Math.min(saved.time,video.duration);}catch{/* Source became unavailable. */}
      if(!saved.paused&&!runtime.visibility.hidden)void video.play().catch(()=>{});
    }
  }
}
