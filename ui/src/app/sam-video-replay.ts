import {
  Component, OnChanges, OnDestroy, SimpleChanges,
  inject, input, output, signal
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LabConnection } from './lab-connection';
import {
  decodeSamVideoResponse, type SamVideoEvidence, type SamVideoFrame
} from './sam-video-evidence';

export interface SamProcessingSelection {
  studyId:string; videoRole:'baseline'|'followup'; sourceSha256:string;
  analysisId:string; port:5391|5393|5398;
}

type SamMode='api-meta'|'api-meta-cache';
interface SamCacheStatus {
  status:string; sourceSha256:string; cacheKey:string;
  sourceFrames:number; estimatedCostUsd:number; canRunLive:boolean;
  provenance:string; mode?:SamMode; cacheHit?:boolean;
  analysisId?:string|null; storage?:string;
}
interface SamPreflight {
  sourceSha256:string;sourceFrames:number;durationSeconds:number;
  codec:string;estimatedCostUsd:number;supportsSingleCall:boolean;
  timingSource:string;framePtsSha256:string;
}
interface SamReadiness {
  apiKeyConfigured:boolean; creditsConfirmedByOwner:boolean;
  ownerApprovedPaidRequests:boolean; liveCallsPermitted:boolean;
  usedRequests:number; remainingRequests:number;
}

/**
 * A local read-only/cache control and separate manual JSON parser.
 * Only the backend can upload a video to Meta. Any user-supplied JSON
 * is unverified and cannot drive the physical overlay.
 */
@Component({
  selector:'app-sam-video-replay',
  standalone:true,
  template:`
    <details class="sam-stage3" open>
      <summary>{{es ? 'Etapa 3 · SAM 3.1 y evidencia persistente' :
        'Stage 3 · SAM 3.1 and persistent evidence'}}</summary>
      <p>{{es
        ? 'SAM detecta la plomada. ProofLab calcula los vectores y el rastro desde sus máscaras verificadas. Muse Spark interpretará los resultados en la etapa siguiente.'
        : 'SAM detects the pendulum bob. ProofLab computes vectors and trails from verified masks. Muse Spark will interpret the findings in the next stage.'}}</p>

      @if(studyId() && videoSha256()) {
        <div class="sam-actions">
          <label class="sam-api-selector">
            {{es?'Modo de análisis':'Analysis mode'}}
            <select class="sam-mode" [value]="mode()" (change)="selectMode($event)"
              [disabled]="sending()">
              <option value="api-meta">API Meta</option>
              <option value="api-meta-cache">{{es?'API Meta + caché':'API Meta + cache'}}</option>
            </select>
          </label>
          <p class="sam-footnote">{{mode()==='api-meta-cache'
            ? (es?'Busca primero en SQLite. Un HIT reutiliza evidencia verificada sin token ni cobro; un MISS requiere autorización manual para llamar a Meta.':
              'Searches SQLite first. A HIT reuses verified evidence without a key or charge; a MISS requires manual permission to call Meta.')
            : (es?'Solicita un análisis nuevo. Una caché existente no se presenta como una nueva ejecución; requiere autorización manual y créditos.':
              'Requests a new analysis. Existing cache is never presented as a new run; manual permission and credits are required.')}}</p>
          <div class="sam-quote" aria-live="polite">
            @if(preflight(); as pf) {
              <div><span>{{es?'Enviar este video a Meta':'Send this video to Meta'}}</span>
                <strong>US$ {{pf.estimatedCostUsd.toFixed(4)}}</strong>
                <small>{{es?'Costo estimado':'Estimated cost'}} · {{pf.sourceFrames}} {{es?'fotogramas':'frames'}} · {{pf.durationSeconds.toFixed(2)}} s</small>
              </div>
              <div><span>{{es?'Costo de esta operación':'Cost of this operation'}}</span>
                <strong>{{cachedResultAvailable()?'US$ 0.00':'US$ '+pf.estimatedCostUsd.toFixed(4)}}</strong>
                <small>{{cachedResultAvailable()
                  ? (es?'Caché válida del mismo video':'Valid cache for this exact video')
                  : (es?'Nueva solicitud a Meta · estimado':'New Meta request · estimated')}}</small>
              </div>
            } @else {
              <p>{{checking()?(es?'Calculando costo del video…':'Estimating video cost…'):
                (es?'Costo aún no disponible. Actualiza la comprobación.':'Cost is unavailable. Refresh the check.')}}</p>
            }
          </div>
          <p class="sam-footnote">{{es
            ? 'La estimación usa los fotogramas originales; no es una factura. Comprobar el costo no envía el video a Meta.'
            : 'The estimate uses source frames; it is not an invoice. Checking cost does not send video to Meta.'}}</p>
          <button type="button" class="sam-refresh" (click)="voidCheck()" [disabled]="checking() || sending() || connection.busy()">
            {{es?'Actualizar costo y disponibilidad':'Refresh cost and availability'}}
          </button>
          <div class="sam-settings-link">
            <span>{{tokenEntered()?(es?'Token listo para un uso':'Key ready for one use'):(es?'Token en Configuración':'API key in Settings')}} · 127.0.0.1:{{localApiPort()}}</span>
            <button type="button" (click)="connection.openSettings()">{{es?'Abrir configuración':'Open settings'}}</button>
          </div>
          @if(cacheStatus(); as s) {
            <p>{{es?'Estado':'Status'}}: <strong>{{s.status}}</strong></p>
            <p class="sam-cache-origin">{{s.storage || '—'}} ·
              {{s.cacheHit===true ? 'HIT' : s.cacheHit===false ? 'MISS' : (es?'Sin confirmar':'Unconfirmed')}}
              @if(s.analysisId) { · {{es?'Análisis':'Analysis'}}: {{s.analysisId}} }
            </p>
            @if(cachedResultAvailable()) {
              <p class="sam-caution">{{es
                ? 'Este video tiene un resultado guardado válido. Pulsa Procesar para cargarlo sin enviar el video ni consumir créditos.'
                : 'A valid saved result exists for this video. Press Process to load it without uploading or spending credits.'}}</p>
            } @else if(s.status.startsWith('CACHED_META_')) {
              <p>{{es?'Nuevo análisis SAM guardado.':'New SAM analysis saved.'}}</p>
            }
            @if(s.status.includes('RECONCILIATION')) {
              <p class="sam-caution">{{es
                ? 'Existe un intento previo posiblemente facturado. Se requiere revisión manual antes de otra ejecución; nunca habrá reintento automático.'
                : 'A prior attempt may have been billed. Manual review is required before another execution; no automatic retry.'}}</p>
            }
          }
          @if(readiness(); as ready) {
            <p class="sam-footnote">{{es?'Acceso Meta':'Meta access'}}:
              {{ready.liveCallsPermitted && ready.remainingRequests>0
                ? (es?'autorizado en API':'allowed on backend')
                : (es?'bloqueado hasta verificar permisos/créditos':'blocked pending consent/credits')}}</p>
          }
          @if(canOfferPaidRun()) {
            <div class="sam-consent">
              <label><input type="checkbox" [checked]="transferConsent()"
                (change)="transferConsent.set($any($event.target).checked)"/>
                {{es?'Autorizo enviar este video privado a Meta para SAM':
                  'I authorize transferring this private video to Meta for SAM'}}</label>
              @if(cacheStatus()?.status==='PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION') {
                <label><input type="checkbox" [checked]="retryConsent()"
                  (change)="retryConsent.set($any($event.target).checked)"/>
                  {{es?'Revisé la caída anterior (~US$0,37). Autorizo exactamente UN segundo intento, conservando el historial.':
                    'I reviewed the previous failed attempt (~US$0.37). I authorize exactly ONE additional run, preserving the history.'}}</label>
              }
              <button type="button" class="sam-approve" (click)="paidConsent.set(true)"
                [disabled]="sending() || paidConsent()">
                {{paidConsent()
                  ? (es?'Créditos de Meta confirmados para esta ejecución':'Meta credits approved for this attempt')
                  : (es?'Confirmar uso de créditos Meta (hasta US$0,50 estimados)':
                    'Confirm Meta credit usage (up to US$0.50 estimated)')}}
              </button>

            </div>
          }
          <div class="sam-process-row">
            <button type="button" class="sam-submit" (click)="process()" [disabled]="!canProcess()">
              {{sending()?(es?'Procesando…':'Processing…'):(es?'Procesar':'Process')}}
            </button>
            <span>{{cachedResultAvailable()
              ? (es?'Recuperar resultado guardado · sin costo':'Load saved result · no cost')
              : (es?'Enviar una solicitud a Meta':'Send one request to Meta')}}</span>
          </div>
          @if(!cachedResultAvailable() && !checking() && !sending()) {
            <p class="sam-footnote">{{canOfferPaidRun()
              ? (es?'Configura el token y confirma transferencia y créditos para habilitar Procesar.':
                'Configure the key and confirm transfer and credits to enable Process.')
              : (es?'Procesar está bloqueado: revisa autorización del servidor, créditos y estado del video.':
                'Process is blocked: check server permission, credits and video status.')}}</p>
          }
          @if(sending()) {
            <p role="status">{{es
              ? 'Solicitud única en curso. No recargues; si falla, revisa el estado antes de reintentar.'
              : 'Single request in progress. Do not reload; reconcile failures before retrying.'}}</p>
          }
        </div>
      }

      <details class="sam-offline">
        <summary>{{es?'Inspector JSON sin conexión (NO evidencia autenticada)':
          'Offline JSON inspector (NOT authenticated evidence)'}}</summary>
        <p>{{es
          ? 'Un JSON aportado manualmente sirve para ensayar el parser, pero JAMÁS activa el seguimiento científico del péndulo.'
          : 'Manually supplied JSON can test the parser but NEVER drives the pendulum scientific tracking.'}}</p>
        <label class="sam-file">
          {{es ? 'Archivo JSON de respuesta' : 'Response JSON file'}}
          <input type="file" accept=".json,application/json" (change)="onFile($event)" />
        </label>
        @if(loading()) {
          <p role="status">{{es?'Decodificando máscaras…':'Decoding masks…'}}</p>
        }
        @if(result(); as r) {
          <p class="sam-caution">{{es
            ? 'REPRODUCCIÓN DE JSON APORTADO · AUTENTICIDAD NO VERIFICADA · NO VINCULADO AL SHA DEL VIDEO · SIN TIMESTAMPS PTS.'
            : 'USER-SUPPLIED JSON REPLAY · AUTHENTICITY NOT VERIFIED · NOT LINKED TO VIDEO SHA · NO FRAME PTS.'}}</p>
          <p>{{es?'Frames con máscaras':'Frames with masks'}}: {{r.frames.length}} ·
             {{es?'Detecciones':'Detections'}}: {{r.detectionCount}} ·
             {{es?'IDs':'IDs'}}: {{r.objectIds.join(', ') || '—'}} ·
             {{r.frameWidth}} × {{r.frameHeight}} px</p>
          @if(r.frames.length) {
            <label>{{es?'Fotograma de SAM':'SAM frame'}}
              <select [value]="selectedIndex()" (change)="selectFrame($event)">
                @for(frame of r.frames; track frame.frameIndex; let i=$index) {
                  <option [value]="i">{{frame.frameIndex}}</option>
                }
              </select>
            </label>
            @if(selectedFrame(); as frame) {
              <svg class="sam-masks" [attr.viewBox]="'0 0 '+r.frameWidth+' '+r.frameHeight"
                role="img" [attr.aria-label]="es?'Máscaras decodificadas, NO fotograma del video':'Decoded masks, NOT a source video frame'">
                <rect x="0" y="0" [attr.width]="r.frameWidth" [attr.height]="r.frameHeight" fill="#101b2a"/>
                @for(d of frame.detections; track d.objectId) {
                  <path [attr.d]="d.maskPath"
                    [attr.transform]="'translate('+d.left+' '+d.top+')'"
                    fill="rgba(15,225,180,.48)" stroke="#42ffe0" stroke-width=".6"/>
                  <rect [attr.x]="d.left" [attr.y]="d.top"
                    [attr.width]="d.right-d.left" [attr.height]="d.bottom-d.top"
                    fill="none" stroke="#f7dc6f" stroke-width=".7"/>
                  @if(d.centroidXpx!==null && d.centroidYpx!==null) {
                    <circle [attr.cx]="d.centroidXpx" [attr.cy]="d.centroidYpx"
                      r="2.2" fill="#fff"/>
                  }
                }
              </svg>
              <div class="sam-frame-caption">{{es?'Solo máscara sobre fondo neutro, NO el video original.':
                'Mask on neutral background only, NOT the original footage.'}}</div>
              <div class="sam-objects">
                @for(d of frame.detections; track d.objectId) {
                  <p>ID {{d.objectId}} ·
                    {{es?'Centroide':'Centroid'}}:
                    ({{numberOrDash(d.centroidXpx)}}, {{numberOrDash(d.centroidYpx)}}) px ·
                    {{es?'Píxeles de máscara':'Mask pixels'}}: {{d.foregroundPixels}} ·
                    {{es?'Confianza':'Confidence'}}: {{numberOrDash(d.boxConfidence)}}</p>
                }
              </div>
            }
          } @else {
            <p>{{es?'SAM no encontró objetos. No se inventan máscaras.':'SAM returned no objects. No masks are fabricated.'}}</p>
          }
        }
      </details>

      @if(error()) {<p role="alert">{{error()}}</p>}
      <p class="sam-disclaimer">{{es
        ? 'Pendiente validación de plomada, incertidumbre y calibración. SAM detecta; ProofLab calcula; Muse interpreta. Esta pantalla no certifica una conclusión científica.'
        : 'Bob identity, uncertainty and calibration remain pending. SAM detects; ProofLab computes; Muse interprets. This viewer does not certify scientific conclusions.'}}</p>
    </details>
  `,
  styles:[`
    .sam-stage3{margin:16px 0;padding:16px;border:1px solid #3a5265;border-radius:12px;background:#13202b;color:#f2f6fc}
    .sam-stage3 summary{cursor:pointer;font-weight:700}
    .sam-stage3 p{line-height:1.45;font-size:.86rem}
    .sam-file{display:block;margin:12px 0}
    .sam-file input{display:block;margin-top:8px;max-width:100%}
    .sam-actions{overflow-wrap:anywhere;border:1px solid #375766;border-radius:8px;padding:12px;margin:12px 0}
    .sam-actions button{border:1px solid #74adba;border-radius:6px;background:#204251;color:white;padding:8px;cursor:pointer}
    .sam-actions button:disabled{opacity:.53;cursor:not-allowed}
    .sam-api-selector{display:flex;gap:10px;align-items:center;flex-wrap:wrap;
      font-weight:600;font-size:.86rem;margin:8px 0}
    .sam-api-selector select{padding:6px;border:1px solid #72899b;
      background:#183040;color:white;border-radius:6px}
    .sam-settings-link{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;margin:14px 0;font-size:12px;color:#acbecd}
    .sam-approve{margin:6px 0}
    .sam-submit{font-weight:700}
    .sam-actions .sam-submit{min-width:170px;padding:12px 24px;background:#459ff0;border-color:#459ff0;color:#07131f;font-size:1rem}
    .sam-process-row{display:flex;align-items:center;gap:16px;flex-wrap:wrap;margin-top:18px}
    .sam-process-row span{font-size:.82rem;color:#bacbdb}
    .sam-quote{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px;margin:18px 0}
    .sam-quote>div{display:grid;gap:7px;padding:18px;background:#181d25;border:1px solid #405368;border-radius:10px}
    .sam-quote strong{font-size:1.7rem;color:#f4f8ff}.sam-quote small{color:#b9cadb}
    @media(max-width:620px){.sam-quote{grid-template-columns:1fr}}
    .sam-consent{padding:12px;margin-top:8px;border:1px dashed #779;display:grid;gap:8px}
    .sam-consent label{font-size:.86rem}
    .sam-caution{color:#f4c875;font-weight:700}
    .sam-footnote{color:#c9d4df}
    .sam-offline,.sam-connection{margin-top:12px}
    .sam-cache-origin{overflow-wrap:anywhere}
    .sam-stage3 select{margin-left:8px}
    .sam-masks{display:block;background:#101b2a;width:100%;max-width:560px;max-height:320px;margin-top:12px;border:1px solid #426277}
    .sam-objects p{margin:4px 0}
    .sam-frame-caption,.sam-disclaimer{font-size:.8rem;color:#c2cdd9}
  `]
})
export class SamVideoReplay implements OnChanges, OnDestroy {
  private readonly http=inject(HttpClient,{optional:true});
  readonly connection=inject(LabConnection);
  readonly localApiPort=this.connection.port;
  constructor(){this.connection.changed.pipe(takeUntilDestroyed()).subscribe(()=>{this.resetServerCheck();this.scheduleCheck();});}
  readonly mode=signal<SamMode>('api-meta-cache');
  private get base():string {
    return 'http://127.0.0.1:'+this.localApiPort()+
      '/api/science/director/pendulum/sessions';
  }
  selectMode(event:Event):void {
    const mode=(event.target as HTMLSelectElement).value;
    if(this.sending() || (mode!=='api-meta' && mode!=='api-meta-cache'))return;
    this.mode.set(mode);
    this.connection.clearToken();
    this.resetServerCheck();
    this.scheduleCheck();
  }
  selectLocalPort(event:Event):void {
    const p=Number((event.target as HTMLSelectElement).value);
    if(this.sending() || (p!==5391 && p!==5393 && p!==5398))return;
    this.connection.setPort(p);
  }
  private resetServerCheck():void {
    this.serial++;
    this.processingReset.emit();
    this.checking.set(false);
    this.error.set('');
    this.cacheStatus.set(null);
    this.preflight.set(null);
    this.readiness.set(null);
    this.transferConsent.set(false);
    this.paidConsent.set(false);
    this.retryConsent.set(false);
  }
  readonly studyId=input('');
  readonly videoRole=input<'baseline'|'followup'>('baseline');
  readonly language=input<'es'|'en'>('es');
  readonly videoSha256=input('');
  readonly autoCheck=input(true);
  readonly cacheUpdated=output<SamProcessingSelection>();
  readonly processingReset=output<void>();
  private disposed=false;
  private scheduleCheck():void {
    const serial=this.serial;
    if(!this.autoCheck())return;
    queueMicrotask(()=>{
      if(!this.disposed && serial===this.serial && this.studyId() && this.videoSha256())
        void this.check();
    });
  }
  ngOnDestroy():void {this.disposed=true;this.serial++;this.offlineSerial++;}
  cachedResultAvailable():boolean {
    const s=this.cacheStatus();
    return this.mode()==='api-meta-cache' && !!s && s.cacheHit===true &&
      s.status==='CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW' &&
      s.sourceSha256===this.videoSha256() && !!s.analysisId;
  }
  canProcess():boolean {
    if(this.checking() || this.sending() || this.connection.busy())return false;
    return this.cachedResultAvailable() || (this.canOfferPaidRun() &&
      this.tokenEntered() && this.transferConsent() && this.paidConsent() &&
      (this.cacheStatus()?.status!=='PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION' ||
        this.retryConsent()));
  }
  private openResult(state:SamCacheStatus):void {
    if(state.sourceSha256!==this.videoSha256() ||
       state.status!=='CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW' ||
       !state.analysisId || (this.mode()==='api-meta' && state.cacheHit!==false))
      throw new Error('SAM_RESULT_DOES_NOT_MATCH_REQUEST');
    this.cacheUpdated.emit({studyId:this.studyId(),videoRole:this.videoRole(),
      sourceSha256:this.videoSha256(),analysisId:state.analysisId,port:this.localApiPort()});
  }
  async process():Promise<void> {
    if(!this.canProcess())return;
    if(this.cachedResultAvailable()) {
      this.processingReset.emit();
      this.openResult(this.cacheStatus()!);
      return;
    }
    await this.analyze();
  }
  readonly cacheStatus=signal<SamCacheStatus|null>(null);
  readonly preflight=signal<SamPreflight|null>(null);
  readonly readiness=signal<SamReadiness|null>(null);
  readonly transferConsent=signal(false);
  readonly paidConsent=signal(false);
  readonly retryConsent=signal(false);
  readonly tokenEntered=this.connection.hasToken;
  private static readonly reconciledOnce =
    'OWNER_APPROVED_ONE_MORE_SAM_ATTEMPT_2026_10_28_AFTER_CREDIT_DELTA_USD_0_37_USAGE_UNVERIFIED';

  readonly checking=signal(false);
  readonly sending=signal(false);
  readonly result=signal<SamVideoEvidence|null>(null);
  readonly loading=signal(false);
  readonly error=signal('');
  readonly selectedIndex=signal(0);
  private serial=0;
  private offlineSerial=0;

  get es():boolean {return this.language()==='es';}
  selectedFrame():SamVideoFrame|null {
    return this.result()?.frames[this.selectedIndex()] ?? null;
  }
  numberOrDash(value:number|null):string {
    return value===null || !Number.isFinite(value)?'—':value.toFixed(2);
  }
  selectFrame(event:Event):void {
    const index=Number((event.target as HTMLSelectElement).value);
    if(Number.isSafeInteger(index) && index>=0 &&
       index<(this.result()?.frames.length ?? 0))
      this.selectedIndex.set(index);
  }
  private url():string {
    return this.base+'/'+this.studyId()+'/sam/'+this.videoRole();
  }
  canOfferPaidRun():boolean {
    const pf=this.preflight(),ready=this.readiness(),s=this.cacheStatus();
    if(!pf || !ready || !s)return false;
    // A genuine prior attempt can be reconciled ONCE by the owner.
    // Do NOT use canRunLive for this case: the status endpoint correctly
    // keeps this false to prohibit unapproved automatic retries.
    const first=(s.status==='NOT_ANALYZED_WITH_SAM' ||
      (this.mode()==='api-meta' && s.status==='READY_FOR_FRESH_META_ANALYSIS')) && s.canRunLive;
    const singleRetry=s.status==='PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION';
    return (first || singleRetry) &&
      s.sourceSha256===this.videoSha256() &&
      pf.sourceSha256===this.videoSha256() &&
      pf.supportsSingleCall &&
      pf.estimatedCostUsd>0 && pf.estimatedCostUsd<=0.50 &&
      ready.apiKeyConfigured && ready.liveCallsPermitted &&
      ready.creditsConfirmedByOwner && ready.ownerApprovedPaidRequests &&
      ready.remainingRequests>0;
  }
  voidCheck():void {void this.check();}
  async check():Promise<void> {
    if(!this.http || this.checking() || this.sending())return;
    const serial=++this.serial;
    this.processingReset.emit();
    this.checking.set(true);
    this.error.set('');
    this.cacheStatus.set(null);
    this.preflight.set(null);
    this.readiness.set(null);
    this.transferConsent.set(false);
    this.paidConsent.set(false);
    this.retryConsent.set(false);
    // A read-only check never consumes the key configured in Settings.
    try {
      const status=await firstValueFrom(this.http.get<SamCacheStatus>(
        this.url()+'/status?mode='+this.mode()));
      if(serial!==this.serial)return;
      if(status.sourceSha256!==this.videoSha256())throw new Error('VIDEO_SHA_MISMATCH');
      this.cacheStatus.set(status);
      const pf=await firstValueFrom(this.http.get<SamPreflight>(this.url()+'/preflight'));
      if(serial!==this.serial)return;
      if(pf.sourceSha256!==this.videoSha256() || !Number.isFinite(pf.estimatedCostUsd) ||
         pf.estimatedCostUsd<=0 || !Number.isSafeInteger(pf.sourceFrames) || pf.sourceFrames<=0)
        throw new Error('INVALID_VIDEO_QUOTE');
      this.preflight.set(pf);
      if(this.cachedResultAvailable())return; // free reuse never depends on billing readiness
      const access=await firstValueFrom(
        this.http.get<SamReadiness>('http://127.0.0.1:'+
          this.localApiPort()+'/api/meta/readiness'));
      if(serial!==this.serial)return;
      this.readiness.set(access);
    } catch(e) {
      if(serial===this.serial)
        this.error.set(this.es?'No se pudo comprobar la evidencia SAM local.':
          'Could not inspect local SAM status.');
    } finally {
      if(serial===this.serial)this.checking.set(false);
    }
  }
  voidAnalyze():void {void this.analyze();}
  async analyze():Promise<void> {
    const isRetry=this.cacheStatus()?.status===
      'PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION';
    if(!this.http || this.checking() || this.sending() || !this.canOfferPaidRun() ||
       !this.tokenEntered() || this.connection.busy() ||
       !this.transferConsent() || !this.paidConsent() ||
       (isRetry && !this.retryConsent()))return;
    // Take and clear the Settings password field only on this authorized click.
    const oneTimeToken=this.connection.takeToken();
    if(!oneTimeToken)return;
    this.processingReset.emit();
    this.connection.busy.set(true);
    const serial=++this.serial;
    this.sending.set(true);
    this.error.set('');
    try {
      const state=await firstValueFrom(this.http.post<SamCacheStatus>(
        this.url()+'/manual-one-shot',{
          mode:this.mode(),
          modelApiToken:oneTimeToken,
          videoSha256:this.videoSha256(),
          confirmPrivateVideoTransfer:true,
          confirmOneBillableSamCall:true,
          maximumEstimatedCostUsd:0.50,
          ownerApprovesOneReconciledRetry:isRetry,
          previousAttemptBillingEvidence:isRetry
            ? SamVideoReplay.reconciledOnce : null
        },{headers:{'X-ProofLab-Manual-Action':'sam-one-shot'}}));
      if(serial!==this.serial)return;
      this.openResult(state); // player verifies this exact analysis before unlocking
      this.cacheStatus.set(state);
    }catch(e){
      if(serial===this.serial){
        this.error.set(this.es?
          'SAM no se completó. Puede existir una solicitud facturada. Revisa el estado; no reintentes automáticamente.':
          'SAM did not complete. A request might have been billed. Check status; never auto-retry.');
        this.cacheStatus.set(null);
      }
    }finally{
      this.connection.busy.set(false);
      if(serial===this.serial){
        this.sending.set(false);
        this.transferConsent.set(false);
        this.paidConsent.set(false);
        this.retryConsent.set(false);
      }
    }
  }
  ngOnChanges(changes:SimpleChanges):void {
    if(!changes['videoSha256'] && !changes['studyId'] &&
       !changes['videoRole'])return;
    this.resetServerCheck();
    this.offlineSerial++;
    this.result.set(null);
    this.cacheStatus.set(null);
    this.preflight.set(null);
    this.readiness.set(null);
    this.transferConsent.set(false);
    this.paidConsent.set(false);
    this.retryConsent.set(false);
    this.error.set('');
    this.selectedIndex.set(0);
    this.loading.set(false);
    this.checking.set(false);
    this.sending.set(false);
    this.scheduleCheck();
  }
  async onFile(event:Event):Promise<void> {
    const control=event.target as HTMLInputElement;
    const file=control.files?.[0];
    if(!file)return;
    control.value='';
    const serial=++this.offlineSerial;
    this.loading.set(false);
    this.result.set(null);
    this.error.set('');
    if(file.size<=0 || file.size>24_000_000 ||
       !file.name.toLowerCase().endsWith('.json')) {
      this.error.set(this.es?'JSON inválido o demasiado grande.':'Invalid or oversized JSON.');
      return;
    }
    this.loading.set(true);
    try {
      const evidence=await decodeSamVideoResponse(await file.text());
      if(serial!==this.offlineSerial)return;
      this.result.set(evidence);
      this.selectedIndex.set(0);
    } catch(e) {
      if(serial===this.offlineSerial)
        this.error.set((this.es?'No se pudo validar el JSON de SAM: ':'SAM JSON could not be validated: ')+
          (e instanceof Error?e.message:'INVALID_RESPONSE'));
    } finally {
      if(serial===this.offlineSerial)this.loading.set(false);
    }
  }
}