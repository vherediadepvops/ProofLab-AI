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

Una unión coherente de segmentos comprueba continuidad, no identidad correcta del objeto. Una respuesta Spark con cajas de formato válido y confianza alta puede localizar la cuerda o el fondo. Si SAM sigue esas cajas, las máscaras pueden ser coherentes entre partes y aun así producir una trayectoria incorrecta. La ausencia de θ/ω con TRACK_INCONSISTENT_WITH_SUSPENSION no se resuelve desactivando la validación ni moviendo manualmente solo el pivote. La validación visual de las cajas antes del seguimiento sigue pendiente; este cambio de tiempos no corrige ese problema.
