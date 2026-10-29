import {Component, computed, input, signal} from '@angular/core';
import {
  measureSamPeriod, periodReportPayload, periodReportEnvelope,
  type SamPeriodInput, type SamPeriodSource, type PeriodIssue
} from './sam-period';

const ISSUE_TEXT:Record<PeriodIssue,[string,string]>={
  INVALID_TIMESTAMPS:['Tiempos originales inválidos.','Invalid source timestamps.'],
  INVALID_CENTROIDS:['Centroides inválidos o duplicados.','Invalid or duplicate centroids.'],
  NO_DETECTIONS:['SAM no detectó el objeto.','SAM did not detect the object.'],
  MISSING_PIVOT:['Marca o confirma el punto de suspensión.','Select or confirm the suspension point.'],
  UNSUPPORTED_ASSEMBLY:['Este método requiere un péndulo simple.','This method requires a simple pendulum.'],
  INVALID_GEOMETRY:['No hay geometría suficiente para medir.','Insufficient geometry for measurement.'],
  LOW_AMPLITUDE:['Oscilación insuficiente para separar los ciclos.','Insufficient oscillation to separate cycles.'],
  TOO_FEW_CYCLES:['Se necesitan al menos 3 ciclos alternos completos.','At least 3 complete alternating cycles are required.'],
  LOW_COVERAGE:['Cobertura utilizable inferior al 80%.','Usable coverage is below 80%.'],
  GAPS:['Hay detecciones ausentes; los ciclos que cruzan huecos se excluyen.','Missing detections: cycles crossing gaps are excluded.'],
  GEOMETRY_EXCLUSIONS:['Se excluyeron posiciones incompatibles con el pivote.','Positions inconsistent with the pivot were excluded.'],
  UNDERSAMPLED_CYCLES:['Hay ciclos con menos de 20 intervalos entre fotogramas.','Some cycles have fewer than 20 frame intervals.'],
  UNSTABLE_PERIOD:['Dispersión de los períodos superior al 5%.','Period dispersion exceeds 5%.'],
  PERIOD_DRIFT:['El período cambia más del 5% entre mitades.','Period changes by more than 5% between halves.'],
  UNSTABLE_RADIUS:['Radio aparente inestable: revisa el pivote y el seguimiento.','Unstable apparent radius: review the pivot and tracking.'],
  TIME_GAPS:['Hay saltos en los tiempos originales; no se conectan esos segmentos.','Source timestamps contain gaps; those segments are not connected.']
};

@Component({
  selector:'app-sam-period-panel',
  standalone:true,
  templateUrl:'./sam-period-panel.html',
  styleUrl:'./sam-period-panel.scss'
})
export class SamPeriodPanel {
  readonly measurement=input.required<SamPeriodInput>();
  readonly source=input<SamPeriodSource|null>(null);
  readonly language=input<'es'|'en'>('es');
  readonly assessment=computed(()=>measureSamPeriod(this.measurement()));
  readonly downloading=signal(false);
  readonly downloadError=signal(false);
  get es():boolean{return this.language()==='es';}
  number(value:number|null,digits=3):string {
    return value===null?'—':value.toLocaleString(this.es?'es-PE':'en-US',
      {minimumFractionDigits:digits,maximumFractionDigits:digits});
  }
  uncertainty():string {
    const u=this.assessment().statisticalStandardUncertaintySeconds;
    if(u===null)return '—';
    if(u===0)return this.es?'0 · ver límites':'0 · see limits';
    if(u<.0001)return u.toExponential(2).replace('.',this.es?',':'.');
    return this.number(u,4);
  }
  issueText(issue:PeriodIssue):string{return ISSUE_TEXT[issue][this.es?0:1];}
  async download():Promise<void> {
    const source=this.source();
    if(!source || this.downloading())return;
    // Capture a self-consistent snapshot before awaiting the integrity hash.
    const payload=periodReportPayload(source,this.measurement(),this.assessment());
    this.downloading.set(true);this.downloadError.set(false);
    try {
      const envelope=await periodReportEnvelope(payload);
      const blob=new Blob([JSON.stringify(envelope,null,2)],{type:'application/json'});
      const url=URL.createObjectURL(blob),link=document.createElement('a');
      link.href=url;link.download='prooflab-period-'+source.analysisId+'.json';
      link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
    } catch {this.downloadError.set(true);}
    finally {this.downloading.set(false);}
  }
}
