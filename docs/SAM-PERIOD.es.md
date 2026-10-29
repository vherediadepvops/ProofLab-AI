# Período observado desde SAM

El laboratorio calcula el período desde los centroides del objeto en una respuesta SAM autenticada y los PTS del video original. No usa la longitud, el período teórico, el seguimiento local FFmpeg ni un FPS supuesto. Es una estimación local preliminar; no representa aprobación de Muse.

## Método V1

1. Convertir centroide y pivote a la geometría de la imagen original y calcular el ángulo con la vertical.
2. Tomar como nivel central el punto medio entre los percentiles 5 y 95 del ángulo. Detectar cruces ascendentes con histéresis.
3. Interpolar el instante de cruce únicamente entre fotogramas originales consecutivos con detección. Una detección ausente, una posición geométricamente inválida o un salto temporal mayor a tres intervalos medianos separan los segmentos.
4. Formar períodos entre cruces consecutivos de la misma dirección dentro del mismo segmento. No conectar segmentos.
5. Usar ciclos alternos para no compartir el mismo cruce entre dos períodos seleccionados. Calcular media, desviación muestral s y u = s/√n, con al menos tres ciclos seleccionados.

La incertidumbre indicada mide solo repetibilidad, suponiendo errores aproximadamente independientes. Alternar ciclos no garantiza independencia. No incluye sesgos de segmentación, pivote, perspectiva, movimiento de cámara o calibración del reloj; tampoco es un intervalo del 95%. Una dispersión numéricamente cero no significa exactitud perfecta. Véase [NIST: incertidumbre estándar](https://physics.nist.gov/cuu/Constants/definitions.html).

## Calidad y límites

Los umbrales están versionados en SAM_PERIOD_POLICY: amplitud mínima 0,5°, al menos 20 intervalos entre fotogramas por ciclo, cobertura utilizable ≥80%, dispersión y deriva del período ≤5%, y MAD del radio aparente ≤10% del radio mediano. Son criterios de ingeniería, no leyes físicas ni certificación.

El resultado es Inconcluso si no satisface esos criterios, faltan ciclos/pivote o el montaje no es un péndulo simple. Los valores diagnósticos se conservan; no se eliminan ciclos atípicos para aparentar acuerdo. Las pérdidas de SAM se muestran aunque queden suficientes ciclos válidos. La revisión visual de objeto y suspensión sigue siendo necesaria.

## Reproducción

Descargar informe reproducible genera JSON privado con procedencia (hash del video, análisis y respuesta SAM, objeto y revisión del pivote), geometría, PTS, centroides, ciclos, política y limitaciones ES/EN. El envoltorio contiene payloadJson y SHA-256 de sus bytes UTF-8 exactos: verifica integridad, no firma ni autenticidad criptográfica.

Después de verificar ese hash, ejecutar measureSamPeriod(JSON.parse(payloadJson).input) reproduce assessment. No se guardan resultados nuevos en SQLite en este paso. La futura evaluación Muse y el congelamiento de una predicción requieren otro flujo.

Pruebas locales: sam-period.spec.ts, sam-period-panel.spec.ts, pendulum-master-player.spec.ts y sam-playback.spec.ts. Incluyen 60/120/240 FPS, PTS irregulares, huecos, geometría, deriva, ausencia de datos, cambio de pivote y reproducción del informe.

Comprobación del 28/10/2026 con evidencia SAM ya guardada: 1781/1828 detecciones, 20 ciclos completos y 10 alternos usados; T = 1,415864 s, u estadística = 0,000432 s. No es incertidumbre total. Cálculo del período ≈2 ms tras decodificar las máscaras. Sin llamadas nuevas a Meta.
