# SAM, mediciones locales y dictamen de Muse Spark

La inferencia directa de SAM usa **`pendulum bob`**, sin filtrar material, color o forma. SAM aporta máscaras y seguimiento; ProofLab calcula centroides, ángulos, período, cobertura y residuos geométricos con tiempos originales. Estos cálculos son datos de entrada, **no un dictamen sobre el fenómeno**.

**Muse Spark interpreta la evidencia de SAM** en la bitácora existente y devuelve `PENDULUM_COMPATIBLE`, `OTHER_MOTION` o `INCONCLUSIVE`, con justificación ES/EN. Su respuesta real, identificador y procedencia quedan registrados. No se deduce una clasificación del nombre del prompt, de una máscara aislada ni de una evaluación histórica que no contenga ese campo.

Muse recibe un resumen acotado de medidas y centroides con índices y tiempos originales, dimensiones, pivote y limitaciones. Las muestras se seleccionan de observaciones reales; no se interpolan huecos. La muestra reducida no sustituye los ciclos medidos. Muse no ha visto las imágenes del video y debe expresar su conclusión como una inferencia desde esos datos, sin inventar material, nudos o fuerzas observadas. Una respuesta SAM vacía solo acredita que no se detectó el objeto y permite un informe inconcluso.

## Flujo y preservación

Se reutiliza **Preparar evidencia → Consultar Muse** con su costo visible; no hay una nueva sección ni otra inferencia automática al procesar SAM. La clasificación se registra en esa misma consulta de Muse, antes de proponer otro experimento. Solo una clasificación compatible junto con medidas suficientes puede sustentar una predicción condicional; la app comprueba los datos y la aritmética.

El protocolo de SAM `PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V3_GENERIC_BOB` distingue el objetivo genérico del antiguo `metal pendulum bob`. La caché histórica con máscaras conserva su procedencia; una respuesta antigua vacía no reemplaza un análisis genérico. El protocolo de investigación `MUSE_SAM_INVESTIGATOR_V3` exige la clasificación explícita de Spark. Las respuestas y predicciones históricas conservan su JSON y hash, sin atribuirles una clasificación nueva.

La identificación de cada montaje depende de la evidencia real disponible. Perspectiva, cámara móvil, oclusiones, objetos múltiples y pivote impreciso pueden limitarla. Un centroide de máscara no certifica el centro de masa.

[Documentación oficial de segmentación de Meta](https://dev.meta.ai/docs/sam/segmenting). [English](SAM-PENDULUM-GENERIC.en.md).
