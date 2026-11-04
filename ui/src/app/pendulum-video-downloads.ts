import {ChangeDetectionStrategy,Component,input} from '@angular/core';

@Component({
  selector:'app-pendulum-video-downloads',
  standalone:true,
  changeDetection:ChangeDetectionStrategy.OnPush,
  template:
    `<section class="video-downloads" aria-labelledby="pendulum-downloads-title">
      <header class="downloads-heading">
        <div>
          <p class="downloads-kicker">{{language()==='es'?'GRABACIONES DEL EXPERIMENTO':'EXPERIMENT RECORDINGS'}}</p>
          <h2 id="pendulum-downloads-title">{{language()==='es'?'Prueba con estos videos':'Try these videos'}}</h2>
        </div>
        <p>{{language()==='es'?'Descarga una grabación y ábrela en el laboratorio.':'Download a recording and open it in the laboratory.'}}</p>
      </header>
      <ul class="video-download-grid">
        @for(video of videos;track video.filename){
          <li>
            <a class="pendulum-video-download" [href]="video.href" [attr.download]="video.filename"
              [attr.aria-label]="(language()==='es'?'Descargar ':'Download ')+video.filename">
              <div class="video-poster">
                <img [src]="video.poster" width="360" height="640" loading="lazy" decoding="async"
                  [alt]="(language()==='es'?'Fotograma del péndulo de ':'Frame of the ')+video.length+(language()==='es'?' cm':' cm pendulum')"/>
                <span class="video-length" aria-hidden="true">{{video.length}} cm</span>
                <span class="video-download-icon" aria-hidden="true">
                  <svg viewBox="0 0 24 24" fill="none"><path d="M12 3v12m-5-5 5 5 5-5M4 16v4a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-4"/></svg>
                </span>
              </div>
              <div class="video-caption">
                <span class="video-filename">{{video.filename}}</span>
                <span class="video-download-label">{{language()==='es'?'Descargar MOV':'Download MOV'}} <span aria-hidden="true">↓</span></span>
              </div>
            </a>
          </li>
        }
      </ul>
    </section>`,
  styleUrl:'./pendulum-video-downloads.scss'
})
export class PendulumVideoDownloads {
  readonly language=input<'es'|'en'>('es');
  readonly videos=[10,20,30,40,50,60,70,80].map(length=>{
    const basename=length+'CM_15G_272.4g',filename=basename+'.MOV';
    return {length,filename,href:'/pendulum-videos/'+filename,poster:'/pendulum-video-posters/'+basename+'.webp'};
  });
}
