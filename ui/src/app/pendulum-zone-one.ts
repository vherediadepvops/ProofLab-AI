import { CommonModule } from '@angular/common';
import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PendulumDirector } from './pendulum-director';
import { PendulumMasterPlayer } from './pendulum-master-player';
import { SamVideoReplay } from './sam-video-replay';

/**
 * Jury-facing Zone 1. Reuses the same validated Director session, upload,
 * model parameters and evidence endpoints; this class only changes the
 * presentation. The full/advanced Director remains preserved in the repo.
 */
@Component({
  selector: 'app-pendulum-zone-one',
  standalone: true,
  imports: [CommonModule, FormsModule, PendulumMasterPlayer, SamVideoReplay],
  templateUrl: './pendulum-zone-one.html',
  styleUrl: './pendulum-zone-one.scss'
})
export class PendulumZoneOne extends PendulumDirector {
  readonly activeTrial = signal<'baseline'|'followup'>('baseline');
  readonly registeringAndUploading = signal(false);
  readonly samRefreshNonce = signal(0);

  /**
   * Select the first recording immediately. The study's theoretical inputs
   * are recomputed and frozen on the server BEFORE any video bytes are sent.
   * Existing sessions and their original evidence are never overwritten.
   */
  async uploadFocused(event: Event, role:'baseline'|'followup'): Promise<void> {
    const control = event.target as HTMLInputElement;
    const file = control.files?.[0];
    if (!file || this.registeringAndUploading() ||
        this.busy() || !!this.stageBusy()) return;

    const autoRegister = role === 'baseline' && !this.session();
    if (autoRegister) {
      // Preflight BEFORE creating a study to avoid orphan records on invalid
      // extension or size. The server performs its own stricter validation.
      if (file.size < 10000 || file.size > 240000000 ||
          !/\.(mov|mp4|m4v|webm|mkv|avi|3gp|3g2)$/i.test(file.name)) {
        this.error.set(this.es
          ? 'Video inválido. Selecciona MOV, MP4 u otro formato compatible de 10 KB a 240 MB.'
          : 'Invalid video. Select MOV, MP4 or another supported format (10 KB–240 MB).');
        control.value = '';
        return;
      }
      this.registeringAndUploading.set(true);
      this.error.set('');
    }

    try {
      if (autoRegister) {
        // Always refresh from the currently visible length/angle/mass, so a
        // previous pending calculation cannot stamp stale parameters.
        await this.refreshTheory();
        if (!this.model()) {
          this.error.set(this.es
            ? 'No se pudo calcular la teoría. Corrige los parámetros antes de cargar el video.'
            : 'Could not calculate the model. Correct the parameters before upload.');
          return;
        }

        await this.start(); // persists model SHA-256 before media submission
        if (!this.session()) return; // no upload if registration failed
      }

      await this.upload(event, role);
      if (role === 'followup' && this.session()?.followup) {
        this.activeTrial.set('followup');
      }
    } finally {
      if (autoRegister) this.registeringAndUploading.set(false);
      // Also clear after failed registration, to allow selecting same file.
      control.value = '';
    }
  }

  newFocusedStudy():void {
    if (this.registeringAndUploading() ||
        this.busy() || !!this.stageBusy()) return;
    this.reset();
    this.activeTrial.set('baseline');
  }
}
