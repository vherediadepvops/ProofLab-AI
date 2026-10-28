# ProofLab AI — Experiencia central correcta del jurado (Misión 2)

**27 de octubre de 2026 — flujo principal de investigación en dos videos.**

## Qué cambió y por qué

El jurado NO debe cargar seis videos. Esa era una **prueba técnica de repetibilidad** útil para desarrollo interno, no la experiencia principal solicitada. El objetivo es que una persona entregue **un video inicial**, el investigador científico evalúe evidencia, solicite otro experimento, **registre su predicción antes de recibir el segundo video**, compare resultados y explique incertidumbre. Se conserva el laboratorio antiguo de hasta 3+3 videos bajo un desplegable **Modo técnico avanzado**, sin perder datos.

En el primer paso el usuario solo declara la longitud del péndulo y carga un archivo original MOV/MP4/etc.; no introduce fórmulas o valores del período. La interfaz «ProofLab Research Director» presenta visualización de la primera señal, solicitud de confirmación humana y resultados. Un modelo futuro podrá dirigir estas etapas, pero **en este incremento Muse Spark NO ejecuta decisiones autónomas y SAM 3.1 NO rastrea video real**.

## Secuencia real implementada sin Meta (prototipo)

1. `POST /api/science/director/pendulum/sessions` crea una investigación local de dos videos desde `baselineLengthMeters` (ejemplo 0.5), **sin preregistrar aún la segunda predicción**.
2. `POST /api/science/director/pendulum/sessions/{id}/video/baseline?filename=...` recibe el primer MOV/MP4, valida códec mediante FFprobe, decodifica con FFmpeg y obtiene seguimiento **local de fondo no SAM**. Estima período con `PendulumSpectralEstimator` independiente de la longitud teórica; conserva medio original, fecha, SHA-256, coordenadas y diagnóstico.
3. `POST /api/science/director/pendulum/sessions/{id}/review-baseline` recibe `{"bobConfirmed":true}` solo tras el reconocimiento visual por el usuario de que la curva corresponde a la plomada. Esta aceptación humana declarativa NO es auditoría independiente.
4. `POST /api/science/director/pendulum/sessions/{id}/propose-local-control` calcula una **propuesta fija de CONTROL LOCAL, NO IA**; por ejemplo 0.50 → 0.80 m. Guarda longitud recomendada, fundamento, fórmula pequeña-amplitud, UTC, SHA-256 y fuente de la decisión. El endpoint se rechaza antes de subir/confirmar la primera observación. El sello previo prueba el orden de **envío de evidencia**, no la fecha real en que alguien filmó el segundo video.
5. `POST /api/science/director/pendulum/sessions/{id}/video/followup?filename=...` recibe **solo el segundo video**, permitido después de registrar la predicción. `POST /api/science/director/pendulum/sessions/{id}/compare` contrasta cambios previstos vs observados; devuelve claramente `PRELIMINARY_LOCAL_COMPARISON_NOT_AI_SAM_OR_CERTIFIED`. Ni Muse ni SAM son invocados; 0 tokens de Meta.
6. `GET /api/science/director/pendulum/sessions/{id}` restaura estados/archivos tras refrescar página; `GET .../poster/{baseline|followup}` recupera miniatura JPEG de cada video. Sesión, evidencias, revisión, predicción y comparación son archivos locales creados una vez en `src/ProofLab.Api/.private/physics/director/{id}`. Nunca se sobrescriben o publican con Git.

## Revisión visual reproducible

El servidor puede producir una **copia de revisión H.264/MP4 de baja resolución** (640 píxeles de ancho, ~12 FPS) desde MOV/HEVC con FFmpeg, por `GET /api/science/director/pendulum/sessions/{id}/preview/{baseline|followup}`. El navegador la muestra con controles de reproducción para que el humano compruebe si el seguimiento corresponde a la plomada y no a sombras o manos. La API admite descargas parciales (HTTP 206). El proxy no sustituye al original como dato científico; se conservan original, SHA-256 y métricas de seguimiento independientemente. Si falla la transcodificación, se sigue mostrando una miniatura JPEG y se advierte que hay que revisar el archivo original en otro reproductor.

**Prueba local con material real:** la primera grabación `50CM_1.MOV` (HEVC/1080p) se subió en una nueva sesión de QA; el estado resultó `AWAITING_HUMAN_BOB_REVIEW` con estimación local ~1,4156 s, R² ~0,970, SHA-256 idéntico al video privado de origen y `previewAvailable=true`. Se generó un proxy H.264/MP4 de ~1,67 MB; la ruta sirvió correctamente HTTP **206** para `Range: bytes=0-1023`. No se ha emitido la confirmación humana, no se generó un segundo experimento y no se gastaron tokens de Meta en esta prueba.

## Lo que falta para cumplir el guion de concurso (NO implementado)

- **Muse Spark 1.3 real**: inferencia validada que *decide por iniciativa propia* el siguiente experimento según evidencia y alternativas, proponga una nueva longitud, contraste hipótesis y guarde tokens/costo. Requiere verificar créditos y autorización explícita antes de usar llamadas pagadas. El botón actual dice «propuesta local (NO IA)», porque NO se debe fingir autonomía.
- **SAM 3.1 real sobre video**: selección económica de fotogramas, segmentación de la plomada, seguimiento temporal de identidad y validación independiente del movimiento. Por ahora se usa FFmpeg más sustracción local de fondo.
- **Incertidumbre científica**: comparar contra cronometraje manual de ciclos en videos auténticos y propagación de error de muestreo/calibración; no confundir R² alto con bob correcto. El usuario debe realizar efectivamente el nuevo experimento DESPUÉS de recibir la predicción para una prueba verdaderamente prospectiva.
- **Scientific Method Engine de cinco etapas**, informes ES/EN e historial de decisiones del Research Director, aún sin integrar a este flujo.
- **Misión 1**: Lean 4 de teoremas conocidos, control de táctica rechazada/aceptada y Goldbach finito sí existen; el agente Muse que interprete errores y elija nuevos intentos aún **no** está verificado en vivo. No etiquetarlo como investigación matemática autónoma resuelta.

## Criterios de aceptación de este incremento

La UI muestra **un solo selector de video inicialmente**, no seis. Tras analizar el primero aparece la revisión humana; únicamente después el control local solicita un segundo video y registra predicción. El servidor rechaza seguir el orden incorrecto. La predicción no puede modificarse una vez escrita. No se envía dato ni video a Meta. La investigación original de seis videos se mantiene detrás de «Modo técnico avanzado». Se conserva procedencia, límites y seguridad local (esta API NO está lista para recibir videos públicos anónimos).

**English:** [FLAGSHIP-DIRECTOR.md](FLAGSHIP-DIRECTOR.md).
