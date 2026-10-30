# SAM por segmentos: estado verificado (29/10/2026)

Estado histórico: el flujo vigente usa hasta tres partes y un único reintento continuo previamente cotizado y autorizado. Ver [flujo actual](SAM-NATIVE-SEGMENTS.es.md). Los cupos/resultados siguientes pertenecen a campañas anteriores.

Integrado en main y disponible en el selector existente «API Meta · segmentos».
Web local: http://localhost:4300/; API principal: 127.0.0.1:5391.
La autorización de pagos del propietario permanece activa en todas las ramas; ver [permisos Meta](META-API-CONNECTION.es.md). No existe un cupo fijo de tres lotes manuales.

## Algoritmo y concurrencia

- El plan nominal busca unos 53 fotogramas centrales por petición: 1000 → 19 y 1500 → 28. Los 50 fotogramas son un mínimo facturable, no una garantía de velocidad.
- Cada segmento debe contener una posición auténtica de Spark en ese fotograma. Si las referencias no cubren el plan nominal, se reduce el número de segmentos; no se interpolan cajas para completar el plan.
- La web usa la concurrencia cotizada por el servidor. `SamAdaptive:Concurrency` tiene valor predeterminado 8, admite 1–12 y se limita al número de segmentos disponibles. Esto limita trabajo simultáneo; no el número de envíos manuales.
- La preparación usa un intermedio compartido, verifica hashes original/intermedio en paralelo y prepara hasta cuatro segmentos a la vez. Para imágenes de al menos 8 megapíxeles usa como máximo dos trabajos locales.
- Se preservan resolución, orientación, píxeles decodificados y PTS originales. Los solapamientos deben tener al menos dos observaciones y un IoU de máscara ≥0,90 en cada fotograma compartido.
- La composición conserva recibos independientes y se etiqueta como derivada. Si falla la continuidad, no se publica como seguimiento válido. No hay reintentos de pago automáticos.

## Pruebas reales autorizadas y resultados

Se realizaron **4 de las 5 pruebas** autorizadas: **1 llamada Spark + 29 llamadas SAM**. SAM estimado total: **US$0,8086**; la llamada Spark tuvo un máximo aprobado de US$0,25. Estos importes no son una factura.

| Prueba | Reparto | Tiempo SAM + preparación/unión | Resultado |
|---|---|---:|---|
| Péndulo de pelota, 951 fotogramas; experimento con 21 referencias Spark | 18 segmentos, 4 simultáneos | 62,575 s | 888 máscaras; uniones inconclusas |
| Mismo video, referencias previas | 4 segmentos, 4 simultáneos efectivos | 32,903 s | 951 máscaras; dos uniones inconclusas |
| Video corto de WhatsApp, 182 fotogramas; Edge real | 3 segmentos simultáneos | 20,071 s de backend + 0,933 s de vista | 182 máscaras, uniones válidas, reproducción, θ, ω, vectores y rastro |
| Péndulo de 80 cm, MOV vertical, 1845 fotogramas | 4 segmentos simultáneos, Spark guardado | 170,660 s | Preparación correcta; 1845 máscaras; dos uniones inconclusas |

La preparación del video corto bajó de 3,602 s en la prueba anterior a 2,293 s en esta prueba. Son observaciones individuales, no una garantía de mejora global: la latencia de Meta varió.

El experimento de muchas referencias Spark produjo cajas fuera de la pelota y **no se incorpora a main**. No se elevó artificialmente la tolerancia de unión para aceptarlo. Tampoco se afirma haber probado 8 o 12 solicitudes simultáneas reales: los casos válidos disponibles se limitaron a 3–4 segmentos. El planificador y el límite 4/8/12 se verificaron con transporte sintético.

## Verificación y continuidad

95 pruebas .NET focalizadas de segmentos, orientación y Spark correctas en esta corrección. Las 42 Angular y la prueba de Edge anteriores siguen documentadas; esta corrección es de backend y se probó directamente por HTTP. Sin CI y sin secciones nuevas de interfaz.
La evidencia, respuestas, videos y libro de consumo permanecen privados en el equipo autorizado; no publicar tokens ni videos.

Pendiente: mejorar la localización densa con Spark y validar continuidad en el video largo antes de recomendar 19/28 peticiones para cualquier grabación. Queda una de las cinco pruebas autorizadas sin consumir.

Fuentes: [facturación y límites Meta](https://dev.meta.ai/docs/pricing-rate-limits), [prompts SAM](https://dev.meta.ai/docs/sam/segmenting), [localización con Spark](https://dev.meta.ai/docs/image-understanding).

## Corrección de orientación y tiempos de Spark

El MOV vertical almacena 1920×1080 con rotación −90°. FFmpeg produce el intermedio orientado 1080×1920 sin etiqueta de rotación. La igualdad anterior de dimensiones almacenadas/rotación lo rechazaba aunque los píxeles mostrados fueran idénticos. Ahora se comparan dimensiones orientadas y se mantienen las comprobaciones SHA-256 de **todos** los fotogramas y sus PTS. No se modifica el original.

El intermedio lossless fuerza IDR en los inicios reales del plan, en vez de cada dos fotogramas. En este video bajó de 1.016.300.036 a 491.210.226 bytes. Los cuatro clips conservaron todos los píxeles y tiempos (desviación máxima de PTS 1,501 ms). La preparación real tardó 42,693 s; no es comparable al fallo anterior de 15,9 s, que abortaba antes de acabar las verificaciones.

Las cuatro respuestas SAM terminaron, sin pérdidas de fotogramas. Los mínimos IoU de las tres uniones fueron 0,8837 / 0,9989 / 0,8212. Dos incumplen el umbral 0,90; el resultado sigue **inconcluso** y no habilita gráficos científicos. No se relajó el umbral ni se repitieron llamadas.

La espera anterior de Spark (72,3 s de interfaz) incluía subida 15,136 s, conteo de tokens 6,420 s, inferencia 25,880 s y limpieza 4,903 s; el resto corresponde a preparación y tareas del flujo. Las tres referencias nativas ahora se extraen en una pasada. Comparación local sobre el mismo MOV: 9,275 s antes y 5,524 s después, con SHA-256 de cada JPEG, índice, dimensiones y tiempo idénticos. Esto reduce esa fase local un 40%; no prueba una reducción equivalente de la latencia total de Meta.

Se registran tiempos privados por remux/verificación, extracción de referencias, subida, conteo, inferencia y limpieza. Se mantiene el envío del video completo, la calidad original, el presupuesto y el nivel de razonamiento. La prueba 4 reutilizó Spark y realizó cuatro llamadas SAM (US$0,3718 estimados, no factura).


## Recuperación continua de uniones inconclusas (30/10 UTC, 29/10 Lima)

Cuando todas las respuestas de un lote terminaron pero sus uniones no superaron la verificación, la cotización ofrece **una solicitud SAM sobre el video completo**. Se reutiliza Spark del mismo SHA y escena. El usuario debe pulsar Procesar otra vez; no se reintenta ni se consume un crédito automáticamente.

La recuperación utiliza una caja auténtica del fotograma original 0 del primer segmento previo, además de las tres referencias de Spark. Se verifican recibo, hash de respuesta, identidad de escena, dimensiones, tiempos y prompt original. Esa caja no se extrapola desde otro instante y su procedencia sigue siendo SAM. Si no hay una referencia inicial verificable, se detiene antes de enviar. No se altera el lote fallido ni se debilita el umbral IoU.

El video completo se remultiplexa sin recodificar y se comprueban todos sus hashes de píxel y PTS. En 80CM_1.MOV el archivo enviado ocupa 44.847.804 bytes, frente al intermedio segmentado de 491.210.226 bytes. El original se conserva.

**Autorización nueva de esta corrección: 5 llamadas; utilizadas 2 SAM, 0 Spark; quedan 3.** Los presupuestos de sesiones anteriores no se suman.
- Intento 1: 1.661 deltas, interrupción del proveedor antes de un resultado completo; 194,760 s. No se usa como evidencia.
- Intento 2, con referencia auténtica en el fotograma 0: 1.845/1.845 máscaras, sin cortes; preparación 33,314 s, solicitud SAM 183,775 s, validación 0,138 s, total 217,393 s.
- Estimación por llamada: US$0,369; potencial total US$0,738. El intento fallido puede haber sido facturado. No hay factura verificada.
- El reproductor decodificó 1.845 máscaras sin advertencias y produjo 1.845 ángulos y 1.843 velocidades angulares. Los extremos carecen de vecinos para derivación central. Suspensión estimada por Spark; sin calibración métrica ni revisión humana certificada.

El corte del primer intento coincide con el tramo hacia adelante desde la referencia 184. La documentación indica que SAM propaga después hacia atrás. La prueba desde el fotograma 0 terminó, pero esto no demuestra por sí solo la causa interna del fallo de Meta. Los nuevos diagnósticos conservan tipo de evento y motivos categóricos permitidos sin texto sensible.

103 pruebas .NET y 73 Angular en Edge correctas. La recuperación corrige continuidad; **no se presenta como una mejora de velocidad**. El modo segmentado conserva su camino paralelo para lotes coherentes y las mismas comprobaciones.
