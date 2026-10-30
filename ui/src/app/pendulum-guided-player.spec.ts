import {TestBed} from '@angular/core/testing';
import {PendulumMasterPlayer} from './pendulum-master-player';
import type {KinematicRevision} from './pendulum-kinematics';
import type {CandidatePixelPoint} from './pendulum-master-sync';
const revision:KinematicRevision={
  studyId:'d'.repeat(32),role:'baseline',videoId:'e'.repeat(32),
  videoSha256:'a'.repeat(64),revisionId:'QA_REVISION',calculatedAtUtc:'2026-10-28T05:10:00Z',
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

describe('Guided player evidence boundaries',()=>{
  beforeEach(async()=>{await TestBed.configureTestingModule({imports:[PendulumMasterPlayer]}).compileComponents();});
  function init(){
    const fixture=TestBed.createComponent(PendulumMasterPlayer);
    fixture.componentRef.setInput('videoUrl','');fixture.detectChanges();
    const component=fixture.componentInstance,root=fixture.nativeElement as HTMLElement;
    const source=root.querySelector('video')!;
    Object.defineProperties(source,{videoWidth:{configurable:true,value:640},videoHeight:{configurable:true,value:360},
      duration:{configurable:true,value:30},readyState:{configurable:true,value:HTMLMediaElement.HAVE_CURRENT_DATA}});
    for(const canvas of Array.from(root.querySelectorAll('canvas')))spyOn(canvas.getContext('2d')!,'drawImage').and.stub();
    component.onLoadedMetadata();fixture.detectChanges();
    return {fixture,component,root};
  }
  it('allows source playback while keeping all unverified analysis hidden',()=>{
    const {fixture,component,root}=init();
    fixture.componentRef.setInput('processingRequired',true);fixture.componentRef.setInput('presentation','source');fixture.detectChanges();
    expect(component.processingRequired()).toBeTrue();expect(component.playbackLocked()).toBeFalse();
    expect(component.samVerified()).toBeFalse();expect(component.analysisVisible()).toBeFalse();
    expect(root.querySelector('.guided-source')).not.toBeNull();expect(root.querySelector('.guided-layers')).toBeNull();
    expect(root.querySelector('.observed-charts')).toBeNull();
    expect((root.querySelector('.player-evidence') as HTMLElement).hidden).toBeTrue();
    fixture.destroy();
  });
  it('derives metric readouts only from valid observed derivatives and a finite positive entered length',()=>{
    const {fixture,component}=init();
    fixture.componentRef.setInput('presentation','understand');fixture.componentRef.setInput('physicalLengthMeters',.5);fixture.detectChanges();
    component.samTrack.set({frameWidth:640,frameHeight:360,source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId:'bob',points:[],masks:[]});
    component.samVerified.set(true);
    const frame={...revision.analysis.samples[0],angularVelocityRadiansPerSecond:-2,angularAccelerationRadiansPerSecondSquared:3,isValid:true};
    component.currentKinematics.set(frame);
    component.currentCordAngle.set({angleDegrees:frame.angleDegrees,bobXpx:frame.bobXpx,bobYpx:frame.bobYpx});
    expect(component.derivedMotion()?.velocity).toBe(-1);expect(component.derivedMotion()?.speed).toBe(1);
    expect(component.derivedMotion()?.acceleration).toBe(2.5);
    for(const length of [0,-.5,NaN,Infinity]){fixture.componentRef.setInput('physicalLengthMeters',length);expect(component.derivedMotion()).toBeNull();}
    fixture.componentRef.setInput('physicalLengthMeters',.8);expect(component.derivedMotion()?.velocity).toBeCloseTo(-1.6,9);
    component.currentKinematics.set({...frame,isValid:false});expect(component.derivedMotion()).toBeNull();
    component.currentKinematics.set(frame);component.samVerified.set(false);expect(component.derivedMotion()).toBeNull();fixture.destroy();
  });
  it('breaks measured curves across missing native frames without inventing angular derivatives',()=>{
    const {fixture,component}=init();fixture.componentRef.setInput('presentation','understand');fixture.detectChanges();
    const indices=[0,1,2,4,5,6];
    const points:CandidatePixelPoint[]=indices.map(index=>{const timeSeconds=index/60,theta=.2+.4*timeSeconds;
      return {timeSeconds,x:240+100*Math.sin(theta),y:60+100*Math.cos(theta),approximateRadiusPixels:8};});
    const masks=points.map((p,i)=>({frameIndex:indices[i],timeSeconds:p.timeSeconds,left:0,top:0,right:1,bottom:1,
      maskPath:'M0 0h1v1Z',foregroundPixels:1,centroidXpx:p.x*640/480,centroidYpx:p.y*360/270}));
    component.pivotRevision.set(revision);component.samVerified.set(true);
    component.samTrack.set({frameWidth:640,frameHeight:360,source:'META_SAM_3_1_CACHED_PROVIDER_EVIDENCE',objectId:'bob',points,masks});
    const chart=component.measuredChart()!;
    expect(chart.points.length).toBe(6);expect((chart.thetaPath.match(/M/g)??[]).length).toBe(2);
    expect(chart.points.filter(p=>p.omega!==null).map(p=>p.index)).toEqual([1,5]);
    expect((chart.omegaPath.match(/M/g)??[]).length).toBe(2);expect(chart.omegaPath).not.toContain('L');
    component.samVerified.set(false);expect(component.measuredChart()).toBeNull();fixture.destroy();
  });
});
