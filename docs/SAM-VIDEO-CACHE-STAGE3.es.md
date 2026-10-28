# Etapa 3 — SAM 3.1, caché segura y posiciones en el reproductor

**28 de octubre de 2026 · En desarrollo.** El flujo de una sola inferencia y reproducción desde caché está implementado; **NO se ejecutó todavía SAM real sobre el péndulo**, porque la API principal mantiene los controles de facturación desactivados. Esta documentación describe funcionalidades implementadas y sus límites, no un resultado físico certificado.

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
- Antes de subir video se genera `sam-upload.mp4` por **remux de stream sin recodificar**, se comparan los fotogramas y sus PTS normalizados con los del original y se guarda su SHA. Si difieren, BLOQUEAR. La subida mediante Files API utiliza `purpose=user_data`, `expires_after=86400` segundos. La inferencia `sam-3.1` usa un prompt fijo `metal pendulum bob`, `input_video.file_id` y streaming SSE. La API del proveedor podría cambiar; esto NO está probado todavía en vivo.
- Se crea `attempt-started.json` antes de enviar datos. Al recibir un resultado completo se escriben en disco privado `response.json` y `receipt.json` con video hash, MP4 hash, protocolo, prompt, modelo, PTS, huella de respuesta, ID del proveedor, `usage.video_frames_processed` si se informa y fecha. Si falla la red o el programa, el intento queda marcado para reconciliación manual, NUNCA reintento automático.
- El reproductor maestro consulta exclusivamente esa caché validada: las posiciones derivadas de máscaras SAM alimentan las vistas «Vectores de movimiento» y «Rastro fantasma», y las derivadas en px/s/rad/s se calculan localmente. Sin máscaras auténticas no se finge su existencia; las mediciones históricas FFmpeg se señalan explícitamente como **control provisional NO SAM**.
- La Etapa 4 indexará esta evidencia en **SQLite**, sin volver a invocar SAM. Los archivos originales, las respuestas masivas y huellas siguen en almacenamiento privado.

## Verificación real de compatibilidad, sin enviar nada a Meta

Video original de 50 cm guardado en la PC autorizada: **HEVC MOV, 1920×1080, 30,496667 s, exactamente 1.828 fotogramas**, con hash del original preservado. Remux privado MP4 con video copiado sin recodificar: **1.828 fotogramas y diferencia máxima PTS = 0 s** según FFprobe. Costo estimado según tarifa de SAM US$0,20/1000 frames: **US$0,3656**, no facturación comprobada.

Pruebas de seguridad de la API temporal local: `GET status` HTTP200 sin caché, `GET preflight` HTTP200 1.828 fotogramas, `GET evidence` HTTP404, `POST analyze` con hash correcto y confirmaciones devuelve **HTTP409** al estar cerrados los controles Meta. Contador `usedRequests=0`, ningún proveedor contactado y original sin cambios. Los endpoints se restringen a loopback; NO desplegar sin autenticación/revisión adicional.

## Criterio pendiente para cerrar Etapas 2 y 3

Se necesita (1) verificar saldo efectivo Meta/privacidad, (2) ejecutar UNA llamada SAM autorizada con control de presupuesto, (3) verificar máscaras e identidad de plomada y mapa PTS sobre video real, (4) contrastar centros SAM contra marcas humanas sobre los mismos fotogramas y tracker local (baseline), (5) calcular incertidumbre y registrar fallos. **Ni la visualización del JSON, ni una caché bien formada equivalen a validación científica.**

**Fuentes:** [SAM oficial](https://dev.meta.ai/docs/sam/overview), [segmentación de video](https://dev.meta.ai/docs/sam/segmenting), [archivos y expiración](https://dev.meta.ai/docs/file-handling), [tarifas](https://dev.meta.ai/docs/pricing-rate-limits).

**English:** [SAM-VIDEO-CACHE-STAGE3.md](SAM-VIDEO-CACHE-STAGE3.md).
