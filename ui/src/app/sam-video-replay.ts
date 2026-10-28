import {
  Component, OnChanges, SimpleChanges, inject, input, output, signal
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  decodeSamVideoResponse, type SamVideoEvidence, type SamVideoFrame
} from './sam-video-evidence';

interface SamCacheStatus {
  status:string; sourceSha256:string; cacheKey:string;
  sourceFrames:number; estimatedCostUsd:number; canRunLive:boolean;
  provenance:string;
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
          <button type="button" (click)="voidCheck()" [disabled]="checking() || sending()">
            {{es?'Comprobar caché, fotogramas y saldo (sin costo)':
              'Check cache, frames and authorization (free)'}}
          </button>
          @if(checking()) {<span role="status">{{es?'Comprobando sin cobro…':'Checking without charge…'}}</span>}
          @if(cacheStatus(); as s) {
            <p>{{es?'Estado':'Status'}}: <strong>{{s.status}}</strong></p>
            @if(s.status.startsWith('CACHED_META_')) {
              <p class="sam-caution">{{es
                ? 'Resultado SAM guardado. Abrirlo o reproducirlo NO genera nuevas llamadas.'
                : 'Cached SAM result. Opening or replaying does NOT call Meta again.'}}</p>
              <button type="button" (click)="cacheUpdated.emit()">
                {{es?'Volver a cargar las vistas SAM':'Reload SAM views'}}
              </button>
            }
            @if(s.status.includes('RECONCILIATION')) {
              <p class="sam-caution">{{es
                ? 'Hubo un intento previo posiblemente facturado. Debe revisarse antes de repetir; el sistema NO reintentará automáticamente.'
                : 'A previous attempt may have been billed. Reconcile first; NO automatic retry.'}}</p>
            }
          }
          @if(preflight(); as pf) {
            <p>{{pf.sourceFrames}} {{es?'fotogramas originales':'source frames'}} ·
              {{pf.durationSeconds.toFixed(3)}} s ·
              {{pf.codec}} · {{es?'Costo estimado':'Estimated cost'}}:
              US$ {{pf.estimatedCostUsd.toFixed(4)}}</p>
            <p class="sam-footnote">{{es
              ? 'Timestamps originales comprobados. El precio se estima, no es el cargo real.'
              : 'Original timestamps verified. The cost is estimated, not a final invoice.'}}</p>
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
              <label><input type="checkbox" [checked]="paidConsent()"
                (change)="paidConsent.set($any($event.target).checked)"/>
                {{es?'Confirmo UNA solicitud facturable, máximo estimado US$0,50':
                  'I confirm ONE billable request, maximum estimated US$0.50'}}</label>
              <button type="button" class="sam-submit" (click)="voidAnalyze()"
                [disabled]="sending() || !transferConsent() || !paidConsent()">
                {{es?'Analizar UNA VEZ con SAM 3.1':'Analyze ONCE with SAM 3.1'}}
              </button>
            </div>
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
    .sam-actions{border:1px solid #375766;border-radius:8px;padding:12px;margin:12px 0}
    .sam-actions button{border:1px solid #74adba;border-radius:6px;background:#204251;color:white;padding:8px;cursor:pointer}
    .sam-submit{margin-top:12px}
    .sam-consent{padding:12px;margin-top:8px;border:1px dashed #779;display:grid;gap:8px}
    .sam-consent label{font-size:.86rem}
    .sam-caution{color:#f4c875;font-weight:700}
    .sam-footnote{color:#c9d4df}
    .sam-offline{margin-top:12px}
    .sam-stage3 select{margin-left:8px}
    .sam-masks{display:block;background:#101b2a;width:100%;max-width:560px;max-height:320px;margin-top:12px;border:1px solid #426277}
    .sam-objects p{margin:4px 0}
    .sam-frame-caption,.sam-disclaimer{font-size:.8rem;color:#c2cdd9}
  `]
})
export class SamVideoReplay implements OnChanges {
  private readonly http=inject(HttpClient,{optional:true});
  private readonly base='http://127.0.0.1:5391/api/science/director/pendulum/sessions';
  readonly studyId=input('');
  readonly videoRole=input<'baseline'|'followup'>('baseline');
  readonly language=input<'es'|'en'>('es');
  readonly videoSha256=input('');
  readonly cacheUpdated=output<void>();
  readonly cacheStatus=signal<SamCacheStatus|null>(null);
  readonly preflight=signal<SamPreflight|null>(null);
  readonly readiness=signal<SamReadiness|null>(null);
  readonly transferConsent=signal(false);
  readonly paidConsent=signal(false);
  readonly checking=signal(false);
  readonly sending=signal(false);
  readonly result=signal<SamVideoEvidence|null>(null);
  readonly loading=signal(false);
  readonly error=signal('');
  readonly selectedIndex=signal(0);
  private serial=0;

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
    return !!pf && !!ready && !!s &&
      s.status==='NOT_ANALYZED_WITH_SAM' &&
      s.sourceSha256===this.videoSha256() &&
      pf.sourceSha256===this.videoSha256() &&
      pf.supportsSingleCall &&
      pf.estimatedCostUsd>0 && pf.estimatedCostUsd<=0.50 &&
      ready.apiKeyConfigured && ready.liveCallsPermitted &&
      ready.creditsConfirmedByOwner && ready.ownerApprovedPaidRequests &&
      ready.remainingRequests>0 && s.canRunLive;
  }
  voidCheck():void {void this.check();}
  async check():Promise<void> {
    if(!this.http || this.checking() || this.sending())return;
    const serial=++this.serial;
    this.checking.set(true);
    this.error.set('');
    this.transferConsent.set(false);
    this.paidConsent.set(false);
    try {
      const status=await firstValueFrom(this.http.get<SamCacheStatus>(this.url()+'/status'));
      if(serial!==this.serial)return;
      this.cacheStatus.set(status);
      if(status.status.startsWith('CACHED_META_')){
        this.preflight.set(null);this.readiness.set(null);
        this.cacheUpdated.emit();
        return;
      }
      const pf=await firstValueFrom(this.http.get<SamPreflight>(this.url()+'/preflight'));
      if(serial!==this.serial)return;
      this.preflight.set(pf);
      const access=await firstValueFrom(
        this.http.get<SamReadiness>('http://127.0.0.1:5391/api/meta/readiness'));
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
    if(!this.http || this.sending() || !this.canOfferPaidRun() ||
        !this.transferConsent() || !this.paidConsent())return;
    const serial=++this.serial;
    this.sending.set(true);
    this.error.set('');
    try {
      const state=await firstValueFrom(this.http.post<SamCacheStatus>(
        this.url()+'/analyze',{
          videoSha256:this.videoSha256(),
          confirmPrivateVideoTransfer:true,
          confirmOneBillableSamCall:true,
          maximumEstimatedCostUsd:0.50
        }));
      if(serial!==this.serial)return;
      this.cacheStatus.set(state);
      this.cacheUpdated.emit(); // master player re-reads provider cache
    }catch(e){
      if(serial===this.serial){
        this.error.set(this.es?
          'SAM no se completó. Puede existir una solicitud facturada. Revisa el estado; no reintentes automáticamente.':
          'SAM did not complete. A request might have been billed. Check status; never auto-retry.');
        this.cacheStatus.set(null);
      }
    }finally{
      if(serial===this.serial){
        this.sending.set(false);
        this.transferConsent.set(false);
        this.paidConsent.set(false);
      }
    }
  }
  ngOnChanges(changes:SimpleChanges):void {
    if(!changes['videoSha256'] && !changes['studyId'] &&
       !changes['videoRole'])return;
    this.serial++;
    this.result.set(null);
    this.cacheStatus.set(null);
    this.preflight.set(null);
    this.readiness.set(null);
    this.transferConsent.set(false);
    this.paidConsent.set(false);
    this.error.set('');
    this.selectedIndex.set(0);
    this.loading.set(false);
    this.checking.set(false);
    this.sending.set(false);
  }
  async onFile(event:Event):Promise<void> {
    const control=event.target as HTMLInputElement;
    const file=control.files?.[0];
    if(!file)return;
    control.value='';
    const serial=++this.serial;
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
      if(serial!==this.serial)return;
      this.result.set(evidence);
      this.selectedIndex.set(0);
    } catch(e) {
      if(serial===this.serial)
        this.error.set((this.es?'No se pudo validar el JSON de SAM: ':'SAM JSON could not be validated: ')+
          (e instanceof Error?e.message:'INVALID_RESPONSE'));
    } finally {
      if(serial===this.serial)this.loading.set(false);
    }
  }
}
