import {
  pivotFromClick,sampleAtTime,vectorAt,idealAngularSpeedAtAngle,originalImageOffset,measureCordAngle,
  type KinematicFrame
} from './pendulum-kinematics';

function sample(t:number,omega:number|null=1,alpha:number|null=2):KinematicFrame{
  return {timeSeconds:t,bobXpx:290,bobYpx:160,angleDegrees:26.57,
    angularVelocityRadiansPerSecond:omega,
    angularAccelerationRadiansPerSecondSquared:alpha,
    apparentTangentialSpeedPixelsPerSecond:omega===null?null:Math.abs(omega)*134.16,
    tangentialSpeedMetersPerSecond:null,
    tangentialAccelerationMetersPerSecondSquared:null,
    centripetalAccelerationMetersPerSecondSquared:null,
    isValid:omega!==null};
}
describe('Stage 2 image-plane pivot and derivative frame mapping',()=>{
  it('maps clicks to tracker pixels while respecting object-fit letterboxing',()=>{
    const rect={left:100,top:50,width:600,height:300};
    const p=pivotFromClick(400,200,rect,1920,1080);
    // Fitted 16:9 media is 533.33x300 with horizontal bars
    expect(p).not.toBeNull();
    expect(p!.pivotXpx).toBeCloseTo(240,5);
    expect(p!.pivotYpx).toBeCloseTo(135,5);
    expect(pivotFromClick(110,200,rect,1920,1080)).toBeNull();
    expect(pivotFromClick(900,200,rect,1920,1080)).toBeNull();
  });
  it('rejects unusable media geometry',()=>{
    expect(pivotFromClick(10,10,{left:0,top:0,width:0,height:100},1920,1080)).toBeNull();
    expect(pivotFromClick(10,10,{left:0,top:0,width:100,height:100},0,1080)).toBeNull();
    expect(pivotFromClick(NaN,20,{left:0,top:0,width:100,height:100},1920,1080)).toBeNull();
  });
  it('uses a measured timestamp without filling a missing section',()=>{
    const frames=[sample(0),sample(1/12),sample(2),sample(2+1/12)];
    expect(sampleAtTime(frames,1/12)?.timeSeconds).toBeCloseTo(1/12,7);
    expect(sampleAtTime(frames,1)).toBeNull();
    expect(sampleAtTime(frames,2+.04)?.timeSeconds).toBe(2);
  });
  it('converts positive angular speed to a screen tangent vector',()=>{
    const result=vectorAt(sample(0,2,3),{pivotXpx:240,pivotYpx:60});
    expect(result).not.toBeNull();
    // Radius to bob = (50,100); derivative vector is (100,-50)*omega
    expect(result!.vx).toBeCloseTo(200,8);
    expect(result!.vy).toBeCloseTo(-100,8);
    expect(result!.atx).toBeCloseTo(300,8);
    expect(result!.aty).toBeCloseTo(-150,8);
    expect(result!.arx).toBeCloseTo(-200,8);
    expect(result!.ary).toBeCloseTo(-400,8);
  });
  it('reverses the tangential arrow and preserves inward radial acceleration',()=>{
    const positive=vectorAt(sample(0,2,0),{pivotXpx:240,pivotYpx:60})!;
    const negative=vectorAt(sample(0,-2,0),{pivotXpx:240,pivotYpx:60})!;
    expect(negative.vx).toBe(-positive.vx);
    expect(negative.vy).toBe(-positive.vy);
    expect(negative.arx).toBe(positive.arx);
  });
  it('ideal speed peaks at bottom and is zero at theoretical extreme angles',()=>{
    expect(idealAngularSpeedAtAngle(0,15,1.2)).toBeCloseTo(1.2,8);
    expect(idealAngularSpeedAtAngle(15,15,1.2)).toBeCloseTo(0,8);
    expect(idealAngularSpeedAtAngle(-15,15,1.2)).toBeCloseTo(0,8);
    expect(idealAngularSpeedAtAngle(7,15,1.2)).toBeGreaterThan(0);
    expect(idealAngularSpeedAtAngle(7,15,1.2)).toBeLessThan(1.2);
    expect(idealAngularSpeedAtAngle(19,15,1.2)).toBe(0);
    expect(idealAngularSpeedAtAngle(NaN,15,1.2)).toBeNull();
  });

  it('never creates vectors in frames with invalid derivatives',()=>{
    expect(vectorAt(sample(0,null,2),{pivotXpx:240,pivotYpx:60})).toBeNull();
    expect(vectorAt(sample(0,1,null),{pivotXpx:240,pivotYpx:60})).toBeNull();
    expect(vectorAt(sample(0,1,2),{pivotXpx:290,pivotYpx:160})).toBeNull();
  });
  it('makes portrait tangential vectors perpendicular to the original radius',()=>{
    const pivot={pivotXpx:240,pivotYpx:60},size={width:1080,height:1920};
    const offset=originalImageOffset(290,160,pivot,size)!;
    const v=vectorAt(sample(0,2,3),pivot,size)!;
    expect(offset.dx).toBeCloseTo(112.5,8);
    expect(offset.dy).toBeCloseTo(100*1920/270,8);
    expect(v.vx*offset.dx+v.vy*offset.dy).toBeCloseTo(0,7);
    expect(v.atx*offset.dx+v.aty*offset.dy).toBeCloseTo(0,7);
    expect(v.vx).toBeCloseTo(2*offset.dy,8);
    expect(v.vy).toBeCloseTo(-2*offset.dx,8);
    expect(v.arx).toBeCloseTo(-4*offset.dx,8);
    expect(v.ary).toBeCloseTo(-4*offset.dy,8);
  });
  it('rejects missing original dimensions instead of assuming landscape geometry',()=>{
    const pivot={pivotXpx:240,pivotYpx:60};
    for(const size of [{width:0,height:1920},{width:1080,height:NaN},{width:-1,height:270}]) {
      expect(originalImageOffset(290,160,pivot,size)).toBeNull();
      expect(vectorAt(sample(0),pivot,size)).toBeNull();
    }
  });
});

describe('String angle from image vertical',()=>{
  for(const image of [{width:1080,height:1920},{width:3840,height:2160},{width:1000,height:1000}])
    for(const degrees of [-30,0,30])it('measures '+degrees+' degrees in '+image.width+'x'+image.height,()=>{
      const theta=degrees*Math.PI/180,pivot={pivotXpx:240,pivotYpx:50};
      const x=pivot.pivotXpx+300*Math.sin(theta)/image.width*480;
      const y=pivot.pivotYpx+300*Math.cos(theta)/image.height*270;
      expect(measureCordAngle(x,y,pivot,image)?.angleDegrees).toBeCloseTo(degrees,9);
    });
  it('rejects an undefined axis or invalid geometry instead of reporting zero',()=>{
    const pivot={pivotXpx:240,pivotYpx:50},image={width:1080,height:1920};
    expect(measureCordAngle(240,50,pivot,image)).toBeNull();
    expect(measureCordAngle(240,50.1,pivot,image)).toBeNull();
    expect(measureCordAngle(NaN,200,pivot,image)).toBeNull();
    expect(measureCordAngle(260,200,pivot,{width:0,height:1920})).toBeNull();
  });
});
