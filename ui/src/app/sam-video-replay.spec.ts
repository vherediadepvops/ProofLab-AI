import { TestBed } from '@angular/core/testing';
import { SamVideoReplay } from './sam-video-replay';

const SYNTHETIC_WIRE =
  '<0f>0<|box;x1=211;y1=228;x2=270;y2=254;w=320;h=334|>' +
  "<|mask;x=0;y=0;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7\`e.ioeAf6-k/#Xd8%dX9x(|>";

function fileFrom(wire:string):File {
  const json=JSON.stringify({
    model:'sam-3.1',status:'completed',
    output:[{content:[{type:'output_text',text:wire}]}]
  });
  return new File([json],'sam-video.json',{type:'application/json'});
}

describe('Stage 3: SAM video offline review UI',()=>{
  beforeEach(async()=>{
    await TestBed.configureTestingModule({imports:[SamVideoReplay]}).compileComponents();
  });

  it('imports a valid mask without any HTTP request, preserving its warning',async()=>{
    const f=TestBed.createComponent(SamVideoReplay);
    f.detectChanges();
    const input=f.nativeElement.querySelector('input[type=file]') as HTMLInputElement;
    Object.defineProperty(input,'files',{configurable:true,value:[fileFrom(SYNTHETIC_WIRE)]});
    await f.componentInstance.onFile({target:input} as unknown as Event);
    f.detectChanges();
    expect(f.componentInstance.result()?.detectionCount).toBe(1);
    expect(f.nativeElement.textContent).toContain('AUTENTICIDAD NO VERIFICADA');
    expect(f.nativeElement.querySelectorAll('svg path').length).toBe(1);
    expect(f.componentInstance.selectedFrame()?.frameIndex).toBe(0);
  });

  it('clears stale masks after changing the source video but not on language change',async()=>{
    const f=TestBed.createComponent(SamVideoReplay);
    f.componentRef.setInput('videoSha256','aaaa');
    f.detectChanges();
    const input=f.nativeElement.querySelector('input[type=file]') as HTMLInputElement;
    Object.defineProperty(input,'files',{configurable:true,value:[fileFrom(SYNTHETIC_WIRE)]});
    await f.componentInstance.onFile({target:input} as unknown as Event);
    expect(f.componentInstance.result()).not.toBeNull();
    f.componentRef.setInput('language','en');
    f.detectChanges();
    expect(f.componentInstance.result()).not.toBeNull();
    f.componentRef.setInput('videoSha256','bbbb');
    f.detectChanges();
    expect(f.componentInstance.result()).toBeNull();
  });

  it('never displays invented masks for a bad response',async()=>{
    const f=TestBed.createComponent(SamVideoReplay);
    f.detectChanges();
    const input=f.nativeElement.querySelector('input[type=file]') as HTMLInputElement;
    Object.defineProperty(input,'files',{configurable:true,value:[fileFrom('<0f>')]});
    await f.componentInstance.onFile({target:input} as unknown as Event);
    f.detectChanges();
    expect(f.componentInstance.result()).toBeNull();
    expect(f.componentInstance.error()).toContain('SAM_VIDEO_GRAMMAR_REJECTED');
  });
});
