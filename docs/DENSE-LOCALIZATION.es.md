# Localización de objeto y suspensión en 50 imágenes

Protocolo `SPARK_DENSE_OBJECT_AND_EXIT_50_V1`. Esta implementación se verificó localmente con proveedor simulado; su precisión y latencia con Meta real aún no se han medido. Publicar el código no despliega producción.

## Flujo nuevo

1. Preparar el video original y 50 referencias repartidas en el tiempo. Spark recibe el video completo y las referencias identificadas por fotograma y tiempo.
2. Pedir, en **cada una de las 50 imágenes**, una caja del cuerpo que oscila y un punto de salida libre de la cuerda. Pelotas, monedas, tuercas y plomadas usan el mismo contrato. Cuerda, soporte, mano y sombra no son el cuerpo.
3. Si el objeto o la suspensión siguen inconclusos, extraer otras 50 referencias distintas. Hay **tres rondas como máximo**, incluida la primera; se detiene antes si hay evidencia suficiente. Si no quedan 50 fotogramas distintos, no se repiten imágenes para completar la ronda.
4. Reutilizar el archivo de video ya subido. No consultar el conteo de tokens ni eliminar el archivo remoto. Conservar respuestas, referencias, tiempos, recibos y hashes de cada ronda.
5. SAM recibe las posiciones temporales del objeto cuando su identidad está suficientemente resuelta. La incertidumbre de suspensión puede mantener θ/ω inconclusos aunque haya máscaras del objeto.

Una respuesta terminada pero insuficiente puede activar la siguiente ronda. Un error de transporte, recibo o formato detiene el proceso y conserva el intento; no dispara automáticamente otra solicitud potencialmente facturable.

## Consenso de suspensión

- Solo con cámara clasificada como estática. `MOVING` o `UNCLEAR` conservan un resultado inconcluso.
- Buscar un grupo espacial dominante; usar su mediana, no el promedio de todos los puntos. Las distancias se calculan en píxeles del video original orientado.
- Radio de acuerdo: máximo entre 3 px y el 0,5 % de la dimensión menor. Requiere al menos 10 observaciones, el 60 % de las observaciones utilizables y el 20 % de todas las imágenes enviadas; un segundo grupo comparable impide aceptar el consenso.
- Exigir cobertura en al menos tres de cinco intervalos temporales y un recorrido temporal mínimo del 60 %. Los puntos fuera del grupo se conservan como evidencia, pero no desplazan la suspensión estimada.
- Estos umbrales son reglas diagnósticas, **no un intervalo de confianza estadístico del 95 %**. La confianza declarada por Spark tampoco certifica precisión geométrica.

El cuerpo se mueve: **no se promedian sus coordenadas entre fotogramas**. Se conserva la primera ronda que resuelve claramente el objetivo con suficientes observaciones distribuidas en el tiempo. Las máscaras de SAM y las verificaciones geométricas por fotograma siguen determinando qué valores pueden mostrarse. La mayoría de respuestas puede compartir un error; el consenso por sí solo no demuestra que el punto sea físicamente correcto.

## Costo, progreso y compatibilidad

La cotización nueva contempla hasta tres consultas Spark (con la configuración actual, US$0,25 estimados por ronda, hasta US$0,75), más las operaciones SAM y reservas del flujo correspondiente. Son estimaciones de autorización, no una factura ni un límite de facturación impuesto al proveedor. Una consulta resuelta temprano utiliza menos rondas.

El progreso de las rondas adicionales aparece en las barras existentes. No se agregan secciones al laboratorio. Los permisos manuales, contadores y credenciales del propietario no se cambian.

Los resultados históricos de tres puntos y las revisiones por recortes siguen siendo legibles; no se modifican sus coordenadas ni se presentan como resultados del protocolo nuevo. Este flujo se aplica a localizaciones nuevas. La recuperación valida la derivación contra las respuestas preservadas y sus hashes.

## Verificación local

- Suite .NET con mocks: **466/466** pruebas correctas, incluidos consenso, grupos ambiguos, cámara incierta, tres rondas sin referencias duplicadas, parada temprana, fallo de proveedor, persistencia y detección de alteraciones.
- Suspensión Angular en Edge headless: **17/17** aserciones correctas. El runner permaneció abierto tras informar éxito y se interrumpió; no se registra como una salida limpia del proceso.
- Compilación Angular de producción y API a carpeta aislada correctas. Angular mantiene advertencias de presupuesto de tamaño existentes.
- **Cero llamadas reales a Meta** en esta validación; sin CI ni despliegue. Faltan comparación visual con referencia independiente, costo efectivo y tiempos de las rondas reales.
