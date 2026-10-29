# Segmentos integrados en Laboratorio / Segments in Laboratory

## Español
El modo **API Meta · segmentos** usa la conexión local seleccionada (5391 por defecto) y el botón Procesar existente. No requiere abrir 4327 ni tener previamente una escena en 5410.

1. La consulta de costo es local. Sin escena Spark, muestra un máximo estimado de SAM más el presupuesto de Spark.
2. Un clic manual con token autoriza ese flujo. Si falta la escena, Spark recibe primero el video completo y las referencias originales. Una identificación inconclusa detiene SAM.
3. Se recalcula el plan con cajas auténticas de Spark. Si el costo SAM supera el máximo mostrado, se detiene y se pide actualizar el costo.
4. Se preparan los fragmentos sin cambiar píxeles ni tiempos del original. Hasta cuatro solicitudes SAM trabajan simultáneamente; el número de fragmentos puede reducirse por la cobertura de las referencias Spark.
5. Se verifican recibos, hashes, tiempos y uniones antes de cargar las máscaras, vectores y rastro en las vistas existentes. Los resultados unidos se identifican como derivados de varias respuestas, nunca como una sola respuesta nativa.

Se eliminó el cupo experimental independiente de tres lotes. Se mantienen los envíos manuales sin límite de sesión, el presupuesto mostrado, la comprobación de origen y la ausencia de reintentos automáticos. No se modifica el cupo autorizado de pruebas de un agente.

Validación local: 32 pruebas .NET de segmentos; 140 pruebas Angular distintas entre las suites de reproductor, flujo manual y segmentos; compilación API. Transporte sintético para las pruebas de envío; ninguna inferencia Meta real. Comprobación Edge contra API local con video 50CM_1.MOV y token sintético, sin pulsar Procesar: presupuesto y botón verde habilitado. El contador real permanece en 43.

Limitación: el resultado derivado permite visualización y mediciones existentes; no se registra como evidencia SAM nativa en el Research Director. Uniones o detección inconclusas no generan gráficos inventados. Falta la validación de calidad y tiempo de un nuevo lote con respuestas reales de Meta.

## English
**API Meta · segments** uses the selected local connection (5391 by default) and the existing Process button. It no longer requires a separate 4327 page or a pre-existing scene on 5410.

A local quote includes a SAM upper estimate and, when needed, the Spark budget. An explicit click with a token runs Spark first if localization is missing. The exact grounded plan must remain within the approved SAM estimate. Original pixels and timestamps are preserved; up to four SAM requests run concurrently, with the part count constrained by authentic Spark seed coverage.

The playback endpoint checks part receipts, hashes, source timestamps and seams before exposing joined masks to the existing vectors and trail views. These archives are explicitly derived from multiple authenticated responses, not one native provider response. The separate three-batch campaign quota is removed; manual admission, displayed cost and no automatic retries remain.

Local validation: 32 .NET segment tests, 140 distinct Angular tests across affected suites, API build and Edge readiness inspection on the real local API. Submission tests use synthetic transports; no real Meta inference. Request counter unchanged at 43.

Limitation: derived archives are not advertised as native SAM evidence to Research Director. A fresh real-provider batch still needs quality and timing validation.
