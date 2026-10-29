const path=require('node:path');
const root=path.resolve(__dirname,'..');
require(path.join(root,'ui/node_modules/esbuild')).buildSync({
  entryPoints:[path.join(root,'scripts/sam-period-worker.ts')],
  bundle:true,platform:'node',target:'node22',format:'cjs',minify:false,
  outfile:path.join(root,'src/ProofLab.Api/Workers/sam-period-worker.cjs'),
  logLevel:'warning'
});
