# Revisión del cuerpo con contexto ampliado — 30/10/2026

## Comportamiento

La revisión visual existente de Spark recibe la escena completa, un recorte sin caja candidata por referencia y tres recortes de suspensión. No añade una inferencia ni una sección de interfaz. La selección inicial puede corresponder a una cuerda: por eso la región del cuerpo conserva un mínimo del 60% de la altura original (40% del ancho, ampliable por caja candidata). Spark debe localizar el cuerpo completo dentro del recorte, independientemente de forma, material o color. Cuerdas, manos y sombras no son el objetivo.

Las cajas se expresan en 0..1000 dentro del recorte y se transforman usando sus dimensiones y desplazamiento exactos. Cajas que tocan un borde, incertidumbre o cuerpo fuera de campo no alimentan SAM. Geometría, imagen y respuesta están vinculadas mediante hashes. V1/V2 siguen siendo legibles; nuevas revisiones usan SPARK_VISUAL_BODY_AND_EXIT_CROPS_V3. Se mantienen la suspensión revisada, originales, recibos y cachés históricos. No se entrenó un modelo ni se reemplazaron máscaras antiguas.

## Cinco llamadas autorizadas

| # | Solicitud | Tiempo de llamada | USD estimados | Resultado |
|---|---|---:|---:|---|
| 1 | Spark, control sintético 50 cm | 11,88 s | 0,0317795 | 10 referencias |
| 2 | SAM continuo, control | 94,80 s | 0,2400000 | 1200/1200 máscaras |
| 3 | Spark, grabación real 50 cm | 23,82 s | 0,06111875 | 4 referencias; 6 inciertas por recorte incompleto |
| 4 | Spark, real con región ampliada | 18,74 s | 0,0577155 | 10 referencias, suspensión consistente |
| 5 | SAM continuo, real | 193,55 s | 0,3656000 | 1828/1828 máscaras |

Total US$0,75621375 estimados, no factura. Autorización agotada: no repetir sin una nueva cantidad autorizada. No son cinco estudios científicos completos. Se reutilizaron propuestas previas de Spark sobre video completo. Tiempos excluyen preparación/validación offline. Las peticiones se hicieron explícitamente, sin reintentos automáticos. Permisos manuales del propietario permanecen habilitados.

## Resultado medido y límites

- Sintético 640×480, 60 FPS: error medio del centro 30,39→1,04 px, θ 7,82→1,47°, ω 0,385→0,059 rad/s; cobertura 86,58→100%. Pivote: error 39,99→7,18 px. Período observado 1,421054 s frente a referencia 1,418746 s.
- La comparación cambió referencias/pivote y SAM segmentado por continuo: no aísla causalmente el recorte. El sintético se ejecutó con la región inicial del 40% de altura. Solo el real tuvo prueba Meta con la regla final del 60%; geometría final cubierta en pruebas locales.
- Real 50CM_1.MOV: 1828 máscaras, suspensión estimada con dispersión 3,32 px; T=1,415824 s y u estadística=0,000424 s (10 de 20 ciclos alternos). Es repetibilidad tipo A, no incertidumbre total. Sin verdad independiente de coordenadas no se certifica precisión angular. Cuatro fotogramas originales (300/600/900/1400) revisados visualmente: el contorno sigue el cuerpo.
- SAM continuo aún tardó 193,55 s; no declarar resuelta la latencia ni prometer precisión para toda forma/material.
- No se emitió nuevo veredicto Muse ni predicción congelada. No se repitieron 80 cm o todos los videos anteriores.

## Verificación local y reproducción

419 pruebas .NET y 97 Angular en Edge correctas. Build/publish .NET y build Angular correctos; warning existente de bundle 709,58/500 kB. Sin CI.

Edge recibió la respuesta SAM real guardada del control H.264 mediante interceptación CDP en un perfil aislado, con todos los POST bloqueados. Se comprobó avance de reproducción y valores dinámicos a 2/4/6 s, vectores y rastro. Se habilitó el componente para consumir ese archivo no registrado, sin crear un estudio servidor. Esta comprobación de componente no equivale a un flujo nuevo de producción. El real HEVC se auditó offline mediante fotogramas originales, no mediante una promesa de decodificación HEVC en Edge aislado.

Evidencias privadas: .private/body-crop-results (attempt-1..5.json, result-1..5.json, originales de respuesta, receipts, imágenes, measurements.json, browser.json, science.json). No publicar respuestas, tokens ni videos privados. Los resultados numéricos y la revisión visual se exportaron al informe del chat prooflab-5-pruebas-cuerpo.html. Ver también [pruebas en Edge](EDGE-TESTING.es.md).
