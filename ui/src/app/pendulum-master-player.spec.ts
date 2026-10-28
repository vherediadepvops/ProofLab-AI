import { TestBed } from '@angular/core/testing';
import { PendulumMasterPlayer } from './pendulum-master-player';
import {
  formatMediaTime, motionAt, nearestMotionTime, seekByFrame,
  trailingMotion, type CandidatePixelPoint
} from './pendulum-master-sync';

const track:CandidatePixelPoint[]=Array.from({length:60},(_,i)=>({
  timeSeconds:i/12,
  x:210+70*Math.sin(2*Math.PI*(i/12)/1.42),
  y:190+7*Math.cos(2*Math.PI*(i/12)/1.42),
  approximateRadiusPixels:8
}));

describe('One-clock three-view pendulum laboratory',()=>{
  beforeEach(async()=>{
    await TestBed.configureTestingModule({imports:[PendulumMasterPlayer]})
      .compileComponents();
  });

  function init(){
    const fixture=TestBed.createComponent(PendulumMasterPlayer);
    fixture.componentRef.setInput('videoUrl','http://127.0.0.1:5391/test.mp4');
    fixture.componentRef.setInput('name','iphone.MOV');
    fixture.componentRef.setInput('points',track);
    fixture.detectChanges();
    const component=fixture.componentInstance;
    const source=fixture.nativeElement.querySelector('video') as HTMLVideoElement;
    Object.defineProperty(source,'videoWidth',{configurable:true,value:640});
    Object.defineProperty(source,'videoHeight',{configurable:true,value:360});
    Object.defineProperty(source,'duration',{configurable:true,value:30});
    Object.defineProperty(source,'readyState',{configurable:true,
      value:HTMLMediaElement.HAVE_CURRENT_DATA});
    component.onLoadedMetadata();
    fixture.detectChanges();
    return {fixture,component,source,root:fixture.nativeElement as HTMLElement};
  }

  it('never starts three separate media decoders',()=>{
    const {fixture,root,component}=init();
    expect(root.querySelectorAll('video').length).toBe(1);
    expect(root.querySelectorAll('canvas').length).toBe(3);
    expect(root.querySelectorAll('.view-panel').length).toBe(4);
    expect(root.textContent).toContain('Cuatro perspectivas, un solo reloj');
    expect(root.textContent).toContain('H.264');
    expect(root.textContent).toContain('NO de SAM');
    expect(component.proxyFps()).toBe(12);
    fixture.destroy();
  });

  it('draws the SAME decoded master frame to BOTH canvases at the same instant',()=>{
    const {fixture,component,source,root}=init();
    const canvases=Array.from(root.querySelectorAll('canvas')) as HTMLCanvasElement[];
    const contexts=canvases.map(canvas=>canvas.getContext('2d')!);
    const spies=contexts.map(ctx=>spyOn(ctx,'drawImage').and.stub());
    component.paintFrame(1.25);
    expect(spies[0]).toHaveBeenCalledOnceWith(source,0,0,640,360);
    expect(spies[1]).toHaveBeenCalledOnceWith(source,0,0,640,360);
    expect(spies[2]).toHaveBeenCalledOnceWith(source,0,0,640,360);
    expect(component.frameNumber()).toBe(15);
    expect(component.lastPaintedTime()).toBe(1.25);
    expect(component.paintedViews()).toBe(4);
    expect(component.overlayStatus()).toBe('LOCAL_PIXEL_CANDIDATE_NOT_SAM');
    fixture.destroy();
  });

  it('starts with combined primary and interchanges cards without recreating or seeking the video',()=>{
    const {fixture,component,source,root}=init();
    const card=(id:string)=>root.querySelector('[data-view="'+id+'"]') as HTMLElement;
    expect(component.activeView()).toBe('combined');
    expect(card('combined').style.gridArea).toBe('main');
    expect(card('original').style.gridArea).toBe('side1');
    expect(card('vectors').style.gridArea).toBe('side2');
    expect(card('ghost').style.gridArea).toBe('side3');
    expect(root.querySelectorAll('.view-panel:not(.is-primary)').length).toBe(3);
    source.currentTime=5.5;
    source.playbackRate=.5;
    const before=root.querySelector('video');
    (card('original').querySelector('.focus-action') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(component.activeView()).toBe('original');
    expect(card('original').style.gridArea).toBe('main');
    expect(card('combined').style.gridArea).toBe('side1');
    expect(card('vectors').style.gridArea).toBe('side2');
    expect(card('ghost').style.gridArea).toBe('side3');
    expect(root.querySelector('video')).toBe(before);
    expect(source.currentTime).toBe(5.5);
    expect(source.playbackRate).toBe(.5);
    (card('vectors').querySelector('.view-surface') as HTMLElement).click();
    fixture.detectChanges();
    expect(card('vectors').style.gridArea).toBe('main');
    expect(card('original').style.gridArea).toBe('side2');
    expect(card('combined').style.gridArea).toBe('side1');
    expect(card('ghost').style.gridArea).toBe('side3');
    expect(root.querySelector('video')).toBe(before);
    expect(source.currentTime).toBe(5.5);
    (card('ghost').querySelector('.focus-action') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(card('ghost').style.gridArea).toBe('main');
    expect(card('vectors').style.gridArea).toBe('side3');
    expect(card('combined').style.gridArea).toBe('side1');
    expect(card('original').style.gridArea).toBe('side2');
    expect(root.querySelectorAll('video').length).toBe(1);
    expect(root.querySelectorAll('canvas').length).toBe(3);
    fixture.destroy();
  });

  it('zooms all views uniformly without changing source frame or playback position',()=>{
    const {fixture,component,source,root}=init();
    source.currentTime=4.2;
    const button=root.querySelector('button.zoom-action') as HTMLButtonElement;
    button.click();fixture.detectChanges();
    expect(component.zoomed()).toBeTrue();
    expect(root.querySelector('.views-grid')?.classList.contains('is-zoomed')).toBeTrue();
    expect(source.currentTime).toBe(4.2);
    button.click();fixture.detectChanges();
    expect(component.zoomed()).toBeFalse();
    expect(source.currentTime).toBe(4.2);
    fixture.destroy();
  });

  it('renders vector and trail overlays into the combined canvas from one shared frame',()=>{
    const {fixture,component,source,root}=init();
    const combined=root.querySelector('canvas') as HTMLCanvasElement;
    const main=Array.from(root.querySelectorAll('canvas'))[0] as HTMLCanvasElement;
    const ctx=main.getContext('2d')!;
    const shapes=spyOn(ctx,'arc').and.callThrough();
    component.paintFrame(1.25);
    expect(component.lastPaintedTime()).toBe(1.25);
    expect(shapes).toHaveBeenCalled();
    expect(root.querySelector('video')).toBe(source);
    expect(combined).not.toBeNull();
    expect(component.paintedViews()).toBe(4);
    fixture.destroy();
  });

  it('pauses master video, frame steps with 12fps decoder cadence, and stops overlays',()=>{
    const {fixture,component,source}=init();
    const videoPause=spyOn(source,'pause').and.stub();
    source.currentTime=2;
    component.stepFrame(1);
    expect(videoPause).toHaveBeenCalled();
    expect(source.currentTime).toBeCloseTo(2+1/12,5);
    component.onDecodedFrame();
    expect(component.currentTime()).toBeCloseTo(2+1/12,5);
    const time=component.lastPaintedTime();
    component.onPause();
    expect(component.lastPaintedTime()).toBe(time);
    component.stepFrame(-1);
    expect(source.currentTime).toBeCloseTo(2,5);
    fixture.destroy();
  });

  it('does not require a tracking signal to keep 3 frames in sync',()=>{
    const {fixture,component}=init();
    fixture.componentRef.setInput('points',null);
    fixture.detectChanges();
    component.paintFrame(3);
    expect(component.overlayStatus()).toBe('NO_2D_TRACK_FOR_THIS_VIDEO');
    expect(component.paintedViews()).toBe(4);
    fixture.destroy();
  });
});

describe('Pixel-space synchronized annotation helpers',()=>{
  it('reuses real time-indexed X/Y points without treating them as SAM',()=>{
    const v=motionAt(track,1);
    expect(v).not.toBeNull();
    expect(v?.source).toBe('LOCAL_CANDIDATE_NOT_SAM');
    expect(v?.x).toBeCloseTo(track[12].x,6);
    expect(v?.y).toBeCloseTo(track[12].y,6);
    expect(v!.speedPixelsPerSecond).toBeGreaterThan(0);
  });

  it('never interpolates across a long gap or past a missing frame',()=>{
    expect(motionAt(track,12)).toBeNull();
    const discontinuous=[track[0],track[30]];
    expect(motionAt(discontinuous,track[0].timeSeconds)).toBeNull();
    expect(nearestMotionTime(track,10)).toBeNull();
  });

  it('produces a bounded past-only ghost without future points',()=>{
    const trail=trailingMotion(track,2,1);
    expect(trail.length).toBeGreaterThan(8);
    expect(trail.every(s=>s.timeSeconds<=2.02)).toBeTrue();
    expect(trail.every(s=>s.timeSeconds>=1)).toBeTrue();
    expect(trailingMotion(track,2,-1)).toEqual([]);
  });

  it('seeks exact proxy frames and formats timestamps',()=>{
    expect(seekByFrame(2,1,12,30)).toBeCloseTo(2+1/12,6);
    expect(seekByFrame(0,-1,12,30)).toBe(0);
    expect(seekByFrame(29.99,1,12,30)).toBeLessThan(30);
    expect(formatMediaTime(2.0833)).toBe('00:02.083');
    expect(formatMediaTime(62.5)).toBe('01:02.500');
  });
});
