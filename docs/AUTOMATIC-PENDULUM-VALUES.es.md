# Valores angulares desde Spark y SAM
Actualización: 29/10/2026.

## Flujo y procedencia
Spark recibe el video completo y tres imágenes originales para localizar un objeto suspendido, sin restringir su forma o material. El transporte de localización usa razonamiento medio, con el mismo límite de tokens y presupuesto. SAM sigue las cajas propuestas. Esto no entrena ni modifica los modelos de Meta.

El archivo SAM añade una referencia de suspensión tomada de la **misma respuesta Spark que originó su escena**. No toma la localización más reciente de otro análisis. Los recibos, identificadores, máscaras, hashes y escenas históricas permanecen intactos.

El reproductor y el worker científico comparten `sam-suspension.ts`. Si las tres propuestas visibles son consistentes y la trayectoria tiene geometría suficiente, se obtiene un pivote aproximado; un ajuste de arco cercano puede refinarlo. Se trabaja en píxeles originales, incluyendo la orientación y proporción del video. θ usa la vertical de la imagen y ω deriva de θ con los PTS originales, no de la velocidad de reproducción. Una corrección humana tiene prioridad.

## Presentación y límites
- θ y ω aparecen con ≈ en la cabecera existente y se actualizan con el fotograma. No se añaden secciones.
- Las detecciones con desviaciones espaciales grandes se omiten **solo de la visualización automática**. No se inventan observaciones ni se unen rastros a través de huecos.
- El informe de período y la entrada a Muse conservan todos los centroides originales: el filtrado visual no puede mejorar artificialmente la evidencia científica.
- El pie existente explica los fotogramas excluidos o una suspensión inconclusa. Una cámara que cambia de posición, perspectiva oblicua, cuerda oculta o seguimiento ambiguo puede impedir valores fiables.
- Son estimaciones de imagen, no calibración física ni centro de masa certificado. La confianza declarada por Spark y los residuos no son una incertidumbre total. La interpretación del fenómeno sigue correspondiendo a Muse.

## Verificación
68 pruebas .NET y 95 Angular, compilación y publicación local limpias; navegador ES/EN a 1440/390, cuatro vistas y cero secciones nuevas. Tres comprobaciones del worker con archivos reales.

Se realizaron seis flujos reales Spark→SAM (12 inferencias de un máximo autorizado de 24). Pelota roja: 916 lecturas angulares válidas y 28 fotogramas excluidos visualmente; video2: 701 lecturas; video4: 1798. Video4 es una **animación**, útil para comprobar geometría (aproximadamente ±30°), no una medición física. Videos1 y3: suspensión/cámara inconclusas, valores vacíos explicados; máscaras y rastro disponibles. No se afirma precisión universal ni seguimiento perfecto. Sin CI.
