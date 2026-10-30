# SAM por segmentos: estado verificado (29/10/2026)

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

Se realizaron **3 de las 5 pruebas** autorizadas: **1 llamada Spark + 25 llamadas SAM**. SAM estimado total: **US$0,4368**; la llamada Spark tuvo un máximo aprobado de US$0,25. Estos importes no son una factura.

| Prueba | Reparto | Tiempo SAM + preparación/unión | Resultado |
|---|---|---:|---|
| Péndulo de pelota, 951 fotogramas; experimento con 21 referencias Spark | 18 segmentos, 4 simultáneos | 62,575 s | 888 máscaras; uniones inconclusas |
| Mismo video, referencias previas | 4 segmentos, 4 simultáneos efectivos | 32,903 s | 951 máscaras; dos uniones inconclusas |
| Video corto de WhatsApp, 182 fotogramas; Edge real | 3 segmentos simultáneos | 20,071 s de backend + 0,933 s de vista | 182 máscaras, uniones válidas, reproducción, θ, ω, vectores y rastro |

La preparación del video corto bajó de 3,602 s en la prueba anterior a 2,293 s en esta prueba. Son observaciones individuales, no una garantía de mejora global: la latencia de Meta varió.

El experimento de muchas referencias Spark produjo cajas fuera de la pelota y **no se incorpora a main**. No se elevó artificialmente la tolerancia de unión para aceptarlo. Tampoco se afirma haber probado 8 o 12 solicitudes simultáneas reales: los casos válidos disponibles se limitaron a 3–4 segmentos. El planificador y el límite 4/8/12 se verificaron con transporte sintético.

## Verificación y continuidad

39 pruebas .NET de segmentos y 42 Angular focalizadas correctas; prueba real en Edge sin errores de consola/red. Sin CI y sin secciones nuevas de interfaz.
La evidencia, respuestas, videos y libro de consumo permanecen privados en el equipo autorizado; no publicar tokens ni videos.

Pendiente: mejorar la localización densa con Spark y validar continuidad en el video largo antes de recomendar 19/28 peticiones para cualquier grabación. Las dos pruebas autorizadas restantes no se consumieron.

Fuentes: [facturación y límites Meta](https://dev.meta.ai/docs/pricing-rate-limits), [prompts SAM](https://dev.meta.ai/docs/sam/segmenting), [localización con Spark](https://dev.meta.ai/docs/image-understanding).
