import { apiOrigin } from './api-origin';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, input, OnChanges, OnInit, signal, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Language, leanExplanation, leanOutcome, scientificStatus, executionMode, friendlyApiError } from './locale';

interface ResearchField {
  key: string; nameEs: string; nameEn: string; guidanceEs: string; guidanceEn: string;
  minimumCharacters: number;
}
interface ResearchStage {
  index: number; nameEs: string; nameEn: string; reportEs: string; reportEn: string;
  descriptionEs: string; descriptionEn: string;
  referenceFrameworks: string[]; fields: ResearchField[];
}
interface ResearchReport {
  index: number; fields: Record<string,string>; status: string;
  revision: number; approvedAtUtc: string|null; sha256: string|null;
}
interface GuidedDraft {
  researchId: string; stage: number; language: Language; mode: string;
  fields: Record<string,string>; evidenceSource: string;
  humanReviewRequired: boolean; isAutonomousAi: boolean; isScientificDiscovery: boolean;
}
interface MetaReadiness {
  provider: string; museModel: string; samModel: string;
  apiKeyConfigured: boolean; creditsConfirmedByOwner: boolean;
  ownerApprovedPaidRequests: boolean; paidRequestsSwitchEnabled: boolean;
  liveCallsPermitted: boolean; usedRequests: number; remainingRequests: number;
  maxRequestsPerProcess: number;
}
interface MuseDraftResponse {
  draft: GuidedDraft; model: string; provenance: string; responseSha256: string;
  generatedAtUtc: string; inputTokens: number | null; outputTokens: number | null;
}
interface ResearchProject {
  id: string; title: string; discipline: string; createdAtUtc: string;
  activeStage: number; reports: ResearchReport[]; disclaimer: string;
}
interface MathProblem {
  id: string; titleEnglish: string; titleSpanish: string;
}
interface ProofVerification {
  attemptId: string; problemId: string; strategy: string; outcome: string;
  sourceSha256: string; checkedAtUtc: string; diagnostic: string;
  isNewDiscovery: boolean;
}
interface MathAttempt {
  researchId: string; executionMode: string; problemResearchStatus: string;
  verification: ProofVerification;
}
interface MathCycle {
  researchId: string; problemId: string; mode: string;
  attempts: MathAttempt[]; conclusion: string; isScientificDiscovery: boolean;
}
interface OpenConjecture {
  id: string; titleEs: string; titleEn: string;
  statementEs: string; statementEn: string; researchStatus: string;
  statusSourceUrl: string; formalizationStatus: string;
}
interface FiniteAttempt {
  researchId: string; executionMode: string;
  result: {
    experimentId: string; checkedThrough: number; evenCasesExamined: number;
    casesWithPrimePair: number; outcome: string; resultSha256: string;
    inputSha256: string; formalProofVerified: boolean; isNewDiscovery: boolean;
    limitationsEs: string; limitationsEn: string;
  };
}
type Lang = Language;

@Component({
  selector: 'app-research-workflow',
  imports: [CommonModule,FormsModule],
  templateUrl: './research-workflow.html',
  styleUrl: './research-workflow.scss'
})
export class ResearchWorkflow implements OnInit, OnChanges {
  private readonly http = inject(HttpClient);
  private readonly api = apiOrigin(5391)+'/api/research';
  readonly language = input<Lang>('es');
  readonly stages = signal<ResearchStage[]>([]);
  readonly project = signal<ResearchProject|null>(null);
  readonly stage = signal(1);
  readonly title = signal('');
  readonly existingId = signal('');
  readonly draft = signal<Record<string,string>>({});
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly proofProblems = signal<MathProblem[]>([]);
  readonly proofProblemId = signal('NAT-ADD-COMM');
  readonly proofStrategy = signal('rfl');
  readonly proofAttempts = signal<MathAttempt[]>([]);
  readonly proofError = signal('');
  readonly proofBusy = signal(false);
  readonly conjectures = signal<OpenConjecture[]>([]);
  readonly finiteLimit = signal(100);
  readonly finiteAttempts = signal<FiniteAttempt[]>([]);
  readonly finiteBusy = signal(false);
  readonly finiteError = signal('');
  readonly metaReadiness = signal<MetaReadiness | null>(null);
  readonly metaError = signal('');
  readonly isGuidedGoldbach = computed(() =>
    this.project()?.discipline === 'Mathematics' &&
    this.project()?.title.toLowerCase().includes('goldbach') === true);
  readonly isMathEvidencePhase = computed(() =>
    this.project()?.discipline === 'Mathematics' &&
    this.project()!.activeStage >= 4 && this.stage() >= 4);
  readonly latestProofAttempt = computed(() => this.proofAttempts().at(-1) ?? null);
  readonly evidenceAttached = computed(() => {
    if (!this.isMathEvidencePhase()) return true;
    const key = this.stage()===4 ? 'manifest' : 'verification';
    const value = this.draft()[key] ?? '';
    return this.proofAttempts().some(item =>
      value.includes(item.verification.attemptId));
  });
  readonly canApproveReport = computed(() =>
    this.completion().ready && this.evidenceAttached() && this.reviewed());
  readonly selected = computed(() => this.stages().find(s => s.index === this.stage()) ?? null);
  readonly selectedReport = computed(() =>
    this.project()?.reports.find(x => x.index === this.stage()) ?? null);
  readonly canEdit = computed(() =>
    this.project()?.activeStage === this.stage() && this.stage() <= 5);
  readonly completion = computed(() => {
    const definition = this.selected();
    if (!definition) return { ready: false, count: 0, total: 0 };
    const fields = this.draft();
    const n = definition.fields.filter(f => {
      const value = (fields[f.key] ?? '').trim();
      if (value.length < f.minimumCharacters) return false;
      if (f.key === 'sources' &&
          !/(https?:\/\/|doi:|10\.)/i.test(value)) return false;
      return true;
    }).length;
    return {ready:n===definition.fields.length, count:n,total:definition.fields.length};
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['language'] && !changes['language'].firstChange) {
      // Avoid showing a previous-language notification after toggling ES/EN.
      this.notice.set('');
      this.error.set('');
      this.proofError.set('');
      this.finiteError.set('');
      this.metaError.set('');
    }
  }

  ngOnInit() {
    this.refreshMetaReadiness();
    this.http.get<MathProblem[]>(apiOrigin(5391)+'/api/math/problems')
      .subscribe({ next: items => this.proofProblems.set(items),
        error: () => this.proofError.set('Could not load Lean benchmark catalog.') });
    this.http.get<OpenConjecture[]>(apiOrigin(5391)+'/api/math/conjectures')
      .subscribe({next: items => this.conjectures.set(items),
        error: () => this.finiteError.set(this.copy(
          'Catálogo de conjeturas no disponible.', 'Open conjecture catalog unavailable.'))});
    this.http.get<ResearchStage[]>(this.api + '/stages')
      .subscribe({next: data => this.stages.set(data),
        error: () => this.error.set(this.language() === 'es'
          ? 'No se pudieron cargar las cinco etapas.' : 'Research stages could not be loaded.')});
    // Project IDs are stored only on this user's own browser.
    const previous = localStorage.getItem('prooflab-research-current');
    if (previous) {
      this.existingId.set(previous);
      this.load(previous);
    }
  }
  refreshMetaReadiness(): void {
    this.http.get<MetaReadiness>(apiOrigin(5391)+'/api/meta/readiness')
      .subscribe({
        next: state => { this.metaReadiness.set(state); this.metaError.set(''); },
        error: () => this.metaError.set(this.language()==='es'
          ? 'Estado de conexión Meta no disponible.' : 'Meta connection status unavailable.')
      });
  }
  copy(es:string,en:string) { return this.language()==='es'?es:en; }
  proofStatus(outcome:string):string { return leanOutcome(outcome,this.language()); }
  proofExplanation(record:MathAttempt):string {
    return leanExplanation(record.verification,this.language());
  }
  proofMode(mode:string):string { return executionMode(mode,this.language()); }
  proofResearchStatus(status:string):string { return scientificStatus(status,this.language()); }
  stageStatus(index:number) {
    const status = this.project()?.reports.find(x => x.index===index)?.status ?? 'LOCKED';
    if (status === 'APPROVED_COMPLETENESS') return this.copy('Informe cerrado','Report frozen');
    if (this.project()?.activeStage === index) return this.copy('En preparación','Draft');
    return this.copy('Bloqueada','Locked');
  }
  updateField(key:string,value:string) {
    this.draft.update(old => ({...old,[key]:value}));
    this.reviewed.set(false);
    this.notice.set('');
  }
  selectStage(index:number) {
    this.stage.set(index);
    this.draft.set({...this.project()?.reports.find(x => x.index === index)?.fields});
    this.reviewed.set(false);
    this.error.set('');
    this.notice.set('');
  }
  private setProject(research:ResearchProject) {
    this.project.set(research);
    this.existingId.set(research.id);
    localStorage.setItem('prooflab-research-current',research.id);
    this.selectStage(Math.min(research.activeStage,5));
    if (research.discipline === 'Mathematics' && research.activeStage >= 4) {
      this.loadProofEvidence(research.id);
      this.loadFiniteEvidence(research.id);
    }
  }
  private loadFiniteEvidence(id: string): void {
    this.http.get<FiniteAttempt[]>(this.api + '/' + id + '/math/finite-checks')
      .subscribe({next: rows => this.finiteAttempts.set(rows),
        error: () => this.finiteError.set(this.copy(
          'No fue posible consultar la evidencia finita.',
          'Could not load finite computational evidence.'))});
  }
  async runFiniteCheck(): Promise<void> {
    if (!this.isMathEvidencePhase() || !this.canEdit() ||
        this.finiteBusy() || this.proofBusy() || !this.project()) return;
    this.finiteBusy.set(true);
    this.finiteError.set('');
    try {
      const check = await firstValueFrom(this.http.post<FiniteAttempt>(
        this.api + '/' + this.project()!.id +
        '/math/conjectures/GOLDBACH-STRONG/finite-check',
        {evenLimit: Number(this.finiteLimit())}));
      this.finiteAttempts.update(items => [...items, check]);
      this.notice.set(this.copy(
        'Observación finita registrada. No es una demostración de Goldbach.',
        'Bounded observation recorded. This does not prove Goldbach.'));
    } catch (e:any) {
      this.finiteError.set(friendlyApiError(e,this.language(),
        'Error en la comprobación finita.', 'Finite check failed.'));
    } finally {this.finiteBusy.set(false);}
  }
  attachFinite(record: FiniteAttempt): void {
    if (!this.canEdit() || !this.isMathEvidencePhase()) return;
    const r = record.result;
    const note = this.copy(
      'Observación computacional FINITA, no prueba Lean ni descubrimiento.',
      'FINITE computational observation, not a Lean proof or discovery.');
    const detail = 'Goldbach evidence ' + r.experimentId + ' | 4..' +
      r.checkedThrough + ' | ' + r.outcome +
      ' | SHA-256 ' + r.resultSha256 + '. ' + note;
    const key = this.stage() === 4 ? 'manifest' : 'verification';
    const previous = this.draft()[key] ?? '';
    if (previous.includes(r.experimentId)) return;
    this.updateField(key, (previous ? previous + '\n\n' : '') + detail);
    this.notice.set(this.copy(
      'Observación agregada al borrador. La etapa sigue exigiendo evidencia Lean.',
      'Observation added to draft. Lean evidence is still required.'));
  }
  private loadProofEvidence(id:string): void {
    this.http.get<MathAttempt[]>(this.api + '/' + id + '/math/attempts')
      .subscribe({next: attempts => this.proofAttempts.set(attempts),
        error: () => this.proofError.set(this.copy(
          'No fue posible leer los intentos de Lean.',
          'Could not read Lean proof attempts.'))});
  }
  async runGuidedExperiment(): Promise<void> {
    if (!this.isMathEvidencePhase() || !this.isGuidedGoldbach() || !this.canEdit() ||
        this.proofBusy() || this.finiteBusy() || this.busy() || !this.project()) return;
    this.proofBusy.set(true);
    this.finiteBusy.set(true);
    this.error.set('');
    this.notice.set('');
    const researchId = this.project()!.id;
    try {
      const result = await firstValueFrom(this.http.post<{
        toolCallCount: number; isAutonomousAi: boolean; isScientificDiscovery: boolean;
        goldbachFormalProofVerified: boolean;
      }>(this.api + '/' + researchId + '/math/offline-cycle', {}));
      if (result.isAutonomousAi || result.isScientificDiscovery ||
          result.goldbachFormalProofVerified || result.toolCallCount < 1 ||
          result.toolCallCount > 3) {
        throw new Error('Unexpected scripted experiment provenance.');
      }
      this.loadProofEvidence(researchId);
      this.loadFiniteEvidence(researchId);
      this.notice.set(this.copy(
        'Experimento ejecutado: Goldbach finito y controles Lean registrados. Ahora prepara el borrador; no es un descubrimiento.',
        'Experiment executed: bounded Goldbach and Lean controls recorded. Now prepare the draft; this is not a discovery.'));
    } catch (e: any) {
      this.error.set(friendlyApiError(e, this.language(),
        'No se pudo ejecutar el experimento guiado; verifica los límites de intentos y los registros.',
        'Could not run the guided experiment; check attempt limits and logs.'));
      this.loadProofEvidence(researchId);
      this.loadFiniteEvidence(researchId);
    } finally {
      this.proofBusy.set(false);
      this.finiteBusy.set(false);
    }
  }
  async runProofCycle():Promise<void> {
    if (!this.isMathEvidencePhase() || !this.canEdit() || this.proofBusy() || !this.project()) return;
    this.proofBusy.set(true);
    this.proofError.set('');
    try {
      const output=await firstValueFrom(this.http.post<MathCycle>(
        this.api + '/' + this.project()!.id + '/math/cycle',
        {problemId:this.proofProblemId()}));
      this.proofAttempts.update(current => [...current,...output.attempts]);
      this.notice.set(this.copy(
        'Ciclo de referencia ejecutado. No es investigación autónoma con Muse Spark.',
        'Controlled baseline completed. This is not autonomous Muse Spark research.'));
    } catch (e:any) {
      this.proofError.set(friendlyApiError(e,this.language(),
        'No se pudo ejecutar el ciclo de Lean.', 'The Lean cycle failed.'));
    } finally { this.proofBusy.set(false); }
  }
  async runSingleProof():Promise<void> {
    if (!this.isMathEvidencePhase() || !this.canEdit() || this.proofBusy() || !this.project()) return;
    this.proofBusy.set(true);this.proofError.set('');
    try {
      const record=await firstValueFrom(this.http.post<MathAttempt>(
        this.api + '/' + this.project()!.id + '/math/attempts',
        {problemId:this.proofProblemId(), strategy:this.proofStrategy()}));
      this.proofAttempts.update(items => [...items,record]);
    } catch(e:any) {
      this.proofError.set(friendlyApiError(e,this.language(),
        'No se pudo ejecutar el intento de Lean.', 'The Lean attempt failed.'));
    } finally {this.proofBusy.set(false);}
  }
  attachProof(record:MathAttempt):void {
    if (!this.isMathEvidencePhase() || !this.canEdit()) return;
    const text = leanExplanation(record.verification,this.language());
    const recordText = 'ProofLab verified execution record: ' +
      record.verification.attemptId + ' | ' +
      record.verification.problemId + ' | ' + record.verification.strategy + ' | ' +
      record.verification.outcome + ' | SHA-256 ' + record.verification.sourceSha256 +
      '. ' + text;
    const key = this.stage()===4 ? 'manifest':'verification';
    const previous=this.draft()[key] ?? '';
    if (previous.includes(record.verification.attemptId)) return;
    this.updateField(key,(previous?previous+'\n\n':'')+recordText);
    this.notice.set(this.copy(
      'Referencia verificable incorporada al borrador; guarda el informe.',
      'Traceable evidence reference added to draft; save the report.'));
  }
  async create() {
    if (this.busy()) return;
    this.busy.set(true);this.error.set('');this.notice.set('');
    try {
      const research = await firstValueFrom(this.http.post<ResearchProject>(this.api,
        {title:this.title(),discipline:'Mathematics'}));
      this.setProject(research);
      this.title.set('');
    } catch (e:any) {this.fail(e);}
    finally {this.busy.set(false);}
  }
  async load(id=this.existingId()) {
    if (!id.trim()) return;
    this.busy.set(true);this.error.set('');this.notice.set('');
    try {
      const research = await firstValueFrom(this.http.get<ResearchProject>(
        this.api + '/' + encodeURIComponent(id.trim())));
      this.setProject(research);
    } catch(e:any) {this.fail(e);}
    finally {this.busy.set(false);}
  }
  private fail(e:any) {
    this.error.set(friendlyApiError(e,this.language(),
      'No se pudo completar la operación. Revisa los datos e inténtalo nuevamente.',
      'Could not complete the operation. Check the inputs and retry.'));
  }
  async prepareMuseDraft(): Promise<void> {
    if (!this.project() || !this.isGuidedGoldbach() || !this.canEdit() ||
        !this.metaReadiness()?.liveCallsPermitted || this.busy()) return;
    const approvedThisRequest = window.confirm(this.copy(
      'Esta acción enviará un protocolo científico curado a Muse Spark 1.3 y puede consumir créditos de Meta. La respuesta no estará verificada. ¿Continuar?',
      'This sends a curated scientific protocol to Muse Spark 1.3 and may consume Meta API credits. Its output is unverified. Continue?'));
    if (!approvedThisRequest) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    const researchId = this.project()!.id;
    const stage = this.stage();
    try {
      const result = await firstValueFrom(this.http.post<MuseDraftResponse>(
        this.api + '/' + researchId + '/stages/' + stage +
        '/muse-draft?lang=' + this.language(), {}));
      const draft = result.draft;
      if (draft.researchId !== researchId || draft.stage !== stage ||
          !draft.humanReviewRequired || draft.isScientificDiscovery ||
          draft.mode !== 'MUSE_SPARK_LIVE_UNVERIFIED_DRAFT') {
        throw new Error('Meta research draft failed provenance checks.');
      }
      this.draft.update(current => {
        const updated = { ...current };
        for (const [key, value] of Object.entries(draft.fields))
          if (!(updated[key] ?? '').trim()) updated[key] = value;
        return updated;
      });
      this.reviewed.set(false);
      this.notice.set(this.copy(
        'Muse Spark propuso texto NO verificado. Revisa las afirmaciones y evidencias, guarda y confirma personalmente.',
        'Muse Spark suggested UNVERIFIED prose. Review claims/evidence, save and acknowledge personally.'));
    } catch (e: any) {
      const reason = String(e?.error?.error ?? '');
      this.error.set(reason.includes('blocked') ? this.copy(
        'Meta todavía no está habilitado: revisa clave, saldo, autorización y límite de solicitudes.',
        'Meta remains blocked: check API key, credits, consent and request limit.') :
        this.copy('No se obtuvo un borrador válido de Meta. No se cambió el informe.',
                  'Meta did not return a valid draft. The report was not changed.'));
    } finally {
      this.busy.set(false);
      this.refreshMetaReadiness();
    }
  }
  async prepareGuidedDraft(): Promise<void> {
    if (!this.project() || !this.isGuidedGoldbach() || !this.canEdit() ||
        this.busy() || this.proofBusy() || this.finiteBusy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      const report = await firstValueFrom(this.http.get<GuidedDraft>(
        this.api + '/' + this.project()!.id + '/stages/' + this.stage() +
        '/guided-draft?lang=' + this.language()));
      if (report.researchId !== this.project()!.id || report.stage !== this.stage() ||
          report.isAutonomousAi || report.isScientificDiscovery || !report.humanReviewRequired) {
        throw new Error('Unexpected guided draft provenance.');
      }
      this.draft.update(current => {
        const updated = {...current};
        for (const [key, value] of Object.entries(report.fields)) {
          if (!(updated[key] ?? '').trim()) updated[key] = value;
        }
        return updated;
      });
      this.reviewed.set(false);
      this.notice.set(this.copy(
        'Borrador preparado con datos curados o evidencias reales. Revisa, ajusta y guarda antes de confirmar el informe.',
        'Draft prepared from curated material or real evidence. Review, edit, and save before acknowledging the report.'));
    } catch (e: any) {
      const message = String(e?.error?.error ?? '');
      if (message.includes('Before preparing stage 4/5')) {
        this.error.set(this.copy(
          'Antes de redactar esta etapa, ejecuta la comprobación finita de Goldbach y al menos un intento Lean real en el panel superior.',
          'Before drafting this stage, run the finite Goldbach check and at least one genuine Lean attempt in the evidence panel.'));
      } else {
        this.error.set(this.copy(
          'No se pudo preparar el borrador guiado. Solo está disponible para expedientes de Goldbach.',
          'Unable to prepare a guided draft. This feature currently supports Goldbach dossiers only.'));
      }
    } finally {
      this.busy.set(false);
    }
  }
  async save() {
    if (!this.project() || !this.canEdit()) return;
    this.busy.set(true);this.error.set('');this.notice.set('');
    try {
      const result = await firstValueFrom(this.http.post<ResearchProject>(
        this.api + '/' + this.project()!.id + '/stages/' + this.stage() + '/draft',
        {fields:this.draft()}));
      this.project.set(result);
      this.notice.set(this.copy('Borrador guardado localmente.','Draft saved locally.'));
    } catch(e:any) {this.fail(e);}
    finally {this.busy.set(false);}
  }
  async approve() {
    if (!this.project() || !this.canEdit() || !this.canApproveReport()) return;
    this.busy.set(true);this.error.set('');this.notice.set('');
    const researchId=this.project()!.id, index=this.stage();
    try {
      const saved = await firstValueFrom(this.http.post<ResearchProject>(
        this.api + '/' + researchId + '/stages/' + index + '/draft',
        {fields:this.draft()}));
      this.project.set(saved);
      const done = await firstValueFrom(this.http.post<ResearchProject>(
        this.api + '/' + researchId + '/stages/' + index + '/approve',
        {reviewedByHuman:true}));
      this.setProject(done);
      this.notice.set(this.copy(
        'Informe cerrado: documentación completa. No equivale a validación científica.',
        'Report frozen: documentation complete. This is not scientific validation.'));
    } catch(e:any) {this.fail(e);}
    finally {this.busy.set(false);}
  }
  async download() {
    if (!this.project()) return;
    const id=this.project()!.id,index=this.stage();
    await this.exportMarkdown(this.api + '/' + id + '/reports/' + index +
      '?lang=' + this.language(),'PROOFLAB-REPORT-' + id.slice(0,8) +
      '-STAGE-' + index + '.' + this.language() + '.md');
  }
  async downloadPaper() {
    if (!this.project() || this.project()!.activeStage<=5) return;
    const id=this.project()!.id;
    await this.exportMarkdown(this.api + '/' + id + '/paper?lang=' +
      this.language(),'PROOFLAB-PAPER-' + id.slice(0,8) +
      '.' + this.language() + '.md');
  }
  private async exportMarkdown(endpoint:string,filename:string) {
    this.error.set('');
    try {
      const markdown=await firstValueFrom(this.http.get(endpoint,{responseType:'text'}));
      const uri=URL.createObjectURL(new Blob([markdown],{type:'text/markdown;charset=utf-8'}));
      const link=document.createElement('a');
      link.href=uri; link.download=filename; link.click();
      URL.revokeObjectURL(uri);
    } catch(e:any) {this.fail(e);}
  }
}
