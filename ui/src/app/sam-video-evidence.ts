import {
  decodeMaskToRaster, decodeMaskToSVGPath, frameIndexOf,
  parseVideoStream, recordsOfKind, type ResponsesEvent
} from '@meta-sam/parser';

/**
 * ETAPA 3: reproducción LOCAL de una respuesta SAM 3.1 ya recibida.
 *
 * No hay solicitudes HTTP ni inferencia. Un JSON pegado por el usuario NO
 * demuestra autenticidad del proveedor, ni el centroide prueba identidad
 * de la plomada. El índice del frame NO es un timestamp PTS.
 */
export interface SamVideoDetection {
  frameIndex: number;
  objectId: string;
  left: number;
  top: number;
  right: number; // exclusive; SAM wire x2/y2 were inclusive
  bottom: number;
  centroidXpx: number | null;
  centroidYpx: number | null;
  foregroundPixels: number;
  maskPath: string;
  boxConfidence: number | null;
  maskConfidence: number | null;
}

export interface SamVideoFrame {
  frameIndex: number;
  /** Unknown until the exact uploaded clip and its frame PTS are verified. */
  timeSeconds: null;
  detections: SamVideoDetection[];
}

export interface SamVideoEvidence {
  model: 'sam-3.1';
  source: 'USER_SUPPLIED_RESPONSE_UNVERIFIED';
  result: 'DECODED_MASKS' | 'NO_MATCH';
  frameWidth: number;
  frameHeight: number;
  frames: SamVideoFrame[];
  objectIds: string[];
  detectionCount: number;
  warningCount: number;
}

const MAX_JSON_CHARS = 90_000_000;
const MAX_OUTPUT_CHARS = 85_000_000;
const MAX_MASK_PIXELS = 200_000_000;
const MAX_VECTOR_CHARS = 50_000_000;
const MAX_DETECTIONS = 3000;

/** Only accept an explicitly completed SAM response; no guessed model output. */
function completedOutput(raw: string): string {
  if (raw.length > MAX_JSON_CHARS) throw new Error('SAM_VIDEO_RESPONSE_TOO_LARGE');
  let value: unknown;
  try { value = JSON.parse(raw); }
  catch { throw new Error('SAM_VIDEO_INVALID_JSON'); }
  if (!value || typeof value !== 'object') throw new Error('SAM_VIDEO_INVALID_RESPONSE');
  const response = value as {
    model?: unknown; status?: unknown;
    output?: Array<{content?: Array<{type?: unknown; text?: unknown}>}>;
  };
  if (response.model !== 'sam-3.1' || response.status !== 'completed' ||
      !Array.isArray(response.output))
    throw new Error('SAM_VIDEO_NOT_COMPLETED');
  const texts: string[] = [];
  for (const item of response.output) {
    if (!Array.isArray(item?.content)) continue;
    for (const part of item.content) {
      if (part?.type === 'output_text' && typeof part.text === 'string')
        texts.push(part.text);
    }
  }
  const output = texts.join('\n');
  if (output.length > MAX_OUTPUT_CHARS) throw new Error('SAM_VIDEO_OUTPUT_TOO_LARGE');
  return output;
}

function sizeFromBoxes(text: string): { width:number; height:number } {
  let width=0,height=0;
  for (const match of text.matchAll(/<\|box;([^|]+)\|>/g)) {
    const fields = new Map<string,string>();
    for (const entry of match[1].split(';')) {
      const equals=entry.indexOf('=');
      if (equals>0) fields.set(entry.slice(0,equals).trim(),entry.slice(equals+1).trim());
    }
    if (!/^\d+$/.test(fields.get('w') ?? '') ||
        !/^\d+$/.test(fields.get('h') ?? ''))
      throw new Error('SAM_VIDEO_FRAME_SIZE_INVALID');
    const w=Number(fields.get('w')),h=Number(fields.get('h'));
    if (w<1 || h<1 || w>4096 || h>4096)
      throw new Error('SAM_VIDEO_FRAME_SIZE_UNSAFE');
    if (width && (w!==width || h!==height))
      throw new Error('SAM_VIDEO_INCONSISTENT_FRAME_DIMENSIONS');
    width=w;height=h;
  }
  return {width,height};
}

export async function decodeSamVideoResponse(raw: string): Promise<SamVideoEvidence> {
  const output=completedOutput(raw);
  const size=sizeFromBoxes(output);
  async function* events():AsyncIterable<ResponsesEvent> {
    const line=output.endsWith('\n') ? output : output+'\n';
    yield {
      type:'response.output_text.delta', item_id:'local-video-replay',
      output_index:0, content_index:0, delta:line
    };
    yield {
      type:'response.output_text.done', item_id:'local-video-replay',
      output_index:0, content_index:0, text:line
    };
    yield {type:'response.completed'};
  }
  const parsed=await parseVideoStream(events()).finalResult;
  if (parsed.outcome.status !== 'completed' ||
      parsed.diagnostics.some(d=>d.severity==='error'))
    throw new Error('SAM_VIDEO_GRAMMAR_REJECTED');

  const boxes=recordsOfKind(parsed.records,'box');
  const masks=recordsOfKind(parsed.records,'mask');
  if (boxes.length!==masks.length || boxes.length>MAX_DETECTIONS)
    throw new Error('SAM_VIDEO_RECORD_LIMIT_OR_MISMATCH');
  const boxByKey=new Map<string,(typeof boxes)[number]>();
  for (const box of boxes) {
    const frame=frameIndexOf(box);
    if (frame===undefined || !Number.isSafeInteger(frame) || frame<0 || frame>=15000)
      throw new Error('SAM_VIDEO_FRAME_INDEX_INVALID');
    const key=frame+':'+box.objectId;
    if (boxByKey.has(key)) throw new Error('SAM_VIDEO_DUPLICATE_OBJECT_IN_FRAME');
    boxByKey.set(key,box);
  }
  const frameMap=new Map<number,SamVideoDetection[]>();
  const ids=new Set<string>();
  let decodedPixels=0,vectorChars=0;
  for (const mask of masks) {
    const frame=frameIndexOf(mask);
    if (frame===undefined || !Number.isSafeInteger(frame) || frame<0 || frame>=15000)
      throw new Error('SAM_VIDEO_FRAME_INDEX_INVALID');
    const key=frame+':'+mask.objectId;
    const box=boxByKey.get(key);
    const b=mask.bounds;
    if (!box || b.left!==box.left || b.top!==box.top ||
        b.right!==box.right || b.bottom!==box.bottom)
      throw new Error('SAM_VIDEO_BOX_MASK_MISMATCH');
    if (mask.mask.width!==b.right-b.left || mask.mask.height!==b.bottom-b.top ||
        b.left<0 || b.top<0 || b.right>size.width || b.bottom>size.height)
      throw new Error('SAM_VIDEO_MASK_BOUNDS_INVALID');
    decodedPixels+=mask.mask.width*mask.mask.height;
    if (decodedPixels>MAX_MASK_PIXELS) throw new Error('SAM_VIDEO_MASK_BUDGET_EXCEEDED');
    const raster=decodeMaskToRaster(mask.mask);
    let pixels=0, sx=0, sy=0;
    for (let i=0;i<raster.length;i++) {
      if (!raster[i]) continue;
      pixels++;
      sx+=(i%mask.mask.width)+0.5;
      sy+=Math.floor(i/mask.mask.width)+0.5;
    }
    const path=decodeMaskToSVGPath(mask.mask);
    vectorChars+=path.length;
    if (vectorChars>MAX_VECTOR_CHARS)
      throw new Error('SAM_VIDEO_VECTOR_BUDGET_EXCEEDED');
    const detection:SamVideoDetection={
      frameIndex:frame,objectId:mask.objectId,
      left:b.left,top:b.top,right:b.right,bottom:b.bottom,
      centroidXpx:pixels?b.left+sx/pixels:null,
      centroidYpx:pixels?b.top+sy/pixels:null,
      foregroundPixels:pixels,maskPath:path,
      boxConfidence:box.confidence??null,maskConfidence:mask.confidence??null
    };
    ids.add(mask.objectId);
    frameMap.set(frame,[...(frameMap.get(frame) ?? []),detection]);
    boxByKey.delete(key);
  }
  if (boxByKey.size) throw new Error('SAM_VIDEO_ORPHAN_BOX');
  const frames=[...frameMap.entries()].sort(([a],[b])=>a-b)
    .map(([frameIndex,detections]):SamVideoFrame=>({
      frameIndex,timeSeconds:null,detections
    }));
  if (frames.some(x=>x.detections.length>16))
    throw new Error('SAM_VIDEO_TOO_MANY_OBJECTS_PER_FRAME');
  return {
    model:'sam-3.1',source:'USER_SUPPLIED_RESPONSE_UNVERIFIED',
    result:masks.length?'DECODED_MASKS':'NO_MATCH',
    frameWidth:size.width,frameHeight:size.height,
    frames,objectIds:[...ids].sort(),detectionCount:masks.length,
    warningCount:parsed.diagnostics.length
  };
}
