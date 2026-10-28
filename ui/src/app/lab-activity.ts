import { Component, input } from '@angular/core';

/** Indeterminate activity: duration is unknown, so no fabricated percentage. */
@Component({
  selector:'app-lab-activity',
  standalone:true,
  template:`
    <div class="activity" role="status" aria-live="polite">
      <div class="activity-heading"><span class="activity-orbit" aria-hidden="true"></span>
        <div><strong>{{label()}}</strong>@if(detail()){<small>{{detail()}}</small>}</div>
      </div>
      <div class="activity-track" role="progressbar" [attr.aria-label]="label()"><span></span></div>
    </div>
  `,
  styles:[`
    :host{display:block;min-width:0}
    .activity{padding:15px 17px;border:1px solid #344c67;border-radius:10px;background:linear-gradient(115deg,#1b2939,#19212c);color:#d2e6ff}
    .activity-heading{display:flex;align-items:center;gap:12px}
    .activity-heading>div{display:grid;gap:5px;min-width:0}
    strong{font-size:13px;font-weight:600}small{font-size:11px;color:#94abc4;line-height:1.5}
    .activity-orbit{width:18px;height:18px;flex-shrink:0;border:2px solid #354b65;border-top-color:#75baff;border-right-color:#75baff;border-radius:50%;animation:orbit 1s linear infinite}
    .activity-track{height:3px;margin-top:14px;border-radius:9px;background:#2d3b4c;overflow:hidden}
    .activity-track span{display:block;width:38%;height:100%;background:linear-gradient(90deg,transparent,#70b6ff,#91dbdf);border-radius:9px;animation:travel 1.6s ease-in-out infinite}
    @keyframes orbit{to{transform:rotate(360deg)}}
    @keyframes travel{from{transform:translateX(-110%)}to{transform:translateX(365%)}}
    @media(prefers-reduced-motion:reduce){.activity-orbit,.activity-track span{animation:none}.activity-track span{width:100%;opacity:.6}}
  `]
})
export class LabActivity {
  readonly label=input.required<string>();
  readonly detail=input('');
}
