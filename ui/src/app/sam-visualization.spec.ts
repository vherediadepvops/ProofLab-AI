import { decodeSamResponse } from './sam-visualization';

// A published, entirely synthetic Meta-SAM parser conformance example.
// No private media and no API key or live provider call.
const SYNTHETIC_WIRE =
  '<0f>0<|box;x1=211;y1=228;x2=270;y2=254;w=320;h=334|>' +
  "<|mask;x=0;y=0;data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7\`e.ioeAf6-k/#Xd8%dX9x(|>";

export const SYNTHETIC_SAM_RESPONSE = JSON.stringify({
  object: 'response', model:'sam-3.1', status:'completed',
  output:[{type:'message',content:[{type:'output_text',text:SYNTHETIC_WIRE}]}]
});

describe('SAM 3.1 offline decoding', () => {
  it('decodes an official synthetic lossless mask without calling Meta', async () => {
    const data = await decodeSamResponse(SYNTHETIC_SAM_RESPONSE);
    expect(data.result).toBe('DECODED_MASK');
    expect(data.frameWidth).toBe(320);
    expect(data.frameHeight).toBe(334);
    expect(data.objects.length).toBe(1);
    expect(data.objects[0].id).toBe('0');
    expect(data.objects[0].left).toBe(211);
    expect(data.objects[0].top).toBe(228);
    expect(data.objects[0].right).toBe(271);
    expect(data.objects[0].bottom).toBe(255);
    expect(data.objects[0].foregroundPixels).toBeGreaterThan(0);
    expect(data.objects[0].path.startsWith('M')).toBeTrue();
  });

  it('rejects invalid or incomplete SAM response JSON', async () => {
    await expectAsync(decodeSamResponse('not JSON')).toBeRejected();
    await expectAsync(decodeSamResponse(JSON.stringify({
      model:'sam-3.1',status:'failed',output:[]}))).toBeRejected();
    await expectAsync(decodeSamResponse(JSON.stringify({
      model:'unverified-llm',status:'completed',output:[]}))).toBeRejected();
  });

  it('does not report a fabricated mask for an empty response', async () => {
    const data = await decodeSamResponse(JSON.stringify({
      model:'sam-3.1',status:'completed',
      output:[{content:[{type:'output_text',text:''}]}]
    }));
    expect(data.result).toBe('NO_MATCH');
    expect(data.objects).toEqual([]);
  });
});
