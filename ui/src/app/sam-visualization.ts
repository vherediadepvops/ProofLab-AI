import {
  decodeMaskToRaster,
  decodeMaskToSVGPath,
  parseImageStream,
  recordsOfKind,
  type ResponsesEvent
} from '@meta-sam/parser';

/**
 * Offline, client-only processing of a full Meta Responses API JSON document.
 * There is no network access, token read, or new billable request.
 */
export interface SamVisualObject {
  id: string;
  left: number;
  top: number;
  right: number;
  bottom: number;
  path: string;
  foregroundPixels: number;
}
export interface SamVisualization {
  frameWidth: number;
  frameHeight: number;
  objects: SamVisualObject[];
  diagnostics: number;
  result: 'DECODED_MASK' | 'NO_MATCH';
}

function getOutputText(raw: string): string {
  if (raw.length > 1_000_000) throw new Error('SAM_RESPONSE_TOO_LARGE');
  let document: unknown;
  try { document = JSON.parse(raw); }
  catch { throw new Error('SAM_RESPONSE_INVALID_JSON'); }
  if (!document || typeof document !== 'object')
    throw new Error('SAM_RESPONSE_NOT_AN_OBJECT');
  const response = document as {
    model?: unknown;
    status?: unknown;
    output?: Array<{ content?: Array<{ type?: string; text?: unknown }> }>;
  };
  if (response.model !== 'sam-3.1' || response.status !== 'completed' ||
      !Array.isArray(response.output))
    throw new Error('SAM_RESPONSE_NOT_COMPLETED');
  const text = response.output.flatMap(item =>
    Array.isArray(item.content) ? item.content : [])
    .filter(part => part.type === 'output_text' && typeof part.text === 'string')
    .map(part => part.text as string)
    .join('\n');
  if (text.length > 500_000) throw new Error('SAM_OUTPUT_TOO_LARGE');
  return text;
}

function imageSizeFromWire(text: string): {width: number; height: number} {
  const match = /<\|box;([^|]+)\|>/.exec(text);
  if (!match) return {width: 0, height: 0};
  const dimensions: Record<string, string> = {};
  for (const field of match[1].split(';')) {
    const index = field.indexOf('=');
    if (index > 0) dimensions[field.slice(0,index)] = field.slice(index+1);
  }
  if (!/^\d+$/.test(dimensions['w'] || '') ||
      !/^\d+$/.test(dimensions['h'] || ''))
    throw new Error('SAM_FRAME_SIZE_INVALID');
  const width = Number(dimensions['w']);
  const height = Number(dimensions['h']);
  if (width < 1 || height < 1 || width > 4096 || height > 4096)
    throw new Error('SAM_FRAME_SIZE_UNSAFE');
  return { width, height };
}

export async function decodeSamResponse(raw: string): Promise<SamVisualization> {
  const output = getOutputText(raw);
  const size = imageSizeFromWire(output);

  // Replay the already-returned Responses output as local parser events.
  // This is NOT a second HTTP request to Meta.
  async function* events(): AsyncIterable<ResponsesEvent> {
    yield {
      type:'response.output_text.delta',
      item_id:'local-sam-result', output_index:0, content_index:0,
      delta:output.endsWith('\n') ? output : output + '\n'
    };
    yield {
      type:'response.output_text.done',
      item_id:'local-sam-result', output_index:0, content_index:0,
      text:output.endsWith('\n') ? output : output + '\n'
    };
    yield {type:'response.completed'};
  }
  const parsed = await parseImageStream(events()).finalResult;
  if (parsed.outcome.status !== 'completed' ||
      parsed.diagnostics.some(d => d.severity === 'error'))
    throw new Error('SAM_SEGMENTATION_GRAMMAR_REJECTED');
  const boxes = recordsOfKind(parsed.records,'box');
  const masks = recordsOfKind(parsed.records,'mask');
  if (masks.length > 12 || boxes.length !== masks.length)
    throw new Error('SAM_OBJECT_COUNT_UNSUPPORTED');
  const objects: SamVisualObject[] = [];
  for (const mask of masks) {
    const bounds = mask.bounds;
    const box = boxes.find(b => b.objectId === mask.objectId);
    if (!box || box.left !== bounds.left || box.top !== bounds.top ||
        box.right !== bounds.right || box.bottom !== bounds.bottom)
      throw new Error('SAM_BOX_MASK_MISMATCH');
    if (mask.mask.width !== bounds.right-bounds.left ||
        mask.mask.height !== bounds.bottom-bounds.top ||
        bounds.left < 0 || bounds.top < 0 ||
        bounds.right > size.width || bounds.bottom > size.height)
      throw new Error('SAM_MASK_SIZE_MISMATCH');
    const raster = decodeMaskToRaster(mask.mask);
    const path = decodeMaskToSVGPath(mask.mask);
    if (path.length > 250_000)
      throw new Error('SAM_VECTOR_PATH_TOO_LARGE');
    let foregroundPixels=0;
    for (const value of raster) foregroundPixels += value;
    objects.push({
      id:mask.objectId,
      left:bounds.left, top:bounds.top, right:bounds.right, bottom:bounds.bottom,
      path, foregroundPixels
    });
  }
  return {
    frameWidth:size.width, frameHeight:size.height,
    objects, diagnostics:parsed.diagnostics.length,
    result:objects.length>0?'DECODED_MASK':'NO_MATCH'
  };
}
