import { Component, OnChanges, SimpleChanges, input, signal } from '@angular/core';
import {
  decodeSamVideoResponse, type SamVideoEvidence, type SamVideoFrame
} from './sam-video-evidence';

/**
 * Offline SAM VIDEO result inspector. It never uploads media or calls Meta.
 * A JSON document may have been fabricated; the operator must independently
 * match the result to the exact submitted clip and inspect the original.
 */
@Component({
  selector:'app-sam-video-replay',
  standalone:true,
  template:`
    <details class="sam-stage3" open>
      <summary>{{es ? 'Etapa 3 · Inspeccionar máscaras de SAM 3.1 (sin conexión)' :
        'Stage 3 · Inspect SAM 3.1 masks (offline)'}}</summary>
      <p>{{es
        ? 'Importa un JSON de respuesta COMPLETADA de SAM 3.1. El análisis es 100 % local y no utiliza tokens ni envía videos. El archivo debe provenir de una solicitud autorizada.'
        : 'Import a COMPLETED SAM 3.1 response JSON. Processing is entirely local; no tokens or videos are sent. The file should come from an authorized request.'}}</p>
      <label class="sam-file">
        {{es ? 'Archivo JSON de respuesta' : 'Response JSON file'}}
        <input type="file" accept=".json,application/json" (change)="onFile($event)" />
      </label>
      @if(loading()) {
        <p role="status">{{es?'Decodificando máscaras…':'Decoding masks…'}}</p>
      }
      @if(error()) {<p role="alert">{{error()}}</p>}
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
      <p class="sam-disclaimer">{{es
        ? 'Para validar científicamente: analizar el clip REAL autorizado, comprobar los fotogramas y su PTS, vincular SHA-256, comparar plomada con marca manual y estimar errores. Esta vista no aprueba la Etapa 3.'
        : 'Scientific validation requires the REAL authorized clip, verified frame PTS and SHA-256, manually checked bob identity, and measurement error. This viewer does not approve Stage 3.'}}</p>
    </details>
  `,
  styles:[`
    .sam-stage3{margin:16px 0;padding:16px;border:1px solid #3a5265;border-radius:12px;background:#13202b;color:#f2f6fc}
    .sam-stage3 summary{cursor:pointer;font-weight:700}
    .sam-stage3 p{line-height:1.45;font-size:.86rem}
    .sam-file{display:block;margin:12px 0}
    .sam-file input{display:block;margin-top:8px;max-width:100%}
    .sam-caution{color:#f4c875;font-weight:700}
    .sam-stage3 select{margin-left:8px}
    .sam-masks{display:block;background:#101b2a;width:100%;max-width:560px;max-height:320px;margin-top:12px;border:1px solid #426277}
    .sam-objects p{margin:4px 0}
    .sam-frame-caption,.sam-disclaimer{font-size:.8rem;color:#c2cdd9}
  `]
})
export class SamVideoReplay implements OnChanges {
  readonly language=input<'es'|'en'>('es');
  readonly videoSha256=input('');
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
  ngOnChanges(changes:SimpleChanges):void {
    // A language switch should not discard an imported result.
    if(!changes['videoSha256'])return;
    // Never display a prior study's unverified SAM masks as belonging
    // to another trial after the source clip changes.
    this.serial++;
    this.result.set(null);
    this.error.set('');
    this.selectedIndex.set(0);
    this.loading.set(false);
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
