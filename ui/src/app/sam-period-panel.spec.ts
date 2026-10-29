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
    expect(text).toContain('Inconcluso');expect(text).toContain('SAM no detectó el objeto.');
    expect(text).toContain('No es incertidumbre total');
    expect(f.nativeElement.querySelector('button').disabled).toBeTrue();f.destroy();
  });
  it('renders the explanation and status in English',()=>{
    const f=init();f.componentRef.setInput('language','en');f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Inconclusive');
    expect(f.nativeElement.textContent).toContain('Not total uncertainty or a 95% interval.');
    expect(f.nativeElement.textContent).toContain('SAM did not detect the object.');f.destroy();
  });
});
