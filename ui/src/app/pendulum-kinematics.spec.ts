import {
  pivotFromClick,sampleAtTime,vectorAt,idealAngularSpeedAtAngle,
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
});
