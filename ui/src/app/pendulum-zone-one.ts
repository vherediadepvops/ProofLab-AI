import { CommonModule } from '@angular/common';
import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PendulumDirector } from './pendulum-director';
import { PendulumMasterPlayer } from './pendulum-master-player';

/**
 * Jury-facing Zone 1. Reuses the same validated Director session, upload,
 * model parameters and evidence endpoints; this class only changes the
 * presentation. The full/advanced Director remains preserved in the repo.
 */
@Component({
  selector: 'app-pendulum-zone-one',
  standalone: true,
  imports: [CommonModule, FormsModule, PendulumMasterPlayer],
  templateUrl: './pendulum-zone-one.html',
  styleUrl: './pendulum-zone-one.scss'
})
export class PendulumZoneOne extends PendulumDirector {
  readonly activeTrial = signal<'baseline'|'followup'>('baseline');

  async uploadFocused(event: Event, role:'baseline'|'followup'): Promise<void> {
    await this.upload(event,role);
    if(role==='followup' && this.session()?.followup){
      this.activeTrial.set('followup');
    }
  }

  newFocusedStudy():void {
    this.reset();
    this.activeTrial.set('baseline');
  }
}
