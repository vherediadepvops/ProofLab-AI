# ProofLab AI — Zona 1: reproductor científico maestro de tres vistas

**Fecha:** 28/10/2026. **Misión:** Autonomous Physics Discovery (M2). **Alcance del incremento:** zona de video con un único reloj. No es integración SAM/Muse ni una medición validada.

## Experiencia

Al abrir ProofLab AI ahora se presenta **Péndulo** y la barra lateral solo muestra ese botón. Las antiguas pantallas Matemáticas, Física general y Pruebas Meta quedan **ocultas**, pero todos sus archivos y APIs continúan presentes para no borrar trabajo ni evidencia del hackathon. Se mantienen ES/EN.

El investigador configura longitud, ángulo, masa, una o dos pruebas y carga MOV/MP4/etc. En cada video con vista previa compatible aparecen tres paneles sincronizados:

1. **Video principal**: el **único** elemento HTML `<video>` reproduce una versión compatible del video original. No tiene un reloj paralelo.
2. **Vectores**: el lienzo obtiene sus fotogramas con `drawImage(video)` de ese mismo elemento. Superpone el centro candidato (píxeles) y una flecha de **velocidad aparente de imagen** (px/s) calculada a partir de muestras locales X/Y. No presenta angularidad ni velocidad física calibrada.
3. **Rastro fantasma**: segundo lienzo que vuelve a dibujar ese **mismo fotograma decodificado** y posiciones candidatas anteriores (últimos 1,5 segundos), con transparencia gradual. No se inventa ninguna trayectoria si faltan datos XY.

### Controles de un único reloj

- Reproducir/pausar controla únicamente el video maestro y detiene/actualiza ambos lienzos.
- `requestVideoFrameCallback` dibuja las dos vistas justo al obtener otro fotograma renderizable; si no existe se utiliza `requestAnimationFrame`. Eventos `pause`, `seeked`, `loadeddata` y `timeupdate` repintan desde el tiempo de ese **mismo video**.
- Línea de tiempo única, reproducción 0,25×, 0,5×, 1×, 1,5×, 2×; avanzar/retroceder **un fotograma de la versión de reproducción**, 1/12 s para la copia H.264 actual. El número de fotograma que se muestra corresponde al **proxy**, no al original de 60 FPS.
- Pausar no pone en marcha otro reproductor; no hay tres `<video>`, solo **uno**. Las dos superficies canvas siempre dibujan la misma fuente. `paintFrame(time)` actualiza juntas ambas vistas.

## Fuentes y límites de la evidencia

- Los originales HEVC/MOV de iPhone y MP4 se guardan **sin modificar** en la carpeta privada (SHA-256 conservado). FFmpeg genera una **copia de revisión H.264 compatible**, actualmente `640 px` de ancho y `12 FPS`, a partir del video original; **esto NO preserva la calidad o FPS originales en la reproducción del navegador**. La UI muestra explícitamente el codec/resolución/FPS originales *como metadatos*, y los FPS del proxy por separado. Futuras mejoras de alta fidelidad se tratarán aparte.
- El seguidor de fondo local opera sobre `480×270 a 12 FPS`. Ahora devuelve **`PixelSamples` opcional** con `timeSeconds, x, y, approximateRadiusPixels` en el formato `LocalMotionTrack`, además de observaciones anteriores de x(t). Las coordenadas pueden incluir manos, sombras u otros objetos: son **candidatas, NO máscaras SAM**.
- La superposición en vectores **NO constituye medición SAM**, seguimiento de identidad física ni cálculo científico de velocidad angular. Se reusa únicamente si hay muestras con tiempo válido; ante huecos de detección se omiten vectores y se indica el motivo.
- Los estudios antiguos sin `PixelSamples` continúan siendo compatibles en JSON; se reproducen las tres vistas pero **no muestran vectores o rastros inventados**. Para disponer de coordenadas 2D hay que analizar nuevamente el video con el tracker local; el archivo original permanece intacto.
- Si falta la versión H.264 de navegador, se mantiene la miniatura y advertencia anterior; la carga/captura de muestras continúa limitada al backend local. El frontend no envía video a Meta.

## Archivos principales y QA

- `ui/src/app/pendulum-master-player.{ts,html,scss,spec.ts}`: un solo reloj, dos lienzos.
- `ui/src/app/pendulum-master-sync.ts`: búsqueda temporal, cálculo de velocidad candidata px/s, traza previa y avance por fotograma; pruebas unitarias correspondientes en el player.
- `src/ProofLab.Science/PendulumMotionTracker.cs`: extensión compatible del registro con puntos bidimensionales del tracker local.
- `ui/src/app/pendulum-director.{ts,html}`: integración del reproductor en cada prueba sin quitar laboratorio avanzado.
- `ui/src/app/app.{ts,html,scss}`: inicio en Péndulo, ocultación del resto del menú, código preservado.
- Pruebas locales ejecutadas: **126/126 .NET Release**, **51/51 Angular ChromeHeadless**, compilaciones .NET y Angular sin errores. Tests comprueban que solo hay un elemento video, ambos canvas dibujan el mismo `HTMLVideoElement`, pausa/seek/avance de frame y tratamiento seguro de muestras faltantes.

## QA real del iPhone

Tras compilar la API y mantener `Meta:AllowPaidRequests=false`, se creó una sesión de **prueba interna independiente** (sin confirmar revisión humana) y se procesó `50CM_1.MOV` con el nuevo seguidor. **Datos verificados:** MOV original HEVC **1920×1080**, **59,94 FPS**, **45.708.115 bytes**, SHA-256 idéntico al video de origen, **316 candidatos bidimensionales X/Y** (316 fotogramas con detección), proxy H.264 generado (~1.666.406 bytes). La petición HTTP Range `bytes=0-1023` devolvió **206 Partial Content**, `Content-Type: video/mp4`, `Access-Control-Allow-Origin: http://127.0.0.1:4300`, contenido de 1024 bytes y rango válido. El estado del experimento quedó **`AWAITING_HUMAN_BOB_REVIEW`**; no se registró confirmación ficticia. Es prueba de carga/decodificación/procedencia X/Y y entrega web, **NO de que SAM haya segmentado al bob** ni validación visual automática de los 316 puntos.

**Pendiente de alcance posterior:** SAM 3.1 real en fotogramas y seguimiento de la plomada, fidelidad nativa 1080p/60FPS o 4K si hardware y decodificador lo permiten, pivote/ángulo/velocidad angular calibrados, SQLite de física, Muse Spark autónomo e informes del método científico de cinco etapas. No hay llamadas pagadas Meta ni gastos autorizados para este incremento.

**English:** [PENDULUM-ZONE1-MASTER-PLAYER.md](PENDULUM-ZONE1-MASTER-PLAYER.md).
