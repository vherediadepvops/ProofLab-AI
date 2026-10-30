# Revisión visual de la suspensión

La revisión existente de cajas del objeto también recibe tres recortes ampliados de la suspensión. La API pide el punto de salida de la cuerda libre; conserva incertidumbre ante oclusión, movimiento de cámara o desacuerdo. No añade una llamada independiente ni una sección de interfaz. Sí añade imágenes/tokens a la revisión.

## Geometría y procedencia

El recorte ocupa el 40% de cada dimensión del fotograma orientado, con mínimo de 96 px y limitado a la imagen. El pivote inicial solo centra la búsqueda. Se escalan las referencias para inspección; no el video. Spark devuelve coordenadas normalizadas al recorte. La transformación usa el origen y dimensiones exactas para volver a los píxeles originales. Se guarda el hash del JPEG y su correspondencia con fotograma/tiempo original.

Las tres referencias se eligen entre las imágenes revisadas cerca del 20/50/80% del intervalo temporal disponible, antes de observar la respuesta. El formato V2 mantiene los ordinales originales en el manifiesto y los compacta a 0/1/2 en los puntos derivados. El lector conserva V1 y verifica respuestas, hashes y derivación. La interfaz V2 no reemplaza el punto visual por un ajuste circular y exige acuerdo de max(3 px, 0.5% del lado menor); todavía comprueba coherencia radial con SAM.

## Evidencia del 30/10/2026

Cinco llamadas reales Spark, ninguna llamada SAM nueva; máscaras anteriores usadas para comparación offline, sin sustituir sus recibos. Estimación de uso US$ 0.201404, no factura. Los casos 1–4 probaron inicio/mitad/final; solo el quinto probó la selección interior final. No es una validación de cinco flujos científicos completos.

| Case | Pivot error before → after | Outcome |
|---|---|---|
| Synthetic 50 cm | 39.99 → 6.43 px | SAM tracking remains wrong; angle MAE 7.82 → 8.37 degrees |
| Synthetic 80 cm | 39.65 → 8.39 px | Angle MAE 4.48 → 1.32 degrees; omega MAE .0557 → .0327 rad/s |
| Real 50CM_1 | No ground truth | Inconclusive: about 30 px disagreement |
| Real 80CM_1 | No ground truth | Inconclusive: occluded/uncertain initial anchor |
| Real 80CM_2 | No ground truth | Interior references recover an estimated pivot; spread 3.39 px |

411 pruebas .NET y 96 Angular aprobadas; build de producción aprobado con aviso de presupuesto del bundle. No CI.

Pendiente: localizar por qué el SAM del control de 50 cm sigue un punto incorrecto, validar cajas/máscaras y repetir una matriz de controles con verdad conocida. No relajar la incertidumbre para presentar valores. La exactitud de los pivotes reales no está certificada.
