import {exportAnnotatedVideo,selectVideoExportMime,VideoExportError,type VideoExportRuntime} from './pendulum-video-export';

class TestVideo extends EventTarget {
  duration=1;readyState=4;videoWidth=100;videoHeight=200;
  currentSrc='https://example.test/original.mov';src=this.currentSrc;
  paused=true;muted=false;playbackRate=2;seeking=false;ended=false;playCalls=0;
  frameBeforeReady=false;
  private time=.4;private next=0;
  readonly callbacks=new Map<number,VideoFrameRequestCallback>();
  get currentTime(){return this.time;}
  set currentTime(value:number){
    this.time=value;this.seeking=true;this.ended=false;this.dispatchEvent(new Event('seeking'));
    queueMicrotask(()=>{
      if(this.frameBeforeReady)this.readyState=1;
      this.frame(value);this.seeking=false;
      if(this.frameBeforeReady){
        this.dispatchEvent(new Event('seeked'));
        queueMicrotask(()=>{this.readyState=4;this.dispatchEvent(new Event('loadeddata'));});
      }else this.dispatchEvent(new Event('seeked'));
    });
  }
  requestVideoFrameCallback(callback:VideoFrameRequestCallback):number{
    const id=++this.next;this.callbacks.set(id,callback);return id;
  }
  cancelVideoFrameCallback(id:number){this.callbacks.delete(id);}
  play():Promise<void>{
    this.playCalls++;this.paused=false;
    queueMicrotask(()=>this.dispatchEvent(new Event('playing')));return Promise.resolve();
  }
  pause(){
    if(!this.paused){this.paused=true;queueMicrotask(()=>this.dispatchEvent(new Event('pause')));}
  }
  frame(time:number){
    this.time=time;
    const callbacks=[...this.callbacks.values()];this.callbacks.clear();
    callbacks.forEach(callback=>callback(0,{mediaTime:time} as VideoFrameCallbackMetadata));
  }
  finish(){this.frame(.98);this.time=1;this.ended=true;this.paused=true;this.dispatchEvent(new Event('ended'));}
}
class TestRecorder extends EventTarget {
  mimeType='video/mp4';state:RecordingState='inactive';includeData=true;
  start(){this.state='recording';}
  pause(){this.state='paused';}
  resume(){this.state='recording';}
  stop(){
    this.state='inactive';queueMicrotask(()=>{
      if(this.includeData){const event=new Event('dataavailable');Object.assign(event,{data:new Blob(['video'])});this.dispatchEvent(event);}
      this.dispatchEvent(new Event('stop'));
    });
  }
}
class TestVisibility extends EventTarget {hidden=false;}
function fixture(){
  const video=new TestVideo(),recorder=new TestRecorder(),visibility=new TestVisibility();
  let now=0,next=0,requested=0,stopped=false;
  const timers=new Map<number,{at:number;fn:()=>void}>();
  const track={requestFrame:()=>requested++,stop:()=>{stopped=true;}};
  const stream={getVideoTracks:()=>[track],getTracks:()=>[track]} as unknown as MediaStream;
  const canvas={width:100,height:200,captureStream:()=>stream,
    getContext:()=>({getImageData:()=>({})})} as unknown as HTMLCanvasElement;
  const runtime:VideoExportRuntime={
    isTypeSupported:type=>type==='video/mp4',
    createRecorder:()=>recorder as unknown as MediaRecorder,
    visibility:visibility as unknown as Document,now:()=>now,
    setTimer:(fn,ms)=>{const id=++next;timers.set(id,{at:now+ms,fn});return id;},
    clearTimer:id=>{timers.delete(id as number);}
  };
  const rendered:number[]=[],progress:number[]=[],abort=new AbortController();
  const run=()=>exportAnnotatedVideo({video:video as unknown as HTMLVideoElement,canvas,runtime,
    signal:abort.signal,renderFrame:time=>rendered.push(time),onProgress:part=>progress.push(part)});
  const advance=(ms:number)=>{
    const end=now+ms;
    for(;;){const nextTimer=[...timers.entries()].filter(([,value])=>value.at<=end).sort((a,b)=>a[1].at-b[1].at)[0];
      if(!nextTimer)break;const [id,value]=nextTimer;timers.delete(id);now=value.at;value.fn();}
    now=end;
  };
  return {video,canvas,runtime,recorder,visibility,rendered,progress,abort,run,advance,timers,
    requested:()=>requested,stopped:()=>stopped};
}
async function settle(){for(let i=0;i<18;i++)await Promise.resolve();}
async function finish(f:ReturnType<typeof fixture>){f.video.frame(.5);f.video.finish();await settle();f.advance(100);await settle();}

describe('complete annotated video export',()=>{
  it('prefers supported MP4 and falls back to an actual WebM type',()=>{
    expect(selectVideoExportMime(()=>true)).toBe('video/mp4');
    expect(selectVideoExportMime(type=>type==='video/webm;codecs=vp8')).toBe('video/webm;codecs=vp8');
    expect(selectVideoExportMime(()=>false)).toBeNull();
  });
  it('records the complete clip at 1×, captures after rendering and restores player state',async()=>{
    const f=fixture(),result=f.run();await settle();
    expect(f.video.currentTime).toBe(0);expect(f.video.playbackRate).toBe(1);expect(f.video.muted).toBeTrue();
    expect(f.rendered[0]).toBe(0);expect(f.requested()).toBe(1);
    await finish(f);const exported=await result;
    expect(exported.extension).toBe('mp4');expect(exported.blob.size).toBeGreaterThan(0);
    expect(f.rendered).toEqual([0,.5,.98]);expect(f.progress.at(-1)).toBe(1);
    expect(f.video.currentTime).toBe(.4);expect(f.video.playbackRate).toBe(2);expect(f.video.muted).toBeFalse();
    expect(f.video.paused).toBeTrue();expect(f.stopped()).toBeTrue();expect(f.timers.size).toBe(0);
  });
  it('does not let a late first seek callback start recording at a stale timestamp',async()=>{
    const f=fixture();f.video.currentTime=0;await settle();
    const result=f.run();await settle();
    expect(f.rendered.slice(0,2)).toEqual([.1,0]);
    await finish(f);await result;
  });
  it('retains an exact decoded timestamp delivered before seeked and loadeddata',async()=>{
    const f=fixture();f.video.frameBeforeReady=true;
    const result=f.run();await settle();
    // There is only one frame callback for the seek, while readyState was still HAVE_METADATA.
    // The later loadeddata must complete preparation using that timestamp, without another frame.
    expect(f.recorder.state).toBe('recording');expect(f.rendered).toEqual([0]);
    await finish(f);expect((await result).blob.size).toBeGreaterThan(0);
  });
  it('uses the actual recorder MIME for the downloaded extension',async()=>{
    const f=fixture();f.recorder.mimeType='video/webm;codecs=vp8';const result=f.run();await settle();
    await finish(f);expect((await result).extension).toBe('webm');
  });
  it('cancels without returning a partial download and closes every capture resource',async()=>{
    const f=fixture(),result=f.run();await settle();f.abort.abort();
    await expectAsync(result).toBeRejectedWithError(VideoExportError,'EXPORT_CANCELLED');
    expect(f.stopped()).toBeTrue();expect(f.video.currentTime).toBe(.4);expect(f.video.callbacks.size).toBe(0);expect(f.timers.size).toBe(0);
  });
  it('restores element mute/rate without seeking or resuming a replaced source',async()=>{
    const f=fixture();f.video.paused=false;const result=f.run();await settle();
    expect(f.video.muted).toBeTrue();expect(f.video.playbackRate).toBe(1);
    f.video.currentSrc='https://example.test/replacement.mov';f.abort.abort('source-changed');
    await expectAsync(result).toBeRejectedWithError(VideoExportError,'EXPORT_CANCELLED');
    expect(f.video.currentTime).toBe(0);expect(f.video.playCalls).toBe(1);expect(f.stopped()).toBeTrue();
    expect(f.video.muted).toBeFalse();expect(f.video.playbackRate).toBe(2);
  });
  it('restores the same source normally when evidence or language changes cancel export',async()=>{
    const f=fixture();f.video.paused=false;const result=f.run();await settle();f.abort.abort();
    await expectAsync(result).toBeRejectedWithError(VideoExportError,'EXPORT_CANCELLED');
    expect(f.video.currentTime).toBe(.4);expect(f.video.playCalls).toBe(2);
    expect(f.video.muted).toBeFalse();expect(f.video.playbackRate).toBe(2);
  });
  it('rejects a hidden page instead of exporting a frozen background recording',async()=>{
    const f=fixture(),result=f.run();await settle();f.visibility.hidden=true;f.visibility.dispatchEvent(new Event('visibilitychange'));
    await expectAsync(result).toBeRejectedWithError(VideoExportError,'EXPORT_HIDDEN');expect(f.stopped()).toBeTrue();
  });
  it('pauses recording during buffering and resumes when playback resumes',async()=>{
    const f=fixture(),result=f.run();await settle();f.video.dispatchEvent(new Event('waiting'));
    expect(f.recorder.state).toBe('paused');f.video.dispatchEvent(new Event('playing'));expect(f.recorder.state).toBe('recording');
    await finish(f);await result;
  });
  it('rejects early ended, manual seeking and an externally stopped recorder',async()=>{
    for(const interrupt of [(f:ReturnType<typeof fixture>)=>f.video.dispatchEvent(new Event('ended')),
      (f:ReturnType<typeof fixture>)=>f.video.dispatchEvent(new Event('seeking')),
      (f:ReturnType<typeof fixture>)=>f.recorder.stop()]){
      const f=fixture(),result=f.run();await settle();interrupt(f);
      await expectAsync(result).toBeRejectedWithError(VideoExportError,'EXPORT_INTERRUPTED');expect(f.stopped()).toBeTrue();
    }
  });
  it('times out a stalled source and never reports success for empty output',async()=>{
    const stalled=fixture(),pending=stalled.run();await settle();stalled.advance(16000);
    await expectAsync(pending).toBeRejectedWithError(VideoExportError,'EXPORT_STALLED');expect(stalled.timers.size).toBe(0);
    const empty=fixture();empty.recorder.includeData=false;const result=empty.run();await settle();await finish(empty);
    await expectAsync(result).toBeRejectedWithError(VideoExportError,'EXPORT_FAILED');
  });
  it('rejects unsupported capture and unavailable media before mutating playback',async()=>{
    const f=fixture();f.runtime.isTypeSupported=()=>false;
    await expectAsync(f.run()).toBeRejectedWithError(VideoExportError,'EXPORT_UNSUPPORTED');
    expect(f.video.currentTime).toBe(.4);expect(f.video.playCalls).toBe(0);
    const unavailable=fixture();unavailable.video.readyState=1;
    await expectAsync(unavailable.run()).toBeRejectedWithError(VideoExportError,'EXPORT_NOT_READY');
  });
});
