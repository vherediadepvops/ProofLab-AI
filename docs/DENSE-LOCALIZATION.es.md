# Localización y revisión de la suspensión

Protocolo vigente: `SPARK_ORIGINAL_50_CONTACT_AUDIT_V2` (03/11/2026).

## Flujo

1. La preparación local produce el video compatible y 50 imágenes originales distribuidas en el tiempo. No consume Meta.
2. La primera llamada Spark recibe el video completo y esas 50 imágenes identificadas. Propone cajas del objeto y posiciones de suspensión; son estimaciones de imagen.
3. La segunda llamada revisa el cuerpo y el contacto de suspensión en nueve referencias originales (ordinales 0, 6, 12, 18, 24, 31, 37, 43 y 49). Recibe nueve escenas completas con cuadrícula y candidato de suspensión marcado, más los nueve recortes de cuerpo ya existentes. No hay recortes de suspensión ni rondas ×1,5/×3.
4. La revisión sigue la cuerda cargada hasta su contacto efectivo con el soporte y compara distintos momentos. Distingue ese contacto del centro del nudo, transportador, sombras y lazo móvil. Cada respuesta confirma, corrige o deja incierto el punto y aporta una explicación visual breve. Las coordenadas del contacto corresponden siempre a la escena completa; las cajas del cuerpo, a su recorte.
5. SAM conserva su única solicitud de video completo cuando el objeto está resuelto. La suspensión debe superar la revisión y las comprobaciones geométricas antes de aportar valores angulares. Una suspensión inconclusa puede resolverse con el marcado manual existente; no activa otra inferencia automática.

## Evidencia y aceptación

Las 50 observaciones iniciales y su consenso permanecen intactos. La auditoría conserva sus nueve observaciones, decisiones, referencias, tiempos originales y consenso propio; nunca se presentan nueve como si fueran cincuenta. Los píxeles de cada escena marcada, recorte y respuesta quedan vinculados mediante hashes, y el lector reconstruye la derivación desde los recibos conservados.

Se exige cámara estática, al menos seis puntos utilizables de nueve y un grupo dominante con al menos el 60 % de los utilizables. Su dispersión máxima es el mayor de 3 px o el 0,5 % del lado menor del original; debe cubrir al menos tres intervalos temporales y el 60 % del video. Contacto móvil, confirmación contradictoria, grupos rivales o evidencia insuficiente dejan la suspensión pendiente. Una corrección estable puede sustituir la posición inicial para el cálculo, sin borrar aquella propuesta. No se rellena una auditoría ausente con el consenso inicial ni con un ajuste circular.

Coincidencia, confianza declarada y verificación por el mismo modelo no prueban precisión física. La estimación sigue pendiente de confirmación humana; las pruebas con respuestas simuladas validan el software, no la exactitud de Spark en nuevos videos.

## Costos y compatibilidad

Se conservan dos llamadas Spark: estimación inicial US$0,25 y revisión US$0,10 (total estimado US$0,35), además de SAM y Muse según la cotización. La revisión conserva 18 imágenes y un máximo de salida de 8192 tokens; las explicaciones adicionales pueden cambiar el consumo efectivo. No son una factura ni un límite de cobro impuesto al proveedor. No hay reintentos automáticos.

Los resultados de tres puntos, rondas densas, zoom progresivo y `SPARK_ORIGINAL_50_OBJECT_REVIEW_V1` siguen usando sus lectores históricos. No se modifican análisis guardados ni se atribuye la auditoría nueva a resultados anteriores. La revisión explícita adicional de suspensión conserva su vinculación al resultado SAM exacto; no es un fallback automático.
