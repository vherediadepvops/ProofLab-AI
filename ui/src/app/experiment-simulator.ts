import {CommonModule} from '@angular/common';
import {Component,DestroyRef,computed,effect,inject,input,signal} from '@angular/core';
import {FormsModule} from '@angular/forms';
// Educational formulas only; no video analysis or provider requests.
interface Sample {x:number;y:number;distance:number;vx:number;vy:number;ax:number;ay:number}
@Component({
  selector:'app-experiment-simulator',standalone:true,imports:[CommonModule,FormsModule],
  templateUrl:'./experiment-simulator.html',styleUrl:'./experiment-simulator.scss'
})
export class ExperimentSimulator {
  readonly kind=input<'ramp'|'rocket'>('ramp');
  readonly language=input<'es'|'en'>('es');
  readonly active=input(true);
  readonly incline=signal(18);readonly friction=signal(.08);
  readonly launchAngle=signal(55);readonly launchSpeed=signal(24);
  readonly time=signal(0);readonly playing=signal(false);
  readonly vectors=signal(true);readonly trail=signal(true);readonly reducedMotion=signal(false);
  readonly gravity=9.80665;readonly rampLength=4;
  readonly gridX=[64,144,224,304,384,464,544,624];readonly gridY=[64,124,184,244,304,364];
  private frame:number|null=null;private previousTime:number|null=null;private previousKind='';
  get es():boolean{return this.language()==='es';}
  get isRamp():boolean{return this.kind()==='ramp';}
  readonly angle=computed(()=>(this.kind()==='ramp'?this.incline():this.launchAngle())*Math.PI/180);
  readonly rampAcceleration=computed(()=>Math.max(0,this.gravity*(Math.sin(this.angle())-this.friction()*Math.cos(this.angle()))));
  readonly flightTime=computed(()=>2*this.launchSpeed()*Math.sin(this.angle())/this.gravity);
  readonly range=computed(()=>this.launchSpeed()*Math.cos(this.angle())*this.flightTime());
  readonly maximumHeight=computed(()=>Math.pow(this.launchSpeed()*Math.sin(this.angle()),2)/(2*this.gravity));
  readonly duration=computed(()=>this.kind()==='ramp'?(this.rampAcceleration()<1e-8?6:Math.sqrt(8/this.rampAcceleration())):this.flightTime());
  readonly current=computed(()=>this.sample(this.time()));
  readonly speed=computed(()=>Math.hypot(this.current().vx,this.current().vy));
  readonly acceleration=computed(()=>Math.hypot(this.current().ax,this.current().ay));
  readonly maximumSpeed=computed(()=>this.kind()==='ramp'?Math.sqrt(8*this.rampAcceleration()):this.launchSpeed());
  readonly rocketScale=computed(()=>Math.min(520/(this.range()*1.15),250/(this.maximumHeight()*1.2)));
  readonly position=computed(()=>this.toScene(this.current()));
  readonly rampEnd=computed(()=>({x:80+420*Math.cos(this.angle()),y:94+420*Math.sin(this.angle())}));
  readonly trajectory=computed(()=>Array.from({length:81},(_,i)=>{
    const p=this.toScene(this.sample(this.duration()*i/80));return(i?'L':'M')+p.x.toFixed(2)+' '+p.y.toFixed(2);
  }).join(' '));
  readonly ghosts=computed(()=>Array.from({length:9},(_,i)=>{
    const t=this.time()-this.duration()*.028*(9-i);
    return t<0?null:{...this.toScene(this.sample(t)),opacity:.1+i*.055};
  }).filter((p):p is {x:number;y:number;opacity:number}=>p!==null));
  readonly velocityTip=computed(()=>{
    const p=this.position(),s=this.current(),scale=70/Math.max(this.maximumSpeed(),1e-6);
    return{x:p.x+s.vx*scale,y:p.y-s.vy*scale};
  });
  readonly accelerationTip=computed(()=>{
    const p=this.position(),s=this.current(),scale=48/Math.max(this.acceleration(),1e-6);
    return{x:p.x+s.ax*scale,y:p.y-s.ay*scale};
  });
  readonly rotation=computed(()=>this.kind()==='ramp'?this.incline():-Math.atan2(this.current().vy,this.current().vx)*180/Math.PI);
  readonly speedPath=computed(()=>Array.from({length:81},(_,i)=>{
    const s=this.sample(this.duration()*i/80);
    return(i?'L':'M')+(30+570*i/80).toFixed(2)+' '+(88-58*Math.hypot(s.vx,s.vy)/Math.max(this.maximumSpeed(),1)).toFixed(2);
  }).join(' '));
  readonly speedDot=computed(()=>({x:30+570*this.time()/this.duration(),y:88-58*this.speed()/Math.max(this.maximumSpeed(),1)}));
  constructor(){
    effect(()=>{const kind=this.kind(),active=this.active();if(kind!==this.previousKind){this.previousKind=kind;this.reset();}if(!active)this.pause();});
    const reduced=typeof window==='undefined'?null:window.matchMedia?.('(prefers-reduced-motion: reduce)');
    const preference=()=>{this.reducedMotion.set(!!reduced?.matches);if(reduced?.matches)this.pause();};
    const visibility=()=>{if(document.hidden)this.pause();};preference();
    reduced?.addEventListener('change',preference);
    if(typeof document!=='undefined')document.addEventListener('visibilitychange',visibility);
    inject(DestroyRef).onDestroy(()=>{this.pause();reduced?.removeEventListener('change',preference);if(typeof document!=='undefined')document.removeEventListener('visibilitychange',visibility);});
  }
  sample(seconds:number):Sample{
    const t=Math.max(0,Math.min(this.duration(),seconds)),angle=this.angle();
    if(this.kind()==='ramp'){
      const a=this.rampAcceleration(),s=Math.min(4,.5*a*t*t),v=a*t;
      return{x:s*Math.cos(angle),y:-s*Math.sin(angle),distance:s,vx:v*Math.cos(angle),vy:-v*Math.sin(angle),ax:a*Math.cos(angle),ay:-a*Math.sin(angle)};
    }
    const vx=this.launchSpeed()*Math.cos(angle),vy0=this.launchSpeed()*Math.sin(angle);
    return{x:vx*t,y:Math.max(0,vy0*t-.5*this.gravity*t*t),distance:vx*t,vx,vy:vy0-this.gravity*t,ax:0,ay:-this.gravity};
  }
  private toScene(s:Sample):{x:number;y:number}{
    return this.kind()==='ramp'?{x:80+s.x*105+12*Math.sin(this.angle()),y:94-s.y*105-12*Math.cos(this.angle())}
      :{x:64+s.x*this.rocketScale(),y:324-s.y*this.rocketScale()};
  }
  setParameter(key:'incline'|'friction'|'launchAngle'|'launchSpeed',value:unknown):void{
    const n=Number(value);if(!Number.isFinite(n))return;
    switch(key){
      case'incline':this.incline.set(Math.min(35,Math.max(5,n)));break;
      case'friction':this.friction.set(Math.min(.25,Math.max(0,n)));break;
      case'launchAngle':this.launchAngle.set(Math.min(75,Math.max(20,n)));break;
      case'launchSpeed':this.launchSpeed.set(Math.min(40,Math.max(10,n)));break;
    }this.reset();
  }
  seek(value:unknown):void{const n=Number(value);if(!Number.isFinite(n))return;this.pause();this.time.set(Math.max(0,Math.min(this.duration(),n)));}
  togglePlay():void{
    if(this.playing()){this.pause();return;}
    if(!this.active()||this.reducedMotion()||this.isRamp&&this.rampAcceleration()<1e-8)return;
    if(this.time()>=this.duration())this.time.set(0);
    this.playing.set(true);this.previousTime=null;this.frame=requestAnimationFrame(t=>this.tick(t));
  }
  pause():void{this.playing.set(false);this.previousTime=null;if(this.frame!==null){cancelAnimationFrame(this.frame);this.frame=null;}}
  reset():void{this.pause();this.time.set(0);}
  private tick(timestamp:number):void{
    this.frame=null;if(!this.playing()||!this.active())return;
    const delta=this.previousTime===null?0:Math.min(.06,(timestamp-this.previousTime)/1000);this.previousTime=timestamp;
    this.time.set(Math.min(this.duration(),this.time()+delta));
    if(this.time()>=this.duration()){this.pause();return;}
    this.frame=requestAnimationFrame(t=>this.tick(t));
  }
}
