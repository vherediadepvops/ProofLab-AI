import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

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
interface ResearchProject {
  id: string; title: string; discipline: string; createdAtUtc: string;
  activeStage: number; reports: ResearchReport[]; disclaimer: string;
}
type Lang = 'es'|'en';

@Component({
  selector: 'app-research-workflow',
  imports: [CommonModule,FormsModule],
  templateUrl: './research-workflow.html',
  styleUrl: './research-workflow.scss'
})
export class ResearchWorkflow implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly api = 'http://127.0.0.1:5391/api/research';
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

  ngOnInit() {
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
  copy(es:string,en:string) { return this.language()==='es'?es:en; }
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
    this.error.set(e?.error?.error || this.copy(
      'No se pudo completar la operación. Revisa los datos y vuelve a intentar.',
      'Could not complete the operation. Check the inputs and retry.'));
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
    if (!this.project() || !this.canEdit() || !this.completion().ready || !this.reviewed()) return;
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
