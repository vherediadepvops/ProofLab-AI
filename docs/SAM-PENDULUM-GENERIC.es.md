# Péndulos de distintas formas y materiales

La inferencia directa usa la frase nominal **`pendulum bob`**. No filtra por metal, color, forma o un video de ejemplo. SAM segmenta el objeto solicitado; ProofLab calcula centroides, vectores y rastro a partir de sus máscaras y los tiempos originales.

El protocolo `PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V3_GENERIC_BOB` y la clave de caché distinguen este objetivo del antiguo `metal pendulum bob`. El modo API Meta siempre solicita un análisis nuevo. El modo con caché puede recuperar evidencia histórica con registros de máscaras, conservando su descripción y procedencia originales; una respuesta histórica vacía no sustituye al nuevo análisis. El parser sigue validando máscaras e identidad antes de dibujar. No se borran recibos ni se ocultan intentos pendientes.

## Interpretación del movimiento

El panel científico existente evalúa si la trayectoria observada es compatible con un péndulo simple: ciclos, cobertura, estabilidad y un arco resoluble alrededor del pivote. Una trayectoria periódica en línea recta no basta. El resultado y sus criterios se incluyen en el informe reproducible.

- **Compatible** significa compatibilidad geométrica y temporal; no certifica que haya una cuerda ni demuestra el mecanismo físico.
- **Inconcluso** si faltan detecciones, un pivote utilizable, suficientes ciclos o curvatura resoluble, o el seguimiento es ambiguo.
- El material y la forma no se deducen del centroide. Este tampoco equivale necesariamente al centro de masa.
- La perspectiva, el movimiento de cámara, el giro de una moneda o las oclusiones pueden afectar la trayectoria. Los péndulos dobles y otras geometrías no se aprueban automáticamente como péndulo simple.

No se añaden secciones de interfaz, llamadas a Muse ni reintentos automáticos. Cada envío manual mantiene el costo visible. Las pruebas locales usan máscaras y trayectorias sintéticas; la detección real de cada nuevo montaje requiere su respuesta SAM y revisión.

La [documentación oficial de Meta](https://dev.meta.ai/docs/sam/segmenting) recomienda una sola frase nominal corta para segmentación textual. Una lista de materiales, instrucciones largas o alternativas no reemplaza el reconocimiento de escena.

[English](SAM-PENDULUM-GENERIC.en.md).