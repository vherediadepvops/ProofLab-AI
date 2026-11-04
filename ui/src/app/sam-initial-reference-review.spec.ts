import {TestBed} from '@angular/core/testing';
import {SamInitialReferenceReview,SamInitialReference} from './sam-initial-reference-review';
const pixel='data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="1080" height="1920"/>');
const ref:SamInitialReference={sceneRunId:'a'.repeat(32),videoSha256:'b'.repeat(64),sceneKey:'c'.repeat(64),frame0Sha256:'d'.repeat(64),
  width:1080,height:1920,initialFrameUrl:pixel,clearFrameUrl:pixel,clearFrameIndex:150,suggestedPoint:{frame:0,x:187,y:1643}};
describe('Explicit native body-point picker',()=>{
 beforeEach(async()=>{await TestBed.configureTestingModule({imports:[SamInitialReferenceReview]}).compileComponents();});
 function setup(){const f=TestBed.createComponent(SamInitialReferenceReview);f.componentRef.setInput('reference',ref);f.detectChanges();return {f,c:f.componentInstance};}
 it('never preselects the AI point and requires both images plus an explicit user selection',()=>{
  const {f,c}=setup(),emit=spyOn(c.confirm,'emit');expect(c.point()).toBeNull();c.initialLoaded.set(true);c.clearLoaded.set(true);
  c.submit();expect(emit).not.toHaveBeenCalled();c.key(new KeyboardEvent('keydown',{key:'ArrowRight'}));
  expect(c.point()).toEqual({x:545,y:960});c.clearLoaded.set(false);expect(c.canConfirm()).toBeFalse();c.clearLoaded.set(true);
  c.submit();expect(emit).toHaveBeenCalledOnceWith({x:545,y:960});f.destroy();
 });
 it('maps a pointer from a zoomed view back to integer native coordinates and rejects letterbox clicks',()=>{
  const {f,c}=setup();c.initialLoaded.set(true);const svg=f.nativeElement.querySelector('svg') as SVGSVGElement;
  const matrix=svg.createSVGMatrix();matrix.a=.25;matrix.d=.25;matrix.e=40;matrix.f=20;
  spyOn(svg,'getScreenCTM').and.returnValue(matrix);
  c.select({currentTarget:svg,clientX:91.3,clientY:437.6} as unknown as PointerEvent);expect(c.point()).toEqual({x:205,y:1670});
  c.setZoom(4);expect(c.viewBox()).toBe('70 1430 270 480');
  spyOn(svg,'createSVGPoint').and.callThrough();c.select({currentTarget:svg,clientX:0,clientY:0} as unknown as PointerEvent);
  expect(c.point()).toEqual({x:205,y:1670});f.destroy();
 });
 it('supports keyboard refinement and prevents submitting when busy, unapproved or an image failed',()=>{
  const {f,c}=setup();c.initialLoaded.set(true);c.clearLoaded.set(true);c.key(new KeyboardEvent('keydown',{key:'Enter'}));
  c.key(new KeyboardEvent('keydown',{key:'ArrowDown',shiftKey:true}));expect(c.point()).toEqual({x:540,y:961});
  f.componentRef.setInput('busy',true);f.detectChanges();c.key(new KeyboardEvent('keydown',{key:'ArrowDown'}));expect(c.point()?.y).toBe(961);expect(c.canConfirm()).toBeFalse();
  f.componentRef.setInput('busy',false);f.componentRef.setInput('continueAllowed',false);f.detectChanges();expect(c.canConfirm()).toBeFalse();
  f.componentRef.setInput('continueAllowed',true);f.detectChanges();c.imageFailed(false);expect(c.point()).toBeNull();expect(c.canConfirm()).toBeFalse();f.destroy();
 });
 it('clears user selection and zoom on context replacement and presents English actions',()=>{
  const {f,c}=setup();c.initialLoaded.set(true);c.key(new KeyboardEvent('keydown',{key:'Enter'}));c.setZoom(4);
  f.componentRef.setInput('reference',{...ref,sceneRunId:'f'.repeat(32)});f.componentRef.setInput('language','en');f.detectChanges();
  expect(c.point()).toBeNull();expect(c.zoom()).toBe(1);expect(c.canConfirm()).toBeFalse();
  expect(f.nativeElement.textContent).toContain('Confirm point and continue with SAM');f.destroy();
 });
});
