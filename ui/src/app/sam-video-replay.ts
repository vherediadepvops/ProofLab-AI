import {
  Component, OnChanges, OnDestroy, SimpleChanges,
  inject, input, output, signal
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, timeout } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LabConnection } from './lab-connection';
import type {SuspensionReview} from './sam-playback';
import { LabActivity } from './lab-activity';
import {
  decodeSamVideoResponse, type SamVideoEvidence, type SamVideoFrame
} from './sam-video-evidence';

export interface SamProcessingSelection {
  studyId:string; videoRole:'baseline'|'followup'; sourceSha256:string;
  analysisId:string; port:5391|5393|5398|5410; adaptiveBatchId?:string;
}

type SamMode='api-meta'|'api-meta-cache'|'api-meta-adaptive';
interface SamCacheStatus {
  status:string; sourceSha256:string; cacheKey:string;
  sourceFrames:number; estimatedCostUsd:number; canRunLive:boolean;
  provenance:string; mode?:SamMode; cacheHit?:boolean;
  analysisId?:string|null; storage?:string; sceneKey?:string|null; recognitionRequired?:boolean;
  previousAttemptStatus?:string|null;
}
interface SamPreflight {
  sourceSha256:string;sourceFrames:number;durationSeconds:number;
  codec:string;estimatedCostUsd:number;supportsSingleCall:boolean;
  timingSource:string;framePtsSha256:string;suspensionReviewMaximumCostUsd?:number;
}
interface GroundingScene {key:string;videoSha256:string}
interface GroundingQuote {
  cacheKey:string;videoSha256:string;maximumEstimatedCostUsd:number;
  cacheHit:boolean;priorAttempt:boolean;canRun:boolean;
  mode:SamMode;freshRequired:boolean;protocol:'ASSEMBLY_FULL_VIDEO_GROUNDING_V2';
  status:'NEEDS_LOCALIZATION'|'READY'|'AMBIGUOUS'|'NOT_VISIBLE'|'PRIOR_ATTEMPT';
  scene:GroundingScene|null;
}
interface SuspensionReviewQuote {
  analysisId:string;required:boolean;priorAttempt:boolean;canRun:boolean;
  maximumEstimatedCostUsd:number;reason:string;evidence:SuspensionReview|null;
}
interface SamReadiness {
  apiKeyConfigured:boolean; creditsConfirmedByOwner:boolean;
  ownerApprovedPaidRequests:boolean; liveCallsPermitted:boolean;
  usedRequests:number; remainingRequests:number; manualRequestsUnlimited?:boolean;
  manualTokenCallsPermitted?:boolean; paidRequestsSwitchEnabled?:boolean;
}
interface SamAdaptiveQuote {
  protocol:string;sourceSha256:string;sparkSceneKey:string;sourcePtsSha256:string;
  planSha256:string;suggestedBatchId:string;sourceFrames:number;
  parts:number;simultaneousRequests:number;submittedFrames:number;
  billableFrames:number;totalSamEstimatedUsd:number;maximumAllowedSamUsd:number;
  remainingTrialSlots:number|null;canRunWithManualToken:boolean;
  existingSparkSceneVerified:boolean;requiresNewSparkCall:boolean;
  idealPartsWithoutSparkCoverage:number;verifiedSparkSeedCount:number;
  reducedForSparseSparkSeeds:boolean;recoveryFromBatchId?:string|null;
}
interface SamAdaptivePartProgress {
  part:number;state:string;events:number;deltas:number;outputChars:number;elapsedMs?:number;
}
interface SamAdaptiveProgress {
  batchId:string;parts:number;started:number;completed:number;failed:number;
  perPart:SamAdaptivePartProgress[];
  phase?:string;preparationElapsedMs?:number;validationElapsedMs?:number;totalElapsedMs?:number;
}
interface SamAdaptiveReport {
  batchId:string;status:string;parts:number;startedParts:number;completedParts:number;
  failedParts:number;skippedParts:number;estimatedSamUsd:number;
  derivedFramesWithMasks:number|null;derivedSeamsVerified:boolean|null;
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
              <option value="api-meta-adaptive">{{es?'API Meta · segmentos':'Meta API · segments'}}</option>
            </select>
          </label>
        }
      </header>
      @if(studyId() && videoSha256()) {
        <div class="sam-actions">
          <div class="sam-quote" aria-live="polite">
            <span class="sam-status" [class.available]="freeCacheAvailable()"><i aria-hidden="true"></i>{{statusLabel()}}</span>
            <div class="sam-price">
              @if(preflight(); as pf) {
                <div class="sam-price-line">
                  <strong [class.paid-cost]="!freeCacheAvailable()" [class.free-cost]="freeCacheAvailable()">{{freeCacheAvailable()?'US$ 0.00':((groundingRequired() || reviewBudget()>0 || reviewOnlyRequired() || adaptiveQuote()?.requiresNewSparkCall)?(es?'Estimado US$ ':'Estimated US$ '):'US$ ')+costLabel(displayedCost())}}</strong>
                  <small>{{freeCacheAvailable()?(es?'desde caché · sin cobro':'from cache · no charge'):((groundingRequired() || reviewBudget()>0 || reviewOnlyRequired() || adaptiveQuote()?.requiresNewSparkCall)?(es?'Costo estimado · no es una factura':'Estimated cost · not an invoice'):(es?'Usará créditos · estimado':'Uses credits · estimate'))}}</small>
                </div>
                @if(!freeCacheAvailable() && groundingRequired()) {
                  <small class="sam-meta-estimate">SAM {{es?'estimado':'estimate'}} US$ {{costLabel(pf.estimatedCostUsd)}} + Spark {{es?'hasta':'up to'}} US$ {{groundingQuote()!.maximumEstimatedCostUsd.toFixed(2)}}</small>
                }
                @if(mode()==='api-meta-adaptive' && adaptiveQuote(); as aq) {
                  <small class="sam-meta-estimate">{{aq.parts}} {{es?'solicitudes SAM':'SAM requests'}} ·
                    {{aq.simultaneousRequests}} {{es?'simultáneas':'concurrent'}} ·
                    {{es?'cuadros facturables':'billable frames'}}: {{aq.billableFrames}}</small>
                  @if(aq.requiresNewSparkCall) {
                    <small class="sam-meta-estimate">{{es?'Incluye Spark estimado US$ ':'Includes Spark estimated US$ '}}{{groundingQuote()?.maximumEstimatedCostUsd?.toFixed(2)}} · {{es?'segmentos máximos; se ajustan tras localizar el objeto':'maximum segments; adjusted after locating the object'}}</small>
                  }
                  @if(aq.recoveryFromBatchId) {
                    <small class="sam-meta-estimate">{{es?
                      'Recuperación de continuidad: 1 solicitud SAM sobre el video completo, sin cortes. Spark guardado; requiere pulsar Procesar.':
                      'Continuity recovery: 1 SAM request for the complete video, without cuts. Saved Spark; press Process to submit.'}}</small>
                  } @else if(aq.reducedForSparseSparkSeeds) {
                    <small class="sam-meta-estimate">{{es?
                      'Spark verificó '+aq.verifiedSparkSeedCount+' posiciones. El plan de '+aq.idealPartsWithoutSparkCoverage+
                        ' segmentos se redujo a '+aq.parts+' para evitar inferencias sin referencia.':
                      'Spark verified '+aq.verifiedSparkSeedCount+' positions. The '+aq.idealPartsWithoutSparkCoverage+
                        '-segment proposal was reduced to '+aq.parts+' to prevent ungrounded inference.'}}</small>
                  }
                }
                @if(!freeCacheAvailable() && !reviewOnlyRequired() && reviewBudget()>0) {
                  <small class="sam-meta-estimate">{{es?'Incluye revisión de suspensión si hace falta: hasta US$ ':'Includes suspension review if needed: up to US$ '}}{{reviewBudget().toFixed(2)}}</small>
                }
                @if(freeCacheAvailable()) {
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
          } @else if(processing() && mode()!=='api-meta-adaptive') {
            <app-lab-activity class="sam-progress" [label]="processingLabel()" [detail]="processingDetail()"/>
          }
          @if(stageTraceStarted()) {
            <div class="sam-stages" aria-label="Process timing">
              @for(row of stageRows();track row.key) {
                <div class="sam-stage-row" [class.stage-error]="row.state==='FAILED'">
                  <div><span>{{row.label}}</span><small>{{stageStateLabel(row.state)}} · {{(row.ms/1000).toFixed(1)}} s @if(row.percent!==undefined && row.percent!==null){ · {{row.percent.toFixed(0)}}% }</small></div>
                  <div class="stage-track" role="progressbar" [attr.aria-label]="row.label"
                    [attr.aria-valuenow]="row.percent ?? (row.state==='COMPLETED'?100:null)"
                    [attr.aria-valuetext]="stageStateLabel(row.state)">
                    <i [style.width.%]="row.percent ?? null" [class.stage-active]="row.state==='IN_PROGRESS' && row.percent==null" [class.stage-done]="row.state==='COMPLETED'"></i>
                  </div>
                </div>
              }
              <small class="sam-stage-help">{{es?'Los segmentos SAM se ejecutan en paralelo. Las barras animadas indican actividad; Meta no informa un porcentaje exacto.':'SAM segments run in parallel. Animated bars indicate activity; Meta does not report an exact percentage.'}}</small>
            </div>
          }
          @if(!checking() && !processing() && !connection.busy()) {
          <div class="sam-message" [class.warning]="!freeCacheAvailable() && !canProcess() && !checking() && !sending()" role="status">
            @if(error()) {
              <span>{{error()}}</span>

            } @else if(adaptiveResult(); as batch) {
              <span>{{es?'Lote SAM dinámico registrado':'Adaptive SAM batch recorded'}}:
                {{batch.completedParts}}/{{batch.parts}} {{es?'solicitudes completas':'requests completed'}} ·
                {{batch.derivedFramesWithMasks??0}} {{es?'máscaras':'masks'}} ·
                {{batch.derivedSeamsVerified===true?(es?'empalmes coherentes':'seams consistent'):(es?'revisión de empalmes pendiente':'seam review pending')}}.
                {{es?'No se declara validación científica automática.':'No automatic scientific validation is claimed.'}}</span>
            } @else if(validationState()==='ready') {
              <span>{{es?'Procesado · reproducción disponible.':'Processed · playback available.'}}</span>
            } @else if(!mediaReady()) {
              <span>{{es?'Esperando que termine de cargar el video original.':'Waiting for the original video to finish loading.'}}</span>
            } @else if(freeCacheAvailable()) {
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
            <p>{{mode()==='api-meta-adaptive'?
              (es?'Prueba de múltiples solicitudes SAM, con una única localización Spark archivada, presupuesto conjunto, recibos separados y sin reintentos automáticos. Las máscaras derivadas requieren revisión humana.':'Experimental multi-request SAM using one archived Spark localization, an aggregate budget, independent receipts and no automatic retries. Derived masks require human review.'):
              mode()==='api-meta-cache'
              ? (es?'Se verifica el mismo video en SQLite. Si hay caché válida se reutiliza; si no, se requiere una solicitud autorizada a Meta.':'SQLite is checked for this exact video. Valid cache is reused; otherwise an authorized Meta request is required.')
              : (es?'Solicita un análisis nuevo a Meta, sujeto a créditos y permisos del servidor.':'Requests a new Meta analysis, subject to credits and server permission.')}}</p>
            @if(preflight(); as pf) {
              <p>{{es?'Estimación completa para enviar a Meta':'Full estimate for sending to Meta'}}: US$ {{displayedCost().toFixed(4)}} · {{pf.codec}}</p>
            }
            @if(adaptiveResult(); as batch) {
              <p>{{es?'Lote':'Batch'}}: {{batch.batchId}} · {{batch.status}} ·
                {{es?'iniciadas':'started'}} {{batch.startedParts}}/{{batch.parts}} ·
                {{es?'fallidas':'failed'}} {{batch.failedParts}}</p>
            }
            @if(cacheStatus(); as s) {
              <p>{{es?'Estado':'Status'}}: <strong>{{s.status}}</strong></p>
              <p class="sam-cache-origin">{{s.storage || '—'}} · {{s.cacheHit===true?'HIT':s.cacheHit===false?'MISS':'—'}}
                @if(s.analysisId) { · {{es?'Análisis':'Analysis'}}: {{s.analysisId}} }
              </p>
              @if(s.status.startsWith('CACHED_META_') && !freeCacheAvailable()) {
                <p>{{es?'Nuevo análisis SAM guardado.':'New SAM analysis saved.'}}</p>
              }
              @if(s.status.includes('RECONCILIATION')) {
                <p class="sam-caution">{{es
                  ? 'Existe un intento previo posiblemente facturado. Se requiere revisión manual antes de otra ejecución; nunca habrá reintento automático.'
                  : 'A prior attempt may have been billed. Manual review is required before another execution; no automatic retry.'}}</p>
              }
            }
            @if(suspensionReviewQuote()?.evidence?.answer; as review) {
              <p class="sam-review-explanation"><strong>{{es?'Revisión de suspensión por Spark':'Suspension review by Spark'}}:</strong>
                {{es?review.justification.es:review.justification.en}}</p>
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
                    [attr.transform]="'translate('+d.left+' '+d.top+') scale('+(d.maskScaleX??1)+' '+(d.maskScaleY??1)+')'"
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
    .sam-actions .sam-submit{min-width:112px;padding:11px 20px;font-weight:700;transition:background-color .2s,border-color .2s}
    .sam-actions .sam-submit:enabled{background:#15803d;border-color:#4ade80;color:#f0fff4;animation:sam-ready-pulse 2s ease-in-out infinite}
    .sam-actions .sam-submit:enabled:hover{background:#166534;border-color:#86efac}
    .sam-actions .sam-submit:focus-visible{outline:2px solid #bbf7d0;outline-offset:3px}
    .sam-actions .sam-submit:disabled{background:#303843;border-color:#48515e;color:#aab6c5;opacity:1;animation:none;box-shadow:none}
    @keyframes sam-ready-pulse{0%,100%{box-shadow:0 0 0 0 rgba(74,222,128,0)}50%{box-shadow:0 0 0 5px rgba(74,222,128,.24)}}
    @media(prefers-reduced-motion:reduce){.sam-actions .sam-submit:enabled{animation:none;transition:none}}
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
    .sam-stages{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:14px;margin-top:16px}
    .sam-stage-row>div:first-child{display:flex;justify-content:space-between;gap:8px;font-size:12px;margin-bottom:7px}
    .sam-stage-row small,.sam-stage-help{color:#9cb2ca;font-size:11px}.sam-stage-help{grid-column:1/-1}
    .stage-track{height:4px;background:#303b4a;border-radius:5px;overflow:hidden}
    .stage-track i{display:block;height:100%;width:0;background:#66cbd5}
    .stage-track i.stage-done{width:100%;background:#69dbad}
    .stage-track i.stage-active{width:35%;animation:sam-stage-slide 1.5s ease-in-out infinite}
    .stage-error .stage-track{background:#823e47}.stage-error small{color:#ff8e96}
    @keyframes sam-stage-slide{from{transform:translateX(-100%)}to{transform:translateX(390%)}}
    @media(prefers-reduced-motion:reduce){.stage-track i.stage-active{animation:none;width:50%}}
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
  readonly processingPhase=signal<'grounding'|'sam'|'review'|'validating'|null>(null);
  private beginProcessing():void {
    this.processing.set(true);this.processingPhase.set('validating');this.connection.busy.set(true);this.processingStarted.emit();
  }
  private finishProcessing():void {
    if(this.stageTraceStarted()){
      this.traceClock.set(Date.now());
      if(this.viewStartedAt && !this.viewFinishedAt)this.viewFinishedAt=Date.now();
      if(this.traceTicker!==null)clearInterval(this.traceTicker);
      this.traceTicker=null;
    }
    this.processing.set(false);this.processingPhase.set(null);this.connection.busy.set(false);
  }
  private failProcessing():void {
    this.traceFailed=true;
    // Clear local activity first so the parent's error state keeps the specific failure message.
    this.finishProcessing();this.processingFailed.emit();
  }
  readonly mode=signal<SamMode>('api-meta-cache');
  readonly adaptiveQuote=signal<SamAdaptiveQuote|null>(null);
  readonly adaptiveProgress=signal<SamAdaptiveProgress|null>(null);
  readonly adaptiveResult=signal<SamAdaptiveReport|null>(null);
  private adaptivePollHandle:ReturnType<typeof setInterval>|null=null;
  readonly stageTraceStarted=signal(false);
  readonly traceClock=signal(0);
  private traceTicker:ReturnType<typeof setInterval>|null=null;
  private traceStartedAt=0;
  private sparkFinishedAt=0;
  readonly sparkStages=signal<{key:string;state:string;elapsedMs:number;percent:number|null}[]>([]);
  private sparkPoll:ReturnType<typeof setInterval>|null=null;
  private async pollSpark():Promise<void> {
    if(!this.http || !this.stageTraceStarted() || !this.sparkWasNeeded)return;
    const url=this.groundingUrl(),started=this.traceStartedAt;
    try {const p=await firstValueFrom(this.http.get<{startedAtUtc:string;stages:{key:string;state:string;elapsedMs:number;percent:number|null}[]}>(url+'/progress').pipe(timeout(5000)));
      if(url===this.groundingUrl() && started===this.traceStartedAt && Date.parse(p.startedAtUtc)>=started-1000)this.sparkStages.set(p.stages);
    }catch{/* Read-only progress cannot cause another provider call. */}
  }
  private stopSparkPoll():void {if(this.sparkPoll!==null)clearInterval(this.sparkPoll);this.sparkPoll=null;}

  private viewStartedAt=0;
  private viewFinishedAt=0;
  private traceFailed=false;
  private sparkWasNeeded=false;
  private startStageTrace():void {
    if(this.traceTicker!==null)clearInterval(this.traceTicker);
    this.traceStartedAt=Date.now();this.traceClock.set(this.traceStartedAt);
    this.sparkFinishedAt=0;this.viewStartedAt=0;this.viewFinishedAt=0;this.traceFailed=false;
    this.sparkWasNeeded=this.mode()==='api-meta-adaptive'?!!this.adaptiveQuote()?.requiresNewSparkCall:this.groundingRequired();
    this.stopSparkPoll();this.sparkStages.set([]);
    if(this.sparkWasNeeded)this.sparkPoll=setInterval(()=>{void this.pollSpark();},1000);
    this.stageTraceStarted.set(true);
    this.traceTicker=setInterval(()=>this.traceClock.set(Date.now()),250);
  }
  stageStateLabel(state:string):string {
    return state==='COMPLETED'?(this.es?'Listo':'Done'):state==='FAILED'?(this.es?'Falló':'Failed'):
      state==='IN_PROGRESS'?(this.es?'En curso':'Running'):(this.es?'Pendiente':'Pending');
  }
  stageRows():{key:string;label:string;state:string;ms:number;percent?:number|null}[] {
    const p=this.adaptiveProgress(),now=this.traceClock(),phase=p?.phase;
    const sparkDone=!!this.sparkFinishedAt;
    const rows:{key:string;label:string;state:string;ms:number;percent?:number|null}[]=[{key:'spark',label:this.sparkWasNeeded?'Spark':(this.es?'Spark · reutilizado':'Spark · reused'),
      state:sparkDone?'COMPLETED':this.traceFailed?'FAILED':'IN_PROGRESS',
      ms:this.sparkWasNeeded?Math.max(0,(this.sparkFinishedAt||now)-this.traceStartedAt):0},
      {key:'preparation',label:this.es?'Preparación del video':'Video preparation',
       state:!sparkDone?'NOT_STARTED':phase==='PREPARING'||!p?(this.traceFailed?'FAILED':'IN_PROGRESS'):
         phase==='FAILED'&&!p.started?'FAILED':'COMPLETED',
       ms:p?.preparationElapsedMs??(sparkDone?Math.max(0,now-this.sparkFinishedAt):0)}];
    if(this.sparkWasNeeded){
      const names=[['prepare','Preparar video','Prepare video'],['references','Extraer 50 imágenes','Extract 50 images'],['upload','Subir video a Spark','Upload video to Spark'],['inference','Respuesta de Spark','Spark response'],['verify','Validar respuesta','Validate response']];
      rows.splice(0,1,...names.map(([key,es,en])=>{const s=this.sparkStages().find(x=>x.key===key);return {key:'spark-'+key,label:this.es?es:en,state:s?.state??'NOT_STARTED',ms:s?.elapsedMs??0,percent:s?.percent};}));
    }
    if(this.mode()!=='api-meta-adaptive'){
      rows.splice(rows.length-1,1,{key:'sam',label:'SAM',state:!sparkDone?'NOT_STARTED':this.viewStartedAt?'COMPLETED':this.traceFailed?'FAILED':'IN_PROGRESS',ms:sparkDone?Math.max(0,(this.viewStartedAt||now)-this.sparkFinishedAt):0});
      return rows;
    }
    for(let i=0;i<(this.adaptiveQuote()?.parts??0);i++){
      const part=p?.perPart.find(x=>x.part===i);
      rows.push({key:'sam-'+i,label:this.adaptiveQuote()?.recoveryFromBatchId?
        (this.es?'SAM · video completo':'SAM · full video'):'SAM · '+(this.es?'segmento ':'segment ')+(i+1),
        state:part?.state??'NOT_STARTED',ms:part?.elapsedMs??0});
    }
    rows.push({key:'join',label:this.adaptiveQuote()?.recoveryFromBatchId?
      (this.es?'Verificación':'Verification'):(this.es?'Unión y verificación':'Join and verification'),
      state:phase==='COMPLETED'?'COMPLETED':phase==='FAILED'?'FAILED':
        phase==='VALIDATING'?'IN_PROGRESS':'NOT_STARTED',ms:p?.validationElapsedMs??0});
    rows.push({key:'view',label:this.es?'Gráficos y reproducción':'Overlays and playback',
      state:this.viewFinishedAt?(this.traceFailed?'FAILED':'COMPLETED'):this.viewStartedAt?'IN_PROGRESS':'NOT_STARTED',
      ms:this.viewStartedAt?Math.max(0,(this.viewFinishedAt||now)-this.viewStartedAt):0});
    return rows;
  }
  private adaptiveUrl():string {
    return this.base+'/'+this.studyId()+'/sam-adaptive/'+this.videoRole();
  }
  private get base():string {
    return 'http://127.0.0.1:'+this.localApiPort()+
      '/api/science/director/pendulum/sessions';
  }
  selectMode(event:Event):void {
    const mode=(event.target as HTMLSelectElement).value;
    if(this.processing() || this.sending() || (mode!=='api-meta' && mode!=='api-meta-cache' && mode!=='api-meta-adaptive'))return;
    this.mode.set(mode);
    this.resetServerCheck();
    this.scheduleCheck();
  }
  selectLocalPort(event:Event):void {
    const p=Number((event.target as HTMLSelectElement).value);
    if(this.processing() || this.sending() || (p!==5391 && p!==5393 && p!==5398 && p!==5410))return;
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
    this.groundingQuote.set(null);this.suspensionReviewQuote.set(null);
    this.readiness.set(null);
    this.transferConsent.set(false);
    this.paidConsent.set(false);
    this.retryConsent.set(false);
    this.adaptiveQuote.set(null);
    this.adaptiveProgress.set(null);
    this.adaptiveResult.set(null);
    this.stageTraceStarted.set(false);
    if(this.traceTicker!==null)clearInterval(this.traceTicker);this.traceTicker=null;
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
  ngOnDestroy():void {
    this.stopSparkPoll();
    this.disposed=true;this.serial++;this.offlineSerial++;
    if(this.adaptivePollHandle!==null)clearInterval(this.adaptivePollHandle);
    if(this.traceTicker!==null)clearInterval(this.traceTicker);this.traceTicker=null;
    this.adaptivePollHandle=null;
    if(this.processing() && !this.sending())this.finishProcessing();
  }
  cachedResultAvailable():boolean {
    const s=this.cacheStatus();
    return this.mode()==='api-meta-cache' && !!s && s.cacheHit===true &&
      s.status==='CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW' &&
      s.sourceSha256===this.videoSha256() && !!s.analysisId;
  }
  canProcess():boolean {
    if(this.mode()==='api-meta-adaptive') {
      const quote=this.adaptiveQuote(),ready=this.readiness(),spark=this.groundingQuote();
      if(this.adaptiveResult())return false;
      return !!quote && !!ready &&
        quote.sourceSha256===this.videoSha256() &&
        (quote.existingSparkSceneVerified || (quote.requiresNewSparkCall &&
          !!spark && spark.videoSha256===this.videoSha256() && spark.canRun &&
          !spark.priorAttempt && spark.status==='NEEDS_LOCALIZATION' &&
          spark.maximumEstimatedCostUsd>0 && Number.isFinite(spark.maximumEstimatedCostUsd))) &&
        quote.canRunWithManualToken &&
        quote.parts>0 && quote.parts<=50 && quote.simultaneousRequests<=12 &&
        quote.totalSamEstimatedUsd>0 && quote.totalSamEstimatedUsd<=0.50 &&
        ready.creditsConfirmedByOwner && ready.ownerApprovedPaidRequests &&
        ready.paidRequestsSwitchEnabled===true && ready.manualTokenCallsPermitted===true &&
        this.tokenEntered() && this.mediaReady() && !this.sourcePending() &&
        !this.processing() && !this.sending() && !this.checking() &&
        !this.connection.busy();
    }
    const quote=this.preflight();
    if(this.sourcePending() || !this.mediaReady() || this.processing() ||
       this.checking() || this.sending() || this.connection.busy() ||
       !quote || quote.sourceSha256!==this.videoSha256() ||
       !Number.isFinite(quote.estimatedCostUsd) || quote.estimatedCostUsd<=0)return false;
    if(this.cachedResultAvailable())return !this.reviewOnlyRequired() ||
      (this.tokenEntered() && this.readiness()?.manualTokenCallsPermitted===true);
    return (this.canOfferPaidRun() &&
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
    if(this.mode()==='api-meta-adaptive') {
      await this.analyzeAdaptive();
      return;
    }
    if(this.cachedResultAvailable()) {
      if(this.reviewOnlyRequired()){await this.reviewCached();return;}
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
  readonly suspensionReviewQuote=signal<SuspensionReviewQuote|null>(null);
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
    if(this.mode()==='api-meta-adaptive'){
      const result=this.adaptiveResult();
      if(result)return result.completedParts===result.parts ?
        (this.es?'Lote terminado':'Batch completed') :
        (this.es?'Lote incompleto':'Incomplete batch');
      if(this.adaptiveQuote()?.recoveryFromBatchId)return this.es?'Recuperación lista':'Recovery ready';
      if(this.adaptiveQuote())return this.es?'Plan dinámico listo':'Adaptive plan ready';
      return this.es?'Sin plan dinámico':'No adaptive plan';
    }
    if(this.validationState()==='ready')return this.es?'Procesado':'Processed';
    if(this.checking())return this.es?'Comprobando':'Checking';
    if(this.cachedResultAvailable())return this.es?'Caché disponible':'Cache available';
    if(this.canProcess() && this.cacheStatus()?.previousAttemptStatus)return this.es?'Nuevo análisis disponible':'New analysis available';
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
    if(this.processingPhase()==='review')return this.es?'Spark revisando la suspensión…':'Spark is reviewing the suspension…';
    if(this.processingPhase()==='grounding')return this.es?'Spark analizando el video…':'Spark is analyzing the video…';
    if(this.processingPhase()==='sam')return this.es?'SAM siguiendo el objeto…':'SAM is tracking the object…';
    return this.es?'Preparando resultados…':'Preparing results…';
  }
  processingDetail():string {
    if(this.mode()==='api-meta-adaptive' && this.processingPhase()==='sam'){
      const progress=this.adaptiveProgress(),q=this.adaptiveQuote();
      return this.es?
        'SAM en segmentos con streaming: '+(progress?.completed??0)+'/'+
        (q?.parts??0)+' solicitudes con recibo. No es un porcentaje de tiempo.':
        'Segmented SAM streaming: '+(progress?.completed??0)+'/'+
        (q?.parts??0)+' requests with receipts. Not a time percentage.';
    }
    if(this.processingPhase()==='review')return this.es?
      'Contrastando el anclaje visible con las posiciones de SAM. Una revisión, sin repetir el seguimiento.':
      'Checking the visible suspension against SAM positions. One review, without repeating tracking.';
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
      (ready.manualRequestsUnlimited===true || ready.remainingRequests>=this.requiredCalls());
  }
  private requiredCalls():number {return (this.groundingRequired()?2:1)+(this.reviewBudget()>0?1:0);}
  reviewOnlyRequired():boolean {
    const r=this.suspensionReviewQuote();
    return this.cachedResultAvailable() && !!r && r.analysisId===this.cacheStatus()?.analysisId &&
      r.required && !r.priorAttempt && !r.evidence;
  }
  freeCacheAvailable():boolean {return this.cachedResultAvailable()&&!this.reviewOnlyRequired();}
  reviewBudget():number {
    return this.mode()==='api-meta-adaptive'?0:
      (this.preflight()?.suspensionReviewMaximumCostUsd??0);
  }
  displayedCost():number {
    if(this.mode()==='api-meta-adaptive')
      return (this.adaptiveQuote()?.totalSamEstimatedUsd??0)+
        (this.adaptiveQuote()?.requiresNewSparkCall?this.groundingQuote()?.maximumEstimatedCostUsd??0:0);
    if(this.reviewOnlyRequired())return this.suspensionReviewQuote()!.maximumEstimatedCostUsd;
    return (this.preflight()?.estimatedCostUsd ?? 0)+this.reviewBudget()+
      (this.groundingRequired()?this.groundingQuote()!.maximumEstimatedCostUsd:0);
  }
  costLabel(value:number):string {return (Math.ceil(value*100-1e-8)/100).toFixed(2);}
  nextActionDescription():string {
    if(this.mode()==='api-meta-adaptive'){
      const q=this.adaptiveQuote();
      return q?(this.es?
        'Procesar autoriza '+(q.requiresNewSparkCall?'localizar el objeto con Spark y después hasta ':'reutilizar Spark y enviar ')+
        q.parts+' solicitudes SAM, hasta '+q.simultaneousRequests+' simultáneas, dentro del costo total mostrado. Sin reintentos automáticos.':
        'Process authorizes '+(q.requiresNewSparkCall?'Spark localization followed by up to ':'reusing Spark and sending ')+
        q.parts+' SAM requests, up to '+q.simultaneousRequests+' concurrent, within the displayed total. No automatic retries.'):
        (this.es?'Esperando el presupuesto de segmentos.':'Waiting for the segment quote.');
    }
    if(this.reviewOnlyRequired())return this.es?
      'SAM está guardado. Procesar autoriza una revisión de Spark del video y la suspensión, hasta el máximo mostrado; no repite SAM.':
      'SAM is saved. Process authorizes one Spark review of the video and suspension up to the displayed maximum; SAM is not repeated.';
    const previous=this.mode()==='api-meta' && this.cacheStatus()?.previousAttemptStatus
      ? (this.es?'Hay un intento anterior registrado. Este clic inicia un análisis nuevo y puede generar otro cobro. ':'A previous attempt is recorded. This click starts a new analysis and may incur another charge. '):'';
    if(this.groundingRequired())return previous+(this.es?
      'Al pulsar Procesar autorizas enviar el video completo y sus imágenes de referencia a Spark; después, el video a SAM, con el costo combinado estimado mostrado, incluida una revisión de suspensión si hace falta. Si Spark no localiza el objeto, SAM no se envía.':
      'Pressing Process authorizes the complete video and its reference images to Spark, then the video to SAM, at the displayed combined estimated cost, including a suspension review if needed. If Spark cannot locate the object, SAM is not sent.');
    return previous+(this.es?'Al pulsar Procesar autorizas enviar este video a Meta y usar los créditos del costo mostrado.':
      'Pressing Process authorizes sending this video to Meta and using credits at the displayed cost.');
  }
  processBlockedReason():string {
    if(this.mode()==='api-meta-adaptive'){
      const q=this.adaptiveQuote(),r=this.readiness();
      if(!q)return this.es?'Actualiza el costo del procesamiento por segmentos.':'Refresh the segment processing cost.';
      if(q.requiresNewSparkCall && !this.groundingQuote()?.canRun)return this.es?
        'Spark aún no puede localizar el objeto. Actualiza el costo y revisa el estado del intento.':
        'Spark cannot localize the object yet. Refresh the cost and review the attempt status.';
      if(!r?.paidRequestsSwitchEnabled || !r.creditsConfirmedByOwner ||
         !r.ownerApprovedPaidRequests || !r.manualTokenCallsPermitted)
        return this.es?'Los controles de créditos o pagos del servidor están deshabilitados.':
          'Server credit or payment gates are disabled.';
      if(!this.tokenEntered())return this.es?
        'Registra el token Meta en Configuración.':
        'Enter the Meta key in Settings.';
      return this.es?'El plan dinámico no cumple los límites.':
        'The adaptive plan does not meet current limits.';
    }
    if(this.reviewOnlyRequired())return this.es?
      'La suspensión requiere revisión de Spark. Confirma tu token para usar el costo mostrado.':
      'The suspension needs Spark review. Confirm your key to use the displayed cost.';
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
  private validAdaptiveQuote(q:SamAdaptiveQuote,sha:string):boolean {
    return q.sourceSha256===sha && /^[a-f0-9]{32}$/.test(q.suggestedBatchId) &&
      /^[a-f0-9]{64}$/.test(q.planSha256) && q.parts>=1 && q.parts<=50 &&
      q.simultaneousRequests>=1 && q.simultaneousRequests<=12 &&
      Number.isFinite(q.totalSamEstimatedUsd) && q.totalSamEstimatedUsd>0 &&
      q.totalSamEstimatedUsd<=.50 &&
      (q.requiresNewSparkCall!==q.existingSparkSceneVerified) &&
      (!q.recoveryFromBatchId || (/^[a-f0-9]{32}$/.test(q.recoveryFromBatchId) &&
        q.parts===1 && q.simultaneousRequests===1 && q.existingSparkSceneVerified && !q.requiresNewSparkCall));
  }
  private async checkAdaptive():Promise<void> {
    if(!this.http || !this.studyId() || !this.videoSha256() ||
       this.processing() || this.checking() || this.sending())return;
    const serial=++this.serial;
    this.processingReset.emit();this.checking.set(true);this.error.set('');
    this.adaptiveQuote.set(null);this.adaptiveResult.set(null);this.groundingQuote.set(null);
    this.adaptiveProgress.set(null);this.readiness.set(null);this.preflight.set(null);
    try {
      const root='http://127.0.0.1:'+this.localApiPort();
      const [quote,preflight,access,spark]=await Promise.all([
        firstValueFrom(this.http.get<SamAdaptiveQuote>(
          this.adaptiveUrl()+'/preflight?targetCoreFrames=53').pipe(timeout(90000))),
        firstValueFrom(this.http.get<SamPreflight>(this.url()+'/preflight').pipe(timeout(90000))),
        firstValueFrom(this.http.get<SamReadiness>(root+'/api/meta/readiness').pipe(timeout(10000))),
        firstValueFrom(this.http.get<GroundingQuote>(this.adaptiveUrl()+'/spark-quote').pipe(timeout(90000)))
      ]);
      if(serial!==this.serial || this.disposed || this.mode()!=='api-meta-adaptive')return;
      if(!this.validAdaptiveQuote(quote,this.videoSha256()) ||
         preflight.sourceSha256!==quote.sourceSha256 || spark.videoSha256!==quote.sourceSha256)
        throw new Error('INVALID_ADAPTIVE_QUOTE');
      this.adaptiveQuote.set(quote);this.preflight.set(preflight);
      this.readiness.set(access);this.groundingQuote.set(spark);
    }catch(e) {
      if(serial===this.serial){
        const status=(e as {status?:number})?.status;
        this.error.set(status===404?(this.es?
          'Este video no está disponible en el servidor seleccionado. Vuelve al servidor donde lo cargaste.':
          'This video is not available on the selected server. Use the server where it was uploaded.'):
          status===0?(this.es?'No se pudo conectar con la API local. Comprueba la conexión en Configuración.':
          'Cannot reach the local API. Check the connection in Settings.'):
          (this.es?'No se pudo preparar el presupuesto de segmentos. Revisa el video y la evidencia de Spark; no se envió nada a Meta.':
          'Could not prepare the segment quote. Review the video and Spark evidence; nothing was sent to Meta.'));
      }
    }finally{if(serial===this.serial)this.checking.set(false);}
  }

  private async analyzeAdaptive():Promise<void> {
    if(!this.http || !this.canProcess() || this.mode()!=='api-meta-adaptive')return;
    let quote=this.adaptiveQuote()!;
    const approvedSam=quote.totalSamEstimatedUsd,spark=this.groundingQuote();
    const token=this.connection.tokenForManualRequest();
    if(!token)return;
    const sha=this.videoSha256(),url=this.adaptiveUrl(),port=this.localApiPort();
    const serial=++this.serial;
    const current=()=>!this.disposed && serial===this.serial &&
      this.mode()==='api-meta-adaptive' && sha===this.videoSha256() &&
      url===this.adaptiveUrl() && port===this.localApiPort();
    this.processingReset.emit();this.beginProcessing();this.sending.set(true);
    this.error.set('');this.adaptiveProgress.set(null);this.adaptiveResult.set(null);
    this.startStageTrace();
    let opened=false;
    try {
      if(quote.requiresNewSparkCall){
        if(!spark || !spark.canRun)throw Error('SPARK_NOT_READY');
        this.processingPhase.set('grounding');
        const located=await firstValueFrom(this.http.post<GroundingQuote>(url+'/spark-once',{
          videoSha256:sha,cacheKey:spark.cacheKey,modelApiToken:token,
          maximumEstimatedCostUsd:spark.maximumEstimatedCostUsd,mode:'api-meta-cache',
          confirmImageTransfer:true,confirmVideoTransfer:true,confirmPaidCall:true
        },{headers:{'X-ProofLab-Manual-Action':'sam-adaptive-spark'}}).pipe(timeout(12*60*1000)));
        if(!current())return;
        if(located.videoSha256!==sha || located.status!=='READY' || !located.scene ||
          located.scene.videoSha256!==sha)throw Error('SPARK_TARGET_INCONCLUSIVE');
        const exact=await firstValueFrom(this.http.get<SamAdaptiveQuote>(
          url+'/preflight?targetCoreFrames=53').pipe(timeout(90000)));
        if(!current())return;
        if(!this.validAdaptiveQuote(exact,sha) || exact.requiresNewSparkCall ||
          exact.sparkSceneKey!==located.scene.key || exact.totalSamEstimatedUsd>approvedSam+1e-9)
          throw Error('SEGMENT_QUOTE_CHANGED');
        quote=exact;this.adaptiveQuote.set(exact);this.groundingQuote.set(located);
      }
      void this.pollSpark();this.stopSparkPoll();
      this.sparkFinishedAt=Date.now();this.traceClock.set(Date.now());
      this.processingPhase.set('sam');
      let polling=false;
      const poll=async()=>{
        if(polling)return;polling=true;
        try{
          const progress=await firstValueFrom(this.http!.get<SamAdaptiveProgress>(
            url+'/batches/'+quote.suggestedBatchId+'/progress').pipe(timeout(8000)));
          if(current())this.adaptiveProgress.set(progress);
        }catch{ /* Status reads never retry provider requests. */ }
        finally{polling=false;}
      };
      this.adaptivePollHandle=setInterval(()=>{void poll();},2000);
      const report=await firstValueFrom(this.http.post<SamAdaptiveReport>(url+'/manual-batch',{
        modelApiToken:token,videoSha256:sha,sceneKey:quote.sparkSceneKey,
        planSha256:quote.planSha256,clientBatchId:quote.suggestedBatchId,
        maximumSamCostUsd:quote.totalSamEstimatedUsd,
        targetCoreFrames:53,maxInFlight:quote.simultaneousRequests,recoveryFromBatchId:quote.recoveryFromBatchId??null,
        confirmPrivateVideoTransfer:true,confirmUpToManyBillableSamCalls:true,confirmNoAutomaticRetry:true
      },{headers:{'X-ProofLab-Manual-Action':'sam-adaptive-batch'}}).pipe(timeout(35*60*1000)));
      if(!current())return;
      if(report.batchId!==quote.suggestedBatchId || report.parts!==quote.parts)
        throw Error('BATCH_ID_MISMATCH');
      this.adaptiveResult.set(report);
      await poll();
      if(report.status!=='PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW' ||
         report.derivedSeamsVerified!==true || !report.derivedFramesWithMasks)
        throw Error('SEGMENT_RESULT_INCONCLUSIVE');
      this.processingPhase.set('validating');this.viewStartedAt=Date.now();
      opened=true;
      this.cacheUpdated.emit({studyId:this.studyId(),videoRole:this.videoRole(),
        sourceSha256:sha,analysisId:report.batchId,adaptiveBatchId:report.batchId,port});
    }catch(e){
      if(current()){
        const code=e instanceof Error?e.message:'';
        this.error.set(code==='SEGMENT_QUOTE_CHANGED'?(this.es?
          'Spark terminó, pero cambió el presupuesto o la evidencia. SAM no se envió; actualiza el costo.':
          'Spark finished, but the quote or evidence changed. SAM was not sent; refresh the cost.'):
          this.processingPhase()==='grounding'?(this.es?
          'Spark no pudo confirmar el objeto. SAM no se envió; revisa el estado antes de otro intento.':
          'Spark could not confirm the object. SAM was not sent; review the status before another attempt.'):
          (this.es?'No se pudieron validar todos los segmentos. Puedes reproducir el original; los gráficos quedan pendientes. Consulta las etapas y actualiza el costo para un nuevo intento manual.':
          'Not all segments could be validated. Original playback is available; overlays remain pending. Review the stages and refresh the cost for a new manual attempt.'));
        this.failProcessing();
      }
    }finally{
      void this.pollSpark();this.stopSparkPoll();
      if(this.adaptivePollHandle!==null)clearInterval(this.adaptivePollHandle);
      this.adaptivePollHandle=null;this.sending.set(false);
      if(!opened || !current())this.finishProcessing();
    }
  }

  voidCheck():void {void this.check();}
  async check():Promise<void> {
    if(this.mode()==='api-meta-adaptive')return this.checkAdaptive();
    if(!this.http || !this.studyId() || !this.videoSha256() || this.processing() || this.checking() || this.sending())return;
    const serial=++this.serial;
    this.processingReset.emit();
    this.checking.set(true);
    this.error.set('');
    this.cacheStatus.set(null);
    this.preflight.set(null);
    this.groundingQuote.set(null);this.suspensionReviewQuote.set(null);
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
      let review:SuspensionReviewQuote|null=null;
      if(samHit && (pf.suspensionReviewMaximumCostUsd??0)>0)review=await firstValueFrom(this.http.get<SuspensionReviewQuote>(this.url()+'/suspension-review').pipe(timeout(90000))).catch(()=>null);
      if(serial!==this.serial || this.disposed)return;
      this.suspensionReviewQuote.set(review);
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
    this.sending.set(true);this.error.set('');this.startStageTrace();
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
      void this.pollSpark();this.stopSparkPoll();this.sparkFinishedAt=Date.now();
      this.processingPhase.set('sam');
      const state=await firstValueFrom(this.http.post<SamCacheStatus>(samUrl+'/manual-one-shot',{
        mode,sceneKey:scene.key,modelApiToken:oneTimeToken,videoSha256:sha,
        confirmPrivateVideoTransfer:true,confirmOneBillableSamCall:true,
        maximumEstimatedCostUsd:quote.estimatedCostUsd,
        ownerApprovesOneReconciledRetry:isRetry,
        previousAttemptBillingEvidence:isRetry?SamVideoReplay.reconciledOnce:null
      },{headers:{'X-ProofLab-Manual-Action':'sam-one-shot'}}));
      if(!current())return;
      this.cacheStatus.set(state);
      await this.reviewAfterSam(state,oneTimeToken,samUrl,quote.suspensionReviewMaximumCostUsd??0,current);
      if(!current())return;
      this.viewStartedAt=Date.now();this.openResult(state);this.processingPhase.set('validating');
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
        this.cacheStatus.set(null);this.groundingQuote.set(null);this.suspensionReviewQuote.set(null);this.failProcessing();
      }
    }finally{
      void this.pollSpark();this.stopSparkPoll();
      if(!this.processing() || this.disposed || !current())this.finishProcessing();
      this.sending.set(false);
      if(serial===this.serial){
        this.transferConsent.set(false);this.paidConsent.set(false);this.retryConsent.set(false);
      }
    }
  }

  private async reviewAfterSam(state:SamCacheStatus,token:string,url:string,budget:number,current:()=>boolean):Promise<void> {
    if(!this.http || budget<=0)return;
    this.processingPhase.set('review');
    try {
      const q=await firstValueFrom(this.http.get<SuspensionReviewQuote>(url+'/suspension-review').pipe(timeout(90000)));
      if(!current())return;
      this.suspensionReviewQuote.set(q);
      if(q.analysisId!==state.analysisId)throw Error('REVIEW_ANALYSIS_CHANGED');
      if(!q.required || q.evidence || q.priorAttempt)return;
      if(!q.canRun || !Number.isFinite(q.maximumEstimatedCostUsd) ||
        q.maximumEstimatedCostUsd<=0 || q.maximumEstimatedCostUsd>budget)throw Error('REVIEW_BUDGET_CHANGED');
      const reviewed=await firstValueFrom(this.http.post<SuspensionReviewQuote>(url+'/suspension-review',{
        videoSha256:state.sourceSha256,analysisId:state.analysisId,modelApiToken:token,
        confirmVideoTransfer:true,confirmPaidCall:true,maximumEstimatedCostUsd:q.maximumEstimatedCostUsd
      },{headers:{'X-ProofLab-Manual-Action':'suspension-review'}}));
      if(current())this.suspensionReviewQuote.set(reviewed);
    } catch {
      if(current())this.error.set(this.es?
        'SAM está guardado. La revisión de suspensión no se completó; puede haber un cobro de Spark. No se repetirá automáticamente.':
        'SAM is saved. The suspension review did not complete; Spark may have charged. It will not retry automatically.');
    }
  }
  private async reviewCached():Promise<void> {
    const token=this.connection.tokenForManualRequest(),state=this.cacheStatus(),quote=this.suspensionReviewQuote();
    if(!token||!state||!quote)return;
    const url=this.url(),sha=this.videoSha256(),serial=++this.serial;
    const current=()=>!this.disposed&&serial===this.serial&&sha===this.videoSha256()&&url===this.url();
    this.processingReset.emit();this.beginProcessing();this.sending.set(true);this.error.set('');
    try {
      await this.reviewAfterSam(state,token,url,quote.maximumEstimatedCostUsd,current);
      if(current()){this.openResult(state);this.processingPhase.set('validating');}
    } finally {
      this.sending.set(false);
      if(!current())this.finishProcessing();
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
    this.groundingQuote.set(null);this.suspensionReviewQuote.set(null);
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
