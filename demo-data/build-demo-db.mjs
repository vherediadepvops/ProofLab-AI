/**
 * Rebuild a standalone SQLite review database from public curated JSON.
 * NEVER accesses private ProofLab storage or calls AWS/Meta.
 * Node.js 24+ (node:sqlite). No npm packages required.
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DatabaseSync } from 'node:sqlite';

const root = path.dirname(fileURLToPath(import.meta.url));
const dataset = JSON.parse(fs.readFileSync(path.join(root,'pendulum-cases.json'),'utf8'));
const cases = dataset.cases;
if (dataset.schemaVersion !== 'PROOFLAB_PUBLIC_PENDULUM_V1' || !Array.isArray(cases) || cases.length !== 25)
  throw new Error('Unexpected public dataset shape (25 required).');
const allowed = new Set(['WITHIN_PREDECLARED_BAND','OUTSIDE_PREDECLARED_BAND','INCONCLUSIVE']);
const tally = {};
const media = new Map();
for (let i=0; i<cases.length; i++) {
  const c=cases[i], id='PEND-'+String(i+1).padStart(2,'0');
  if(c.caseId!==id || !c.integrityVerified ||
     !c.publicVideos?.baseline?.sha256 || !c.publicVideos?.followup?.sha256 ||
     c.frozenPrediction?.frozenBeforeFollowupUpload!==true ||
     !allowed.has(c.contrast?.recordedVerdict)) throw Error('Invalid case '+id);
  tally[c.contrast.recordedVerdict]=(tally[c.contrast.recordedVerdict]??0)+1;
  for(const v of [c.publicVideos.baseline,c.publicVideos.followup]){
    if(!/^[1-8]0CM_15G_272\.4g\.MOV$/.test(v.file)|| !/^[a-f0-9]{64}$/.test(v.sha256))throw Error('Unexpected public media '+id);
    if(media.has(v.file)&&media.get(v.file).sha256!==v.sha256)throw Error('Media SHA mismatch '+id);
    media.set(v.file,v);
  }
}
if(media.size!==8 || tally.WITHIN_PREDECLARED_BAND!==18 ||
   tally.OUTSIDE_PREDECLARED_BAND!==6 || tally.INCONCLUSIVE!==1 ||
   cases.filter(x=>x.numericComplete).length!==24)throw Error('Unexpected dataset statistics');
if(process.argv[2]==='--verify'){
 console.log('VERIFIED 25 cases, 8 published recordings; 18 within, 6 outside, 1 inconclusive; 24 numeric-complete');
 process.exit(0);
}
const output=path.resolve(process.argv[2]??'prooflab-demo.sqlite');
if(fs.existsSync(output))throw Error('Refusing to overwrite existing file: '+output);
fs.mkdirSync(path.dirname(output),{recursive:true});
const fields=[
 'case_id','baseline_video','followup_video','baseline_sha256','followup_sha256',
 'baseline_length_m','followup_length_m','initial_angle_deg','mass_g',
 'measured_t1_s','predicted_t2_s','predicted_low_s','predicted_high_s','measured_t2_s',
 'tolerance_percent','relative_difference_percent','recorded_verdict','numerical_verdict',
 'baseline_status','followup_status','baseline_motion_status','followup_motion_status',
 'integrity_verified','numeric_complete','frozen_before_second_upload',
 'pivot_independently_verified','total_uncertainty_available'
];
const db=new DatabaseSync(output);
try{
 db.exec('PRAGMA foreign_keys=ON; PRAGMA journal_mode=DELETE;');
 db.exec([
 'CREATE TABLE pendulum_cases (',
 ' case_id TEXT PRIMARY KEY,',
 ' baseline_video TEXT NOT NULL, followup_video TEXT NOT NULL,',
 ' baseline_sha256 TEXT NOT NULL, followup_sha256 TEXT NOT NULL,',
 ' baseline_length_m REAL, followup_length_m REAL, initial_angle_deg REAL, mass_g REAL,',
 ' measured_t1_s REAL, predicted_t2_s REAL, predicted_low_s REAL, predicted_high_s REAL,',
 ' measured_t2_s REAL, tolerance_percent REAL, relative_difference_percent REAL,',
 " recorded_verdict TEXT NOT NULL CHECK(recorded_verdict IN ('WITHIN_PREDECLARED_BAND','OUTSIDE_PREDECLARED_BAND','INCONCLUSIVE')),",
 ' numerical_verdict TEXT, baseline_status TEXT, followup_status TEXT,',
 ' baseline_motion_status TEXT, followup_motion_status TEXT,',
 ' integrity_verified INTEGER NOT NULL CHECK(integrity_verified IN (0,1)),',
 ' numeric_complete INTEGER NOT NULL CHECK(numeric_complete IN (0,1)),',
 ' frozen_before_second_upload INTEGER NOT NULL CHECK(frozen_before_second_upload IN (0,1)),',
 ' pivot_independently_verified INTEGER NOT NULL, total_uncertainty_available INTEGER NOT NULL',
 ');',
 'CREATE TABLE public_recordings (filename TEXT PRIMARY KEY, sha256 TEXT NOT NULL, download_url TEXT NOT NULL);',
 'CREATE TABLE dataset_metadata (name TEXT PRIMARY KEY, value TEXT NOT NULL);'
 ].join('\n'));
 db.exec('BEGIN TRANSACTION');
 const stmt=db.prepare('INSERT INTO pendulum_cases ('+fields.join(',')+') VALUES ('+fields.map(()=>'?').join(',')+')');
 for(const c of cases){
   const row={
    case_id:c.caseId,
    baseline_video:c.publicVideos.baseline.file,followup_video:c.publicVideos.followup.file,
    baseline_sha256:c.publicVideos.baseline.sha256,followup_sha256:c.publicVideos.followup.sha256,
    baseline_length_m:c.parameters.baselineLengthMeters,followup_length_m:c.parameters.followupLengthMeters,
    initial_angle_deg:c.parameters.initialAngleDegrees,mass_g:c.parameters.bobMassGrams,
    measured_t1_s:c.observations.baseline?.periodSeconds,
    predicted_t2_s:c.frozenPrediction.periodSeconds,predicted_low_s:c.frozenPrediction.lowerSeconds,
    predicted_high_s:c.frozenPrediction.upperSeconds,measured_t2_s:c.observations.followup?.periodSeconds,
    tolerance_percent:c.frozenPrediction.tolerancePercent,
    relative_difference_percent:c.contrast.relativeDifferencePercent,
    recorded_verdict:c.contrast.recordedVerdict,numerical_verdict:c.contrast.numericalVerdict,
    baseline_status:c.observations.baseline?.status,followup_status:c.observations.followup?.status,
    baseline_motion_status:c.observations.baseline?.motionStatus,
    followup_motion_status:c.observations.followup?.motionStatus,
    integrity_verified:Number(c.integrityVerified),numeric_complete:Number(c.numericComplete),
    frozen_before_second_upload:Number(c.frozenPrediction.frozenBeforeFollowupUpload),
    pivot_independently_verified:Number(c.review.pivotIndependentlyVerified),
    total_uncertainty_available:Number(c.review.completeUncertaintyBudget)
   };
   stmt.run(...fields.map(k=>row[k]??null));
 }
 const mediaInsert=db.prepare('INSERT INTO public_recordings VALUES (?,?,?)');
 for(const m of media.values()) mediaInsert.run(m.file,m.sha256,m.url);
 const metaInsert=db.prepare('INSERT INTO dataset_metadata VALUES (?,?)');
 for(const [k,v] of Object.entries({
    schema:dataset.schemaVersion,source:dataset.source,extractedOn:dataset.extractedOn,
    observationType:dataset.observationType,privacy:dataset.privacy,
    verdicts:JSON.stringify(tally),limitations:JSON.stringify(dataset.limitations)
 }))metaInsert.run(k,String(v));
 db.exec('COMMIT');
 const actual=db.prepare('SELECT recorded_verdict, COUNT(*) AS n FROM pendulum_cases GROUP BY recorded_verdict ORDER BY recorded_verdict').all();
 if(actual.reduce((n,r)=>n+r.n,0)!==25 || db.prepare('SELECT COUNT(*) AS n FROM public_recordings').get().n!==8)throw Error('Post-import validation failed');
 console.log('CREATED '+output);
 console.log('25 cases imported; 8 public recordings; '+JSON.stringify(actual));
}catch(error){
 try{db.exec('ROLLBACK');}catch{}
 throw error;
}finally{db.close();}
