import { decodeSamVideoResponse } from './sam-video-evidence';

// This parser conformance fixture is SYNTHETIC, not the owner's MOV or a live SAM result.
const SYNTHETIC_WIRE =
  '<0f>0<|box;x1=211;y1=228;x2=270;y2=254;w=320;h=334|>' +
  "<|mask;x=0;y=0;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7\`e.ioeAf6-k/#Xd8%dX9x(|>";

function response(wire:string,model='sam-3.1',status='completed'):string {
  return JSON.stringify({
    object:'response',model,status,
    output:[{type:'message',content:[{type:'output_text',text:wire}]}]
  });
}

describe('Stage 3: offline SAM 3.1 video evidence',()=>{
  it('decodes a mask centroid and retains frame and object identity',async()=>{
    const raw=response(SYNTHETIC_WIRE.replace('<0f>','<17f>'));
    const result=await decodeSamVideoResponse(raw);
    expect(result.result).toBe('DECODED_MASKS');
    expect(result.source).toBe('USER_SUPPLIED_RESPONSE_UNVERIFIED');
    expect(result.frameWidth).toBe(320);
    expect(result.frameHeight).toBe(334);
    expect(result.objectIds).toEqual(['0']);
    expect(result.detectionCount).toBe(1);
    const d=result.frames[0].detections[0];
    expect(d.frameIndex).toBe(17);
    expect(d.left).toBe(211);
    expect(d.right).toBe(271);
    expect(d.foregroundPixels).toBeGreaterThan(0);
    expect(d.centroidXpx!).toBeGreaterThanOrEqual(d.left);
    expect(d.centroidXpx!).toBeLessThan(d.right);
    expect(d.centroidYpx!).toBeGreaterThanOrEqual(d.top);
    expect(d.centroidYpx!).toBeLessThan(d.bottom);
    expect(d.maskPath.startsWith('M')).toBeTrue();
    expect(result.frames[0].timeSeconds).toBeNull(); // never infer video PTS from frame index
  });

  it('handles out-of-order video frames and gaps without fabricating frames',async()=>{
    const frame7=SYNTHETIC_WIRE.replace('<0f>','<7f>');
    const frame2=SYNTHETIC_WIRE.replace('<0f>','<2f>');
    const result=await decodeSamVideoResponse(response(frame7+'\n'+frame2));
    expect(result.frames.map(f=>f.frameIndex)).toEqual([2,7]);
    expect(result.frames.length).toBe(2);
    expect(result.objectIds).toEqual(['0']);
    expect(result.detectionCount).toBe(2);
  });

  it('retains different object IDs rather than reassigning them by slot',async()=>{
    const second=SYNTHETIC_WIRE.replace('<0f>','<4f>')
      .replace('>0<|box','>12<|box');
    const result=await decodeSamVideoResponse(response(SYNTHETIC_WIRE+'\n'+second));
    expect(result.objectIds).toEqual(['0','12']);
    expect(result.frames[1].detections[0].objectId).toBe('12');
  });

  it('rejects duplicate object/frame records rather than merging masks',async()=>{
    await expectAsync(decodeSamVideoResponse(
      response(SYNTHETIC_WIRE+'\n'+SYNTHETIC_WIRE)
    )).toBeRejected();
  });

  it('rejects inconsistent source frame sizes',async()=>{
    const second=SYNTHETIC_WIRE.replace('<0f>','<1f>')
      .replace('w=320;h=334','w=321;h=334');
    await expectAsync(decodeSamVideoResponse(
      response(SYNTHETIC_WIRE+'\n'+second)
    )).toBeRejectedWithError('SAM_VIDEO_INCONSISTENT_FRAME_DIMENSIONS');
  });

  it('rejects misleading responses and malformed data',async()=>{
    await expectAsync(decodeSamVideoResponse('x')).toBeRejectedWithError('SAM_VIDEO_INVALID_JSON');
    await expectAsync(decodeSamVideoResponse(response(SYNTHETIC_WIRE,'not-sam')))
      .toBeRejectedWithError('SAM_VIDEO_NOT_COMPLETED');
    await expectAsync(decodeSamVideoResponse(response(SYNTHETIC_WIRE,'sam-3.1','failed')))
      .toBeRejectedWithError('SAM_VIDEO_NOT_COMPLETED');
    await expectAsync(decodeSamVideoResponse(response('<5f>')))
      .toBeRejectedWithError('SAM_VIDEO_GRAMMAR_REJECTED');
  });

  it('preserves valid zero-detection responses as no match',async()=>{
    const result=await decodeSamVideoResponse(response(''));
    expect(result.result).toBe('NO_MATCH');
    expect(result.frames).toEqual([]);
    expect(result.detectionCount).toBe(0);
    expect(result.objectIds).toEqual([]);
  });
});
