import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import type { KinematicRevision } from './pendulum-kinematics';
import { PendulumMasterPlayer } from './pendulum-master-player';
import {
  formatMediaTime, motionAt, nearestMotionTime, seekByFrame,
  seekSourceFrame, trailingMotion, type CandidatePixelPoint
} from './pendulum-master-sync';

const track:CandidatePixelPoint[]=Array.from({length:60},(_,i)=>({
  timeSeconds:i/12,
  x:210+70*Math.sin(2*Math.PI*(i/12)/1.42),
  y:190+7*Math.cos(2*Math.PI*(i/12)/1.42),
  approximateRadiusPixels:8
}));


const study='d'.repeat(32);
const sha='a'.repeat(64);
function flushNoSamCache(http:HttpTestingController):void {
  const pending=http.match(req=>req.method==='GET' &&
    req.url.includes('/sam/') && req.url.endsWith('/evidence'));
  for(const request of pending)
    request.flush({error:'No provider response cached'},
      {status:404,statusText:'Not Found'});
}
const revision:KinematicRevision={
  studyId:study,role:'baseline',videoId:'e'.repeat(32),
  videoSha256:sha,revisionId:'QA_REVISION',calculatedAtUtc:'2026-10-28T05:10:00Z',
  pivotProvenance:'MANUALLY_SELECTED_PIXEL_PIVOT_NOT_INDEPENDENTLY_VERIFIED',
  analysis:{
    method:'LOCAL_2D_PIVOT_FINITE_DIFFERENCE_V1_NOT_SAM',
    source:'FFMPEG_BACKGROUND_PIXEL_CANDIDATES_USER_PIVOT_NOT_INDEPENDENT',
    pivotXpx:240,pivotYpx:60,sourceWidth:480,sourceHeight:270,
    medianRadiusPixels:112,radiusMedianAbsoluteDeviationPercent:2,
    observedMaximumAngularSpeedRadPerSec:2.0,
    observedCenterAngularSpeedRadPerSec:1.9,
    observedExtremeAngularSpeedRadPerSec:.1,
    theoreticalMaxAngularSpeedRadPerSec:1.2,
    physicalUnitsEnabled:false,
    qualityStatus:'LOCAL_2D_CANDIDATE_REQUIRES_VISUAL_REVIEW',
    sourcePoints:120,derivativePoints:100,
    limitationsEs:'Pivote provisional sin calibración',limitationsEn:'Uncalibrated pivot',
    samples:[{
      timeSeconds:1,bobXpx:290,bobYpx:160,angleDegrees:26.57,
      angularVelocityRadiansPerSecond:2,angularAccelerationRadiansPerSecondSquared:3,
      apparentTangentialSpeedPixelsPerSecond:223.6,
      tangentialSpeedMetersPerSecond:null,tangentialAccelerationMetersPerSecondSquared:null,
      centripetalAccelerationMetersPerSecondSquared:null,isValid:true
    }]
  }
};

describe('One-clock three-view pendulum laboratory',()=>{
  beforeEach(async()=>{
    await TestBed.configureTestingModule({
      imports:[PendulumMasterPlayer],
      providers:[provideHttpClient(),provideHttpClientTesting()]
    })
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

  it('unlocks only the original after failure, leaving SAM evidence unavailable',()=>{
    const {fixture,component,root}=init();
    fixture.componentRef.setInput('processingRequired',true);
    fixture.detectChanges();
    expect(component.playbackLocked()).toBeTrue();
    fixture.componentRef.setInput('processingError',true);
    fixture.detectChanges();
    expect(component.playbackLocked()).toBeFalse();
    expect(component.samVerified()).toBeFalse();
    expect(component.samTrack()).toBeNull();
    component.onDecodedFrame();fixture.detectChanges();
    expect(root.textContent).toContain('Puedes reproducir el video original');
    fixture.componentRef.setInput('processingError',false);
    fixture.detectChanges();
    expect(component.playbackLocked()).toBeTrue();
    fixture.destroy();
  });

  it('never starts three separate media decoders',()=>{
    const {fixture,root,component}=init();
    expect(root.querySelectorAll('video').length).toBe(1);
    expect(root.querySelectorAll('canvas').length).toBe(3);
    expect(root.querySelectorAll('.view-panel').length).toBe(4);
    expect(root.querySelector('.studio-heading')).toBeNull();
    expect(root.querySelector('.cord-angle-card')).toBeNull();
    expect(root.querySelector('[data-view="combined"] .header-readings')).not.toBeNull();
    expect(root.textContent).toContain('Video original');
    expect(root.textContent).toContain('No hay evidencia SAM 3.1');
    expect(component.sourceFps()).toBe(60);
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
    expect(component.frameNumber()).toBe(75);
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

  it('allows dragging the 1.6x zoomed image with clamped shared pan without seeking',()=>{
    const {fixture,component,source,root}=init();
    component.toggleZoom();
    source.currentTime=7.25;
    source.playbackRate=.5;
    const target={
      getBoundingClientRect:()=>({width:800,height:450}),
      setPointerCapture:()=>{},hasPointerCapture:()=>false,
      releasePointerCapture:()=>{}
    };
    const pointer=(x:number,y:number)=>({
      pointerId:1,button:0,clientX:x,clientY:y,
      currentTarget:target,preventDefault:()=>{}
    } as unknown as PointerEvent);
    component.onPanStart(pointer(200,200));
    component.onPanMove(pointer(320,255));
    expect(component.panOffset().x).toBeCloseTo(15,5);
    expect(component.panOffset().y).toBeCloseTo(12.22222,3);
    expect(component.imageTransform()).toContain('translate(');
    component.onPanMove(pointer(2000,-2000));
    expect(component.panOffset()).toEqual({x:30,y:-30});
    component.onPanEnd(pointer(2000,-2000));
    component.onSurfaceClick('original',new MouseEvent('click'));
    expect(component.activeView()).toBe('combined');
    expect(root.querySelector('video')).toBe(source);
    expect(source.currentTime).toBe(7.25);
    expect(source.playbackRate).toBe(.5);
    component.toggleZoom();
    expect(component.panOffset()).toEqual({x:0,y:0});
    expect(component.imageTransform()).toBe('none');
    fixture.destroy();
  });

  it('does not pan unzoomed images or synthesize click-swaps after dragging',()=>{
    const {fixture,component}=init();
    const target={getBoundingClientRect:()=>({width:600,height:400}),
      setPointerCapture:()=>{},hasPointerCapture:()=>false,releasePointerCapture:()=>{}};
    const event={pointerId:2,button:0,clientX:40,clientY:30,
      currentTarget:target,preventDefault:()=>{}} as unknown as PointerEvent;
    component.onPanStart(event);
    component.onPanMove({...event,clientX:250} as PointerEvent);
    expect(component.panOffset()).toEqual({x:0,y:0});
    expect(component.dragging()).toBeFalse();
    fixture.destroy();
  });

  it('loads a saved SHA-linked pivot and paints measured angular vectors',async()=>{
    const {fixture,component,source,root}=init();
    const http=TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('studyId',study);
    fixture.componentRef.setInput('videoSha256',sha);
    fixture.detectChanges();
    const req=http.expectOne(r=>r.method==='GET' &&
      r.url.endsWith('/'+study+'/kinematics/baseline'));
    req.flush(revision);
    flushNoSamCache(http);
    await fixture.whenStable();
    source.currentTime=1;
    const combined=root.querySelector('canvas.synchronized-canvas') as HTMLCanvasElement;
    const ctx=combined.getContext('2d')!;
    const stroke=spyOn(ctx,'stroke').and.callThrough();
    component.paintFrame(1);
    fixture.detectChanges();
    expect(component.pivotRevision()?.revisionId).toBe('QA_REVISION');
    expect(component.currentKinematics()?.angleDegrees).toBe(26.57);
    expect(component.overlayStatus()).toBe('LOCAL_ANGULAR_CANDIDATE_NOT_SAM');
    expect(stroke).toHaveBeenCalled();
    expect(root.querySelector('.kinematics-strip')).toBeNull();
    expect(component.currentKinematics()?.angularVelocityRadiansPerSecond).toBe(2);
    expect(root.querySelector('.pivot-button')?.closest('.playback-buttons')).not.toBeNull();
    expect(root.querySelectorAll('video').length).toBe(1);
    expect(root.querySelectorAll('canvas').length).toBe(3);
    http.verify();
    fixture.destroy();
  });


  it('uses decoded portrait geometry for SAM angles and arrows, preserving the saved pivot',()=>{
    const {fixture,component,source,root}=init();
    Object.defineProperty(source,'videoWidth',{configurable:true,value:1080});
    Object.defineProperty(source,'videoHeight',{configurable:true,value:1920});
    component.onLoadedMetadata();
    // Container metadata and SAM raster may differ; the displayed original is portrait.
    const radius=300,at=.12,px=240,py=60;
    const points=[.04,.08,.12,.16,.20].map(t=>{
      const dt=t-at,theta=Math.PI/6+.4*dt+.4*dt*dt;
      return {timeSeconds:t,x:px+radius*Math.sin(theta)/1080*480,
        y:py+radius*Math.cos(theta)/1920*270,approximateRadiusPixels:8};
    });
    component.samTrack.set({frameWidth:640,frameHeight:360,
      source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId:'bob',points,masks:points.map((p,i)=>({
        frameIndex:i,timeSeconds:p.timeSeconds,left:0,top:0,right:4,bottom:4,
        maskPath:'M0 0L1 0L1 1Z',centroidXpx:p.x,centroidYpx:p.y,foregroundPixels:2}))});
    component.pivotRevision.set(revision);
    for(const canvas of Array.from(root.querySelectorAll('canvas')))
      spyOn(canvas.getContext('2d')!,'drawImage').and.stub();
    const arrows=spyOn<any>(component,'drawArrow').and.stub();
    component.paintFrame(at);
    expect(component.currentKinematics()?.angleDegrees).toBeCloseTo(30,8);
    expect(component.currentKinematics()?.angularVelocityRadiansPerSecond).toBeCloseTo(.4,8);
    expect(component.currentKinematics()?.angularAccelerationRadiansPerSecondSquared).toBeCloseTo(.8,8);
    expect(component.currentKinematics()?.apparentTangentialSpeedPixelsPerSecond).toBeCloseTo(120,8);
    const velocity=arrows.calls.allArgs().find(args=>args[5]==='#3cf2da')!;
    expect(velocity[3]).toBeCloseTo(120*Math.cos(Math.PI/6),8);
    expect(velocity[4]).toBeCloseTo(-60,8);
    expect(component.pivotRevision()?.analysis.pivotXpx).toBe(px);
    expect(component.pivotRevision()?.analysis.pivotYpx).toBe(py);
    component.toggleZoom();component.paintFrame(at);
    expect(component.currentKinematics()?.angleDegrees).toBeCloseTo(30,8);
    fixture.destroy();
  });


  it('scales the painted cyan shaft linearly with measured angular speed, independent of radius',()=>{
    const {fixture,component,source,root}=init();
    Object.defineProperty(source,'videoWidth',{configurable:true,value:1080});
    Object.defineProperty(source,'videoHeight',{configurable:true,value:1920});
    component.onLoadedMetadata();component.pivotRevision.set(revision);
    // θ(t)=t² has an exact central derivative 2t on unequal source intervals.
    // Its varying radius must not alter the displayed length for a given ω.
    const points=[0,.04,.10,.16,.20].map(timeSeconds=>{
      const radius=300+100*timeSeconds,theta=timeSeconds*timeSeconds;
      return {timeSeconds,x:240+radius*Math.sin(theta)/1080*480,
        y:60+radius*Math.cos(theta)/1920*270,approximateRadiusPixels:8};
    });
    const sam={frameWidth:1080,frameHeight:1920,
      source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE' as const,objectId:'bob',points,masks:points.map((p,i)=>({
        frameIndex:i,timeSeconds:p.timeSeconds,left:0,top:0,right:4,bottom:4,
        maskPath:'M0 0L1 0L1 1Z',centroidXpx:p.x,centroidYpx:p.y,foregroundPixels:2}))};
    component.samTrack.set(sam);
    const canvas=root.querySelector<HTMLCanvasElement>('[data-view="vectors"] canvas')!;
    const ctx=canvas.getContext('2d')!;
    let start:{x:number;y:number}|null=null;
    const shafts:{length:number;dx:number;dy:number}[]=[];
    spyOn(ctx,'moveTo').and.callFake((x,y)=>{start={x,y};});
    let tip:{x:number;y:number}|null=null;
    spyOn(ctx,'lineTo').and.callFake((x,y)=>{tip={x,y};});
    spyOn(ctx,'stroke').and.callFake(()=>{
      if(ctx.strokeStyle==='#3cf2da' && start && tip)
        shafts.push({length:Math.hypot(tip.x-start.x,tip.y-start.y),
          dx:tip.x-start.x,dy:tip.y-start.y});
    });
    const paint=(omega:number,radius:number)=>{
      shafts.length=0;
      const theta=.2;
      const frame={...revision.analysis.samples[0],
        bobXpx:240+radius*Math.sin(theta)/1080*480,
        bobYpx:60+radius*Math.cos(theta)/1920*270,
        angularVelocityRadiansPerSecond:omega,angularAccelerationRadiansPerSecondSquared:0};
      (component as any).paintAngular(ctx,canvas,frame,revision.analysis,true,{width:1080,height:1920});
      return shafts[0];
    };
    // Peak from the full valid track is 0.32 rad/s. No instantaneous saturation.
    const full=paint(.32,300),half=paint(.16,700),quarter=paint(.08,200);
    expect(full.length).toBeCloseTo(90*1080/480,7);
    expect(half.length/full.length).toBeCloseTo(.5,8);
    expect(quarter.length/full.length).toBeCloseTo(.25,8);
    const reverse=paint(-.16,500);
    expect(reverse.length).toBeCloseTo(half.length,8);
    expect(reverse.dx).toBeLessThan(0);expect(half.dx).toBeGreaterThan(0);
    expect(reverse.dx*Math.sin(.2)+reverse.dy*Math.cos(.2)).toBeCloseTo(0,8);
    expect(paint(0,300)).toBeUndefined();
    const tiny=paint(.000001,300);
    expect(tiny.length/full.length).toBeCloseTo(.000001/.32,8);
    expect(paint(.32,300).length).toBeCloseTo(full.length,8); // seeking does not rescale
    component.samTrack.set({...sam,points:points.map(p=>({...p,
      x:240+(p.x-240)*2,y:60+(p.y-60)*2}))});
    expect(paint(.16,600).length).toBeCloseTo(half.length,8);
    fixture.destroy();
  });

  it('shows SAM string angles at endpoints without derivatives, clears missing frames and keeps the original clean',()=>{
    const {fixture,component,source,root}=init();
    fixture.componentRef.setInput('studyId',study);
    fixture.componentRef.setInput('videoSha256',sha);
    fixture.componentRef.setInput('processingRequired',true);
    fixture.detectChanges();
    Object.defineProperty(source,'videoWidth',{configurable:true,value:1080});
    Object.defineProperty(source,'videoHeight',{configurable:true,value:1920});
    component.onLoadedMetadata();
    const points=[-30,0,30].map((degrees,timeSeconds)=>{
      const theta=degrees*Math.PI/180;
      return {timeSeconds,x:240+300*Math.sin(theta)/1080*480,
        y:60+300*Math.cos(theta)/1920*270,approximateRadiusPixels:8};
    });
    const masks=points.map((p,frameIndex)=>({
      frameIndex,timeSeconds:p.timeSeconds,left:0,top:0,right:1,bottom:1,
      centroidXpx:p.x/480*1920,centroidYpx:p.y/270*1080,
      foregroundPixels:100,maskPath:'M0 0h1v1Z'
    }));
    const sam={frameWidth:1920,frameHeight:1080,
      source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE' as const,objectId:'bob',points,masks};
    component.samTrack.set(sam);component.samVerified.set(true);
    component.pivotRevision.set(revision);
    for(const canvas of Array.from(root.querySelectorAll('canvas')))
      spyOn(canvas.getContext('2d')!,'drawImage').and.stub();
    const combined=root.querySelector('canvas')!.getContext('2d')!;
    const arc=spyOn(combined,'arc').and.callThrough();
    const dash=spyOn(combined,'setLineDash').and.callThrough();
    component.paintFrame(0);fixture.detectChanges();
    expect(component.currentKinematics()?.isValid).toBeFalse();
    expect(component.currentCordAngle()?.angleDegrees).toBeCloseTo(-30,8);
    expect(root.querySelector('.angle-value')?.textContent).toContain('−30,0°');
    expect(root.querySelector('.angle-value')?.getAttribute('title')).toContain('Izquierda');
    expect(arc.calls.allArgs().some(a=>Math.abs(a[3]-Math.PI/2)<1e-8 &&
      Math.abs(a[4]-2*Math.PI/3)<1e-8 && a[5]===false)).toBeTrue();
    expect(dash.calls.allArgs().some(a=>Array.from(a[0]).length===2)).toBeTrue();
    component.paintFrame(1);fixture.detectChanges();
    expect(component.cordAngleText()).toBe('0,0°');
    expect(component.cordAngleDirection()).toBe('En la vertical');
    component.paintFrame(2);fixture.detectChanges();
    expect(component.cordAngleText()).toBe('+30,0°');
    expect(component.cordAngleDirection()).toBe('Derecha');
    const original=root.querySelector('[data-view="original"]')!;
    expect(original.querySelectorAll('canvas').length).toBe(0);
    expect(original.querySelector('video')).toBe(source);
    component.toggleZoom();component.paintFrame(2);
    expect(component.currentCordAngle()?.angleDegrees).toBeCloseTo(30,8);
    source.currentTime=2;
    fixture.componentRef.setInput('language','en');fixture.detectChanges();
    expect(component.cordAngleText()).toBe('+30.0°');
    expect(component.cordAngleDirection()).toBe('Right');
    expect(root.querySelector('.angle-value')?.getAttribute('title')).toContain('image vertical');
    component.paintFrame(1.03);fixture.detectChanges();
    expect(component.currentCordAngle()).toBeNull();
    expect(root.querySelector('.angle-value')?.textContent).toContain('—');
    expect(component.cordAngleHint()).toContain('No valid detection');
    component.pivotRevision.set(null);component.paintFrame(1);fixture.detectChanges();
    expect(component.currentCordAngle()).toBeNull();
    expect(component.cordAngleHint()).toContain('suspension point');
    component.pivotRevision.set(revision);component.paintFrame(1);
    component.samVerified.set(false);component.paintFrame(1);
    expect(component.currentCordAngle()).toBeNull();
    TestBed.inject(HttpTestingController).verify();fixture.destroy();
  });


  it('paints angle and angular velocity on the same video frame, with no stale or ideal fallback',()=>{
    const {fixture,component,source,root}=init();
    fixture.componentRef.setInput('studyId',study);
    fixture.componentRef.setInput('videoSha256',sha);
    fixture.componentRef.setInput('processingRequired',true);
    fixture.detectChanges();
    Object.defineProperty(source,'videoWidth',{configurable:true,value:1080});
    Object.defineProperty(source,'videoHeight',{configurable:true,value:1920});
    component.onLoadedMetadata();
    component.samVerified.set(true);component.pivotRevision.set(revision);
    for(const canvas of Array.from(root.querySelectorAll('canvas')))
      spyOn(canvas.getContext('2d')!,'drawImage').and.stub();
    const readoutContexts=['combined','vectors'].map(view=>
      root.querySelector<HTMLCanvasElement>('[data-view="'+view+'"] canvas')!.getContext('2d')!);
    const readouts=readoutContexts.map(ctx=>spyOn(ctx,'fillText').and.callThrough());
    const ghostText=spyOn(root.querySelector<HTMLCanvasElement>('[data-view="ghost"] canvas')!.getContext('2d')!,'fillText');
    for(const velocity of [-.4,0,.4]){
      readouts.forEach(spy=>spy.calls.reset());
      const points=[.10,.13,.18].map(timeSeconds=>{
        const dt=timeSeconds-.13,theta=.2+velocity*dt+.4*dt*dt;
        return {timeSeconds,x:240+300*Math.sin(theta)/1080*480,
          y:60+300*Math.cos(theta)/1920*270,approximateRadiusPixels:8};
      });
      const masks=points.map((p,frameIndex)=>({
        frameIndex,timeSeconds:p.timeSeconds,left:0,top:0,right:1,bottom:1,
        centroidXpx:p.x/480*1920,centroidYpx:p.y/270*1080,
        foregroundPixels:100,maskPath:'M0 0h1v1Z'
      }));
      component.samTrack.set({frameWidth:1920,frameHeight:1080,
        source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId:'bob',points,masks});
      component.paintFrame(.13);fixture.detectChanges();
      // Same positive position, but either motion direction (or zero) is possible.
      expect(component.currentCordAngle()!.angleDegrees).toBeGreaterThan(0);
      expect(component.angularVelocityAtFrame()).toBeCloseTo(velocity,9);
      const expected=velocity<0?'−0,400':velocity>0?'+0,400':'0,000';
      expect(root.querySelector('.angular-velocity-value')?.textContent).toContain(expected);
      expect(root.querySelector('.angular-velocity-value')?.textContent).toContain('rad/s');
      expect(root.querySelector('.angular-velocity-value')?.getAttribute('title')).toContain(
        velocity<0?'−22,9':velocity>0?'+22,9':'0,0');
      expect(component.angularVelocityDirection()).toContain(
        velocity<0?'Hacia la izquierda':velocity>0?'Hacia la derecha':'≈ 0');
      for(const readout of readouts){
        const labels=readout.calls.allArgs().map(args=>args[0]);
        expect(labels).toContain('θ: '+component.cordAngleText());
        expect(labels).toContain('ω: '+expected+' rad/s');
        expect(labels.length).toBe(2);
        const [theta,omega]=readout.calls.allArgs();
        expect(theta[1]).toBe(omega[1]);
        expect(theta[2]).toBeLessThan(omega[2]);
        expect(omega[2]).toBeLessThan(component.currentCordAngle()!.bobYpx*1920/270);
      }
      expect(ghostText).not.toHaveBeenCalled();
      component.setSpeed(.25);component.paintFrame(.13);
      expect(component.angularVelocityAtFrame()).toBeCloseTo(velocity,9);
      component.setSpeed(2);component.paintFrame(.13);
      expect(component.angularVelocityAtFrame()).toBeCloseTo(velocity,9);
    }
    source.currentTime=.13;
    readouts.forEach(spy=>spy.calls.reset());
    fixture.componentRef.setInput('language','en');fixture.detectChanges();
    for(const readout of readouts)
      expect(readout.calls.allArgs().map(args=>args[0])).toContain('ω: +0.400 rad/s');
    expect(component.angularVelocityText()).toBe('+0.400');
    expect(component.angularVelocityDirection()).toBe('Moving right');
    expect(root.querySelector('.angular-velocity-value')?.getAttribute('aria-label')).toBe('Angular velocity');
    readouts.forEach(spy=>spy.calls.reset());
    component.paintFrame(.10);fixture.detectChanges();
    expect(component.currentCordAngle()).not.toBeNull();
    expect(component.angularVelocityAtFrame()).toBeNull();
    expect(root.querySelector('.angular-velocity-value')?.textContent).toContain('—');
    expect(root.querySelector('.angular-degrees')).toBeNull();
    expect(component.angularVelocityHint()).toContain('Not enough valid nearby detections');
    for(const readout of readouts){
      expect(readout.calls.allArgs().map(args=>args[0])).toContain('ω: — rad/s');
      expect(readout.calls.count()).toBe(2);
    }
    readouts.forEach(spy=>spy.calls.reset());
    component.paintFrame(.16);fixture.detectChanges();
    for(const readout of readouts){
      const labels=readout.calls.allArgs().map(args=>args[0]);
      expect(labels).toEqual([]);
      expect(labels).not.toContain('ω: +0.400 rad/s');
    }
    expect(component.currentCordAngle()).toBeNull();
    expect(component.angularVelocityAtFrame()).toBeNull();
    component.paintFrame(.13);
    component.currentCordAngle.update(a=>({...a!,bobXpx:a!.bobXpx+10}));
    expect(component.angularVelocityAtFrame()).toBeNull();
    component.paintFrame(.13);component.samTrack.set(null);
    expect(component.angularVelocityAtFrame()).toBeNull();
    fixture.componentRef.setInput('samRefreshNonce',1);fixture.detectChanges();
    expect(component.angularVelocityAtFrame()).toBeNull();
    TestBed.inject(HttpTestingController).verify();fixture.destroy();
  });

  it('marks the real video pivot after forcing fit, then POSTs SHA-linked geometry',async()=>{
    const {fixture,component,source,root}=init();
    const http=TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('studyId',study);
    fixture.componentRef.setInput('videoSha256',sha);
    fixture.detectChanges();
    http.expectOne(r=>r.method==='GET'&&r.url.endsWith('/kinematics/baseline'))
      .flush({error:'not yet registered'},{status:404,statusText:'Not Found'});
    flushNoSamCache(http);
    await fixture.whenStable();
    component.selectView('combined');
    component.toggleZoom();
    component.panOffset.set({x:12,y:-10});
    source.currentTime=1;
    const pause=spyOn(source,'pause').and.stub();
    const button=root.querySelector('button.pivot-button') as HTMLButtonElement;
    button.click();fixture.detectChanges();
    expect(pause).toHaveBeenCalled();
    expect(component.activeView()).toBe('original');
    expect(component.zoomed()).toBeFalse();
    expect(component.panOffset()).toEqual({x:0,y:0});
    expect(root.textContent).toContain('haz clic sobre el punto de suspensión');
    spyOn(source,'getBoundingClientRect').and.returnValue({
      left:0,top:0,right:640,bottom:360,x:0,y:0,
      width:640,height:360,toJSON:()=>{}
    } as DOMRect);
    (root.querySelector('[data-view="original"] .view-surface') as HTMLElement)
      .dispatchEvent(new MouseEvent('click',{bubbles:true,clientX:320,clientY:80}));
    const post=http.expectOne(r=>r.method==='POST' &&
      r.url.endsWith('/'+study+'/kinematics/baseline'));
    expect(post.request.body.pivotXpx).toBeCloseTo(240,5);
    expect(post.request.body.pivotYpx).toBeCloseTo(60,5);
    expect(post.request.body.videoSha256).toBe(sha);
    post.flush(revision);
    await fixture.whenStable();fixture.detectChanges();
    expect(component.pivotMode()).toBeFalse();
    expect(component.pivotRevision()?.revisionId).toBe('QA_REVISION');
    expect(source.currentTime).toBe(1);
    http.verify();
    fixture.destroy();
  });

  it('ignores stale pivot GET responses after switching research video role',async()=>{
    const {fixture,component}=init();
    const http=TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('studyId',study);
    fixture.componentRef.setInput('videoSha256',sha);
    fixture.detectChanges();
    const old=http.expectOne(r=>r.method==='GET'&&r.url.endsWith('/kinematics/baseline'));
    fixture.componentRef.setInput('videoRole','followup');
    fixture.detectChanges();
    const latest=http.expectOne(r=>r.method==='GET'&&r.url.endsWith('/kinematics/followup'));
    old.flush(revision);
    latest.flush({error:'No pivot'},{status:404,statusText:'Not Found'});
    flushNoSamCache(http);
    await fixture.whenStable();
    expect(component.pivotRevision()).toBeNull();
    expect(component.pivotMode()).toBeFalse();
    http.verify();fixture.destroy();
  });

  it('treats a completed SAM zero-match archive as inconclusive, without invented motion',async()=>{
    const {fixture,component}=init();
    const http=TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('studyId',study);
    fixture.componentRef.setInput('videoSha256',sha);
    fixture.detectChanges();
    http.expectOne(r=>r.method==='GET'&&r.url.endsWith('/kinematics/baseline'))
      .flush({error:'No pivot'},{status:404,statusText:'Not Found'});
    http.expectOne(r=>r.method==='GET'&&r.url.endsWith('/sam/baseline/evidence'))
      .flush({
        state:{status:'CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW',
          sourceSha256:sha,provenance:'META_SAM_3_1_CACHED_PROVIDER_OUTPUT'},
        receipt:{model:'sam-3.1',concept:'metal pendulum bob',
          originalVideoSha256:sha,providerEvidenceStatus:
            'COMPLETED_PROVIDER_RESPONSE_REQUIRES_VISUAL_REVIEW',
          framePtsSeconds:[0,.016,.032],sourceFrameCount:3},
        rawResponseJson:JSON.stringify({model:'sam-3.1',status:'completed',
          output:[{content:[{type:'output_text',text:''}]}]})
      });
    await fixture.whenStable();
    // Lazy-loaded @meta-sam parser is a separate browser module and may
    // complete after Angular's own stability signal.
    for(let i=0;i<10 && component.samStatus()==='CHECKING_SAM_CACHE';i++)
      await new Promise<void>(resolve=>setTimeout(resolve,10));
    component.paintFrame(1);
    fixture.detectChanges();
    expect(component.samStatus()).toBe('SAM_3_1_COMPLETED_NO_MATCH');
    expect(component.samTrack()).toBeNull();
    expect(component.overlayStatus()).toBe('SAM_3_1_COMPLETED_NO_MATCH');
    http.verify();
    fixture.destroy();
  });

  it('blocks all playback and local overlays before Process even when local tracking and pivot exist',()=>{
    const {fixture,component,source,root}=init();
    const http=TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('processingRequired',true);
    fixture.componentRef.setInput('studyId',study);
    fixture.componentRef.setInput('videoSha256',sha);
    fixture.detectChanges();
    component.pivotRevision.set(revision);
    const play=spyOn(source,'play').and.resolveTo();
    const pause=spyOn(source,'pause').and.stub();
    const ctx=(root.querySelector('canvas') as HTMLCanvasElement).getContext('2d')!;
    spyOn(ctx,'drawImage').and.stub();const arrow=spyOn(ctx,'stroke');
    source.currentTime=1;
    component.togglePlayback();component.stepFrame(1);
    component.scrub({target:{value:'3'}} as unknown as Event);
    component.onPlay();component.paintFrame(1);
    expect(play).not.toHaveBeenCalled();expect(pause).toHaveBeenCalled();
    expect(source.currentTime).toBe(1);expect(arrow).not.toHaveBeenCalled();
    expect(component.currentKinematics()).toBeNull();
    expect(component.samTrack()).toBeNull();
    expect((root.querySelector('.play-main') as HTMLButtonElement).disabled).toBeTrue();
    http.expectNone(()=>true);http.verify();fixture.destroy();
  });

  it('rejects an older analysis when a newly processed result was requested',async()=>{
    const {fixture,component}=init();
    const http=TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('processingRequired',true);
    fixture.componentRef.setInput('processedAnalysisId','new-analysis');
    fixture.componentRef.setInput('studyId',study);
    fixture.componentRef.setInput('videoSha256',sha);
    fixture.detectChanges();
    http.expectOne(r=>r.url.endsWith('/kinematics/baseline')).flush({}, {status:404,statusText:'Not Found'});
    http.expectOne(r=>r.url.endsWith('/sam/baseline/evidence?analysisId=new-analysis')).flush({
      state:{analysisId:'old-analysis'},receipt:{analysisId:'old-analysis'}
    });
    await fixture.whenStable();fixture.detectChanges();
    expect(component.playbackLocked()).toBeTrue();
    expect(component.samTrack()).toBeNull();
    expect(component.samStatus()).toBe('SAM_CACHE_INVALID_OR_AMBIGUOUS_NO_FALLBACK');
    http.verify();fixture.destroy();
  });

  it('clears the verified result and pauses immediately when the processed selection resets',()=>{
    const {fixture,component,source}=init();
    fixture.componentRef.setInput('processingRequired',true);
    fixture.detectChanges();
    component.samVerified.set(true);
    const pause=spyOn(source,'pause').and.stub();
    fixture.componentRef.setInput('samRefreshNonce',1);
    fixture.detectChanges();
    expect(pause).toHaveBeenCalled();
    expect(component.playbackLocked()).toBeTrue();
    expect(component.samTrack()).toBeNull();
    TestBed.inject(HttpTestingController).verify();fixture.destroy();
  });

  it('pauses master video, frame steps with the original 60fps cadence, and stops overlays',()=>{
    const {fixture,component,source}=init();
    const videoPause=spyOn(source,'pause').and.stub();
    source.currentTime=2;
    component.stepFrame(1);
    expect(videoPause).toHaveBeenCalled();
    expect(source.currentTime).toBeCloseTo(2+1/60,5);
    component.onDecodedFrame();
    expect(component.currentTime()).toBeCloseTo(2+1/60,5);
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
  it('retains native 4K raster size and uses 60/120/240 fps without a 12fps clamp',()=>{
    const {fixture,component,source,root}=init();
    Object.defineProperty(source,'videoWidth',{configurable:true,value:3840});
    Object.defineProperty(source,'videoHeight',{configurable:true,value:2160});
    component.onLoadedMetadata();
    for(const canvas of Array.from(root.querySelectorAll('canvas'))) {
      expect(canvas.width).toBe(3840);expect(canvas.height).toBe(2160);
    }
    spyOn(source,'pause').and.stub();
    for(const fps of [60,120,240]) {
      fixture.componentRef.setInput('originalFps',fps);fixture.detectChanges();
      source.currentTime=2;component.stepFrame(1);
      expect(source.currentTime).toBeCloseTo(2+1/fps,6);
      expect(component.frameStepSeconds).toBeCloseTo(1/fps,8);
    }
    fixture.destroy();
  });

  it('uses decoded mediaTime and ignores the playback clock when painting or pausing',()=>{
    const {fixture,component,source}=init();
    let callback:VideoFrameRequestCallback|undefined;
    spyOn(source,'requestVideoFrameCallback').and.callFake(cb=>{callback=cb;return 42;});
    spyOn(source,'cancelVideoFrameCallback').and.stub();
    const paint=spyOn(component,'paintFrame').and.stub();
    component.onLoadedMetadata();
    source.currentTime=1.047;
    callback!(0,{mediaTime:1} as VideoFrameCallbackMetadata);
    expect(paint).toHaveBeenCalledOnceWith(1);
    component.onDecodedFrame();component.onPause();
    expect(paint).toHaveBeenCalledTimes(1);
    // A paused seek also presents a decoded frame.
    source.currentTime=2.019;
    callback!(0,{mediaTime:2.002} as VideoFrameCallbackMetadata);
    expect(paint.calls.mostRecent().args).toEqual([2.002]);
    // HEVC may report presentation metadata before seeked restores HAVE_CURRENT_DATA.
    Object.defineProperty(source,'readyState',{configurable:true,value:1});
    callback!(0,{mediaTime:3.003} as VideoFrameCallbackMetadata);
    expect(paint).toHaveBeenCalledTimes(2);
    Object.defineProperty(source,'readyState',{configurable:true,value:2});
    component.onDecodedFrame();
    expect(paint.calls.mostRecent().args).toEqual([3.003]);
    fixture.destroy();
  });

  it('reports unsupported originals without switching to a reduced proxy',()=>{
    const {fixture,component,source,root}=init();
    fixture.componentRef.setInput('originalCodec','hevc');fixture.detectChanges();
    const selected=source.getAttribute('src');
    component.onPreviewError();fixture.detectChanges();
    expect(source.getAttribute('src')).toBe(selected);
    expect(root.textContent).toContain('No se ha sustituido');
    expect(root.querySelector('.player-error a')?.getAttribute('href')).toBe(selected+'?download=true');
    expect(component.previewLoading()).toBeFalse();
    fixture.destroy();
  });

  it('period inputs require verified SAM and follow pivot/evidence changes, not playback time',()=>{
    const {fixture,component}=init();
    const internal=component as any;
    const receipt={model:'sam-3.1',originalVideoSha256:sha,framePtsSeconds:[0,.016,.033],
      sourceFrameCount:3,analysisId:'f'.repeat(32),responseJsonSha256:'b'.repeat(64)};
    internal.periodReceipt.set(receipt);
    component.pivotRevision.set(revision);
    component.samTrack.set({frameWidth:1080,frameHeight:1920,
      source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId:'0',points:[],
      masks:[{frameIndex:1,timeSeconds:.016,left:500,top:1100,right:560,bottom:1200,
        maskPath:'',centroidXpx:530,centroidYpx:1150,foregroundPixels:200}]});
    expect(component.periodMeasurement()).toBeNull();expect(component.periodSource()).toBeNull();
    component.samVerified.set(true);
    const measured=component.periodMeasurement()!;
    expect(measured.centroids).toEqual([{frameIndex:1,x:530,y:1150}]);
    expect(measured.framePtsSeconds).toEqual(receipt.framePtsSeconds);
    expect(component.periodSource()?.providerResponseSha256).toBe('b'.repeat(64));

    component.currentTime.set(3);
    expect(component.periodMeasurement()).toBe(measured);
    component.scene.set({key:'c'.repeat(64),videoSha256:sha,width:1080,height:1920,
      arrangement:'simple_pendulum',origin:'SPARK_GROUNDING',
      roles:[{id:'1',role:'object'}],pivotX:null,pivotY:null});
    expect(component.periodSource()?.pivotProvenance).toBe(revision.pivotProvenance);
    expect(component.periodSource()?.pivotRevisionId).toBe(revision.revisionId);
    component.pivotRevision.set(null);
    expect(component.periodMeasurement()?.pivot).toBeNull();
    expect(component.periodSource()?.pivotProvenance).toBe('PIVOT_MISSING');
    component.automaticSuspension.set({pivot:{pivotXpx:240,pivotYpx:27},reason:'ESTIMATED_NOT_HUMAN_CONFIRMED',
      method:'SPARK',spreadPixels:1,radialRmsPixels:1,radiusPixels:800,samples:1,excludedFrameIndices:[1]});
    expect(component.periodMeasurement()?.pivot).toEqual({x:.5,y:.1});
    expect(component.periodSource()?.pivotProvenance).toBe('SPARK_SAM_ESTIMATE_REVIEW_PENDING');
    expect(component.playbackSamTrack()?.masks.length).toBe(0);
    // Filtering for display must not improve the scientific evidence by deletion.
    expect(component.periodMeasurement()?.centroids.length).toBe(1);
    component.pivotRevision.set(revision);
    expect(component.periodSource()?.pivotProvenance).toBe(revision.pivotProvenance);
    expect(component.playbackSamTrack()?.masks.length).toBe(1);
    component.automaticSuspension.set(null);
    component.scene.set(null);component.pivotRevision.set(revision);
    component.pivotRevision.set({...revision,revisionId:'new-pivot',
      analysis:{...revision.analysis,pivotXpx:250}});
    expect(component.periodMeasurement()!.pivot!.x).toBeCloseTo(250/480,8);
    expect(component.periodSource()?.pivotRevisionId).toBe('new-pivot');
    component.samTrack.set(null);
    expect(component.periodMeasurement()!.centroids).toEqual([]);
    fixture.componentRef.setInput('videoSha256','c'.repeat(64));fixture.detectChanges();
    expect(component.periodMeasurement()).toBeNull();expect(component.periodSource()).toBeNull();
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

  it('seeks nominal frames without imposing a proxy cadence and formats timestamps',()=>{
    expect(seekByFrame(2,1,12,30)).toBeCloseTo(2+1/12,6);
    expect(seekByFrame(0,-1,12,30)).toBe(0);
    expect(seekByFrame(29.99,1,12,30)).toBeLessThan(30);
    expect(formatMediaTime(2.0833)).toBe('00:02.083');
    expect(formatMediaTime(62.5)).toBe('01:02.500');
  });
  it('steps using verified VFR source timestamps instead of nominal FPS',()=>{
    const pts=[0,.010,.025,.048,.070];
    expect(seekSourceFrame(pts,.025,1,120,1)).toBeCloseTo(.049,8);
    expect(seekSourceFrame(pts,.025,-1,120,1)).toBeCloseTo(.011,8);
    expect(seekSourceFrame(pts,0,-1,120,1)).toBeCloseTo(.001,8);
    expect(seekSourceFrame(pts,.070,1,120,1)).toBeCloseTo(.071,8);
    expect(seekSourceFrame([],2,1,240,4)).toBeCloseTo(2+1/240,8);
  });

});

describe('Original video decoder readiness',()=>{
  it('does not report an audio-only decode as a ready video or zero geometry',()=>{
    TestBed.configureTestingModule({imports:[PendulumMasterPlayer]});
    const fixture=TestBed.createComponent(PendulumMasterPlayer),player=fixture.componentInstance;
    fixture.componentRef.setInput('videoUrl','');
    const media=document.createElement('video');Object.defineProperties(media,{readyState:{value:4},videoWidth:{value:0},videoHeight:{value:0}});
    player.masterVideo={nativeElement:media};const states:string[]=[];player.previewStateChanged.subscribe(s=>states.push(s));
    player.onLoadedMetadata();player.onDecodedFrame();
    expect(player.decodedSize()).toBeNull();expect(states).toEqual(['error']);expect(player.error()).toContain('original');fixture.destroy();
  });
});
