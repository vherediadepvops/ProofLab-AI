# Spark localiza; SAM sigue; Muse interpreta

Al pulsar **Procesar**, el flujo de un video nuevo comienza con Muse Spark: examina tres fotogramas originales y localiza el mismo objeto suspendido, sea una pelota, moneda, arandela, pesa u otra forma/material. Devuelve cajas sobre la imagen completa orientada y una decisión: objeto claro, ambiguo o no visible. No basta la frase `pendulum bob`.

Con al menos dos observaciones claras, ProofLab convierte las coordenadas a píxeles originales y envía a SAM cajas con el mismo ID de objeto y sus índices de fotograma reales. SAM propaga el seguimiento por el video. No se mezclan las cajas con una descripción textual del objeto.

## Interfaz y costo

Se conserva el botón Procesar y la tarjeta de costo actuales. Las consultas de costo/caché son GET locales en paralelo, sin llamadas de pago ni envío de tokens. El precio mostrado incluye SAM y, cuando falta localización guardada, hasta US$0,05 de Spark. La estimación SAM conserva su límite de US$0,50; el total puede llegar a US$0,55 y se muestra antes del clic. Un HIT SAM utilizable cuesta US$0.

Durante el mismo clic se muestra primero la localización de Spark y luego el seguimiento SAM. Una localización ambigua, un error o un cambio de video/presupuesto detiene la continuación; no hay reintentos automáticos. La evidencia de Spark guardada para el mismo video puede reutilizarse. No se añade una sección ni se exige confirmar Reconocer montaje.

## Procedencia y límites

El protocolo de localización es `ASSEMBLY_OBJECT_GROUNDING_3_NATIVE_FRAMES_V1`. La escena queda como `SPARK_GROUNDING`, distinta de una revisión humana. Solo se sigue el rol object; no se exige identificar cuerda/nudo/polea para localizarlo. El pivote no se confirma automáticamente: las mediciones angulares conservan su requisito de un punto de suspensión definido.

Las respuestas, cuadros y hashes de Spark se conservan; SAM mantiene su propio recibo y máscaras. La caché histórica válida sigue disponible; una búsqueda textual vacía no bloquea la nueva localización visual y no se presenta como un HIT con trayectoria.

Localizar un objeto en tres imágenes no prueba que su movimiento sea pendular. Después del seguimiento, ProofLab calcula medidas, cobertura e incertidumbre. **Muse Spark investigador**, mediante Consultar Muse, interpreta esa evidencia temporal y devuelve compatible con péndulo, otro movimiento o inconcluso con justificación ES/EN. Las predicciones históricas no cambian.

La detección puede fallar por ambigüedad u oclusiones. No se inventan máscaras, centros de masa ni revisión humana.

[Formato oficial de prompts de video SAM](https://dev.meta.ai/docs/sam/segmenting#prompt-several-video-frames). [English](SAM-PENDULUM-GENERIC.en.md).
