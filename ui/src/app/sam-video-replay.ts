import { apiOrigin } from './api-origin';
import {
  Component, ElementRef, OnChanges, OnDestroy, SimpleChanges, ViewChild,
  inject, input, output, signal
} from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
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

interface SamReusableResult {sourceSha256:string;cacheHit:boolean;analysisId:string|null;adaptiveBatchId:string|null;excludedResults:number;quality:{usable:boolean;reason:string}|null;storage:string;}
type SamMode='api-meta'|'api-meta-cache'|'api-meta-adaptive'|'cache-only'|'none';
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
  scene:GroundingScene|null;requiredSparkCalls?:number;visualReviewEstimatedCostUsd?:number;
  evidence?:{runId?:string|null}|null;
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
  continuousRetryEstimatedUsd?:number;maximumSamWithRetryEstimatedUsd?:number;
  mode?:'api-meta'|'api-meta-cache';sceneRunId?:string|null;
}
interface SamStageRetryQuote {
  failedBatchId:string;newBatchId:string;videoSha256:string;sparkSceneKey:string;
  sceneRunId:string;planSha256:string;frames:number;estimatedSamUsd:number;
  targetCoreFrames:number;alreadyRequested:boolean;previousMayHaveBeenBilled:boolean;
}
interface SamAdaptivePartProgress {
  part:number;state:string;events:number;deltas:number;outputChars:number;elapsedMs?:number;
}
interface SamAdaptiveProgress {
  batchId:string;parts:number;started:number;completed:number;failed:number;
  perPart:SamAdaptivePartProgress[];
  phase?:string;preparationElapsedMs?:number;validationElapsedMs?:number;totalElapsedMs?:number;
  continuousRetry?:SamAdaptiveProgress|null;continuousRetryBatchId?:string|null;
}
interface SamAdaptiveReport {
  sourceSha256:string;sparkSceneKey:string;planSha256:string;
  batchId:string;status:string;parts:number;startedParts:number;completedParts:number;
  failedParts:number;skippedParts:number;estimatedSamUsd:number;
  derivedFramesWithMasks:number|null;derivedSeamsVerified:boolean|null;
  automaticRetryOfBatchId?:string|null;automaticRetryBatchId?:string|null;automaticRetryStatus?:string|null;
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
    <section class="sam-stage3" [class.guided]="guided()" [attr.aria-busy]="sourcePending() || checking() || processing()">
      <header class="sam-heading">
        <h2>{{guided()?(es?'Costo antes de analizar':'Cost before analysis'):(es?'Etapa 3 · SAM 3.1':'Stage 3 · SAM 3.1')}}</h2>
      </header>
      @if(studyId() && videoSha256()) {
        <div class="sam-actions">
          <div class="sam-quote" aria-live="polite">
            <span class="sam-status" [class.available]="freeCacheAvailable()"><i aria-hidden="true"></i>{{statusLabel()}}</span>
            <div class="sam-price">
              @if(preflight(); as pf) {
                @if(!apiEnabled()) {
                  <div class="sam-price-line"><strong class="free-cost">US$ 0.00</strong><small>{{es?'Solo caché · sin llamadas a Meta':'Cache only · no Meta calls'}}</small></div>
                } @else {
                <div class="sam-price-line">
                  <strong [class.paid-cost]="!freeCacheAvailable()" [class.free-cost]="freeCacheAvailable()">{{freeCacheAvailable()?'US$ 0.00':((groundingRequired() || reviewBudget()>0 || reviewOnlyRequired() || adaptiveQuote()?.requiresNewSparkCall)?(es?'Estimado US$ ':'Estimated US$ '):'US$ ')+costLabel(displayedCost())}}</strong>
                  <small>{{freeCacheAvailable()?(es?'desde caché · sin cobro':'from cache · no charge'):((groundingRequired() || reviewBudget()>0 || reviewOnlyRequired() || adaptiveQuote()?.requiresNewSparkCall)?(es?'Costo estimado · no es una factura':'Estimated cost · not an invoice'):(es?'Usará créditos · estimado':'Uses credits · estimate'))}}</small>
                </div>
                @if(!stageRetryQuote() && !freeCacheAvailable() && groundingRequired()) {
                  <small class="sam-meta-estimate">SAM {{es?'estimado':'estimate'}} US$ {{costLabel(pf.estimatedCostUsd)}} + Spark {{es?'hasta':'up to'}} US$ {{groundingQuote()!.maximumEstimatedCostUsd.toFixed(2)}}</small>
                }
                @if(segmentedMode() && adaptiveQuote(); as aq) {
                  <small class="sam-meta-estimate">{{es?'1 solicitud SAM · video completo':'1 SAM request · complete video'}} ·
                    {{es?'cuadros facturables':'billable frames'}}: {{aq.billableFrames}}</small>
                  @if(aq.requiresNewSparkCall) {
                    <small class="sam-meta-estimate">{{es?'Incluye Spark estimado US$ ':'Includes Spark estimated US$ '}}{{groundingQuote()?.maximumEstimatedCostUsd?.toFixed(2)}} · {{es?'para localizar y verificar el objeto':'to locate and verify the object'}}</small>
                  }
                }
                @if(!freeCacheAvailable() && !reviewOnlyRequired() && reviewBudget()>0) {
                  <small class="sam-meta-estimate">{{es?'Incluye revisión de suspensión si hace falta: hasta US$ ':'Includes suspension review if needed: up to US$ '}}{{reviewBudget().toFixed(2)}}</small>
                }
                @if(freeCacheAvailable()) {
                  <small class="sam-meta-estimate">{{es?'Enviar a Meta':'Send to Meta'}}: US$ {{pf.estimatedCostUsd.toFixed(2)}} {{es?'estimados':'estimated'}}</small>
                }
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
              <button type="button" class="sam-refresh" (click)="voidCheck()" [disabled]="!hasSource() || checking() || processing() || sending() || connection.busy()">{{!apiEnabled()?(es?'Actualizar caché':'Refresh cache'):(es?'Actualizar costo':'Refresh cost')}}</button>
              <button type="button" class="sam-submit" (click)="process()" [disabled]="!canProcess()">{{processing() || connection.busy()?(es?'Procesando…':'Processing…'):(es?'Procesar':'Process')}}</button>
            </div>
            <fieldset class="sam-sources" [disabled]="sourcesLocked()">
              <legend>{{es?'Fuentes de procesamiento':'Processing sources'}}</legend>
              <label><input type="checkbox" [disabled]="sourcesLocked()" [checked]="apiEnabled()" (change)="selectSource('api',$any($event.target).checked)"/> API Meta</label>
              <label><input type="checkbox" [disabled]="sourcesLocked()" [checked]="cacheEnabled()" (change)="selectSource('cache',$any($event.target).checked)"/> {{es?'Caché + SQLite':'Cache + SQLite'}}</label>
            </fieldset>
            <small class="sam-source-help">{{sourceDescription()}}</small>
            @if(mode()==='api-meta' && stageRetryQuote(); as retry) {
              <div class="sam-stage-retry" role="group" [attr.aria-label]="es?'Reintentar solo SAM':'Retry SAM only'">
                <strong>{{es?'Retomar desde la etapa SAM':'Continue from the SAM stage'}}</strong>
                <p>{{es?'Spark y el MP4 original ya preparados se reutilizan. No se repiten las etapas anteriores; Meta debe procesar otra vez SAM y puede volver a cobrar.':
                  'The previous Spark localization and prepared MP4 are reused. Earlier stages will not run again; Meta must process SAM again and may charge again.'}}</p>
                <div class="sam-retry-price">{{es?'Costo máximo de un NUEVO intento SAM':'Maximum cost of a NEW SAM request'}}:
                  <strong>US$ {{costLabel(retry.estimatedSamUsd)}}</strong></div>
                @if(!retry.alreadyRequested) {
                  <button type="button" class="sam-retry-button" [disabled]="!canStageRetry()"
                    (click)="retryStage()">{{es?'Reintentar SAM · US$ ':'Retry SAM · US$ '}}{{costLabel(retry.estimatedSamUsd)}}</button>
                } @else {
                  <button type="button" (click)="recoverStageRetry()" [disabled]="!!processing()||connection.busy()">
                    {{es?'Consultar reintento guardado · sin nuevo cobro':'Inspect saved retry · no new charge'}}
                  </button>
                }
                @if(!tokenEntered()){<button type="button" class="sam-settings-link" (click)="openTokenDialog()">
                  {{es?'Registrar token Meta':'Enter Meta key'}}</button>}
              </div>
            } @else if(mode()==='api-meta' && !processing() && !checking() && preflight()) {
              <button type="button" class="sam-retry-discover" (click)="findStageRetry()">
                {{es?'Buscar reintento SAM pendiente':'Find available SAM retry'}}
              </button>
            }
          </div>
          @if(checking()) {
            <app-lab-activity class="sam-progress" [label]="es?'Calculando costo…':'Estimating cost…'"
              [detail]="cacheEnabled()?(es?'Comprobando fotogramas y caché. No se consumen créditos.':'Checking frames and cache. No credits are used.'):(es?'Comprobando el video y el costo de un análisis nuevo. No se consumen créditos.':'Checking the video and the cost of a new analysis. No credits are used.')"/>
          } @else if(processing() && !segmentedMode()) {
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
              <small class="sam-stage-help">{{freeCacheAvailable()?(es?'Recuperando las mediciones guardadas, sin llamadas a Meta.':'Loading saved measurements without Meta calls.'):(es?'El video completo se envía a SAM en una solicitud, sin cortes ni reintentos automáticos. Las barras indican actividad; Meta no informa un porcentaje exacto.':'The complete video is sent to SAM in one request, without cuts or automatic retries. Bars indicate activity; Meta does not report an exact percentage.')}}</small>
            </div>
          }
          @if(!checking() && !processing() && !connection.busy()) {
          @if(!checking() && (reusableEvidence()?.excludedResults??0)>0) {
            <small class="sam-meta-estimate">{{es?
              'Se excluyeron '+reusableEvidence()!.excludedResults+' resultados guardados por mediciones insuficientes. Se conserva el historial.':
              reusableEvidence()!.excludedResults+' saved results excluded because measurements were insufficient. History is preserved.'}}</small>
          }
          <div class="sam-message" [class.warning]="!freeCacheAvailable() && !canProcess() && !checking() && !sending()" role="status">
            @if(!hasSource()) {
              <span>{{es?'Selecciona al menos una fuente para procesar.':'Select at least one processing source.'}}</span>
            } @else if(error()) {
              <span>{{error()}}</span>

            } @else if(adaptiveResult(); as batch) {
              <span>{{batch.parts===1?(es?'SAM · video completo registrado':'SAM · complete video recorded'):(es?'Resultado segmentado anterior':'Previous segmented result')}}:
                {{batch.completedParts}}/{{batch.parts}} {{es?'solicitudes completas':'requests completed'}} ·
                {{batch.derivedFramesWithMasks??0}} {{es?'máscaras':'masks'}} ·
                {{batch.parts===1?(batch.derivedSeamsVerified===true?(es?'continuidad verificada':'continuity verified'):(es?'verificación pendiente':'verification pending')):(batch.derivedSeamsVerified===true?(es?'empalmes coherentes':'seams consistent'):(es?'revisión de empalmes pendiente':'seam review pending'))}}.
                {{es?'No se declara validación científica automática.':'No automatic scientific validation is claimed.'}}</span>
            } @else if(validationState()==='ready') {
              <span>{{es?'Procesado · resultados disponibles.':'Processed · results available.'}}</span>
            } @else if(!mediaReady()) {
              <span>{{es?'Esperando que termine de guardarse el video original.':'Waiting for the original video to finish saving.'}}</span>
            } @else if(freeCacheAvailable()) {
              <span>{{es?'Resultado disponible. Pulsa Procesar para cargarlo sin consumir créditos.':'Saved result available. Press Process to load it without spending credits.'}}</span>
            } @else if(!apiEnabled()) {
              <span>{{processBlockedReason()}}</span>
            } @else if(!canProcess()) {
              @if(!tokenEntered()) {
                <button type="button" class="sam-token-needed" (click)="openTokenDialog()" aria-haspopup="dialog">
                  <span aria-hidden="true">⚠</span>
                  {{es?'Falta el token Meta · Configurar token':'Meta token missing · Configure token'}}
                </button>
              } @else {
                <span aria-hidden="true">⚠</span><span>{{processBlockedReason()}}</span>
              }
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
            <p>{{sourceDescription()}}</p>
            @if(preflight(); as pf) {
              <p>{{(!apiEnabled() || freeCacheAvailable())?(es?'Costo de recuperar las mediciones':'Cost of loading measurements'):(es?'Estimación completa para enviar a Meta':'Full estimate for sending to Meta')}}: US$ {{displayedCost().toFixed(4)}} · {{pf.codec}}</p>
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
            <p>{{tokenEntered()?(es?'Token registrado para esta sesión':'Key registered for this session'):(es?'Token en Configuración':'API key in Settings')}} · {{apiAddress()}}
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

      <dialog #tokenDialog class="sam-token-dialog" aria-labelledby="sam-token-title" aria-describedby="sam-token-help"
        (cancel)="$event.preventDefault(); closeTokenDialog()" (close)="clearTokenDraft()">
        <form (submit)="$event.preventDefault(); acceptToken()">
          <h2 id="sam-token-title">{{es?'Configurar token Meta':'Configure Meta token'}}</h2>
          <p id="sam-token-help">{{es?'Ingresa tu token para continuar en el laboratorio. Solo permanece en esta sesión.':'Enter your token to continue in the laboratory. It stays only in this session.'}}</p>
          <label for="sam-token-draft">{{es?'Token de API':'API token'}}</label>
          <input #tokenDraft id="sam-token-draft" type="password" maxlength="1024" placeholder="LLM_…" autocomplete="off"
            autocapitalize="off" autocorrect="off" spellcheck="false" [disabled]="connection.busy()"
            [attr.aria-invalid]="tokenInvalid()" [attr.aria-describedby]="tokenInvalid()?'sam-token-error':null"
            (input)="tokenInvalid.set(false)"/>
          @if(tokenInvalid()) {<p id="sam-token-error" class="sam-token-error" role="alert">{{es?'Revisa el formato: debe comenzar con LLM_ y no contener espacios.':'Check the format: it must start with LLM_ and contain no spaces.'}}</p>}
          <p class="sam-token-note">{{es?'Aceptar registra el token. El análisis comienza cuando pulses Procesar con el costo listo.':'Accept registers the token. Analysis starts when you press Process with the cost ready.'}}</p>
          <div class="sam-token-buttons">
            <button type="button" (click)="closeTokenDialog()">{{es?'Cancelar':'Cancel'}}</button>
            <button type="submit" class="sam-token-accept" [disabled]="connection.busy()">{{es?'Aceptar':'Accept'}}</button>
          </div>
        </form>
      </dialog>
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
    .sam-sources{display:flex;flex-wrap:wrap;gap:8px 24px;min-width:0;width:100%;box-sizing:border-box;border:0;padding:8px 0 0;margin:8px 0 0}
    .sam-sources legend{font-size:12px;color:#adc2d8;padding:0}
    .sam-sources label{display:flex;align-items:center;gap:9px;min-height:44px;font-size:14px;cursor:pointer}
    .sam-sources input{width:18px;height:18px;margin:0;accent-color:#8bdfc7}
    .sam-sources:disabled label{opacity:.65;cursor:default}
    .sam-source-help{display:block;width:100%;color:#adc2d8;font-size:12px;line-height:1.6}
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
    .sam-stage-retry{flex-basis:100%;margin-top:14px;padding:13px 14px;border:1px solid #d6a453;border-radius:8px;background:#2b271a;color:#ffe8b8}
    .sam-stage-retry>strong{font-size:13px}.sam-stage-retry p{font-size:11px;margin:7px 0;color:#f4d3a2}
    .sam-retry-price{font-size:12px;margin:10px 0}.sam-retry-price strong{font-size:19px;margin-left:6px;color:#fff0ce}
    .sam-actions .sam-retry-button:enabled{background:#6ee5c1;color:#08251f;border-color:#6ee5c1;font-weight:700}
    .sam-retry-discover{margin-top:11px;font-size:11px;align-self:flex-start}
    @keyframes sam-ready-pulse{0%,100%{box-shadow:0 0 0 0 rgba(74,222,128,0)}50%{box-shadow:0 0 0 5px rgba(74,222,128,.24)}}
    @media(prefers-reduced-motion:reduce){.sam-actions .sam-submit:enabled{animation:none;transition:none}}
    .sam-message{display:flex;align-items:baseline;flex-wrap:wrap;gap:5px;margin-top:16px;padding-top:13px;border-top:1px solid #343e4a;color:#a9c8da;font-size:12px;line-height:1.55}
    .sam-message.warning,.sam-caution{color:#ffbd52}
    .sam-actions .sam-settings-link,.sam-settings-link{border:0;background:none;padding:0;color:#69b7ff;cursor:pointer;font:inherit;text-align:left}
    .sam-actions .sam-token-needed{display:flex;align-items:center;gap:8px;width:100%;padding:11px 13px;border:1px solid #f87171;background:#451a24;color:#ffc3c9;font-weight:700;line-height:1.5;text-align:left;animation:sam-token-pulse 2s ease-in-out infinite}
    .sam-token-needed:focus-visible{outline:2px solid #fecaca;outline-offset:3px}
    @keyframes sam-token-pulse{0%,100%{box-shadow:0 0 0 0 #f8717100}50%{box-shadow:0 0 0 4px #f871713d;background:#60202d}}
    .sam-token-dialog{box-sizing:border-box;width:min(460px,calc(100vw - 32px));max-height:calc(100dvh - 32px);padding:26px;border:1px solid #38526c;border-radius:16px;background:#101e2d;color:#e7f2ff;box-shadow:0 24px 80px #0008}
    .sam-token-dialog::backdrop{background:#03101cbb;backdrop-filter:blur(4px)}
    .sam-token-dialog h2{margin:0 0 12px;font-size:20px}.sam-token-dialog p{color:#acc4db;font-size:13px;line-height:1.6}
    .sam-token-dialog label{display:block;margin:20px 0 8px;font-weight:650}
    .sam-token-dialog input{box-sizing:border-box;width:100%;padding:13px;border:1px solid #57738d;border-radius:8px;background:#091320;color:#e7f2ff;font:inherit}
    .sam-token-dialog input:focus{outline:2px solid #6ee5c1;outline-offset:2px}.sam-token-dialog .sam-token-error{color:#ffadb7}.sam-token-dialog .sam-token-note{font-size:12px}
    .sam-token-buttons{display:flex;justify-content:flex-end;gap:10px;margin-top:22px}.sam-token-buttons button{min-height:42px;padding:10px 18px;border:1px solid #476078;border-radius:8px;background:transparent;color:#deedff;cursor:pointer;font:inherit;font-weight:650}
    .sam-token-buttons .sam-token-accept{background:#6ee5c1;color:#08251f;border-color:#6ee5c1}.sam-token-buttons button:focus-visible{outline:2px solid #6ee5c1;outline-offset:3px}.sam-token-buttons button:disabled{opacity:.5;cursor:not-allowed}
    @media(prefers-reduced-motion:reduce){.sam-actions .sam-token-needed{animation:none}}
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

    .guided.sam-stage3{padding:0;margin:18px 0 0;background:transparent;border:0;box-shadow:none}
    .guided .sam-heading{display:block;margin-bottom:12px}
    .guided .sam-heading h2{font-size:12px;color:#cce7dd;margin-bottom:9px}
    .guided .sam-api-selector{display:flex;gap:7px;flex-wrap:wrap;margin-top:9px;font-size:10px}
    .guided .sam-api-selector select{max-width:100%;border:1px solid #344e69;border-radius:6px;background:#0a1725;color:#bcd6ee;font-size:11px;padding:7px}
    .guided .sam-actions{border:1px solid #376b58;border-radius:10px;background:#102921;padding:17px}
    .guided .sam-quote{display:block}
    .guided .sam-status{display:inline-flex;margin-bottom:12px;font-size:10px;color:#bbd6c7;background:transparent}
    .guided .sam-price{display:grid;gap:7px}
    .guided .sam-price-line{display:block}
    .guided .sam-price strong{font-size:31px;line-height:1.25;white-space:normal;overflow-wrap:anywhere;color:#e8fff3;letter-spacing:-.5px}
    .guided .sam-price small{display:block;color:#a3c5b4;font-size:10px;line-height:1.65;margin-top:4px}
    .guided .sam-video-meta{display:block;font-size:10px;margin-top:11px;color:#a4bfad}
    .guided .sam-buttons{display:flex;margin:16px 0 0;gap:8px;flex-wrap:wrap}
    .guided .sam-buttons .sam-submit{flex:1;min-width:150px;order:-1;min-height:41px}
    .guided .sam-submit:enabled{background:#6ee5c1;border-color:#6ee5c1;color:#08251f}
    .guided .sam-submit:enabled:hover{background:#88edce}
    .guided .sam-refresh{font-size:10px;border-color:#385b4b;background:transparent;color:#a8ccbb}
    .guided .sam-message{font-size:11px;line-height:1.65;color:#b2cebf}
    .guided .sam-message.warning,.guided .sam-consent{color:#ead19e}
    .guided .sam-technical{border-color:#2f455e;margin-top:14px;color:#9eb9d4}
    .guided .sam-technical summary{font-size:10px}
    .guided .sam-detail-body{font-size:10px;line-height:1.7}
    .guided .sam-stages{border-color:#345449}
    .guided .sam-stage-row>div{font-size:10px}
    .guided .sam-stage-help{font-size:9px;line-height:1.65}
  `]

})
export class SamVideoReplay implements OnChanges, OnDestroy {
  apiAddress():string {return apiOrigin(this.localApiPort());}

  private readonly http=inject(HttpClient,{optional:true});
  readonly connection=inject(LabConnection);
  readonly localApiPort=this.connection.port;
  constructor(){this.connection.changed.pipe(takeUntilDestroyed()).subscribe(()=>{this.closeTokenDialog();this.resetServerCheck();this.scheduleCheck();});}
  @ViewChild('tokenDialog',{static:true}) private tokenDialog!:ElementRef<HTMLDialogElement>;
  @ViewChild('tokenDraft',{static:true}) private tokenDraft!:ElementRef<HTMLInputElement>;
  readonly tokenInvalid=signal(false);
  private tokenFocusFrame:number|null=null;
  openTokenDialog():void {
    if(this.connection.busy())return;
    this.clearTokenDraft();
    this.tokenDialog.nativeElement.showModal();
    this.tokenDraft.nativeElement.focus();
  }
  closeTokenDialog():void {
    this.tokenDialog?.nativeElement.close();
    this.clearTokenDraft();
  }
  clearTokenDraft():void {
    if(this.tokenDraft)this.tokenDraft.nativeElement.value='';
    this.tokenInvalid.set(false);
  }
  acceptToken():void {
    if(this.connection.busy())return;
    if(!this.connection.confirmTokenFrom(this.tokenDraft.nativeElement)){
      this.tokenInvalid.set(true);this.tokenDraft.nativeElement.focus();return;
    }
    this.closeTokenDialog();
    this.tokenFocusFrame=requestAnimationFrame(()=>{
      this.tokenFocusFrame=null;
      this.tokenDialog.nativeElement.closest('section')?.querySelector<HTMLButtonElement>('.sam-submit:enabled')?.focus();
    });
  }
  readonly guided=input(false);
  readonly sourcePending=input(false);
  readonly recoveryRunning=input(false);
  // True once the original is registered on the server; browser decoding is independent.
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
  readonly mode=signal<SamMode>('api-meta');
  // Keep the verified archive transport and historical batch recovery.
  // Every new quote and submission must contain one complete-video SAM call.
  // Legacy single-call methods remain available for old embedded clients.
  segmentedMode():boolean {return true;}
  apiEnabled():boolean {return this.mode()==='api-meta' || this.mode()==='api-meta-cache' || this.mode()==='api-meta-adaptive';}
  cacheEnabled():boolean {return this.mode()==='cache-only' || this.mode()==='api-meta-cache' || this.mode()==='api-meta-adaptive';}
  hasSource():boolean {return this.apiEnabled() || this.cacheEnabled();}
  sourcesLocked():boolean {return this.processing() || this.sending() || this.connection.busy() || this.recoveryRunning();}
  private paidMode():'api-meta'|'api-meta-cache' {return this.cacheEnabled()?'api-meta-cache':'api-meta';}
  selectSource(source:'api'|'cache',selected:boolean):void {
    if(this.sourcesLocked())return;
    const api=source==='api'?selected:this.apiEnabled(),cache=source==='cache'?selected:this.cacheEnabled();
    this.mode.set(api?(cache?'api-meta-cache':'api-meta'):(cache?'cache-only':'none'));
    this.closeTokenDialog();this.resetServerCheck();this.scheduleCheck();
  }
  sourceDescription():string {
    if(!this.hasSource())return this.es?'Selecciona al menos una fuente para procesar.':'Select at least one processing source.';
    if(!this.apiEnabled())return this.es?'Solo resultados guardados. Si no hay caché válida, no se enviará nada a Meta.':'Saved results only. Without valid cache, nothing is sent to Meta.';
    return this.cacheEnabled()?(this.es?'Usa caché verificada; si falta, procesa con API Meta.':'Uses verified cache; otherwise processes with Meta API.'):
      (this.es?'Análisis nuevo con API Meta, sin reutilizar caché ni consultar SQLite.':'New analysis with Meta API, without reusing cache or querying SQLite.');
  }
  readonly reusableEvidence=signal<SamReusableResult|null>(null);
  readonly adaptiveQuote=signal<SamAdaptiveQuote|null>(null);
  readonly adaptiveProgress=signal<SamAdaptiveProgress|null>(null);
  readonly adaptiveResult=signal<SamAdaptiveReport|null>(null);
  readonly stageRetryQuote=signal<SamStageRetryQuote|null>(null);
  readonly stageRetryActive=signal(false);
  private adaptivePollHandle:ReturnType<typeof setInterval>|null=null;
  readonly stageTraceStarted=signal(false);
  readonly traceClock=signal(0);
  private traceTicker:ReturnType<typeof setInterval>|null=null;
  private traceStartedAt=0;
  private sparkFinishedAt=0;
  private samStartedAt=0;
  private samFinishedAt=0;
  private samWasReused=false;
  private reviewStartedAt=0;
  private reviewFinishedAt=0;
  private reviewFailed=false;
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
  private traceAwaitingRecovery=false;
  private sparkWasNeeded=false;
  private startStageTrace(cached=false):void {
    if(this.traceTicker!==null)clearInterval(this.traceTicker);
    this.traceStartedAt=Date.now();this.traceClock.set(this.traceStartedAt);
    this.sparkFinishedAt=cached?this.traceStartedAt:0;this.viewStartedAt=0;this.viewFinishedAt=0;this.traceFailed=false;this.traceAwaitingRecovery=false;
    this.samStartedAt=0;this.samFinishedAt=0;this.samWasReused=cached;
    this.reviewStartedAt=0;this.reviewFinishedAt=0;this.reviewFailed=false;
    this.sparkWasNeeded=!cached && (this.segmentedMode()?!!this.adaptiveQuote()?.requiresNewSparkCall:this.groundingRequired());
    this.stopSparkPoll();this.sparkStages.set([]);
    if(this.sparkWasNeeded)this.sparkPoll=setInterval(()=>{void this.pollSpark();},1000);
    this.stageTraceStarted.set(true);
    this.traceTicker=setInterval(()=>this.traceClock.set(Date.now()),250);
  }
  stageStateLabel(state:string):string {
    if(state==='RECOVERY_REQUIRED')return this.es?'Retomar con código':'Resume with code';
    return state==='SKIPPED'?(this.es?'No necesario':'Not needed'):state==='COMPLETED'?(this.es?'Listo':'Done'):state==='FAILED'?(this.es?'Falló':'Failed'):
      state==='IN_PROGRESS'?(this.es?'En curso':'Running'):(this.es?'Pendiente':'Pending');
  }
  stageRows():{key:string;label:string;state:string;ms:number;percent?:number|null}[] {
    const p=this.adaptiveProgress(),now=this.traceClock(),phase=p?.phase;
    if(this.stageRetryActive())return [
      {key:'spark-reused',label:this.es?'Spark · reutilizado':'Spark · reused',state:'COMPLETED',ms:0},
      {key:'clip-reused',label:this.es?'MP4 · reutilizado':'MP4 · reused',state:'COMPLETED',ms:p?.preparationElapsedMs??0},
      {key:'sam-0',label:this.es?'SAM · reintento manual':'SAM · manual retry',
        state:p?.perPart[0]?.state??(this.traceFailed?'FAILED':'IN_PROGRESS'),
        ms:p?.perPart[0]?.elapsedMs??Math.max(0,now-this.traceStartedAt)},
      {key:'validation',label:this.es?'Verificación':'Verification',
        state:phase==='COMPLETED'?'COMPLETED':phase==='FAILED'?'FAILED':
          phase==='VALIDATING'?'IN_PROGRESS':'NOT_STARTED',ms:p?.validationElapsedMs??0},
      {key:'view',label:this.es?'Validación de resultados':'Result validation',
        state:this.viewFinishedAt?'COMPLETED':this.traceFailed?'FAILED':
          this.traceAwaitingRecovery?'RECOVERY_REQUIRED':this.viewStartedAt?'IN_PROGRESS':'NOT_STARTED',
        ms:this.viewStartedAt?Math.max(0,(this.viewFinishedAt||now)-this.viewStartedAt):0}
    ];
    if(this.samWasReused && this.segmentedMode())return [
      {key:'cache',label:this.es?'Caché verificada':'Verified cache',state:'COMPLETED',ms:0},
      {key:'view',label:this.es?'Validación de resultados':'Result validation',
       state:this.viewFinishedAt?(this.traceFailed?'FAILED':'COMPLETED'):this.traceAwaitingRecovery?'RECOVERY_REQUIRED':this.viewStartedAt?'IN_PROGRESS':this.traceFailed?'FAILED':'NOT_STARTED',
       ms:this.viewStartedAt?Math.max(0,(this.viewFinishedAt||now)-this.viewStartedAt):0}];
    const sparkDone=!!this.sparkFinishedAt;
    const rows:{key:string;label:string;state:string;ms:number;percent?:number|null}[]=[{key:'spark',label:this.sparkWasNeeded?'Spark':(this.es?'Spark · reutilizado':'Spark · reused'),
      state:sparkDone?'COMPLETED':this.traceFailed?'FAILED':'IN_PROGRESS',
      ms:this.sparkWasNeeded?Math.max(0,(this.sparkFinishedAt||now)-this.traceStartedAt):0},
      {key:'preparation',label:this.es?'Preparación del video':'Video preparation',
       state:!sparkDone?'NOT_STARTED':phase==='PREPARING'||!p?(this.traceFailed?'FAILED':'IN_PROGRESS'):
         phase==='FAILED'&&!p.started?'FAILED':'COMPLETED',
       ms:p?.preparationElapsedMs??(sparkDone?Math.max(0,now-this.sparkFinishedAt):0)}];
    if(this.sparkWasNeeded){
      const names=[['prepare','Preparar video','Prepare video'],['references','Extraer 50 imágenes','Extract 50 images'],['upload','Subir video a Spark','Upload video to Spark'],['inference','Respuesta de Spark','Spark response'],['verify','Validar respuesta','Validate response'],['target-review','Verificar objeto con Spark','Verify object with Spark']];
      const reviewOnly=this.sparkStages().some(s=>s.key==='target-review') && !this.sparkStages().some(s=>s.key==='inference');
      const stages=this.sparkStages();
      const initial=names.filter(([key])=>(!reviewOnly||key==='prepare'||key==='target-review') &&
        (key!=='target-review'||stages.some(s=>s.key===key)));
      const extra=stages.filter(s=>/^round-[23]-/.test(s.key)).map(s=>{
        const match=/^round-([23])-(.+)$/.exec(s.key)!;
        const name=names.find(n=>n[0]===match[2]);
        return [s.key,`Ronda ${match[1]} · ${name?.[1]??match[2]}`,`Round ${match[1]} · ${name?.[2]??match[2]}`];
      });
      rows.splice(0,1,...[...initial,...extra].map(([key,es,en])=>{const s=stages.find(x=>x.key===key);return {key:'spark-'+key,label:this.es?es:en,state:s?.state??'NOT_STARTED',ms:s?.elapsedMs??0,percent:s?.percent};}));
    }
    if(!this.segmentedMode()){
      rows.splice(rows.length-1,1,{key:'sam',label:this.samWasReused?(this.es?'SAM · caché':'SAM · cache'):(this.es?'SAM · video completo':'SAM · full video'),
        state:this.samWasReused||this.samFinishedAt?'COMPLETED':!this.samStartedAt?'NOT_STARTED':this.traceFailed?'FAILED':'IN_PROGRESS',
        ms:this.samStartedAt?Math.max(0,(this.samFinishedAt||now)-this.samStartedAt):0});
      if(this.reviewStartedAt)rows.push({key:'review',label:this.es?'Revisión de suspensión · Spark':'Suspension review · Spark',
        state:this.reviewFailed?'FAILED':this.reviewFinishedAt?'COMPLETED':'IN_PROGRESS',
        ms:Math.max(0,(this.reviewFinishedAt||now)-this.reviewStartedAt)});
      rows.push({key:'view',label:this.es?'Validación de resultados':'Result validation',
        state:this.viewFinishedAt?(this.traceFailed?'FAILED':'COMPLETED'):this.traceAwaitingRecovery?'RECOVERY_REQUIRED':this.viewStartedAt?'IN_PROGRESS':this.traceFailed?'FAILED':'NOT_STARTED',
        ms:this.viewStartedAt?Math.max(0,(this.viewFinishedAt||now)-this.viewStartedAt):0});
      return rows;
    }
    for(let i=0;i<(this.adaptiveQuote()?.parts??0);i++){
      const part=p?.perPart.find(x=>x.part===i);
      rows.push({key:'sam-'+i,label:this.adaptiveQuote()?.parts===1?
        (this.es?'SAM · video completo':'SAM · full video'):'SAM · '+(this.es?'segmento ':'segment ')+(i+1),
        state:part?.state??'NOT_STARTED',ms:part?.elapsedMs??0});
    }
    rows.push({key:this.adaptiveQuote()?.parts===1?'validation':'join',label:this.adaptiveQuote()?.parts===1?
      (this.es?'Verificación':'Verification'):(this.es?'Unión y verificación':'Join and verification'),
      state:phase==='COMPLETED'?'COMPLETED':phase==='FAILED'?'FAILED':
        phase==='VALIDATING'?'IN_PROGRESS':'NOT_STARTED',ms:p?.validationElapsedMs??0});
    if((this.adaptiveQuote()?.continuousRetryEstimatedUsd??0)>0 || p?.continuousRetry){
      const r=p?.continuousRetry,rp=r?.phase,result=this.adaptiveResult();
      const notNeeded=!!result && !result.automaticRetryOfBatchId && !result.automaticRetryBatchId && !r;
      const waiting=notNeeded?'SKIPPED':'NOT_STARTED';
      rows.push({key:'retry-preparation',label:this.es?'Reintento · preparar video completo':'Retry · prepare full video',
        state:!r?waiting:rp==='PREPARING'?'IN_PROGRESS':rp==='FAILED'&&!r.started?'FAILED':'COMPLETED',ms:r?.preparationElapsedMs??0},
        {key:'retry-sam',label:this.es?'SAM · reintento único, video completo':'SAM · one retry, full video',
         state:r?.perPart[0]?.state??(rp==='FAILED'?'FAILED':waiting),ms:r?.perPart[0]?.elapsedMs??0},
        {key:'retry-validation',label:this.es?'Reintento · verificar resultado':'Retry · verify result',
         state:!r?waiting:rp==='COMPLETED'?'COMPLETED':rp==='FAILED'?'FAILED':rp==='VALIDATING'?'IN_PROGRESS':'NOT_STARTED',ms:r?.validationElapsedMs??0});
    }
    rows.push({key:'view',label:this.es?'Validación de resultados':'Result validation',
      state:this.viewFinishedAt?(this.traceFailed?'FAILED':'COMPLETED'):this.traceAwaitingRecovery?'RECOVERY_REQUIRED':this.viewStartedAt?'IN_PROGRESS':this.traceFailed?'FAILED':'NOT_STARTED',
      ms:this.viewStartedAt?Math.max(0,(this.viewFinishedAt||now)-this.viewStartedAt):0});
    return rows.map(row=>this.traceAwaitingRecovery && (row.state==='IN_PROGRESS' || row.state==='NOT_STARTED')
      ? {...row,state:'RECOVERY_REQUIRED'} : row);
  }
  private adaptiveUrl():string {
    return this.base+'/'+this.studyId()+'/sam-adaptive/'+this.videoRole();
  }
  private validStageRetryQuote(q:SamStageRetryQuote):boolean {
    return q.videoSha256===this.videoSha256() &&
      /^[a-f0-9]{32}$/.test(q.failedBatchId) && /^[a-f0-9]{32}$/.test(q.newBatchId) &&
      q.failedBatchId!==q.newBatchId && /^[a-f0-9]{32}$/.test(q.sceneRunId) &&
      /^[a-f0-9]{64}$/.test(q.sparkSceneKey) && /^[a-f0-9]{64}$/.test(q.planSha256) &&
      Number.isSafeInteger(q.frames) && q.frames>=50 && q.frames<=2500 &&
      Number.isInteger(q.targetCoreFrames) && q.targetCoreFrames>0 &&
      Number.isFinite(q.estimatedSamUsd) && q.estimatedSamUsd>0 && q.estimatedSamUsd<=.50;
  }
  async findStageRetry():Promise<void> {
    if(!this.http || this.mode()!=='api-meta' || !this.hasSource() ||
       this.processing() || this.checking() || this.sending())return;
    const url=this.adaptiveUrl(),sha=this.videoSha256(),port=this.localApiPort(),serial=this.serial;
    try{
      const q=await firstValueFrom(this.http.get<SamStageRetryQuote>(
        url+'/manual-stage-retry/quote').pipe(timeout(20000)));
      if(!this.disposed && this.serial===serial && url===this.adaptiveUrl() &&
        port===this.localApiPort() && sha===this.videoSha256() && this.validStageRetryQuote(q)){
        this.stageRetryQuote.set(q);this.error.set('');
      }
    }catch{
      if(!this.disposed && this.serial===serial && url===this.adaptiveUrl())
        this.stageRetryQuote.set(null);
    }
  }
  canStageRetry():boolean {
    const q=this.stageRetryQuote(),access=this.readiness();
    return this.mode()==='api-meta' && !!q && this.validStageRetryQuote(q) &&
      !q.alreadyRequested && !!access?.creditsConfirmedByOwner &&
      !!access.ownerApprovedPaidRequests && access.paidRequestsSwitchEnabled===true &&
      access.manualTokenCallsPermitted===true && this.tokenEntered() &&
      this.mediaReady() && !this.recoveryRunning() && !this.sourcePending() &&
      !this.processing() && !this.sending() && !this.checking() && !this.connection.busy();
  }
  async recoverStageRetry():Promise<void> {
    const q=this.stageRetryQuote();
    if(!q?.alreadyRequested || !this.http || this.processing() || this.connection.busy())return;
    const url=this.adaptiveUrl(),sha=this.videoSha256(),serial=this.serial;
    try{
      const report=await firstValueFrom(this.http.get<SamAdaptiveReport>(
        url+'/batches/'+q.newBatchId+'/report').pipe(timeout(10000)));
      if(serial!==this.serial || url!==this.adaptiveUrl() || sha!==this.videoSha256())return;
      if(report.batchId!==q.newBatchId || report.sourceSha256!==sha ||
         report.sparkSceneKey!==q.sparkSceneKey || report.planSha256!==q.planSha256 ||
         report.parts!==1 || report.startedParts!==1)throw Error('RETRY_REPORT_MISMATCH');
      this.adaptiveResult.set(report);
      if(report.status==='PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW' &&
         report.derivedSeamsVerified && (report.derivedFramesWithMasks??0)>0) {
        this.processingReset.emit();this.beginProcessing();this.startStageTrace(true);
        this.stageRetryActive.set(true);this.processingPhase.set('validating');
        this.viewStartedAt=Date.now();
        this.cacheUpdated.emit({studyId:this.studyId(),videoRole:this.videoRole(),sourceSha256:sha,
          analysisId:report.batchId,adaptiveBatchId:report.batchId,port:this.localApiPort()});
        this.error.set('');
      }else this.error.set(this.es?
        'El reintento SAM quedó inconcluso. No se enviará otra solicitud.':
        'The SAM retry is inconclusive. No further request will be sent.');
    }catch{this.error.set(this.es?
      'El reintento aún no tiene un informe final recuperable. Revisa su estado antes de cualquier otro análisis.':
      'The retry has no recoverable final report yet. Inspect its status before another analysis.');}
  }
  async retryStage():Promise<void> {
    if(!this.canStageRetry() || !this.http)return;
    const q=this.stageRetryQuote()!,token=this.connection.tokenForManualRequest();
    if(!token)return;
    const url=this.adaptiveUrl(),sha=this.videoSha256(),port=this.localApiPort(),serial=++this.serial;
    const current=()=>!this.disposed && this.serial===serial && url===this.adaptiveUrl() &&
      sha===this.videoSha256() && port===this.localApiPort() && this.mode()==='api-meta';
    // This explicit click authorizes one NEW potentially billable SAM request,
    // not a free continuation of the provider's incomplete SSE stream.
    this.processingReset.emit();this.beginProcessing();this.startStageTrace(true);
    this.stageRetryActive.set(true);this.processingPhase.set('sam');this.sending.set(true);
    this.adaptiveProgress.set(null);this.adaptiveResult.set(null);this.error.set('');
    this.stageRetryQuote.set({...q,alreadyRequested:true}); // fail closed on lost HTTP reply
    let opened=false,polling=false;
    const poll=async()=>{
      if(polling || !current())return;polling=true;
      try{
        const status=await firstValueFrom(this.http!.get<SamAdaptiveProgress>(
          url+'/batches/'+q.newBatchId+'/progress').pipe(timeout(8000)));
        if(current() && status.batchId===q.newBatchId)this.adaptiveProgress.set(status);
      }catch{/* no automatic provider retry */}
      finally{polling=false;}
    };
    this.adaptivePollHandle=setInterval(()=>{void poll();},2000);
    try{
      let report:SamAdaptiveReport|null=null;
      try{
        report=await firstValueFrom(this.http.post<SamAdaptiveReport>(
          url+'/manual-stage-retry',{
            failedBatchId:q.failedBatchId,newBatchId:q.newBatchId,
            videoSha256:sha,planSha256:q.planSha256,modelApiToken:token,
            maximumEstimatedCostUsd:q.estimatedSamUsd,
            confirmPotentialSecondCharge:true,confirmVideoTransfer:true
          },{headers:{'X-ProofLab-Manual-Action':'sam-adaptive-stage-retry'}})
          .pipe(timeout(70*60*1000)));
      }catch{
        // A lost/blocked reply may coexist with a running paid operation.
        // Reconciliation is read-only and must never POST again.
        try{report=await firstValueFrom(this.http.get<SamAdaptiveReport>(
          url+'/batches/'+q.newBatchId+'/report').pipe(timeout(8000)));}
        catch{report=null;}
      }
      if(!current())return;
      await poll();
      if(!report || report.batchId!==q.newBatchId || report.sourceSha256!==sha ||
         report.sparkSceneKey!==q.sparkSceneKey || report.planSha256!==q.planSha256 ||
         report.status!=='PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW' ||
         report.derivedSeamsVerified!==true || !(report.derivedFramesWithMasks!>0))
        throw Error('STAGE_RETRY_INCOMPLETE');
      this.adaptiveResult.set(report);this.processingPhase.set('validating');
      this.viewStartedAt=Date.now();opened=true;
      this.cacheUpdated.emit({studyId:this.studyId(),videoRole:this.videoRole(),sourceSha256:sha,
        analysisId:report.batchId,adaptiveBatchId:report.batchId,port});
    }catch{
      if(current()){
        this.error.set(this.es?
          'No se completó el reintento SAM. Consulta el resultado guardado; no se repetirá el envío. El intento anterior puede haber sido facturado.':
          'The SAM retry did not complete. Inspect its saved result; no second retry will be sent. The previous attempt may have been billed.');
        this.failProcessing();
      }
    }finally{
      if(this.adaptivePollHandle!==null)clearInterval(this.adaptivePollHandle);
      this.adaptivePollHandle=null;this.sending.set(false);
      if(!opened || !current())this.finishProcessing();
    }
  }
  private get base():string {
    return apiOrigin(this.localApiPort())+
      '/api/science/director/pendulum/sessions';
  }
  selectMode(event:Event):void {
    const mode=(event.target as HTMLSelectElement).value;
    if(this.sourcesLocked() || !['api-meta','api-meta-cache','api-meta-adaptive','cache-only','none'].includes(mode))return;
    this.mode.set(mode as SamMode);
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
    this.cacheStatus.set(null);this.reusableEvidence.set(null);
    this.preflight.set(null);
    this.groundingQuote.set(null);this.suspensionReviewQuote.set(null);
    this.readiness.set(null);
    this.transferConsent.set(false);
    this.paidConsent.set(false);
    this.retryConsent.set(false);
    this.adaptiveQuote.set(null);
    this.adaptiveProgress.set(null);
    this.adaptiveResult.set(null);
    this.stageRetryQuote.set(null);this.stageRetryActive.set(false);
    this.stageTraceStarted.set(false);this.traceAwaitingRecovery=false;
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
    if(!this.autoCheck() || !this.hasSource())return;
    queueMicrotask(()=>{
      if(!this.disposed && serial===this.serial && this.studyId() && this.videoSha256())
        void this.check();
    });
  }
  ngOnDestroy():void {
    if(this.tokenFocusFrame!==null)cancelAnimationFrame(this.tokenFocusFrame);
    this.closeTokenDialog();
    this.stopSparkPoll();
    this.disposed=true;this.serial++;this.offlineSerial++;
    if(this.adaptivePollHandle!==null)clearInterval(this.adaptivePollHandle);
    if(this.traceTicker!==null)clearInterval(this.traceTicker);this.traceTicker=null;
    this.adaptivePollHandle=null;
    if(this.processing() && !this.sending())this.finishProcessing();
  }
  cachedResultAvailable():boolean {
    if(!this.cacheEnabled())return false;
    if(this.segmentedMode()) {
      const r=this.reusableEvidence();
      return !!r?.cacheHit && r.quality?.usable===true && r.sourceSha256===this.videoSha256() &&
        /^[a-f0-9]{32}$/.test(r.analysisId??'') && (!r.adaptiveBatchId || r.adaptiveBatchId===r.analysisId);
    }
    const s=this.cacheStatus();
    return this.mode()==='api-meta-cache' && !!s && s.cacheHit===true &&
      s.status==='CACHED_META_SAM_3_1_RESPONSE_REQUIRES_VISUAL_REVIEW' &&
      s.sourceSha256===this.videoSha256() && !!s.analysisId;
  }
  canProcess():boolean {
    if(this.recoveryRunning() || !this.hasSource())return false;
    if(this.mode()==='api-meta' && this.stageRetryQuote())return false;
    if(this.segmentedMode() && this.cachedResultAvailable())return this.mediaReady() && !this.sourcePending() &&
      !this.processing() && !this.sending() && !this.checking() && !this.connection.busy();
    if(!this.apiEnabled() || this.traceAwaitingRecovery)return false;
    if(this.segmentedMode()) {
      const quote=this.adaptiveQuote(),ready=this.readiness(),spark=this.groundingQuote();
      if(this.adaptiveResult())return false;
      return !!quote && !!ready && this.validAdaptiveQuote(quote,this.videoSha256()) &&
        quote.sourceSha256===this.videoSha256() &&
        (quote.existingSparkSceneVerified || (quote.requiresNewSparkCall &&
          !!spark && spark.videoSha256===this.videoSha256() && spark.canRun &&
           spark.mode===this.paidMode() && (this.cacheEnabled() || (spark.freshRequired && !spark.cacheHit)) &&
          !spark.priorAttempt && spark.status==='NEEDS_LOCALIZATION' &&
          spark.maximumEstimatedCostUsd>0 && Number.isFinite(spark.maximumEstimatedCostUsd))) &&
        quote.canRunWithManualToken &&
        quote.parts===1 && quote.simultaneousRequests===1 &&
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
    if(this.segmentedMode() && this.cachedResultAvailable()) {
      const cached=this.reusableEvidence()!;
      this.processingReset.emit();this.beginProcessing();this.startStageTrace(true);this.viewStartedAt=Date.now();
      this.cacheUpdated.emit({studyId:this.studyId(),videoRole:this.videoRole(),sourceSha256:cached.sourceSha256,
        analysisId:cached.analysisId!,adaptiveBatchId:cached.adaptiveBatchId??undefined,port:this.localApiPort()});
      return;
    }
    if(this.segmentedMode()) {
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
    if(!this.hasSource())return this.es?'Selecciona una fuente':'Select a source';
    if(this.checking())return this.cacheEnabled()?(this.es?'Verificando caché':'Checking cache'):(this.es?'Calculando costo':'Estimating cost');
    if(this.cachedResultAvailable())return this.es?'Caché verificada':'Verified cache';
    if(!this.apiEnabled())return this.es?'Sin caché válida':'No valid cache';
    if(this.traceAwaitingRecovery)return this.es?'Resultado por recuperar':'Result awaiting recovery';
    if(this.segmentedMode()){
      const result=this.adaptiveResult();
      if(result)return result.completedParts===result.parts ?
        (this.es?'Solicitud SAM finalizada':'SAM request finished') :
        (this.es?'Solicitud SAM incompleta':'SAM request incomplete');
      if(this.adaptiveQuote())return this.es?'Video completo listo':'Complete video ready';
      return this.es?'Sin cotización':'No quote';
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
      Number.isFinite(quote.maximumEstimatedCostUsd) && quote.maximumEstimatedCostUsd>=0 &&
      (quote.maximumEstimatedCostUsd>=.025 || quote.cacheHit) && quote.maximumEstimatedCostUsd<=1.1 &&
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
    if(this.segmentedMode() && this.processingPhase()==='sam' && this.adaptiveProgress()?.continuousRetry)
      return this.es?'SAM · reintento con el video completo…':'SAM · retrying the full video…';
    if(this.processingPhase()==='sam')return this.es?'SAM siguiendo el objeto…':'SAM is tracking the object…';
    return this.es?'Preparando resultados…':'Preparing results…';
  }
  processingDetail():string {
    if(this.segmentedMode() && this.processingPhase()==='sam'){
      const progress=this.adaptiveProgress(),q=this.adaptiveQuote();
      if(progress?.continuousRetry)return this.es?
        'El lote no se validó. Se está realizando el único reintento autorizado, sin cortes ni otra consulta Spark.':
        'The batch was not validated. Running the single authorized retry, without cuts or another Spark call.';
      if(q?.parts===1)return this.es?
        'SAM procesa el video completo en una solicitud. Las barras muestran actividad, no un porcentaje de tiempo.':
        'SAM processes the complete video in one request. Bars show activity, not a time percentage.';
      return this.es?
        'Resultado segmentado anterior: '+(progress?.completed??0)+'/'+
        (q?.parts??0)+' solicitudes con recibo.':
        'Previous segmented result: '+(progress?.completed??0)+'/'+
        (q?.parts??0)+' requests with receipts.';
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
    if(!this.apiEnabled())return false;
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
  private requiredCalls():number {return (this.groundingRequired()?(this.groundingQuote()?.requiredSparkCalls??1)+1:1)+(this.reviewBudget()>0?1:0);}
  reviewOnlyRequired():boolean {
    if(!this.apiEnabled() || this.segmentedMode())return false;
    const r=this.suspensionReviewQuote();
    return this.cachedResultAvailable() && !!r && r.analysisId===this.cacheStatus()?.analysisId &&
      r.required && !r.priorAttempt && !r.evidence;
  }
  freeCacheAvailable():boolean {return this.cachedResultAvailable()&&!this.reviewOnlyRequired();}
  reviewBudget():number {
    return this.segmentedMode()?0:
      (this.preflight()?.suspensionReviewMaximumCostUsd??0);
  }
  private combinedSamCost(q:SamAdaptiveQuote):number {
    return q.maximumSamWithRetryEstimatedUsd??q.totalSamEstimatedUsd;
  }
  displayedCost():number {
    if(!this.apiEnabled() || this.freeCacheAvailable())return 0;
    if(this.mode()==='api-meta' && this.stageRetryQuote())return this.stageRetryQuote()!.estimatedSamUsd;
    if(this.segmentedMode())
      return (this.adaptiveQuote()?this.combinedSamCost(this.adaptiveQuote()!):0)+
        (this.adaptiveQuote()?.requiresNewSparkCall?this.groundingQuote()?.maximumEstimatedCostUsd??0:0);
    if(this.reviewOnlyRequired())return this.suspensionReviewQuote()!.maximumEstimatedCostUsd;
    return (this.preflight()?.estimatedCostUsd ?? 0)+this.reviewBudget()+
      (this.groundingRequired()?this.groundingQuote()!.maximumEstimatedCostUsd:0);
  }
  costLabel(value:number):string {return (Math.ceil(value*100-1e-8)/100).toFixed(2);}
  nextActionDescription():string {
    if(this.freeCacheAvailable())return this.es?'Resultado comprobado en SQLite. Procesar carga las mediciones sin llamadas a Meta.':'Checked SQLite result. Process loads measurements without Meta calls.';
    if(this.mode()==='api-meta' && this.stageRetryQuote())return this.es?
      'Reintentar SAM utiliza las etapas anteriores y muestra el costo de una nueva inferencia; no pulses Procesar.':
      'Retry SAM reuses earlier stages and shows the cost of a new inference; do not click Process.';
    if(this.segmentedMode()){
      const q=this.adaptiveQuote();
      return q?(this.es?
        'Procesar autoriza '+(q.requiresNewSparkCall?'localizar y verificar el objeto con Spark y enviar ':'usar la localización verificada y enviar ')+
        'el video completo en UNA solicitud SAM. Sin cortes ni reintentos automáticos.':
        'Process authorizes '+(q.requiresNewSparkCall?'Spark localization and visual verification, then sending ':'using verified localization and sending ')+
        'the complete video in ONE SAM request. No cuts or automatic retries.'):
        (this.es?'Esperando el presupuesto del video completo.':'Waiting for the complete-video quote.');
    }
    if(this.reviewOnlyRequired())return this.es?
      'SAM está guardado. Procesar autoriza una revisión de Spark del video y la suspensión, hasta el máximo mostrado; no repite SAM.':
      'SAM is saved. Process authorizes one Spark review of the video and suspension up to the displayed maximum; SAM is not repeated.';
    const previous=this.mode()==='api-meta' && this.cacheStatus()?.previousAttemptStatus
      ? (this.es?'Hay un intento anterior registrado. Este clic inicia un análisis nuevo y puede generar otro cobro. ':'A previous attempt is recorded. This click starts a new analysis and may incur another charge. '):'';
    if(this.groundingRequired())return previous+(this.es?
      'Procesar autoriza el video completo y 50 imágenes a Spark para localizar objeto y suspensión. Si la evidencia es insuficiente, se consultan hasta dos rondas adicionales de 50 imágenes distintas, reutilizando el video. Se detiene al obtener evidencia suficiente. El costo mostrado incluye las tres rondas y SAM. Si Spark no localiza el objeto, SAM no se envía.':
      'Process authorizes the complete video and 50 images to Spark to locate the object and suspension. Insufficient evidence can trigger up to two further rounds of 50 distinct images, reusing the uploaded video. It stops when evidence is sufficient. The displayed cost includes all three rounds and SAM. If Spark cannot locate the object, SAM is not sent.');
    return previous+(this.es?'Al pulsar Procesar autorizas enviar este video a Meta y usar los créditos del costo mostrado.':
      'Pressing Process authorizes sending this video to Meta and using credits at the displayed cost.');
  }
  processBlockedReason():string {
    if(!this.hasSource())return this.es?'Selecciona al menos una fuente para procesar.':'Select at least one processing source.';
    if(!this.apiEnabled())return this.es?'No hay un resultado guardado válido para este video. Activa API Meta para realizar un análisis nuevo.':'No valid saved result is available for this video. Enable Meta API to run a new analysis.';
    if(this.traceAwaitingRecovery)return this.es?
      'Retoma el resultado con el código de recuperación antes de iniciar otro análisis.':
      'Resume the saved result with the recovery code before starting another analysis.';
    if(this.mode()==='api-meta' && this.stageRetryQuote())return this.es?
      'El análisis SAM anterior falló. Usa Reintentar SAM para conservar las etapas completadas.':
      'The previous SAM analysis failed. Use Retry SAM to keep completed stages.';
    if(this.segmentedMode()){
      const q=this.adaptiveQuote(),r=this.readiness();
      if(!q)return this.es?'Actualiza el costo del video completo.':'Refresh the complete-video cost.';
      if(q.requiresNewSparkCall && !this.groundingQuote()?.canRun)return this.es?
        (this.groundingQuote()?.priorAttempt?
          'Un intento anterior de Spark quedó sin resolver. No se ha iniciado otra consulta; revisa los detalles del intento.':
          'No se pudo habilitar la consulta a Spark. Actualiza el costo y comprueba la conexión y los permisos del servidor.'):
        (this.groundingQuote()?.priorAttempt?
          'A previous Spark attempt is unresolved. No new request has started; review the attempt details.':
          'The Spark request could not be enabled. Refresh the cost and check the connection and server permissions.');
      if(!r?.paidRequestsSwitchEnabled || !r.creditsConfirmedByOwner ||
         !r.ownerApprovedPaidRequests || !r.manualTokenCallsPermitted)
        return this.es?'Los controles de créditos o pagos del servidor están deshabilitados.':
          'Server credit or payment gates are disabled.';
      if(!this.tokenEntered())return this.es?
        'Registra tu token Meta para continuar.':
        'Enter your Meta key to continue.';
      return this.es?'El video no cumple los límites de una solicitud completa.':
        'The video does not meet the limits for one complete-video request.';
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
    if(!ready.manualRequestsUnlimited && ready.remainingRequests<this.requiredCalls())
      return this.es?'El servidor necesita habilitar los envíos de Spark y SAM para este proceso.':
        'The server must permit the Spark and SAM requests for this process.';
    if(!this.tokenEntered())return this.es?'Configura y confirma tu token Meta.':'Configure and confirm your Meta key.';
    if(!this.canOfferPaidRun())return this.es?'El servidor no permite esta solicitud. Actualiza el costo o revisa el estado.':'The server does not allow this request. Refresh the cost or inspect status.';
    return this.es?'Confirma la revisión del intento anterior antes de procesar.':'Confirm review of the previous attempt before processing.';
  }
  private singleVideoLimitError(error:unknown):string|null {
    if(!(error instanceof HttpErrorResponse) || error.error?.error!=='SAM_SINGLE_VIDEO_FRAME_LIMIT')return null;
    return this.es?
      'El video completo debe tener entre 50 y 2500 fotogramas para una solicitud SAM. Carga una grabación dentro de ese límite; no se dividirá automáticamente.':
      'The complete video must contain 50 to 2500 frames for one SAM request. Upload a recording within that limit; it will not be split automatically.';
  }

  private validAdaptiveQuote(q:SamAdaptiveQuote,sha:string):boolean {
    const retry=q.continuousRetryEstimatedUsd??0,combined=this.combinedSamCost(q);
    return q.mode===this.paidMode() && (this.cacheEnabled() || (!q.recoveryFromBatchId &&
      (q.requiresNewSparkCall || /^[a-f0-9]{32}$/.test(q.sceneRunId??'')))) &&
      retry===0 && Number.isFinite(combined) && Math.abs(combined-q.totalSamEstimatedUsd)<1e-8 &&
      q.sourceSha256===sha && /^[a-f0-9]{32}$/.test(q.suggestedBatchId) &&
      /^[a-f0-9]{64}$/.test(q.planSha256) && q.parts===1 && q.simultaneousRequests===1 &&
      Number.isSafeInteger(q.sourceFrames) && q.sourceFrames>0 &&
      q.submittedFrames===q.sourceFrames && q.billableFrames===Math.max(50,q.sourceFrames) &&
      Number.isFinite(q.totalSamEstimatedUsd) && q.totalSamEstimatedUsd>0 &&
      q.totalSamEstimatedUsd<=.50 &&
      (q.requiresNewSparkCall!==q.existingSparkSceneVerified) &&
      (!q.recoveryFromBatchId || (/^[a-f0-9]{32}$/.test(q.recoveryFromBatchId) &&
        q.existingSparkSceneVerified && !q.requiresNewSparkCall));
  }

  private async checkAdaptive():Promise<void> {
    if(!this.hasSource() || !this.http || !this.studyId() || !this.videoSha256() ||
       this.processing() || this.checking() || this.sending())return;
    const serial=++this.serial,mode=this.paidMode(),sha=this.videoSha256(),useCache=this.cacheEnabled(),useApi=this.apiEnabled();
    this.processingReset.emit();this.checking.set(true);this.error.set('');
    this.adaptiveQuote.set(null);this.adaptiveResult.set(null);this.groundingQuote.set(null);
    this.stageRetryQuote.set(null);this.adaptiveProgress.set(null);this.readiness.set(null);this.preflight.set(null);this.reusableEvidence.set(null);
    try {
      const root=apiOrigin(this.localApiPort());
      const [reuse,preflight]=await Promise.all([
        useCache?firstValueFrom(this.http.get<SamReusableResult>(this.url()+'/reusable').pipe(timeout(180000))):Promise.resolve(null),
        firstValueFrom(this.http.get<SamPreflight>(this.url()+'/preflight').pipe(timeout(90000)))
      ]);
      if(serial!==this.serial || this.disposed || !this.segmentedMode())return;
      if(preflight.sourceSha256!==sha || (reuse && reuse.sourceSha256!==sha))throw Error('CACHE_SOURCE_MISMATCH');
      this.reusableEvidence.set(reuse);this.preflight.set(preflight);
      if(reuse?.cacheHit){if(!this.cachedResultAvailable())throw Error('CACHE_QUALITY_INVALID');return;}
      if(!useApi)return;
      const [quote,access,spark]=await Promise.all([
        firstValueFrom(this.http.get<SamAdaptiveQuote>(
          this.adaptiveUrl()+'/preflight?mode='+mode).pipe(timeout(90000))),
        firstValueFrom(this.http.get<SamReadiness>(root+'/api/meta/readiness').pipe(timeout(10000))),
        firstValueFrom(this.http.get<GroundingQuote>(this.adaptiveUrl()+'/spark-quote?mode='+mode).pipe(timeout(90000)))
      ]);
      if(serial!==this.serial || this.disposed || !this.segmentedMode())return;
      if(!this.validAdaptiveQuote(quote,this.videoSha256()) ||
         preflight.sourceSha256!==quote.sourceSha256 || spark.videoSha256!==quote.sourceSha256 ||
         spark.mode!==mode || (!useCache && (!quote.requiresNewSparkCall || !spark.freshRequired || spark.cacheHit)))
        throw new Error('INVALID_ADAPTIVE_QUOTE');
      this.adaptiveQuote.set(quote);this.preflight.set(preflight);
      this.readiness.set(access);this.groundingQuote.set(spark);
    }catch(e) {
      if(serial===this.serial){
        const status=(e as {status?:number})?.status;
        this.error.set(this.singleVideoLimitError(e) ?? (status===404?(this.es?
          'Este video no está disponible en el servidor seleccionado. Vuelve al servidor donde lo cargaste.':
          'This video is not available on the selected server. Use the server where it was uploaded.'):
          status===0?(this.es?'No se pudo conectar con la API local. Comprueba la conexión en Configuración.':
          'Cannot reach the local API. Check the connection in Settings.'):
          (this.es?'No se pudo preparar el presupuesto del video completo. Revisa el video y la evidencia de Spark; no se envió nada a Meta.':
          'Could not prepare the complete-video quote. Review the video and Spark evidence; nothing was sent to Meta.')));
      }
    }finally{
      if(serial===this.serial){
        this.checking.set(false);
        // Returning to the saved investigation checks the failed stage without
        // another click, using only read-only receipts. No Meta request is sent.
        if(this.autoCheck() && this.mode()==='api-meta')void this.findStageRetry();
      }
    }
  }

  private recoverableAdaptiveReply(error:unknown):boolean {
    return error instanceof HttpErrorResponse
      ? error.status===0 || error.status===409 || error.status>=500
      : error instanceof Error && error.name==='TimeoutError';
  }
  private async recoverAdaptiveReport(url:string,quote:SamAdaptiveQuote,current:()=>boolean):Promise<SamAdaptiveReport|null> {
    const read=async<T>(path:string):Promise<T|null>=>{
      try{return await firstValueFrom(this.http!.get<T>(path).pipe(timeout(5000)));}
      catch(error){
        if(error instanceof HttpErrorResponse || error instanceof Error && error.name==='TimeoutError')return null;
        throw error;
      }
    };
    const sameSource=(report:SamAdaptiveReport)=>report.sourceSha256===quote.sourceSha256 &&
      report.sparkSceneKey===quote.sparkSceneKey && /^[a-f0-9]{64}$/.test(report.planSha256);
    const usable=(report:SamAdaptiveReport)=>report.status==='PROVIDER_PARTS_COMPLETE_DERIVED_MASKS_PENDING_HUMAN_REVIEW' &&
      report.derivedSeamsVerified===true && (report.derivedFramesWithMasks??0)>0;
    // Only read durable evidence. Never resend the POST or authorize another provider call.
    for(let attempt=0;attempt<2 && current();attempt++){
      const progress=await read<SamAdaptiveProgress>(url+'/batches/'+quote.suggestedBatchId+'/progress');
      if(!current())return null;
      if(progress){
        if(progress.batchId!==quote.suggestedBatchId || progress.parts!==quote.parts)throw Error('BATCH_ID_MISMATCH');
        this.adaptiveProgress.set(progress);
      }
      const parent=await read<SamAdaptiveReport>(url+'/batches/'+quote.suggestedBatchId+'/report');
      if(!current())return null;
      if(parent){
        if(!sameSource(parent) || parent.batchId!==quote.suggestedBatchId || parent.parts!==quote.parts ||
          parent.planSha256!==quote.planSha256 || parent.automaticRetryOfBatchId)throw Error('BATCH_ID_MISMATCH');
        const childId=progress?.continuousRetryBatchId || parent.automaticRetryBatchId;
        if(progress?.continuousRetry && progress.continuousRetry.batchId!==childId ||
          childId && parent.automaticRetryBatchId && parent.automaticRetryBatchId!==childId)throw Error('BATCH_ID_MISMATCH');
        if(childId){
          if((quote.continuousRetryEstimatedUsd??0)<=0 || !/^[a-f0-9]{32}$/.test(childId) ||
            childId===quote.suggestedBatchId)throw Error('BATCH_ID_MISMATCH');
          const child=await read<SamAdaptiveReport>(url+'/batches/'+childId+'/report');
          if(!current())return null;
          if(child){
            if(!sameSource(child) || child.batchId!==childId || child.parts!==1 ||
              child.automaticRetryOfBatchId!==quote.suggestedBatchId || child.automaticRetryBatchId)
              throw Error('BATCH_ID_MISMATCH');
            // The existing evidence endpoint also rechecks the child plan, receipts and hashes.
            return child;
          }
        }else if(usable(parent) || (quote.continuousRetryEstimatedUsd??0)===0)return parent;
      }
      if(attempt===0 && current())await new Promise<void>(resolve=>setTimeout(resolve,1000));
    }
    return null;
  }

  private async analyzeAdaptive():Promise<void> {
    if(!this.apiEnabled() || !this.http || !this.canProcess() || !this.segmentedMode())return;
    let quote=this.adaptiveQuote()!;
    const approvedSam=quote.totalSamEstimatedUsd,approvedCombined=this.combinedSamCost(quote),spark=this.groundingQuote();
    const token=this.connection.tokenForManualRequest();
    if(!token)return;
    const sha=this.videoSha256(),url=this.adaptiveUrl(),port=this.localApiPort(),mode=this.paidMode();
    const serial=++this.serial;
    const current=()=>!this.disposed && serial===this.serial &&
      this.segmentedMode() && this.apiEnabled() && mode===this.paidMode() && sha===this.videoSha256() &&
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
          maximumEstimatedCostUsd:spark.maximumEstimatedCostUsd,mode,
          confirmImageTransfer:true,confirmVideoTransfer:true,confirmPaidCall:true
        },{headers:{'X-ProofLab-Manual-Action':'sam-adaptive-spark'}}).pipe(timeout(12*60*1000)));
        if(!current())return;
        if(located.videoSha256!==sha || located.status!=='READY' || !located.scene ||
          located.scene.videoSha256!==sha || located.mode!==mode)throw Error('SPARK_TARGET_INCONCLUSIVE');
        const sceneRunId=located.evidence?.runId;
        if(mode==='api-meta' && !/^[a-f0-9]{32}$/.test(sceneRunId??''))throw Error('SPARK_RUN_MISSING');
        const exact=await firstValueFrom(this.http.get<SamAdaptiveQuote>(
          url+'/preflight?mode='+mode+(sceneRunId?'&sceneRunId='+encodeURIComponent(sceneRunId):'')).pipe(timeout(90000)));
        if(!current())return;
        if(!this.validAdaptiveQuote(exact,sha) || exact.requiresNewSparkCall ||
          exact.sparkSceneKey!==located.scene.key || (sceneRunId && exact.sceneRunId!==sceneRunId) ||
          exact.totalSamEstimatedUsd>approvedSam+1e-9 ||
          this.combinedSamCost(exact)>approvedCombined+1e-9)
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
        modelApiToken:token,videoSha256:sha,sceneKey:quote.sparkSceneKey,mode,sceneRunId:quote.sceneRunId??null,
        planSha256:quote.planSha256,clientBatchId:quote.suggestedBatchId,
        maximumSamCostUsd:quote.totalSamEstimatedUsd,
        confirmOneContinuousRetry:false,maximumCombinedSamCostUsd:quote.totalSamEstimatedUsd,
        maxInFlight:1,recoveryFromBatchId:quote.recoveryFromBatchId??null,
        confirmPrivateVideoTransfer:true,confirmUpToManyBillableSamCalls:true,confirmNoAutomaticRetry:true
      },{headers:{'X-ProofLab-Manual-Action':'sam-adaptive-batch'}}).pipe(timeout(70*60*1000))).catch(async failure=>{
        if(!current() || !this.recoverableAdaptiveReply(failure))throw failure;
        if(this.adaptivePollHandle!==null)clearInterval(this.adaptivePollHandle);
        this.adaptivePollHandle=null;
        return this.recoverAdaptiveReport(url,quote,current);
      });
      if(!current())return;
      if(!report){
        this.traceAwaitingRecovery=true;
        this.error.set(this.es?
          'No se recibió la respuesta final y aún no se pudo recuperar. La operación puede continuar en el servidor. Retómala con su código de recuperación; no se ha enviado otro análisis.':
          'The final response was not received and could not yet be recovered. The operation may still be running on the server. Resume it with its recovery code; no new analysis was sent.');
        this.finishProcessing();this.processingFailed.emit();
        return;
      }
      const initial=report.batchId===quote.suggestedBatchId && report.parts===quote.parts && !report.automaticRetryOfBatchId;
      const fallback=(quote.continuousRetryEstimatedUsd??0)>0 && report.parts===1 &&
        /^[a-f0-9]{32}$/.test(report.batchId) && report.batchId!==quote.suggestedBatchId &&
        report.automaticRetryOfBatchId===quote.suggestedBatchId;
      if(report.sourceSha256!==sha || report.sparkSceneKey!==quote.sparkSceneKey ||
        !/^[a-f0-9]{64}$/.test(report.planSha256) || (initial && report.planSha256!==quote.planSha256) ||
        (!initial && !fallback))throw Error('BATCH_ID_MISMATCH');
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
        this.error.set(this.singleVideoLimitError(e) ?? (code==='BATCH_ID_MISMATCH'?(this.es?
          'El resultado guardado no coincide con este video y su solicitud. No se abrirán gráficos ni se enviará otro análisis; revisa el código de recuperación.':
          'The saved result does not match this video and request. No overlays will open and no new analysis will be sent; review the recovery code.'):
          code==='SEGMENT_QUOTE_CHANGED'?(this.es?
          'Spark terminó, pero cambió el presupuesto o la evidencia. SAM no se envió; actualiza el costo.':
          'Spark finished, but the quote or evidence changed. SAM was not sent; refresh the cost.'):
          this.processingPhase()==='grounding'?(this.es?
          'Spark no pudo confirmar el objeto. SAM no se envió; revisa el estado antes de otro intento.':
          'Spark could not confirm the object. SAM was not sent; review the status before another attempt.'):
          (this.adaptiveResult()?.automaticRetryOfBatchId || this.adaptiveResult()?.automaticRetryBatchId)?(this.es?
          'El reintento con el video completo también quedó inconcluso. No se harán más envíos automáticos. Puedes reproducir el original; los gráficos quedan pendientes.':
          'The whole-video retry was also inconclusive. No further automatic requests will be made. Original playback is available; overlays remain pending.'):
          (this.es?'No se pudo validar el resultado del video completo. Puedes reproducir el original; los gráficos quedan pendientes. No se enviará otra solicitud automáticamente.':
          'The complete-video result could not be validated. Original playback is available; overlays remain pending. No further request will be sent automatically.')));
        this.failProcessing();
      }
    }finally{
      void this.pollSpark();this.stopSparkPoll();
      if(this.adaptivePollHandle!==null)clearInterval(this.adaptivePollHandle);
      this.adaptivePollHandle=null;this.sending.set(false);
      if(!opened || !current())this.finishProcessing();
      if(current() && this.mode()==='api-meta' && (this.adaptiveResult()?.failedParts??0)>0)
        void this.findStageRetry(); // read-only: never resubmit to Meta
    }
  }

  voidCheck():void {void this.check();}
  async check():Promise<void> {
    if(!this.hasSource())return;
    if(this.segmentedMode())return this.checkAdaptive();
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
      const root=apiOrigin(this.localApiPort());
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
          this.sparkFinishedAt=Date.now();this.samWasReused=true;this.viewStartedAt=Date.now();
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
      this.processingPhase.set('sam');this.samStartedAt=Date.now();
      const state=await firstValueFrom(this.http.post<SamCacheStatus>(samUrl+'/manual-one-shot',{
        mode,sceneKey:scene.key,modelApiToken:oneTimeToken,videoSha256:sha,
        confirmPrivateVideoTransfer:true,confirmOneBillableSamCall:true,
        maximumEstimatedCostUsd:quote.estimatedCostUsd,
        ownerApprovesOneReconciledRetry:isRetry,
        previousAttemptBillingEvidence:isRetry?SamVideoReplay.reconciledOnce:null
      },{headers:{'X-ProofLab-Manual-Action':'sam-one-shot'}}));
      if(!current())return;
      this.samFinishedAt=Date.now();this.cacheStatus.set(state);
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
    if(!this.apiEnabled())return;
    if(!this.http || budget<=0)return;
    this.processingPhase.set('review');this.reviewStartedAt=Date.now();
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
      if(current()){this.reviewFailed=true;this.error.set(this.es?
        'SAM está guardado. La revisión de suspensión no se completó; puede haber un cobro de Spark. No se repetirá automáticamente.':
        'SAM is saved. The suspension review did not complete; Spark may have charged. It will not retry automatically.');}
    } finally {if(current())this.reviewFinishedAt=Date.now();}
  }
  private async reviewCached():Promise<void> {
    if(!this.apiEnabled())return;
    const token=this.connection.tokenForManualRequest(),state=this.cacheStatus(),quote=this.suspensionReviewQuote();
    if(!token||!state||!quote)return;
    const url=this.url(),sha=this.videoSha256(),serial=++this.serial;
    const current=()=>!this.disposed&&serial===this.serial&&sha===this.videoSha256()&&url===this.url();
    this.processingReset.emit();this.beginProcessing();this.sending.set(true);this.error.set('');this.startStageTrace(true);
    try {
      await this.reviewAfterSam(state,token,url,quote.maximumEstimatedCostUsd,current);
      if(current()){this.viewStartedAt=Date.now();this.openResult(state);this.processingPhase.set('validating');}
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
