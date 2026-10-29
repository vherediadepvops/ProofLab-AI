import {TestBed} from '@angular/core/testing';
import {SamPeriodPanel} from './sam-period-panel';
import type {SamPeriodInput} from './sam-period';

describe('SAM period panel',()=>{
  beforeEach(async()=>{await TestBed.configureTestingModule({imports:[SamPeriodPanel]}).compileComponents();});
  function init(){
    const fixture=TestBed.createComponent(SamPeriodPanel);
    const input:SamPeriodInput={framePtsSeconds:[0,.016,.033],centroids:[],
      sourceWidth:1080,sourceHeight:1920,imageWidth:1080,imageHeight:1920,
      pivot:null,arrangement:'simple_pendulum'};
    fixture.componentRef.setInput('measurement',input);fixture.detectChanges();return fixture;
  }
  it('shows an inconclusive result without invented numbers or an export identity',()=>{
    const f=init(),text=f.nativeElement.textContent;
    expect(text).toContain('Medición local insuficiente');expect(text).toContain('SAM no detectó el objeto.');
    expect(text).toContain('No es incertidumbre total');
    expect(f.nativeElement.querySelector('button').disabled).toBeTrue();f.destroy();
  });
  it('renders the explanation and status in English',()=>{
    const f=init();f.componentRef.setInput('language','en');f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Insufficient local measurement');
    expect(f.nativeElement.textContent).toContain('Not total uncertainty or a 95% interval.');
    expect(f.nativeElement.textContent).toContain('SAM did not detect the object.');f.destroy();
  });
  it('reports local quality without issuing a phenomenon classification',()=>{
    const f=init(),times=Array.from({length:1200},(_,i)=>i/60);
    const input:SamPeriodInput={framePtsSeconds:times,
      sourceWidth:1000,sourceHeight:1000,imageWidth:1000,imageHeight:1000,
      pivot:{x:.5,y:.15},arrangement:'legacy_pendulum',
      centroids:times.map((t,frameIndex)=>{const a=.18*Math.sin(2*Math.PI*t/1.4-.7);
        return {frameIndex,x:500+500*Math.sin(a),y:150+500*Math.cos(a)};})};
    f.componentRef.setInput('measurement',input);f.detectChanges();
    expect(f.nativeElement.querySelector('.status').textContent).toContain('Medición local disponible');
    expect(f.nativeElement.textContent).not.toContain('Movimiento compatible con un péndulo');
    expect(f.nativeElement.textContent).toContain('Estos cálculos no clasifican el fenómeno');
    expect(f.nativeElement.querySelectorAll('section').length).toBe(1);
    input.centroids=input.centroids.map(c=>({...c,y:650}));
    f.componentRef.setInput('measurement',{...input});f.detectChanges();
    expect(f.nativeElement.querySelector('.status').textContent).toContain('Medición local disponible');
    expect(f.nativeElement.textContent).toContain('Se observa periodicidad');
    expect(f.nativeElement.textContent).not.toContain('Movimiento pendular:');
    f.componentRef.setInput('language','en');f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Periodicity is observed');
    expect(f.nativeElement.querySelectorAll('section').length).toBe(1);f.destroy();
  });
});
