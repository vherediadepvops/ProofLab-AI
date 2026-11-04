# Péndulos de distintas formas: Spark + SAM

El flujo vigente usa 50 imágenes iniciales y una revisión de nueve pares escena/cuerpo, con comprobación V5 del punto visible del primer fotograma. Consulta [Localización y revisión de la suspensión](DENSE-LOCALIZATION.es.md) para la selección actual y la compatibilidad de referencias antiguas.

## Actualización histórica — 2 de noviembre de 2026

La localización densa de Spark conserva sus 50 referencias por ronda (hasta 150 si necesita las tres rondas). Antes de usar sus cajas como indicaciones para SAM, ahora se comprueba el cuerpo oscilante con recortes ampliados independientes. La revisión debe distinguir el cuerpo de la cuerda, la sombra y el soporte, sin depender de que sea una plomada, pelota, moneda o tuerca.

La evidencia revisada usa `SPARK_DENSE_BODY_CROPS_V4`; conserva el consenso de suspensión de las referencias densas. Los resultados densos antiguos sin esta revisión no se reutilizan como una localización ya verificada. Se mantienen los hashes de archivos, imágenes y respuestas y los tiempos originales: no se restaura el costoso hash de píxeles decodificados por fotograma. El presupuesto cotizado incluye hasta US$0,10 para la revisión del cuerpo; no se inicia una llamada por el mero hecho de actualizar la cotización.

Verificación: 512 pruebas locales aprobadas, incluidas caja sobre la cuerda corregida hacia el cuerpo, revisión inconclusa, integridad de recortes y referencias de una segunda ronda. Son pruebas con respuestas simuladas, no una certificación de precisión del proveedor. Esta corrección no ejecutó llamadas a Meta ni modificó resultados históricos.

## Descripción histórica del flujo inicial

El texto siguiente documenta la primera versión (tres referencias y conteo de tokens) y no describe todos los pasos del flujo vigente.

## Flujo y modos
El botón **Procesar** conserva la tarjeta y los controles existentes. **API Meta** ejecuta una evaluación nueva de Muse Spark sobre el video completo y después un análisis SAM nuevo. **API Meta + caché** reutiliza máscaras auténticas compatibles; si faltan, puede reutilizar una localización válida o iniciar el flujo completo. Una respuesta SAM vacía se conserva en el historial, pero no es un HIT útil.

Spark recibe un MP4 privado de toda la grabación, sin recodificar ni quitar fotogramas, junto con tres imágenes originales etiquetadas como referencias espaciales. El remux MOV→MP4 verifica índices y tiempos originales. El contexto completo ayuda a identificar el objeto y las limitaciones visibles; los anclajes precisan sus cajas para SAM. Esto no garantiza que el proveedor examine cada fotograma nativo.

El objetivo no depende de material, color o forma: pelota, moneda, pesa u otro cuerpo suspendido. Spark declara CLEAR_SINGLE_OBJECT, AMBIGUOUS o NOT_VISIBLE. Sin un objeto inequívoco no se envía SAM. Las cajas emplean dimensiones orientadas e índices originales; SAM conserva el mismo ID de objeto y recibe el video completo.

## Costo y consentimiento
Antes del clic se muestra **Hasta US$…**, desglosando SAM y el presupuesto máximo Spark que cotiza el servidor (predeterminado US$0,25). Es un techo estimado, no una factura. SAM conserva su límite de US$0,50 por operación. La carga del video y las cotizaciones locales no ejecutan inferencia.
Solo al pulsar Procesar se autoriza transferir el video completo y las referencias. Se cuenta el contexto real del payload antes de la inferencia Spark y se comprueba el presupuesto. No hay reintentos automáticos. Una caché SAM válida cuesta US$0,00.

## Medición y procedencia
SAM entrega máscaras; ProofLab calcula centroides, ángulo, derivadas y rastro con tiempos originales. El centro visual no certifica el centro de masa. La localización automática usa origen SPARK_GROUNDING y no confirma un pivote humano.
La consulta científica a Muse en la bitácora existente interpreta las mediciones, la incertidumbre y las limitaciones. Es una llamada independiente: no se presume que comparta memoria con la localización ni se añade una tercera inferencia automática.

Cada ejecución Spark y cada recibo SAM preservan su identidad y respuesta original. El protocolo de video completo se distingue del histórico de tres imágenes; reutilizar una respuesta antigua no la convierte en un análisis del video completo.
Una observación ausente con caja cero puede expresar confianza en la ausencia. No debe invalidar la detección de otro objeto. La recuperación local conserva respuesta original y hashes; no inventa cajas ni hace otra llamada Meta.
Un fallo de Spark/SAM o una localización ambigua cierra el indicador de carga y conserva el mensaje de error.

## Límites
Las oclusiones, un objeto pequeño, la perspectiva y el movimiento de cámara pueden impedir una medición válida. El modelo no garantiza detección universal ni precisión científica. Los límites locales de formato, fotogramas y presupuesto siguen vigentes; no se promete admitir cualquier video 4K/240 FPS.

[Video en Muse](https://dev.meta.ai/docs/video-understanding) · [Prompts SAM](https://dev.meta.ai/docs/sam/segmenting#prompt-several-video-frames) · [English](SAM-PENDULUM-GENERIC.en.md).
