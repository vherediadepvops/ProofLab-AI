# Localización y revisión de la suspensión

Protocolo vigente: `SPARK_ORIGINAL_50_VISIBLE_BODY_POINT_REVIEW_V5`. La auditoría de suspensión conserva `SPARK_ORIGINAL_50_CONTACT_AUDIT_V3`.

## Flujo

1. La preparación local produce el video compatible y 50 imágenes originales distribuidas en el tiempo. No consume Meta.
2. La primera llamada Spark recibe el video completo y esas 50 imágenes identificadas. Propone cajas del objeto y posiciones de suspensión; son estimaciones de imagen.
3. La segunda llamada revisa el cuerpo y el contacto de suspensión en nueve referencias originales (ordinales 0, 6, 12, 18, 24, 31, 37, 43 y 49). Recibe nueve escenas completas con cuadrícula y candidato de suspensión marcado, cada una seguida inmediatamente de su recorte de cuerpo correspondiente. V5 acerca el recorte del fotograma 0 cuando existe una propuesta inicial visible y suficientemente confiada; las otras ocho referencias mantienen su contexto amplio. Sigue siendo la misma revisión, sin imágenes ni llamadas adicionales. No hay recortes de suspensión ni rondas ×1,5/×3.
4. La revisión sigue la cuerda cargada hasta su contacto efectivo con el soporte y compara distintos momentos. Distingue ese contacto del centro del nudo, transportador, sombras y lazo móvil. Spark devuelve la ubicación observada, su visibilidad y una explicación visual breve. ProofLab calcula si coincide con el candidato (CONFIRMED), cambia de posición (CORRECTED) o no es utilizable (UNCERTAIN); no delega esa comparación al modelo. Las coordenadas del contacto corresponden siempre a la escena completa; las cajas del cuerpo, a su recorte.
5. SAM conserva su única solicitud de video completo cuando el objeto está resuelto. La suspensión debe superar la revisión y las comprobaciones geométricas antes de aportar valores angulares. Una suspensión inconclusa puede resolverse con el marcado manual existente; no activa otra inferencia automática.

## Evidencia y aceptación

Las 50 observaciones iniciales y su consenso permanecen intactos. La auditoría conserva sus nueve observaciones, decisiones, referencias, tiempos originales y consenso propio; nunca se presentan nueve como si fueran cincuenta. Los píxeles de cada escena marcada, recorte y respuesta quedan vinculados mediante hashes, y el lector reconstruye la derivación desde los recibos conservados.

Se exige cámara estática, al menos seis puntos utilizables de nueve y un grupo dominante con al menos el 60 % de los utilizables. Su dispersión máxima es el mayor de 3 px o el 0,5 % del lado menor del original; debe cubrir al menos tres intervalos temporales y el 60 % del video. Contacto móvil, confirmación contradictoria, grupos rivales o evidencia insuficiente dejan la suspensión pendiente. Una corrección estable puede sustituir la posición inicial para el cálculo, sin borrar aquella propuesta. No se rellena una auditoría ausente con el consenso inicial ni con un ajuste circular.

Coincidencia, confianza declarada y verificación por el mismo modelo no prueban precisión física. La estimación sigue pendiente de confirmación humana; las pruebas con respuestas simuladas validan el software, no la exactitud de Spark en nuevos videos.

## Una referencia principal para SAM

La política de cajas `SAM_SINGLE_PRINCIPAL_REFERENCE_V2` se conserva para reconstruir sus recibos y elegir referencias completas. Spark declara si el contorno completo está visible, si existe oclusión (por ejemplo, una mano) y si el cuerpo está recortado. Se requieren dos referencias completas y sin oclusión ni recorte, con geometría y tamaño/proporción coherentes. Entre ellas se elige la primera referencia temporal, aunque sea posterior al inicio del video. Las referencias parciales siguen conservadas, pero no pueden iniciar SAM como cajas completas. El flujo V5 exige el punto interior admisible del primer fotograma descrito abajo. Una caja completa, incluso del primer fotograma, no sustituye esa validación V5; las políticas anteriores conservan sus propias reglas al reconstruir recibos históricos. Se conservan sus coordenadas y fotograma exactos: no se inventan ni interpolan cajas. La selección es una heurística de imagen, no una garantía de identidad o precisión física.

Cuando se usa la política de caja, solo esa caja entra en el prompt SAM. Las demás observaciones permanecen en la escena y sirven para corroborar la selección y comparar las máscaras del fotograma correspondiente; no vuelven a indicar a SAM otro punto de inicio. La política y referencia exacta quedan vinculadas al presupuesto y al recibo. Los recibos históricos reconstruyen el prompt que realmente enviaron.

## Revisión de continuidad del seguimiento

Una comprobación local busca cambios bruscos de área que coincidan con un desplazamiento inesperado del centro y persistan en varios fotogramas. Usa los tiempos nativos y registra por separado las detecciones ausentes. La línea de estado existente indica el primer instante que necesita revisión y muestra por separado las detecciones faltantes y una suspensión sin resolver. Las comparaciones con otras cajas Spark son auxiliares: también pueden contener errores y no equivalen a etiquetas humanas.

Este aviso no demuestra que SAM haya seleccionado otro objeto, ni cambia las máscaras originales, los filtros de medición o los informes guardados. Los huecos siguen sin valores y las derivadas requieren vecinos válidos. No consume Meta, no activa reintentos y no rellena posiciones faltantes. La eficacia de la referencia única debe comprobarse con una prueba real autorizada; las pruebas locales verifican selección, integridad y detección de discontinuidades.

## Costos y compatibilidad

Se conservan dos llamadas Spark: estimación inicial US$0,25 y revisión US$0,10 (total estimado US$0,35), además de SAM y Muse según la cotización. La revisión conserva 18 imágenes y un máximo de salida de 8192 tokens; las explicaciones adicionales pueden cambiar el consumo efectivo. No son una factura ni un límite de cobro impuesto al proveedor. No hay reintentos automáticos.

Los resultados de tres puntos, rondas densas, zoom progresivo y `SPARK_ORIGINAL_50_OBJECT_REVIEW_V1` siguen usando sus lectores históricos. No se modifican análisis guardados ni se atribuye la auditoría nueva a resultados anteriores. La revisión explícita adicional de suspensión conserva su vinculación al resultado SAM exacto; no es un fallback automático.

### Punto visible del primer fotograma

V5 revisa el punto interior del fotograma original 0 introducido en `SPARK_ORIGINAL_50_BODY_POINT_REVIEW_V4`. En el recorte inicial más cercano, Spark debe identificar el material del punto y un rectángulo pequeño contenido por completo en una superficie visible del mismo cuerpo observado después. Debe excluir mano, uñas, cuerda, huecos, sombras, fondo y marcas añadidas. El punto se observa por separado, con margen respecto de los bordes: no se calcula como centro del rectángulo ni se copia de otro fotograma. Una mano puede tapar parte del contorno sin tapar toda la superficie; eso no convierte una caja parcial en cuerpo completo.

ProofLab transforma punto y superficie desde el recorte a coordenadas originales y comprueba localmente material, visibilidad, confianza declarada, tamaño de la superficie, margen interior del píxel exacto que recibirá SAM y coherencia con la ubicación inicial. Si la identidad o esa coherencia no se resuelven, devuelve `INITIAL_POINT_IDENTITY_UNRESOLVED` y no envía SAM. Estas comprobaciones no certifican que todos los píxeles sean del objeto ni garantizan precisión física.

El punto queda ligado a la respuesta, imágenes y escena verificadas. Con al menos dos referencias completas y claras del mismo cuerpo, `SAM_INITIAL_BODY_POINT_WITH_CLEAR_REFERENCES_V3` envía el punto inicial admisible como única referencia de seguimiento; las cajas posteriores permanecen para auditoría. SAM recibe el video completo desde el primer fotograma, sin reiniciar la trayectoria con otras cajas. Si no hay punto utilizable en V5, no se envía SAM: no se inventan coordenadas ni se activa automáticamente una caja o un inicio posterior.

Los puntos guardados anteriores a V5 no autorizan un nuevo envío SAM ni un reintento que reutilice esa referencia. `SAM_INITIAL_REFERENCE_REVIEW_REQUIRED` pide dejar API Meta activada, desactivar Caché + SQLite, actualizar el costo y confirmar Procesar para una nueva revisión. Los resultados SAM completos y válidos ya guardados siguen disponibles sin otra inferencia. Los protocolos, hashes y respuestas históricos V2/V3/V4 se conservan; no se presentan como revisión V5. La auditoría de suspensión conserva V3.

### Confirmación humana del punto inicial ambiguo

Para nuevos envíos, el punto V5 no se acepta automáticamente si falta la referencia de calidad del fotograma 0, si su contorno está incompleto, si está ocluido o si está recortado. Las referencias claras de fotogramas posteriores no eliminan este requisito. `SAM_INITIAL_BODY_POINT_CONFIRMATION_REQUIRED` detiene SAM antes de preparar su solicitud o reservar créditos. La restricción también se aplica a las rutas legadas que podían enviar un análisis nuevo; no afecta a la lectura de resultados guardados.

La pantalla presenta el primer fotograma original junto a una referencia clara del mismo cuerpo. El usuario debe marcar expresamente un píxel dentro de una superficie visible del objeto, evitando mano, uña, cuerda y sombra. No se selecciona por defecto el punto sugerido por Spark. Si el cuerpo no se distingue, se puede cancelar sin enviar SAM. Guardar el punto es una operación local: no repite Spark ni consume Meta.

La confirmación `HUMAN_CONFIRMED_VISIBLE_BODY_POINT_V1` se guarda en un registro inmutable ligado a investigación, rol, video registrado, SHA original, ejecución y escena Spark, respuesta Spark, imagen original del fotograma 0, dimensiones, coordenadas nativas y fecha. No altera el punto original de Spark ni su evidencia. La referencia `SAM_HUMAN_CONFIRMED_INITIAL_BODY_POINT_V4` vincula ese registro al nuevo presupuesto, prompt y recibo. SAM recibe el video completo con un único punto en el fotograma 0; no se añade un ancla posterior ni un reintento automático. Cambiar el origen o manipular el registro invalida la confirmación.

La continuación conserva los permisos Meta y el costo previamente autorizado. Recargar o volver a la revisión recupera datos locales y no envía otra inferencia por sí solo. La selección humana del píxel no equivale a aprobar todo el seguimiento, verificar la suspensión o certificar una conclusión científica; esas revisiones siguen separadas. Los recibos anteriores reconstruyen su prompt y hashes originales.
