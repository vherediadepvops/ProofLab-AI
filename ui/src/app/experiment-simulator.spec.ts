import {TestBed} from '@angular/core/testing';
import {ExperimentSimulator} from './experiment-simulator';
describe('Educational preview boundaries',()=>{
  beforeEach(async()=>{await TestBed.configureTestingModule({imports:[ExperimentSimulator]}).compileComponents();});
  it('keeps a friction-held block at rest and never accelerates it uphill',()=>{
    const fixture=TestBed.createComponent(ExperimentSimulator),s=fixture.componentInstance;fixture.detectChanges();
    s.setParameter('incline',5);s.setParameter('friction',.25);
    expect(s.rampAcceleration()).toBe(0);
    for(const t of [0,2,6]){const sample=s.sample(t);expect(sample.distance).toBe(0);expect(Math.hypot(sample.vx,sample.vy)).toBe(0);}
    s.togglePlay();expect(s.playing()).toBeFalse();fixture.destroy();
  });
  it('conserves mechanical energy in the ideal ballistic preview and returns to launch height',()=>{
    const fixture=TestBed.createComponent(ExperimentSimulator),s=fixture.componentInstance;
    fixture.componentRef.setInput('kind','rocket');fixture.detectChanges();
    const energy=.5*s.launchSpeed()**2;
    for(const ratio of [0,.25,.5,.75,1]){const p=s.sample(s.duration()*ratio);
      expect(.5*(p.vx*p.vx+p.vy*p.vy)+s.gravity*p.y).toBeCloseTo(energy,8);}
    expect(s.sample(s.duration()).y).toBeCloseTo(0,8);
    expect(s.sample(s.duration()/2).vy).toBeCloseTo(0,8);fixture.destroy();
  });
  it('pauses on leaving the laboratory and resets time when the model changes',()=>{
    const fixture=TestBed.createComponent(ExperimentSimulator),s=fixture.componentInstance;fixture.detectChanges();
    s.reducedMotion.set(false);s.seek(.2);s.togglePlay();expect(s.playing()).toBeTrue();
    fixture.componentRef.setInput('active',false);fixture.detectChanges();expect(s.playing()).toBeFalse();
    s.setParameter('incline',25);expect(s.time()).toBe(0);
    expect((fixture.nativeElement.querySelector('.future-upload button') as HTMLButtonElement).disabled).toBeTrue();fixture.destroy();
  });
});
