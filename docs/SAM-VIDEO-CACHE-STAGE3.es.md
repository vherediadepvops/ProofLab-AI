# Etapa 3 — SAM 3.1, caché segura y posiciones en el reproductor

> **Permisos vigentes (29/10/2026):** las referencias a pagos apagados en este documento describen el ensayo o estado histórico de esa instancia. No son instrucciones para desactivar la API del propietario ni las ramas nuevas. Aplicar la [política actual de autorización y pruebas](META-API-CONNECTION.es.md).

**28 de octubre de 2026 · En desarrollo.** El flujo de una inferencia y reproducción desde caché está implementado. Posteriormente se realizó **una única solicitud real** autorizada: Meta aceptó Files y Responses (HTTP 200 inicial), pero ProofLab devolvió HTTP 502 después de ~116 segundos, sin máscaras guardadas. El crédito visible bajó de US$149,99 a US$149,62; esto no confirma el desglose exacto del uso. **NO hay detección científica verificada de la plomada** y los controles de pago de la API principal siguen desactivados.

## Responsabilidad científica de los modelos

1. **SAM 3.1** segmenta la plomada en cada fotograma, devuelve máscaras, identidades y coordenadas candidatas. Una máscara de SAM no certifica automáticamente qué objeto físico se rastreó.
2. **ProofLab, motor determinista**, calcula centroide de píxeles positivos, trayectoria, velocidad y aceleraciones en coordenadas de imagen, proyecta vectores y rastro fantasma en los canvas. No utiliza Muse para calcular estos valores; no atribuye a SAM el algoritmo de derivadas.
3. **Muse Spark** evaluará evidencia e incertidumbre y propondrá un experimento nuevo en la Etapa 5. Todavía no decide ni ejecuta este flujo.
4. La **revisión humana** confirma pivote, identidad del bob y errores; ninguna respuesta de los modelos significa aprobación científica.

## Procedimiento sin cobros repetidos

- `GET /api/science/director/pendulum/sessions/{id}/sam/{role}/status`: consulta solo lectura. Se valida el SHA-256 real del original antes de relacionarlo con la caché. Si está completo, se recupera sin solicitar inferencia. Si hubo un intento interrumpido, marca `PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION` y NO reintenta.
- `GET .../sam/{role}/preflight`: decodifica los timestamps `best_effort_timestamp_time` del MOV original (no el proxy H.264 12 FPS) mediante FFprobe y calcula el número real de fotogramas. No llama a Meta.
- `GET .../sam/{role}/evidence`: devuelve únicamente un archivo `response.json` del proveedor, precedido por recibo privado y comprobación de SHA/protocolo/modelo. Los JSON pegados por el usuario en el inspector offline NO alimentan al reproductor científico.
- `POST .../sam/{role}/analyze`: acción manual local con doble confirmación de transferencia del video privado y una inferencia potencialmente facturable. Requiere `MODEL_API_KEY`, crédito confirmado por titular, autorización de gasto y `Meta:AllowPaidRequests=true`, más el presupuesto de llamadas del proceso. Máximo **2.500 fotogramas** y costo calculado **≤ US$0,50**; no son un límite monetario de la cuenta. En un hit de caché no hay nuevo consumo.
- Antes de subir video se genera `sam-upload.mp4` por **remux de stream sin recodificar**, se comparan los fotogramas y sus PTS normalizados con los del original y se guarda su SHA. Si difieren, BLOQUEAR. La subida mediante Files API utiliza `purpose=user_data`, `expires_after=86400` segundos. La inferencia `sam-3.1` usa un prompt fijo `metal pendulum bob`, `input_video.file_id` y streaming SSE. La ruta de envío fue probada una vez en vivo (HTTP 200 en Files/Responses), pero NO se obtuvo respuesta final interpretable; el streaming corregido se valida únicamente con fixtures locales.
- Se crea `attempt-started.json` antes de enviar datos. Al recibir un resultado completo se escriben en disco privado `response.json` y `receipt.json` con video hash, MP4 hash, protocolo, prompt, modelo, PTS, huella de respuesta, ID del proveedor, `usage.video_frames_processed` si se informa y fecha. Si falla la red o el programa, el intento queda marcado para reconciliación manual, NUNCA reintento automático.
- El reproductor maestro consulta exclusivamente esa caché validada: las posiciones derivadas de máscaras SAM alimentan las vistas «Vectores de movimiento» y «Rastro fantasma», y las derivadas en px/s/rad/s se calculan localmente. Sin máscaras auténticas no se finge su existencia; las mediciones históricas FFmpeg se señalan explícitamente como **control provisional NO SAM**.
- La Etapa 4 indexará esta evidencia en **SQLite**, sin volver a invocar SAM. Los archivos originales, las respuestas masivas y huellas siguen en almacenamiento privado.

## Verificación local de compatibilidad (anterior al único intento real)

Video original de 50 cm guardado en la PC autorizada: **HEVC MOV, 1920×1080, 30,496667 s, exactamente 1.828 fotogramas**, con hash del original preservado. Remux privado MP4 con video copiado sin recodificar: **1.828 fotogramas y diferencia máxima PTS = 0 s** según FFprobe. Costo estimado según tarifa de SAM US$0,20/1000 frames: **US$0,3656**, no facturación comprobada.

Pruebas de seguridad de la API temporal local: `GET status` HTTP200 sin caché, `GET preflight` HTTP200 1.828 fotogramas, `GET evidence` HTTP404, `POST analyze` con hash correcto y confirmaciones devuelve **HTTP409** al estar cerrados los controles Meta. Contador `usedRequests=0`, ningún proveedor contactado y original sin cambios. Los endpoints se restringen a loopback; NO desplegar sin autenticación/revisión adicional.

## Diagnóstico SSE, timeouts y único intento real

El intento real efectuado tras las verificaciones locales obtuvo **Files HTTP200 y Responses HTTP200 inicial**, pero terminó en **ProofLab HTTP502** sin un mensaje final utilizable. No tenemos los eventos SSE de aquel intento; el descenso de créditos US$149,99 → US$149,62 no identifica el número de fotogramas cobrados. El archivo privado `attempt-started.json` **se conserva** para impedir reintento automático.

La revisión posterior agrega:
- Timeout para SAM video: **15 → 30 minutos**, con **cancelación explícita de 30 minutos sobre la lectura SSE** porque `ResponseHeadersRead` no aplica `HttpClient.Timeout` al cuerpo posterior; para otras llamadas Meta (`meta`): **45 → 180 segundos**. La desconexión del cliente aún puede cancelar antes el procesamiento. Aumentar el timeout no demuestra que el HTTP502 previo fuese causado por un tiempo de espera.
- Parser `SamVideoSseParser` desacoplado del transporte: reconoce `response.output_text.delta`, `response.output_text.done` y `response.completed`; respeta `response.failed`, `response.incomplete`, errores, heartbeats y límites de salida. Una respuesta completada con **cero objetos** se registra como `NO_MATCH`, nunca como error, sin fabricar máscaras o posiciones.
- Log estructurado privado `attempt-telemetry.jsonl` **solo para futuras solicitudes expresamente autorizadas**, junto al marcador `attempt-started.json`. Registra fecha UTC, etapa segura, milisegundos transcurridos, número de eventos/deltas y caracteres acumulados. **No conserva tokens, IDs privados, contenido SSE, prompts ni máscaras**. Logs de eventos solo cada 25 deltas y al completar/fallar. No se puede reconstruir retroactivamente el streaming anterior.
- La lectura de resultados en Angular distingue `SAM_3_1_COMPLETED_NO_MATCH` de caché corrupta y sigue sin activar vectores SAM sin detecciones válidas.
- Pruebas sintéticas .NET del parser y Angular de NO_MATCH; ninguna llamada Meta ni cambio del estado de la solicitud antigua.

## Criterio pendiente para cerrar Etapas 2 y 3

Se necesita (1) verificar uso real Meta/privacidad, (2) **solo con una nueva autorización** evaluar otra llamada SAM con control de presupuesto, (3) verificar máscaras e identidad de plomada y mapa PTS sobre video real, (4) contrastar centros SAM contra marcas humanas sobre los mismos fotogramas y tracker local (baseline), (5) calcular incertidumbre y registrar fallos. **Ni la visualización del JSON, ni una caché bien formada equivalen a validación científica.**

**Fuentes:** [SAM oficial](https://dev.meta.ai/docs/sam/overview), [segmentación de video](https://dev.meta.ai/docs/sam/segmenting), [archivos y expiración](https://dev.meta.ai/docs/file-handling), [tarifas](https://dev.meta.ai/docs/pricing-rate-limits).

**English:** [SAM-VIDEO-CACHE-STAGE3.md](SAM-VIDEO-CACHE-STAGE3.md).


## Segundo intento manual autorizado, sin borrar el historial (28/10/2026)

Después del HTTP502 del intento inicial y de las capturas de Billing con disminución aproximada de **US$0,37 en créditos gratuitos** (el detalle exacto en Usage continúa sin comprobar), el titular autorizó expresamente **una sola prueba adicional**. Se añadió una política de reconciliación limitada:

- La ruta `POST .../sam/{role}/analyze` no repite automáticamente. Solo el operador local, con la nueva confirmación `ownerApprovesOneReconciledRetry=true` y `previousAttemptBillingEvidence` igual al reconocimiento documentado en `ManualRetryReconciliationPolicy.OwnerAuthorization`, puede atravesar el bloqueo **una vez** si las cuatro puertas de facturación están activas.
- El marcador previo `attempt-started.json` **no se mueve ni borra**; antes del segundo envío se escribe de forma exclusiva `manual-retry-started.json`. Si este segundo marcador ya existe, se rechaza un tercer intento, incluso tras reiniciar el servidor.
- Se reutiliza la copia `sam-upload.mp4` original **sin recodificar** y se verifican otra vez todos los tiempos PTS y el SHA registrado en el primer marcador. Ningún vídeo privado se publica.
- La telemetría del segundo intento va exclusivamente a `manual-retry-telemetry.jsonl`, sin sobreescribir ni inventar logs del primero. El resultado, si completa, se guardará con recibo privado y hash.
- Un ensayo en una API temporal con pagos desactivados verificó HTTP409 tanto para una reconciliación incorrecta como para un reconocimiento válido sin créditos habilitados; ningún marcador nuevo ni llamada Meta se produjo.

La nueva autorización **no certifica el cobro exacto del primer intento** ni permite pruebas adicionales sin autorización.

## Operación manual desde Zona 1: token temporal y confirmación (28/10/2026)

En la parte superior del laboratorio, justo debajo de **Configura el péndulo**, aparece **Etapa 3 · SAM 3.1 y evidencia persistente** cuando el estudio tiene video. El usuario selecciona un **servicio local previamente autorizado** (puertos permitidos 5391, 5393 o 5398), pulsa «Comprobar caché, fotogramas y autorización» y, solo después, pega el token Meta en un campo protegido tipo contraseña. Ese valor **no se almacena en señales Angular, localStorage, sessionStorage, SQLite, ningún JSON, README ni logs**: se lee exclusivamente al pulsar el botón y se limpia inmediatamente del input. Se envía una sola vez al backend .NET de loopback usando `POST .../sam/{role}/manual-one-shot` con cabecera antifalsificación `X-ProofLab-Manual-Action: sam-one-shot`, origen permitido `http://127.0.0.1:4300` o `http://localhost:4300`, cuerpo menor de 4 KiB y respuesta `Cache-Control: no-store`. Nunca se envía el token del navegador directamente a Meta: **solo el backend** establece HTTPS contra `https://api.meta.ai/v1`.

Para una llamada, el usuario debe marcar la transferencia del video privado, pulsar **«Confirmar uso de créditos Meta»**, y después pulsar **«Ejecutar UNA prueba manual con SAM 3.1»**. Si ya existe un intento fallido, debe marcar además la casilla que reconcilia el cargo anterior y autoriza **un único segundo intento**. No hay reintentos automáticos. El presupuesto estimado permanece limitado a US$0,50/2.500 fotogramas, pero no es un límite real del proveedor. El segundo marcador `manual-retry-started.json` bloquea un tercero; no se borra ninguna evidencia anterior.

**Protección obligatoria:** el token introducido NO habilita los switches de pago. El backend rechaza la llamada si no está previamente autorizado con `Meta:CreditsConfirmedByOwner=true`, `Meta:OwnerApprovedPaidRequests=true`, `Meta:AllowPaidRequests=true`, una clave configurada en su entorno (como segunda protección) y presupuesto disponible. El servicio principal **5391 conserva pagos OFF**. La pantalla permite elegir un puerto solo en loopback, nunca dominio externo. No debe crearse una sesión que evada un bloqueo previo de herramientas. El formulario no sustituye un procedimiento de autorización aceptado por los controles del equipo.

**Advertencia de credenciales:** Meta recomienda que las claves permanezcan en variables de entorno o gestores de secretos; pegarlas en un formulario del navegador, aunque sea temporal y localhost, tiene mayor exposición a extensiones y herramientas de desarrollo. Utilizar solo en el equipo propio y una clave revocable dedicada; nunca pegarla en el chat ni en capturas de pantalla. No se hace ninguna petición con el token hasta que el usuario pulsa el botón final con todas las puertas activas.

**Logs diagnósticos:** la aplicación registra para cualquier intento futuro autorizado los eventos sanitizados `UPLOAD_STARTED`, `UPLOAD_HTTP_ACCEPTED`, `INFERENCE_REQUEST_STARTED`, `INFERENCE_HTTP_ACCEPTED`, `SSE_DELTA_PROGRESS`, `SSE_COMPLETED` o códigos de fallo y el guardado del recibo, con UTC, duración, cantidad de eventos y caracteres; archivo privado `.private/physics/sam-cache/<cache-key>/manual-retry-telemetry.jsonl` para el segundo intento, o `attempt-telemetry.jsonl` para un primero. **No** se registran tokens, prompts completos, URL con IDs, imágenes, coordenadas ni máscaras. La prueba histórica original no tiene estos logs. El equipo/usuario podrá mostrar el estado final y un agente autorizado puede revisar exclusivamente el archivo de diagnóstico. Los métodos de prueba sin pagos no crean otro marcador ni llaman a Meta.
