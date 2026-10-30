import {readFileSync} from 'node:fs';
import {decodeMaskToRaster} from '../ui/node_modules/@meta-sam/parser/dist/mask-codec.js';
// Local official SAM decoder only: no network, tokens, uploads or model inference.
function raster(record:string) {
  const b=/^<\|box;x1=(\d+);y1=(\d+);x2=(\d+);y2=(\d+);w=(\d+);h=(\d+)(?:;[^|]*)?\|>/.exec(record);
  const m=/<\|mask;x=0;y=0;data=(\d+),(\d+),([^|]+)\|>$/.exec(record);
  if(!b||!m)throw Error('MASK_FORMAT');
  const [left,top,right,bottom,width,height]=b.slice(1).map(Number);
  const rows=+m[1],cols=+m[2];
  if(width<1||height<1||width>8192||height>8192||rows<1||cols<1||right<left||bottom<top||
     right>=width||bottom>=height||rows*cols>16_000_000||(right-left+1)*(bottom-top+1)>16_000_000)
    throw Error('MASK_DIMENSIONS');
  const pixels=decodeMaskToRaster({width:cols,height:rows,payload:m[3],
    encoding:m[3][0]==='~'?'lossless':'one_bit'});
  const set=new Set<number>();
  // Compare both masks in original image coordinates, including reduced rasters.
  const bw=right-left+1,bh=bottom-top+1;
  for(let y=0;y<bh;y++)for(let x=0;x<bw;x++){
    const sx=Math.min(cols-1,Math.floor((x+.5)*cols/bw));
    const sy=Math.min(rows-1,Math.floor((y+.5)*rows/bh));
    if(pixels[sy*cols+sx])set.add((top+y)*width+left+x);
  }
  return {set,width,height};
}
try {
  const raw=readFileSync(0,'utf8');if(raw.length>32_000_000)throw Error('LIMIT');
  const pairs=JSON.parse(raw);if(!Array.isArray(pairs)||pairs.length>500)throw Error('LIMIT');
  const metrics=pairs.map(({Left,Right}:{Left:string;Right:string})=>{
    const a=raster(Left),b=raster(Right);
    if(a.width!==b.width||a.height!==b.height)throw Error('SOURCE_SIZE');
    let intersection=0;for(const p of a.set)if(b.set.has(p))intersection++;
    const union=a.set.size+b.set.size-intersection;
    return union?intersection/union:0;
  });
  process.stdout.write(JSON.stringify(metrics));
}catch{process.stderr.write('SAM_SEAM_MASK_VERIFICATION_FAILED');process.exitCode=1;}
