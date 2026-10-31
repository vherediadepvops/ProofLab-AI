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

## Revisión Spark posterior a SAM (29/10/2026)
Si la suspensión automática no supera la comprobación, el clic en Procesar puede autorizar una revisión adicional de Spark, con hasta US$0,05 incluidos de antemano en el máximo mostrado. Spark recibe el video completo, tres imágenes originales de referencia, diagnósticos calculados con todos los centroides SAM y 96 posiciones de muestra. Enviar el archivo completo no garantiza que Meta examine cada fotograma nativo. Spark propone una salida de cuerda y explica la discrepancia en ES/EN; el código conserva las comprobaciones numéricas y recalcula θ/ω. La confianza declarada por el modelo no es incertidumbre calibrada.

La revisión está ligada al SHA del video, análisis/respuesta SAM y escena originales. Se guardan respuesta cruda, uso, imágenes, diagnósticos y hashes en el archivo privado. La respuesta no reescribe máscaras ni predicciones históricas. Una revisión con cámara incierta/móvil o ubicación poco fiable mantiene el resultado inconcluso. Su explicación aparece en Detalles técnicos, sin secciones nuevas. La revisión guardada se reutiliza; una revisión fallida no vuelve a enviarse automáticamente. SAM y su reproducción sobreviven al fallo. Un reintento de diagnóstico requiere reconocimiento explícito del posible cobro anterior y conserva ambos intentos.

**Pruebas de esta implementación:** 72 pruebas .NET y 81 Angular; build local y navegador ES/EN a 1440/390. Tres solicitudes reales autorizadas: la primera terminó con rechazo local tras HTTP200, sin respuesta cruda conservada ni causa exacta demostrable (posible cobro); se corrigió la captura anterior a la validación y se realizaron otras dos. La pelota roja del análisis eab408fa1c094f66bb9479336e033705 tiene 932/951 máscaras, pero Spark propuso una salida de cuerda con confianza 0,62–0,78: ángulos aún inconclusos. En video3 indicó cámara UNCLEAR y confianza 0,68–0,72: también inconcluso. Son respuestas reales guardadas, no una demostración de mayor precisión. El resultado anterior de pelota roja descrito arriba corresponde a otro análisis. Video2 sigue dando valores dinámicos; abrir las tres pruebas desde caché no produjo llamadas adicionales. La primera prueba fallida cuenta dentro de las 3/3; no se repitió SAM. Sin CI.

## Superposición del laboratorio (30/10/2026)
La vista guiada muestra θ junto al objeto, más v y a junto a sus flechas, en etiquetas oscuras que siguen el fotograma. La caja redondeada se calcula desde los límites de la máscara actual. El rastro muestra hasta diez siluetas SAM reales de los últimos 1,5 s con opacidad decreciente; no genera formas para fotogramas sin máscara. La flecha de velocidad conserva su escala proporcional y desaparece con velocidad cero.

Un ángulo válido puede mostrarse aunque falten muestras para calcular derivadas. Sin observación angular válida no se mantienen valores anteriores. Las medidas y sus límites científicos no cambian; las capas existentes permiten ocultar seguimiento, vectores y rastro.

Verificado: 37 pruebas Angular del reproductor y del rastro, compilación de producción, capas y reproducción original 1080 × 1920 en Edge. Validación con evidencia guardada: cero solicitudes nuevas a Meta. Sin nuevas secciones ni CI.
