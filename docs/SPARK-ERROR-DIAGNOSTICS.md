# Spark errors and upload timing / Errores Spark y tiempos de carga

## Español

- Todas las peticiones a Meta Responses incluyen `store: false`. Esto evita solicitar la persistencia de la respuesta en Meta; no elimina los archivos subidos ni cambia la evidencia privada de ProofLab.
- Los errores conservan estado HTTP, código, tipo, ID de solicitud del proveedor y un mensaje acotado y depurado. Se excluyen cuerpos completos, tokens, imágenes, URLs y datos estructurados reflejados. El cliente recibe un ID de diagnóstico; los detalles quedan en registros privados. Un 413 se identifica como `PAYLOAD_TOO_LARGE`.
- El localizador registra `failure-diagnostic.json` y el estado de las etapas en el directorio privado de la ejecución, también cuando falla antes de SAM. No se reintenta automáticamente por este cambio.
- La carga inicial registra espera, recepción + SHA-256, sondeo, seguimiento local, análisis espectral, portada y guardado. Se guarda `baseline-upload-<videoId>.timings.json` (o `followup-...`) junto a la sesión privada. El hash se calcula mientras se recibe el archivo, evitando volver a leerlo completo.
- El tiempo de la petición de carga incluye el análisis local posterior a la transferencia. Las mediciones de recepción se toman en la aplicación: un proxy que almacene la petición antes de reenviarla puede ocultar parte de la transferencia. Producción usa Nginx con `proxy_request_buffering off`.
- Incidente observado: Meta respondió 413 después de subir el video correctamente. El registro anterior descartaba el cuerpo, por lo que la causa exacta no puede certificarse retrospectivamente. La explicación de persistencia con `store=true` es una hipótesis compatible con la documentación, pendiente de repetir el video.
- Validación: 472 pruebas .NET, publicación Linux y compilación Angular de producción correctas. Cero llamadas pagadas a Meta durante esta corrección; sin ejecución de CI.

## English

- All Meta Responses requests explicitly use `store: false`. This does not delete uploaded Files or alter ProofLab's private evidence.
- Errors retain HTTP status, provider code/type/request ID and a bounded sanitized message. Raw bodies, tokens, images, URLs and structured echoes are omitted. Clients receive a diagnostic ID; details stay private. HTTP 413 maps to `PAYLOAD_TOO_LARGE`.
- Grounding failures persist `failure-diagnostic.json` and stage states in the private run folder, including failures before SAM. This change adds no automatic retry.
- Initial uploads record queue, receive + SHA-256, probe, local tracking, spectral analysis, poster and persistence timings in a private per-video JSON file. Incremental hashing avoids a second full-file read.
- The upload request includes post-transfer local analysis. Application-side receiving measurements can exclude proxy buffering; production Nginx has `proxy_request_buffering off`.
- The observed incident returned HTTP 413 after successful video upload. Previous logs omitted the error body; the precise historical cause remains unproven. Response persistence is a plausible explanation to verify by repeating the video.
- Validation: 472 .NET tests, Linux API publish and Angular production build passed. No paid Meta calls or CI runs.
