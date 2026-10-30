# SAM: segmentos originales y referencias Spark

Implementación verificada el 29/10/2026 (America/Lima). El modo **API Meta · segmentos** usa la tarjeta de procesamiento existente.

## Flujo

1. Leer los tiempos de presentación y los paquetes del archivo original. Elegir hasta ocho ventanas que comiencen en fotogramas clave; mantener cobertura completa, solapamientos verificables y al menos 50 fotogramas centrales por parte. Si faltan claves o referencias válidas, usar menos partes.
2. Preparar el video completo de Spark mediante remux y 50 imágenes originales orientadas en una sola extracción. Priorizar los fotogramas compartidos entre partes, cubrir principio/final y distribuir los restantes por la duración. La selección es determinista y auditable.
3. Enviar video completo + 50 imágenes a Spark. Pedir cajas del mismo objeto en cada referencia, sin presuponer forma/material. Mantener las tres estimaciones de suspensión identificadas por los ordinales 0, 25 y 49. Enviar el archivo completo no demuestra que el proveedor examine cada fotograma nativo.
4. Crear los segmentos con `-c:v copy`: sin codificador, escalado ni cambio de FPS. Los filtros de bitstream seleccionan paquetes y reubican los tiempos. Una lectura comprimida abastece los ocho archivos. Audio y otras pistas no se requieren para SAM; la reproducción conserva el original.
5. Verificar geometría, número de fotogramas, píxeles decodificados y PTS (tolerancia máxima 5 ms) contra la fuente. Enviar las partes válidas, hasta ocho simultáneas, con cajas Spark de sus propios fotogramas.
6. Unir solo resultados con continuidad comprobada. Una unión inconclusa conserva las respuestas; una recuperación continua requiere una nueva acción manual y presupuesto mostrado, no un reintento automático.

No existe una conversión sin pérdida de respaldo: si la copia original no puede verificarse, la operación falla de forma explícita. Se conserva la compatibilidad de lectura con lotes históricos. Los límites preexistentes de duración/fotogramas/tamaño del laboratorio siguen vigentes; esto no promete soporte ilimitado de 4K/240 FPS.

## Tiempo, reutilización y coste

- La cotización utiliza los límites nativos, no la división histórica de unas 50 imágenes por petición.
- Se quitó la consulta opcional de conteo de tokens en este flujo. El valor mostrado para Spark es una **estimación**, no un tope de facturación impuesto por Meta. La respuesta conserva el consumo real de tokens.
- El manifiesto privado de preparación verifica hashes del original, PTS, MP4 e imágenes antes de reutilizarse. La comprobación de píxeles de la fuente se conserva en memoria del servicio.
- La subida de Spark se reutiliza para la misma credencial y contenido durante la vida del transporte. Se identifica con hashes, sin guardar el token. Reiniciar el servicio pierde este índice de subidas; el manifiesto local sí persiste. Un fallo del proveedor no genera una nueva inferencia automática.
- No se solicita borrar los archivos subidos ni se configura caducidad al subir. Esto cumple la petición del propietario; la retención final depende del proveedor.
- La tarjeta existente muestra preparación, imágenes de referencia, subida, inferencia y verificación de Spark, cada SAM, unión y reproducción. Todos muestran duración; la subida indica bytes enviados. Durante inferencia se muestra actividad indeterminada porque Meta no entrega porcentaje. El 100% de subida no implica respuesta final del servidor.

## Correcciones encontradas durante pruebas

El parser y el dibujo aceptan máscaras cuya cuadrícula interna tiene dimensiones diferentes a su caja en la imagen original. Se transforman contorno, centro y área a píxeles originales; la verificación de unión compara las dos máscaras en ese mismo espacio. No se modifican las respuestas originales ni se rebaja el criterio de continuidad. La suspensión acepta tanto los ordinales antiguos 0/1/2 como 0/25/49.

## Verificación y límites

- Diez videos privados probados: seis MOV y cuatro MP4. Los seis MOV admitieron ocho partes; los MP4 admitieron 4, 5, 2 y 7 según sus fotogramas clave. Los diez pasaron conservación de píxeles/PTS. Los MOV de aproximadamente 46–48 MB produjeron totales de aproximadamente 47–49 MB, en lugar del caso anterior de 524 MB. Otros archivos pueden aumentar más por solapamientos; no se garantiza igualdad de bytes.
- 13 pruebas reales de las 30 autorizadas: 12 inferencias Spark y 71 SAM. Nueve videos obtuvieron unión verificada; el décimo se recuperó con una inferencia continua tras un resultado inconcluso. Se conservan los intentos fallidos en la bitácora privada.
- En un MOV, repetir Spark reutilizando preparación/subida redujo su etapa de 79,2 a 33,4 s. Es una comparación puntual: unos 33 s siguieron siendo inferencia del proveedor.
- 96 pruebas .NET dirigidas, 138 pruebas Angular en Edge, build/publicación locales y reproducción visual. El worker publicado acepta máscaras reescaladas y rechaza cajas disjuntas. Sin CI.
- **Seguimiento válido no equivale a física certificada.** Los diez archivos finales se decodifican, pero solo uno pasó la estimación automática de suspensión en esta campaña. En los otros nueve, θ/ω siguen inconclusos hasta revisar suspensión/cámara/trayectoria. Se mantienen los controles existentes de corrección humana.

## Archivos y reproducción local

- `SamAdaptiveNativeCopy.cs` y `SamAdaptiveSegmentPlan.cs`: planificación/copia y verificación.
- `AssemblyDenseGrounding.cs`, `AssemblyVideoGrounding.cs`, `AssemblyMetaTransport.cs`, `AssemblyProgress.cs`: referencias, reutilización y telemetría Spark.
- `sam-video-evidence.ts`, `sam-playback.ts`, `sam-suspension.ts`, `scripts/sam-seam-worker.ts`: geometría de máscaras y continuidad.
- `sam-video-replay.ts`: indicadores en el panel existente.
- Preparación offline: `dotnet run --project tools/SamAdaptiveQa -- --verify-clips <archivo> <sha256> <salida-dentro-de-.private>`; no llama a Meta.
- Ver [pruebas en Edge](EDGE-TESTING.es.md). La campaña utiliza perfil/CDP propios y respuestas archivadas para comprobar reproducción sin nuevas inferencias. Nunca guardar tokens, videos o recibos privados en Git.

Los tres permisos Meta y los envíos manuales ilimitados del propietario permanecen habilitados. La cuota de pruebas autorizadas al agente se controla en su bitácora, separada del servicio del usuario.

## Seguimiento del 30/10: tiempos y calidad del objeto

En API Meta/API Meta + caché, el cronómetro SAM se detiene cuando termina su solicitud; la revisión posterior de suspensión con Spark y la preparación de gráficos tienen tiempos separados en la tarjeta existente. SAM reutilizado aparece como caché con cero segundos de inferencia. Una revisión fallida no convierte una respuesta SAM guardada en fallida. 56 pruebas Angular locales verifican estos estados y el flujo segmentado; no se hicieron llamadas Meta para esta corrección.

Una unión coherente de segmentos comprueba continuidad, no identidad correcta del objeto. Una respuesta Spark con cajas de formato válido y confianza alta puede localizar la cuerda o el fondo. Si SAM sigue esas cajas, las máscaras pueden ser coherentes entre partes y aun así producir una trayectoria incorrecta. La ausencia de θ/ω con TRACK_INCONSISTENT_WITH_SUSPENSION no se resuelve desactivando la validación ni moviendo manualmente solo el pivote. Ese diagnóstico dio lugar a la verificación visual descrita a continuación.

## Revisión visual del cuerpo antes de SAM — 30/10

Spark primero localiza el montaje usando el video completo y las 50 referencias. Una segunda inferencia recibe imágenes originales orientadas con las cajas propuestas marcadas y una cuadrícula numérica. Debe localizar el cuerpo de forma independiente, confirmar o corregir cada caja, o declarar incertidumbre. Se aceptan cuerpos de cualquier forma/material/color (tuerca, moneda, bola, plomada, etc.); no se usa una plantilla de plomada metálica. La confianza declarada por el modelo no es una probabilidad calibrada.

La revisión cubre principio, mitad, final y referencias exactas compartidas por los segmentos: hasta 16 imágenes seleccionadas de las 50, típicamente 9–10 con siete/ocho cortes. No es una segunda revisión de todos los fotogramas. Solo las cajas revisadas pasan a SAM; nunca se interpolan ni se recuperan silenciosamente cajas descartadas del borrador. Una identificación incierta conserva la evidencia e impide lanzar SAM con ese objetivo.

Se preservan por separado respuesta original, imágenes marcadas, intento y respuesta de revisión, evidencia derivada y sus hashes. La suspensión se vincula a la localización exacta usada; sus controles geométricos no se relajan. Los archivos históricos siguen legibles. Una localización antigua sin esta revisión puede revisarse sin repetir la subida ni la inferencia inicial; los resultados SAM antiguos no se corrigen retroactivamente.

La tarjeta existente añade **Verificar objeto con Spark**, con duración y actividad indeterminada. La cotización incluye US$0,10 estimados para esa llamada: un análisis nuevo necesita dos inferencias Spark; un borrador reutilizable, una; una revisión ya guardada, ninguna. No es un tope impuesto al proveedor. Se mantienen hasta ocho segmentos sin recodificar, sin conteo previo de tokens, borrado remoto ni reintentos automáticos. Las claves históricas normalizan explícitamente CRLF para no invalidar la caché por el checkout del código.

Verificación local: 391 pruebas .NET y 56 Angular correctas; después del ajuste final del registro, 43 pruebas Assembly correctas. Dos recorridos de Edge con respuestas reales archivadas mostraron θ/ω variables a 2, 4 y 6 segundos, máscaras, vectores y rastro: plomada real (1938/1938 fotogramas) y animación de bola (1800/1800). En la plomada, el residuo radial pasó de unos 211 a 16,4 px con la caja corregida; la revisión sola tardó 18,5 s y SAM por partes 25,6–32,2 s. Son observaciones de estos archivos, no garantía de precisión universal ni aprendizaje del modelo.

Código: ObjectVisualReview.cs, AssemblyObjectReview.cs, AssemblyVideoGrounding.cs y VideoMediaDecoder.cs. Pruebas de incertidumbre, correspondencia exacta de fotograma, manipulación de cajas, recibos y borradores no revisados. Ninguna sección nueva de interfaz, sin CI ni cambios a los permisos del propietario.

Prueba adicional: animación de tuerca pequeña. Spark la identificó y revisó en 43,4 s (13,1 s de revisión), pero siete cortes SAM tuvieron diferencias de contorno en cuatro empalmes. Se mantuvo el resultado inconcluso. Una recuperación continua autorizada reutilizó la revisión (sin otro Spark), obtuvo 1799/1800 máscaras en 160 s de SAM y recuperó θ/ω, vectores y rastro en Edge. El coste/latencia del seguimiento continuo siguen siendo una limitación para objetos pequeños; no se rebajó la verificación de empalmes.

Esta actualización usó cinco pruebas reales adicionales: siete inferencias Spark y 23 SAM, contando el intento segmentado inconcluso y su recuperación. La bitácora de la campaña contiene 19 registros de 30 (uno terminó antes de consultar al proveedor); el ejecutor conserva un límite conservador de 11 registros adicionales. No ejecutar los restantes por rutina. Total acumulado: 19 Spark y 94 SAM. Build Angular local correcto con advertencia existente de tamaño inicial: 703,54 kB / 500 kB.
