# ProofLab AI — Demostración estrella del péndulo (competencia 2026)

**Estado: primer incremento funcional, NO demo final.** Implementado el 27/10/2026 durante el hackathon. La demostración estrella es el péndulo, por decisión expresa del propietario del proyecto. Esta decisión **sustituye D-004** de la memoria compartida, donde el péndulo figuraba solo como instrumento de calibración. La canica puede continuar como control secundario.

## Objetivo de la demostración

El jurado observará una investigación de péndulo supervisada por **ProofLab Research Director**: un video real de longitud 50 cm, detección/seguimiento mediante SAM 3.1, mediciones independientes, propuesta de un segundo experimento a 80 cm por Muse Spark, **predicción sellada ANTES de procesar el segundo experimento**, contraste con video auténtico y cinco informes científicos ES/EN.

El momento principal no será una curva bonita: será observar cómo un agente usa información real para pedir una nueva medición, registrar su pronóstico primero y admitir incertidumbre o un resultado inconcluso después. **Este flujo integral todavía no funciona**. Muse Spark no tiene una inferencia exitosa confirmada; SAM únicamente ha segmentado una esfera sintética en imagen fija.

## Implementación comprobable de este incremento (0 tokens Meta)

- Calculadora determinista: período teórico `T = 2π√(L/g)` y proporción `T₂/T₁ = √(L₂/L₁)`, reutilizando `ScientificModels`.
- `POST /api/science/pendulum/preregister` con `{"baselineLengthMeters":0.5,"followUpLengthMeters":0.8}` registra ID local, sello horario UTC, predicción y SHA-256 en `.private/physics/pendulum/`.
- `GET /api/science/pendulum/preregistrations/{id}` devuelve predicción congelada, verificando su suma SHA-256. No hay prueba notarial ni sistema externo de firmas.
- `POST /api/science/pendulum/preregistrations/{id}/compare` consume `baseline` y `followUp` de tipo `EvidenceRequest` (ambos con `setup.kind="Pendulum"`, longitudes coherentes, `origin`, `reference` y arreglos `observations:[{"timeSeconds":...,"position":...}]`). Cada secuencia exige 120 a 30 000 muestras.
- El comparador usa **cruces de línea media extraídos de posiciones**, no el período teórico, para medir el período. Reporta cambio porcentual observado, cambio previsto y diferencia en puntos porcentuales; conserva datos privados, huellas y conclusiones sin reclamar hallazgo original.
- Nueva pantalla en Angular: **Demo estrella · Péndulo**, ES/EN; registro de pronóstico, fechas/huella, lectura de dos CSV (`tiempo_segundos,posicion`, con o sin cabecera) y comparación local.
- Los CSV solo representan coordenadas **aportadas por el usuario**. No son videos autenticados, aunque se hayan obtenido manualmente a partir de un video. Los controles sintéticos se marcan sintéticos, no evidencia física.

**Predicción ejemplo, NO resultado observado:** a 50 cm, ~1,419 s; a 80 cm, ~1,795 s; incremento teórico ~26,49 %. No confundirla con datos reales. Una coincidencia numérica tampoco demuestra descubrimiento científico nuevo.

### Reproducción local en el equipo autorizado

En `DESKTOP-ESGSLJH`, carpeta `C:\PROOFLAB-AI-HACKATHON`, utilizar la API de competencia habitual en `127.0.0.1:5391` y Angular en `127.0.0.1:4300`. Abrir la pestaña **Demo estrella · Péndulo**, registrar 0,5 → 0,8 m, guardar ID y SHA y luego cargar dos series CSV. Si se hace una prueba con datos artificiales, etiquetarla `Simulated` desde API; la UI solo envía `ManuallyEntered` (por diseño conservador).

Ni esta pantalla ni el endpoint realizan peticiones a Meta ni cambian la bandera `Meta:AllowPaidRequests=false`. El proceso temporal de pruebas 5393 es independiente.

## Pendientes obligatorios para la demo final

1. Capturar **video real** del péndulo de 300 g, longitudes del pivote al centro de masa de 0,5 y 0,8 m, cámara fija, escala/timestamps reales, tres repeticiones de 20 s por condición. Permitir primero una prueba piloto sin valor de evidencia definitiva.
2. Verificar respuesta de **Muse Spark 1.3** en vivo y diagnóstico del HTTP502 anterior, solo con crédito/gasto autorizado. Permitir que Muse proponga y justifique una longitud antes del segundo ensayo, sin alterar un pronóstico ya registrado.
3. Decodificar y usar **SAM 3.1** sobre fotogramas reales: correspondencia de máscaras por tiempo, centro de masa por frame, control de identidad del objeto y calibración; registrar video fuente, SHA, parámetros y fallos. **No extrapolar una máscara fija a un seguimiento de video.**
4. Propagar incertidumbre instrumental y del algoritmo, comparar medición automatizada con cronometraje/manual sobre el mismo video, y predefinir un criterio de éxito/fallo antes de revelar la segunda prueba.
5. Conectar los resultados al Scientific Method Engine de cinco etapas, con decisiones de director Muse y revisión humana, informe ES/EN y trazas de tokens/costos reales.
6. Consolidar demos adicionales: matemática Lean 4 (teoremas conocidos/Goldbach finito); análisis ciego y sin fuga temporal de datos históricos NASA Kepler; director autónomo que selecciona herramientas, pide evidencias y limita presupuesto.

## Guion del video (3 minutos, en inglés)

- 00:00–00:20: investigación iniciada por el director.
- 00:20–01:20: video auténtico 50 cm → SAM → trayectoria → comparación del período.
- 01:20–01:45: Muse recomienda 80 cm, predicción sellada, nuevo video y contraste.
- 01:45–02:10: matemática Lean, aceptación y rechazo controlados.
- 02:10–02:35: datos históricos, análisis ciego con conjunto de validación.
- 02:35–03:00: director, evidencia, límites, costo y reporte final.

**No se debe grabar ningún paso todavía no implementado como si ya funcionara.** El material final en inglés debe mostrar salidas auténticas y sus límites. Fuente de verdad del código: `vherediadepvops/ProofLab-AI`. Datos científicos privados y secretos nunca al repositorio público.

## Incremento de interoperabilidad móvil — 27/10/2026

ProofLab ya dispone de un mecanismo de **carga directa de video** para hasta tres archivos 50 cm y tres archivos 80 cm. Acepta contenedores MOV, MP4, M4V, WebM, MKV, AVI y 3GP/3G2 por la API local, verifica el códec con FFprobe y decodifica con FFmpeg sin pasar datos a Meta. El servidor genera una miniatura JPEG compatible con navegador y un seguimiento local preliminar, preservando archivo original y SHA-256 en carpeta privada ignorada por Git. El usuario debe indicar a qué longitud pertenece cada archivo. El flujo CSV previo continúa como opción avanzada.

**Prueba auténtica del formato:** se subió a la API un MOV HEVC real de iPhone 14 Pro Max (1920×1080, ~60 FPS, 30,5 s); decodificación exitosa, detecciones locales en 316/366 muestras, pero **no se obtuvo período científicamente fiable**; quedó `UPLOADED_NEEDS_MANUAL_TRACKING_REVIEW`. Se comprobó también un MP4 H.264 sintético con poster JPEG y HTTP 200. Esto no demuestra SAM sobre video ni garantiza todas las combinaciones de móvil/códec. Guía técnica [VIDEO-UPLOAD.es.md](VIDEO-UPLOAD.es.md). En un despliegue público de jurado harán falta autenticación, cuotas y privacidad.

**English:** [FLAGSHIP-DEMO.md](FLAGSHIP-DEMO.md).
