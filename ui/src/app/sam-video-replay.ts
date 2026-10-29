import {
  Component, OnChanges, OnDestroy, SimpleChanges,
  inject, input, output, signal
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, timeout } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LabConnection } from './lab-connection';
import { LabActivity } from './lab-activity';
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
  analysisId?:string|null; storage?:string; sceneKey?:string|null; recognitionRequired?:boolean;
}
interface SamPreflight {
  sourceSha256:string;sourceFrames:number;durationSeconds:number;
  codec:string;estimatedCostUsd:number;supportsSingleCall:boolean;
  timingSource:string;framePtsSha256:string;
}
interface GroundingScene {key:string;videoSha256:string}
interface GroundingQuote {
  cacheKey:string;videoSha256:string;maximumEstimatedCostUsd:number;
  cacheHit:boolean;priorAttempt:boolean;canRun:boolean;
  mode:SamMode;freshRequired:boolean;protocol:'ASSEMBLY_FULL_VIDEO_GROUNDING_V2';
  status:'NEEDS_LOCALIZATION'|'READY'|'AMBIGUOUS'|'NOT_VISIBLE'|'PRIOR_ATTEMPT';
  scene:GroundingScene|null;
}
interface SamReadiness {
  apiKeyConfigured:boolean; creditsConfirmedByOwner:boolean;
  ownerApprovedPaidRequests:boolean; liveCallsPermitted:boolean;
  usedRequests:number; remainingRequests:number; manualRequestsUnlimited?:boolean;
  manualTokenCallsPermitted?:boolean; paidRequestsSwitchEnabled?:boolean;
}

/**
 * A local read-only/cache control and separate manual JSON parser.
 * Only the backend can upload a video to Meta. Any user-supplied JSON
 * is unverified and cannot drive the physical overlay.
 */
@Component({
  selector:'app-sam-video-replay',
  standalone:true,
  imports:[LabActivity],
  template:`
    <section class="sam-stage3" [attr.aria-busy]="sourcePending() || checking() || processing()">
      <header class="sam-heading">
        <h2>{{es?'Etapa 3 · SAM 3.1':'Stage 3 · SAM 3.1'}}</h2>
        @if(studyId() && videoSha256()) {
          <label class="sam-api-selector">
            <span>{{es?'Modo de análisis':'Analysis mode'}}</span>
            <select class="sam-mode" [value]="mode()" (change)="selectMode($event)" [disabled]="processing() || sending() || connection.busy()">
              <option value="api-meta">API Meta</option>
              <option value="api-meta-cache">{{es?'API Meta + caché':'API Meta + cache'}}</option>
            </select>
          </label>
        }
      </header>
      @if(studyId() && videoSha256()) {
        <div class="sam-actions">
          <div class="sam-quote" aria-live="polite">
            <span class="sam-status" [class.available]="cachedResultAvailable()"><i aria-hidden="true"></i>{{statusLabel()}}</span>
            <div class="sam-price">
              @if(preflight(); as pf) {
                <div class="sam-price-line">
                  <strong [class.paid-cost]="!cachedResultAvailable()" [class.free-cost]="cachedResultAvailable()">{{cachedResultAvailable()?'US$ 0.00':(groundingRequired()?(es?'Hasta US$ ':'Up to US$ '):'US$ ')+costLabel(displayedCost())}}</strong>
                  <small>{{cachedResultAvailable()?(es?'desde caché · sin cobro':'from cache · no charge'):(groundingRequired()?(es?'Máximo autorizado · no es una factura':'Authorized maximum · not an invoice'):(es?'Usará créditos · estimado':'Uses credits · estimate'))}}</small>
                </div>
                @if(!cachedResultAvailable() && groundingRequired()) {
                  <small class="sam-meta-estimate">SAM {{es?'estimado':'estimate'}} US$ {{costLabel(pf.estimatedCostUsd)}} + Spark {{es?'hasta':'up to'}} US$ {{groundingQuote()!.maximumEstimatedCostUsd.toFixed(2)}}</small>
                }
                @if(cachedResultAvailable()) {
                  <small class="sam-meta-estimate">{{es?'Enviar a Meta':'Send to Meta'}}: US$ {{pf.estimatedCostUsd.toFixed(2)}} {{es?'estimados':'estimated'}}</small>
                }
              } @else {
                <strong class="sam-price-pending">—</strong>
                <small>{{checking()?(es?'Estimación pendiente':'Estimate pending'):(es?'Costo no disponible':'Cost unavailable')}}</small>
              }
            </div>
            @if(preflight(); as pf) {
              <span class="sam-video-meta">{{pf.sourceFrames}} {{es?'fotogramas':'frames'}} · {{pf.durationSeconds.toFixed(2)}} s</span>
            }
            <div class="sam-buttons">
              <button type="button" class="sam-refresh" (click)="voidCheck()" [disabled]="checking() || processing() || sending() || connection.busy()">{{es?'Actualizar costo':'Refresh cost'}}</button>
              <button type="button" class="sam-submit" (click)="process()" [disabled]="!canProcess()">{{processing() || connection.busy()?(es?'Procesando…':'Processing…'):(es?'Procesar':'Process')}}</button>
            </div>
          </div>
          @if(checking()) {
            <app-lab-activity class="sam-progress" [label]="es?'Calculando costo…':'Estimating cost…'"
              [detail]="es?'Comprobando fotogramas y caché. No se consumen créditos.':'Checking frames and cache. No credits are used.'"/>
          } @else if(processing()) {
            <app-lab-activity class="sam-progress" [label]="processingLabel()" [detail]="processingDetail()"/>
          }
          @if(!checking() && !processing() && !connection.busy()) {
          <div class="sam-message" [class.warning]="!cachedResultAvailable() && !canProcess() && !checking() && !sending()" role="status">
            @if(error()) {
              <span>{{error()}}</span>

            } @else if(validationState()==='ready') {
              <span>{{es?'Procesado · reproducción disponible.':'Processed · playback available.'}}</span>
            } @else if(!mediaReady()) {
              <span>{{es?'Esperando que termine de cargar el video original.':'Waiting for the original video to finish loading.'}}</span>
            } @else if(cachedResultAvailable()) {
              <span>{{es?'Resultado disponible. Pulsa Procesar para cargarlo sin consumir créditos.':'Saved result available. Press Process to load it without spending credits.'}}</span>
            } @else if(!canProcess()) {
              <span aria-hidden="true">⚠</span>
              <span>{{processBlockedReason()}}</span>
              @if(!tokenEntered()) {<button type="button" class="sam-settings-link" (click)="connection.openSettings()">{{es?'Configurar token':'Configure key'}}</button>}
            } @else {
              <span>{{nextActionDescription()}}</span>
            }
          </div>
          }
          @if(cacheStatus()?.status==='PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION' && !groundingRequired()) {
            <div class="sam-consent"><label><input type="checkbox" [checked]="retryConsent()"
              (change)="retryConsent.set($any($event.target).checked)"/>
              {{es?'Revisé el intento anterior posiblemente facturado y autorizo UN reintento manual.':'I reviewed the previous potentially billed attempt and authorize ONE manual retry.'}}
            </label></div>
          }

        </div>
        <details class="sam-technical">
          <summary>{{es?'Detalles técnicos':'Technical details'}}</summary>
          <div class="sam-detail-body">
            <p>{{mode()==='api-meta-cache'
              ? (es?'Se verifica el mismo video en SQLite. Si hay caché válida se reutiliza; si no, se requiere una solicitud autorizada a Meta.':'SQLite is checked for this exact video. Valid cache is reused; otherwise an authorized Meta request is required.')
              : (es?'Solicita un análisis nuevo a Meta, sujeto a créditos y permisos del servidor.':'Requests a new Meta analysis, subject to credits and server permission.')}}</p>
            @if(preflight(); as pf) {
              <p>{{es?'Estimación completa para enviar a Meta':'Full estimate for sending to Meta'}}: US$ {{displayedCost().toFixed(4)}} · {{pf.codec}}</p>
            }
            @if(cacheStatus(); as s) {
              <p>{{es?'Estado':'Status'}}: <strong>{{s.status}}</strong></p>
              <p class="sam-cache-origin">{{s.storage || '—'}} · {{s.cacheHit===true?'HIT':s.cacheHit===false?'MISS':'—'}}
                @if(s.analysisId) { · {{es?'Análisis':'Analysis'}}: {{s.analysisId}} }
              </p>
              @if(s.status.startsWith('CACHED_META_') && !cachedResultAvailable()) {
                <p>{{es?'Nuevo análisis SAM guardado.':'New SAM analysis saved.'}}</p>
              }
              @if(s.status.includes('RECONCILIATION')) {
                <p class="sam-caution">{{es
                  ? 'Existe un intento previo posiblemente facturado. Se requiere revisión manual antes de otra ejecución; nunca habrá reintento automático.'
                  : 'A prior attempt may have been billed. Manual review is required before another execution; no automatic retry.'}}</p>
              }
            }
            <p>{{tokenEntered()?(es?'Token registrado para esta sesión':'Key registered for this session'):(es?'Token en Configuración':'API key in Settings')}} · 127.0.0.1:{{localApiPort()}}
              <button type="button" class="sam-settings-link" (click)="connection.openSettings()">{{es?'Abrir configuración':'Open settings'}}</button>
            </p>
            <p class="sam-disclaimer">{{es
              ? 'Spark localiza el objeto y SAM lo sigue; ProofLab calcula los vectores y el rastro. Pendientes revisión del objeto, calibración e incertidumbre. Esta pantalla no certifica una conclusión científica.'
              : 'Spark locates the object and SAM tracks it; ProofLab computes vectors and trails. Object review, calibration and uncertainty remain pending. This screen does not certify a scientific conclusion.'}}</p>
          </div>
        </details>
      } @else {
        <div class="sam-actions sam-empty">
          @if(sourcePending()) {
            <app-lab-activity [label]="es?'Preparando estimación…':'Preparing estimate…'"
              [detail]="es?'Esperando los datos del video. El costo aparecerá aquí automáticamente.':'Waiting for video details. The cost will appear here automatically.'"/>
          } @else {
            <div class="sam-empty-caption"><span class="sam-price-pending">—</span>
              {{es?'Selecciona un video para calcular su costo.':'Select a video to estimate its cost.'}}</div>
          }
          <button type="button" class="sam-submit" disabled>{{es?'Procesar':'Process'}}</button>
        </div>
      }

      <details class="sam-offline">
        <summary>{{es?'Inspector JSON · sin evidencia autenticada':
          'JSON inspector · unauthenticated evidence'}}</summary>
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

      @if(!studyId() && error()) {<p role="alert">{{error()}}</p>}
    </section>
  `,
  styles:[`
    :host{display:block;min-width:0}
    .sam-stage3{margin:16px 0;padding:16px;border:1px solid #343b45;border-radius:12px;background:#23262b;color:#e8edf5;font-size:13px}
    .sam-heading{display:flex;justify-content:space-between;align-items:center;gap:16px;margin-bottom:14px}
    .sam-heading h2{margin:0;font-size:14px;font-weight:700}
    .sam-api-selector{display:flex;gap:8px;align-items:center;color:#a8b7c9;font-size:12px}
    .sam-mode{padding:7px 9px;border:1px solid #343e4a;background:#171b22;color:#f1f5fa;border-radius:6px;max-width:185px}
    .sam-actions{border:1px solid #343e4a;border-radius:10px;padding:16px 18px;background:#171b22;overflow-wrap:anywhere}
    .sam-quote{display:flex;align-items:center;gap:20px;flex-wrap:wrap}
    .sam-status{display:inline-flex;align-items:center;gap:7px;border:1px solid #343e4a;border-radius:30px;padding:6px 11px;font-size:11px;color:#acbdce;white-space:nowrap}
    .sam-status i{width:7px;height:7px;border-radius:50%;background:#818a97}.sam-status.available i{background:#5bc6a4}
    .sam-price{display:grid;gap:3px;min-width:0}.sam-price-line{display:flex;align-items:baseline;gap:7px;flex-wrap:wrap}
    .sam-price strong{font-size:25px;font-weight:750;letter-spacing:-.5px;white-space:nowrap}
    .sam-price .paid-cost{color:#ff8e96}.sam-price .free-cost{color:#69dbad}
    .sam-price small,.sam-video-meta{font-size:11px;color:#9cb2ca}.sam-price-pending{color:#9cb2ca}
    .sam-buttons{display:flex;gap:12px;margin-left:auto}
    .sam-actions button{border:1px solid #343e4a;border-radius:6px;background:#14181f;color:#e9f1fa;padding:9px 12px;font:inherit;cursor:pointer}
    .sam-actions button:disabled{cursor:not-allowed;opacity:.6}
    .sam-actions .sam-submit{min-width:112px;padding:11px 20px;border-color:#2c6399;background:#2c6399;font-weight:700}
    .sam-actions .sam-submit:disabled{opacity:.85}
    .sam-message{display:flex;align-items:baseline;flex-wrap:wrap;gap:5px;margin-top:16px;padding-top:13px;border-top:1px solid #343e4a;color:#a9c8da;font-size:12px;line-height:1.55}
    .sam-message.warning,.sam-caution{color:#ffbd52}
    .sam-actions .sam-settings-link,.sam-settings-link{border:0;background:none;padding:0;color:#69b7ff;cursor:pointer;font:inherit;text-align:left}
    .sam-technical,.sam-offline{margin-top:10px;border:1px solid #343e4a;border-radius:8px;background:#171b22;padding:11px 13px}
    summary{cursor:pointer;font-size:12px;font-weight:650}p{font-size:12px;line-height:1.5}
    .sam-detail-body{padding-top:5px;overflow-wrap:anywhere;color:#b6c8db}
    .sam-disclaimer{font-size:11px;color:#96abc2}
    .sam-consent{display:grid;gap:9px;margin-top:14px;padding-top:14px;border-top:1px solid #343e4a;font-size:12px}
    .sam-consent label{display:flex;align-items:flex-start;gap:7px;line-height:1.5}
    .sam-consent input{margin:3px 0 0}.sam-consent .sam-approve{justify-self:start}
    .sam-file{display:block;margin:12px 0}.sam-file input{display:block;margin-top:8px;max-width:100%}
    .sam-masks{display:block;background:#101b2a;width:100%;max-width:560px;max-height:320px;margin-top:12px;border:1px solid #426277}
    .sam-objects p{margin:4px 0}.sam-frame-caption{font-size:11px;color:#9cb2ca}
    .sam-progress{margin-top:16px}.sam-empty{display:flex;align-items:center;gap:20px}
    .sam-empty>app-lab-activity,.sam-empty-caption{flex:1;min-width:0}.sam-empty-caption{display:flex;align-items:center;gap:16px;color:#94abc4;font-size:12px}
    @media(max-width:700px){.sam-heading{align-items:flex-start;flex-wrap:wrap}.sam-stage3{padding:12px}.sam-actions{padding:14px}.sam-quote{gap:12px}.sam-buttons{width:100%;margin-left:0}.sam-buttons button{flex:1}.sam-video-meta{width:100%}.sam-price{flex:1}.sam-price strong{font-size:23px}}
  `]

})
export class SamVideoReplay implements OnChanges, OnDestroy {
  private readonly http=inject(HttpClient,{optional:true});
  readonly connection=inject(LabConnection);
  readonly localApiPort=this.connection.port;
  constructor(){this.connection.changed.pipe(takeUntilDestroyed()).subscribe(()=>{this.resetServerCheck();this.scheduleCheck();});}
  readonly sourcePending=input(false);
  readonly mediaReady=input(true);
  readonly validationState=input<'idle'|'loading'|'ready'|'error'>('idle');
  readonly processingStarted=output<void>();
  readonly processingFailed=output<void>();
  readonly processing=signal(false);
  readonly processingPhase=signal<'grounding'|'sam'|'validating'|null>(null);
  private beginProcessing():void {
    this.processing.set(true);this.processingPhase.set('validating');this.connection.busy.set(true);this.processingStarted.emit();
  }
  private finishProcessing():void {
    this.processing.set(false);this.processingPhase.set(null);this.connection.busy.set(false);
  }
  private failProcessing():void {
    // Clear local activity first so the parent's error state keeps the specific failure message.
    this.finishProcessing();this.processingFailed.emit();
  }
  readonly mode=signal<SamMode>('api-meta-cache');
  private get base():string {
    return 'http://127.0.0.1:'+this.localApiPort()+
      '/api/science/director/pendulum/sessions';
  }
  selectMode(event:Event):void {
    const mode=(event.target as HTMLSelectElement).value;
    if(this.processing() || this.sending() || (mode!=='api-meta' && mode!=='api-meta-cache'))return;
    this.mode.set(mode);
    this.resetServerCheck();
    this.scheduleCheck();
  }
  selectLocalPort(event:Event):void {
    const p=Number((event.target as HTMLSelectElement).value);
    if(this.processing() || this.sending() || (p!==5391 && p!==5393 && p!==5398))return;
    this.connection.setPort(p);
  }
  private resetServerCheck():void {
    this.serial++;
    if(this.processing() && !this.sending())this.finishProcessing();
    else this.processing.set(false);
    this.processingReset.emit();
    this.checking.set(false);
    this.error.set('');
    this.cacheStatus.set(null);
    this.preflight.set(null);
    this.groundingQuote.set(null);
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
  ngOnDestroy():void {this.disposed=true;this.serial++;this.offlineSerial++;if(this.processing() && !this.sending())this.finishProcessing();}
  cachedResultAvailable():boolean {
    const s=this.cacheStatus();
    return this.mode()==='api-meta-cache' && !!s && s.cacheHit===true &&
      s.status==='CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW' &&
      s.sourceSha256===this.videoSha256() && !!s.analysisId;
  }
  canProcess():boolean {
    const quote=this.preflight();
    if(this.sourcePending() || !this.mediaReady() || this.processing() ||
       this.checking() || this.sending() || this.connection.busy() ||
       !quote || quote.sourceSha256!==this.videoSha256() ||
       !Number.isFinite(quote.estimatedCostUsd) || quote.estimatedCostUsd<=0)return false;
    return this.cachedResultAvailable() || (this.canOfferPaidRun() &&
      this.tokenEntered() &&
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
      this.beginProcessing();
      this.openResult(this.cacheStatus()!);
      return;
    }
    // The labeled Process click confirms the displayed cost and transfer for this request.
    this.transferConsent.set(true);this.paidConsent.set(true);
    await this.analyze();
  }
  readonly cacheStatus=signal<SamCacheStatus|null>(null);
  readonly preflight=signal<SamPreflight|null>(null);
  readonly groundingQuote=signal<GroundingQuote|null>(null);
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
  statusLabel():string {
    if(this.processing())return this.es?'Procesando':'Processing';
    if(this.validationState()==='ready')return this.es?'Procesado':'Processed';
    if(this.checking())return this.es?'Comprobando':'Checking';
    if(this.cachedResultAvailable())return this.es?'Caché disponible':'Cache available';
    if(this.cacheStatus()?.status.includes('RECONCILIATION'))return this.es?'Revisar intento':'Review attempt';
    if(this.cacheStatus()?.status.startsWith('CACHED_META_'))return this.es?'Resultado guardado':'Result saved';
    return this.es?'Sin analizar':'Not analyzed';
  }
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
  private groundingUrl():string {
    return this.base+'/'+this.studyId()+'/assembly/'+this.videoRole()+'/grounding';
  }
  private validGrounding(quote:GroundingQuote|null):quote is GroundingQuote {
    return !!quote && quote.videoSha256===this.videoSha256() &&
      /^[a-f0-9]{64}$/i.test(quote.cacheKey) &&
      Number.isFinite(quote.maximumEstimatedCostUsd) && quote.maximumEstimatedCostUsd>0 &&
      quote.maximumEstimatedCostUsd>=.025 && quote.maximumEstimatedCostUsd<=1 &&
      quote.mode===this.mode() && quote.protocol==='ASSEMBLY_FULL_VIDEO_GROUNDING_V2' &&
      typeof quote.freshRequired==='boolean' &&
      ['NEEDS_LOCALIZATION','READY','AMBIGUOUS','NOT_VISIBLE','PRIOR_ATTEMPT'].includes(quote.status) &&
      (quote.status!=='READY' || (!quote.freshRequired && !!quote.scene &&
        /^[a-f0-9]{64}$/i.test(quote.scene.key) && quote.scene.videoSha256===this.videoSha256()));
  }
  groundingRequired():boolean {
    const g=this.groundingQuote();
    return !!g && g.status==='NEEDS_LOCALIZATION' && !g.cacheHit;
  }
  private groundingPermitted():boolean {
    const g=this.groundingQuote();
    return this.validGrounding(g) && (
      (this.mode()==='api-meta-cache' && g.status==='READY' && g.cacheHit && !g.freshRequired) ||
      (this.groundingRequired() && !g.priorAttempt && g.canRun &&
        (this.mode()==='api-meta-cache' || g.freshRequired)));
  }
  processingLabel():string {
    if(this.processingPhase()==='grounding')return this.es?'Spark analizando el video…':'Spark is analyzing the video…';
    if(this.processingPhase()==='sam')return this.es?'SAM siguiendo el objeto…':'SAM is tracking the object…';
    return this.es?'Preparando resultados…':'Preparing results…';
  }
  processingDetail():string {
    if(this.processingPhase()==='grounding')return this.es?
      'Localizando el objeto con el video completo. SAM solo se enviará si la localización es válida.':
      'Locating the object using the complete video. SAM will run only if localization is valid.';
    if(this.processingPhase()==='sam')return this.es?
      'Siguiendo las posiciones indicadas por Spark. Sin reintentos automáticos.':
      'Tracking the positions provided by Spark. No automatic retries.';
    return this.es?'Cargando y validando la evidencia para generar los gráficos.':
      'Loading and validating evidence to generate overlays.';
  }
  canOfferPaidRun():boolean {
    const pf=this.preflight(),ready=this.readiness(),s=this.cacheStatus();
    if(!pf || !ready || !s || !this.groundingPermitted() ||
      (this.groundingRequired() && s.status.includes('RECONCILIATION')))return false;
    // A genuine prior attempt can be reconciled ONCE by the owner.
    // Do NOT use canRunLive for this case: the status endpoint correctly
    // keeps this false to prohibit unapproved automatic retries.
    const first=(s.status==='NOT_ANALYZED_WITH_SAM' ||
      (this.mode()==='api-meta' && s.status==='READY_FOR_FRESH_META_ANALYSIS')) &&
      (s.canRunLive || ready.manualTokenCallsPermitted===true);
    const singleRetry=s.status==='PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION';
    return (first || singleRetry) &&
      s.sourceSha256===this.videoSha256() &&
      pf.sourceSha256===this.videoSha256() &&
      pf.supportsSingleCall &&
      pf.estimatedCostUsd>0 && pf.estimatedCostUsd<=0.50 &&
      (ready.manualTokenCallsPermitted===true || (ready.apiKeyConfigured && ready.liveCallsPermitted)) &&
      ready.creditsConfirmedByOwner && ready.ownerApprovedPaidRequests &&
      (ready.manualRequestsUnlimited===true || ready.remainingRequests>=(this.groundingRequired()?2:1));
  }
  displayedCost():number {
    return (this.preflight()?.estimatedCostUsd ?? 0)+
      (this.groundingRequired()?this.groundingQuote()!.maximumEstimatedCostUsd:0);
  }
  costLabel(value:number):string {return (Math.ceil(value*100-1e-8)/100).toFixed(2);}
  nextActionDescription():string {
    if(this.groundingRequired())return this.es?
      'Al pulsar Procesar autorizas enviar el video completo y sus imágenes de referencia a Spark; después, el video a SAM, hasta el máximo combinado mostrado. Si Spark no localiza el objeto, SAM no se envía.':
      'Pressing Process authorizes the complete video and its reference images to Spark, then the video to SAM, up to the displayed combined maximum. If Spark cannot locate the object, SAM is not sent.';
    return this.es?'Al pulsar Procesar autorizas enviar este video a Meta y usar los créditos del costo mostrado.':
      'Pressing Process authorizes sending this video to Meta and using credits at the displayed cost.';
  }
  processBlockedReason():string {
    const pf=this.preflight(),ready=this.readiness(),s=this.cacheStatus();
    if(!pf)return this.es?'No se pudo calcular el costo. Pulsa Actualizar costo.':'Cost unavailable. Press Refresh cost.';
    if(!pf.supportsSingleCall || pf.estimatedCostUsd>.50)return this.es?
      'Este video supera el límite de una solicitud (US$0,50 estimados).':'This video exceeds the single-request limit (US$0.50 estimated).';
    if(s?.status.includes('RECONCILIATION') &&
      (s.status!=='PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION' || this.groundingRequired()))
      return this.es?'Un intento anterior requiere revisión manual antes de otro envío.':'A previous attempt requires manual review before another upload.';
    const g=this.groundingQuote();
    if(!this.validGrounding(g))return this.es?'No se pudo comprobar el costo de localización. Pulsa Actualizar costo.':
      'Could not check localization cost. Press Refresh cost.';
    if(g.status==='AMBIGUOUS' || g.status==='NOT_VISIBLE')return this.es?
      'Spark no pudo localizar un único objeto visible. Repite la grabación con el objeto y su suspensión claramente visibles; no se enviará a SAM.':
      'Spark could not locate one visible object. Record again with the object and suspension clearly visible; SAM will not be sent.';
    if(g.status==='PRIOR_ATTEMPT' || g.priorAttempt)return this.es?
      'La localización anterior requiere revisar el intento guardado. No se hará otro envío automático.':
      'The earlier localization requires review of the saved attempt. No automatic request will be made.';
    if(!ready)return this.es?'No se pudo verificar el permiso del servidor. Actualiza el costo.':'Could not verify server permission. Refresh the cost.';
    if(ready.paidRequestsSwitchEnabled===false || !ready.creditsConfirmedByOwner || !ready.ownerApprovedPaidRequests)
      return this.es?(this.tokenEntered()?'Token registrado. ':'')+
        'Procesar está bloqueado: falta habilitar créditos y autorización de esta sesión en el servidor.':
        (this.tokenEntered()?'Key registered. ':'')+'Process is blocked: server credit and spending authorization for this session is missing.';
    if(!ready.manualRequestsUnlimited && ready.remainingRequests<=0)return this.es?'Se agotó el cupo de solicitudes autorizadas de esta sesión.':'This session has no authorized requests remaining.';
    if(!ready.manualRequestsUnlimited && ready.remainingRequests<(this.groundingRequired()?2:1))
      return this.es?'El servidor necesita habilitar los envíos de Spark y SAM para este proceso.':
        'The server must permit the Spark and SAM requests for this process.';
    if(!this.tokenEntered())return this.es?'Configura y confirma tu token en Configuración.':'Configure and confirm your key in Settings.';
    if(!this.canOfferPaidRun())return this.es?'El servidor no permite esta solicitud. Actualiza el costo o revisa el estado.':'The server does not allow this request. Refresh the cost or inspect status.';
    return this.es?'Confirma la revisión del intento anterior antes de procesar.':'Confirm review of the previous attempt before processing.';
  }
  voidCheck():void {void this.check();}
  async check():Promise<void> {
    if(!this.http || !this.studyId() || !this.videoSha256() || this.processing() || this.checking() || this.sending())return;
    const serial=++this.serial;
    this.processingReset.emit();
    this.checking.set(true);
    this.error.set('');
    this.cacheStatus.set(null);
    this.preflight.set(null);
    this.groundingQuote.set(null);
    this.readiness.set(null);
    this.transferConsent.set(false);
    this.paidConsent.set(false);
    this.retryConsent.set(false);
    // A read-only check never consumes the key configured in Settings.
    try {
      // Independent read-only requests start together; none sends the token or calls Meta.
      const root='http://127.0.0.1:'+this.localApiPort();
      const [status,pf,access,ground]=await Promise.all([
        firstValueFrom(this.http.get<SamCacheStatus>(this.url()+'/status?mode='+this.mode()).pipe(timeout(15000))),
        firstValueFrom(this.http.get<SamPreflight>(this.url()+'/preflight').pipe(timeout(90000))),
        firstValueFrom(this.http.get<SamReadiness>(root+'/api/meta/readiness').pipe(timeout(10000))).catch(()=>null),
        firstValueFrom(this.http.get<GroundingQuote>(this.groundingUrl()+'/preflight?mode='+this.mode()).pipe(timeout(15000))).catch(()=>null)
      ]);
      if(serial!==this.serial || this.disposed)return;
      if(status.sourceSha256!==this.videoSha256() || pf.sourceSha256!==this.videoSha256() ||
        !Number.isFinite(pf.estimatedCostUsd) || pf.estimatedCostUsd<=0 ||
        !Number.isSafeInteger(pf.sourceFrames) || pf.sourceFrames<=0)throw new Error('INVALID_VIDEO_QUOTE');
      const samHit=this.mode()==='api-meta-cache' && status.cacheHit===true &&
        status.status==='CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW' && !!status.analysisId;
      if(!samHit && !this.validGrounding(ground))throw new Error('INVALID_GROUNDING_QUOTE');
      this.cacheStatus.set(status);this.preflight.set(pf);this.readiness.set(access);
      this.groundingQuote.set(this.validGrounding(ground)?ground:null);
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
    const isRetry=this.cacheStatus()?.status==='PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION';
    if(!this.http || !this.canProcess() || !this.canOfferPaidRun() ||
       !this.tokenEntered() || this.connection.busy() || !this.transferConsent() ||
       !this.paidConsent() || (isRetry && !this.retryConsent()))return;
    // Snapshot the same session credential and consented quote for the whole explicit click.
    const oneTimeToken=this.connection.tokenForManualRequest();
    if(!oneTimeToken)return;
    const samUrl=this.url(),groundUrl=this.groundingUrl(),mode=this.mode(),
      sha=this.videoSha256(),quote=this.preflight()!,ground=this.groundingQuote()!;
    this.processingReset.emit();this.beginProcessing();
    const serial=++this.serial;
    const current=()=>serial===this.serial && !this.disposed && sha===this.videoSha256() &&
      samUrl===this.url() && mode===this.mode();
    this.sending.set(true);this.error.set('');
    try {
      let scene=ground.scene;
      if(this.groundingRequired()) {
        this.processingPhase.set('grounding');
        const located=await firstValueFrom(this.http.post<GroundingQuote>(groundUrl,{
          mode,videoSha256:sha,cacheKey:ground.cacheKey,modelApiToken:oneTimeToken,
          confirmImageTransfer:true,confirmVideoTransfer:true,confirmPaidCall:true,
          maximumEstimatedCostUsd:ground.maximumEstimatedCostUsd
        },{headers:{'X-ProofLab-Manual-Action':'assembly'}}));
        if(!current())return;
        if(!this.validGrounding(located) || located.cacheKey!==ground.cacheKey)
          throw new Error('GROUNDING_RESULT_MISMATCH');
        this.groundingQuote.set(located);
        if(located.status!=='READY' || !located.scene) {
          this.error.set(this.es?
            'Spark no pudo localizar el objeto con suficiente claridad. SAM no se envió. Revisa la grabación antes de otra prueba.':
            'Spark could not locate the object clearly enough. SAM was not sent. Review the recording before another test.');
          this.failProcessing();return;
        }
        scene=located.scene;
        // Scene identity and source timing may have changed while Spark was running.
        const [nextStatus,nextQuote]=await Promise.all([
          firstValueFrom(this.http.get<SamCacheStatus>(samUrl+'/status?mode='+mode).pipe(timeout(15000))),
          firstValueFrom(this.http.get<SamPreflight>(samUrl+'/preflight').pipe(timeout(90000)))
        ]);
        if(!current())return;
        if(nextStatus.sourceSha256!==sha || nextQuote.sourceSha256!==sha ||
          !nextQuote.supportsSingleCall || !Number.isFinite(nextQuote.estimatedCostUsd) ||
          nextQuote.estimatedCostUsd<=0 || nextQuote.estimatedCostUsd>quote.estimatedCostUsd+1e-9)
          throw new Error('POST_GROUNDING_QUOTE_CHANGED');
        if(mode==='api-meta-cache' && nextStatus.cacheHit===true) {
          this.openResult(nextStatus);this.cacheStatus.set(nextStatus);
          this.preflight.set(nextQuote);this.processingPhase.set('validating');return;
        }
        if(nextStatus.sceneKey!==scene.key ||
          (nextStatus.status.includes('RECONCILIATION') &&
            !(isRetry && nextStatus.status==='PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION')))
          throw new Error('POST_GROUNDING_STATE_CHANGED');
        this.cacheStatus.set(nextStatus);this.preflight.set(nextQuote);
      }
      if(!current() || !scene || scene.videoSha256!==sha)throw new Error('GROUNDING_RESULT_MISMATCH');
      this.processingPhase.set('sam');
      const state=await firstValueFrom(this.http.post<SamCacheStatus>(samUrl+'/manual-one-shot',{
        mode,sceneKey:scene.key,modelApiToken:oneTimeToken,videoSha256:sha,
        confirmPrivateVideoTransfer:true,confirmOneBillableSamCall:true,
        maximumEstimatedCostUsd:quote.estimatedCostUsd,
        ownerApprovesOneReconciledRetry:isRetry,
        previousAttemptBillingEvidence:isRetry?SamVideoReplay.reconciledOnce:null
      },{headers:{'X-ProofLab-Manual-Action':'sam-one-shot'}}));
      if(!current())return;
      this.openResult(state);this.cacheStatus.set(state);this.processingPhase.set('validating');
    }catch(e){
      if(current()){
        const changed=e instanceof Error && e.message.startsWith('POST_GROUNDING_');
        const response=e as {status?:number;error?:{error?:unknown}};
        const noInference=this.processingPhase()==='grounding' && response?.status===409 &&
          response.error?.error==='GROUNDING_NO_INFERENCE_SENT_REVIEW_COST';
        this.error.set(noInference?(this.es?
          'Spark no llegó a ejecutar la inferencia. Revisa el presupuesto o la conexión y actualiza el costo; SAM no se envió.':
          'Spark did not execute inference. Review the budget or connection and refresh the cost; SAM was not sent.'):
          changed?(this.es?
          'Spark terminó, pero cambió la cotización o la evidencia. SAM no se envió. Actualiza el costo antes de continuar.':
          'Spark completed, but the quote or evidence changed. SAM was not sent. Refresh the cost before continuing.'):
          this.processingPhase()==='grounding'?(this.es?
          'La localización con Spark no se completó. Puede existir una solicitud facturada. SAM no se envió; revisa el estado antes de otro intento.':
          'Spark localization did not complete. A request might have been billed. SAM was not sent; check status before another attempt.'):
          (this.es?'SAM no se completó. Puede existir una solicitud facturada. Revisa el estado; no reintentes automáticamente.':
          'SAM did not complete. A request might have been billed. Check status; never auto-retry.'));
        this.cacheStatus.set(null);this.groundingQuote.set(null);this.failProcessing();
      }
    }finally{
      if(!this.processing() || this.disposed || !current())this.finishProcessing();
      this.sending.set(false);
      if(serial===this.serial){
        this.transferConsent.set(false);this.paidConsent.set(false);this.retryConsent.set(false);
      }
    }
  }
  ngOnChanges(changes:SimpleChanges):void {
    if(changes['validationState'] && this.processing() &&
       (this.validationState()==='ready' || this.validationState()==='error')) {
      this.finishProcessing();
      if(this.validationState()==='error')this.error.set(this.es?
        'No se pudo validar el resultado. Actualiza el costo y revisa el estado antes de volver a procesar.':
        'Could not validate the result. Refresh the cost and inspect status before processing again.');
    }
    if(!changes['videoSha256'] && !changes['studyId'] &&
       !changes['videoRole'])return;
    this.resetServerCheck();
    this.offlineSerial++;
    this.result.set(null);
    this.cacheStatus.set(null);
    this.preflight.set(null);
    this.groundingQuote.set(null);
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
