# Segmentos integrados en Laboratorio / Segments in Laboratory

## Español
El modo **API Meta · segmentos** usa la conexión local seleccionada (5391 por defecto) y el botón Procesar existente. No requiere abrir 4327 ni tener previamente una escena en 5410.

1. La consulta de costo es local. Sin escena Spark, muestra un máximo estimado de SAM más el presupuesto de Spark.
2. Un clic manual con token autoriza ese flujo. Si falta la escena, Spark recibe primero el video completo y las referencias originales. Una identificación inconclusa detiene SAM.
3. Se recalcula el plan con cajas auténticas de Spark. Si el costo SAM supera el máximo mostrado, se detiene y se pide actualizar el costo.
4. Se preparan los fragmentos sin cambiar píxeles ni tiempos del original. Hasta cuatro solicitudes SAM trabajan simultáneamente; el número de fragmentos puede reducirse por la cobertura de las referencias Spark.
5. Se verifican recibos, hashes, tiempos y uniones antes de cargar las máscaras, vectores y rastro en las vistas existentes. Los resultados unidos se identifican como derivados de varias respuestas, nunca como una sola respuesta nativa.

Se eliminó el cupo experimental independiente de tres lotes. Se mantienen los envíos manuales sin límite de sesión, el presupuesto mostrado, la comprobación de origen y la ausencia de reintentos automáticos. No se modifica el cupo autorizado de pruebas de un agente.

Validación vigente: ver la corrección y telemetría al final. El resultado derivado permite visualización y mediciones existentes; no se registra como evidencia SAM nativa en el Research Director. Una unión inconclusa no habilita gráficos.

## English
**API Meta · segments** uses the selected local connection (5391 by default) and the existing Process button. It no longer requires a separate 4327 page or a pre-existing scene on 5410.

A local quote includes a SAM upper estimate and, when needed, the Spark budget. An explicit click with a token runs Spark first if localization is missing. The exact grounded plan must remain within the approved SAM estimate. Original pixels and timestamps are preserved; up to four SAM requests run concurrently, with the part count constrained by authentic Spark seed coverage.

The playback endpoint checks part receipts, hashes, source timestamps and seams before exposing joined masks to the existing vectors and trail views. These archives are explicitly derived from multiple authenticated responses, not one native provider response. The separate three-batch campaign quota is removed; manual admission, displayed cost and no automatic retries remain.

Validated with 36 .NET tests, 71 Angular tests and one real authorized batch on a 7.60-second / 182-frame video. Three concurrent SAM requests returned all masks; original playback, seeking and angle/angular-velocity overlays passed in Edge. Estimated total US$ 0.0382; not a billing receipt. Segment streams are reordered by source-frame index. All shared masks must have raster IoU >= 0.90, with at least two shared observations per seam. The bundled local worker uses the official SAM decoder; this is a continuity check, not scientific approval.

The existing card displays separate stage activity bars and elapsed times. Meta supplies no exact completion percentage. A failed validation permits original playback without unverified overlays. Derived archives remain distinct from native SAM evidence in Research Director.

## Corrección y telemetría — 29/10/2026

SAM propaga desde la referencia hacia delante y hacia atrás: el orden de llegada no es el índice de fotograma. Se validan índices, duplicados, identidad dentro del segmento y hashes; se ordenan las ventanas originales y se conserva el objeto canónico `1` de Spark. No se interpolan ni reemplazan máscaras.

La continuidad de cada empalme se verifica con el decodificador oficial `@meta-sam/parser`, ejecutado localmente en el worker empaquetado `Workers/sam-seam-worker.cjs`. Se exigen al menos dos observaciones compartidas y una intersección sobre unión (IoU) de máscaras >= 0,90 en todas ellas. Es un criterio de continuidad visual, no validación científica. El mínimo observado queda registrado. Empalmes divergentes continúan inconclusos. El worker requiere Node y se genera al compilar; no usa token ni red.

La tarjeta existente muestra preparación, Spark, cada segmento SAM, unión/verificación y gráficos/reproducción, con duración real y barras de actividad. No se presenta un porcentaje de Meta inexistente. Los tiempos de segmentos concurrentes no se suman. Tras un fallo se permite el original, sin habilitar evidencia o gráficos no verificados. Un lote consumido requiere actualizar la cotización antes de un nuevo envío manual.

Validación: 36 pruebas .NET y 71 Angular en Edge. Prueba real autorizada con video de 7,60 s / 182 fotogramas: una ejecución, tres solicitudes SAM simultáneas, Spark reutilizado, 182 máscaras y empalmes válidos. Preparación 3,602 s; SAM 14,177 / 14,987 / 15,544 s; unión 0,117 s; interfaz 0,817 s. Estimación conjunta US$ 0,0382, no factura. Reproducción, pausa, desplazamiento, θ y ω comprobados en Edge, sin errores JavaScript. Videos, token, recibos e identificadores privados quedan fuera de Git. Sin CI ni reintentos automáticos.
