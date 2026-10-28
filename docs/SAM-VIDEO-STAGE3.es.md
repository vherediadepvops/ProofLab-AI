# Etapa 3 — SAM 3.1 en video: primer incremento SIN llamadas Meta

**Fecha:** 28/10/2026 · **Estado:** decodificador de VIDEO y visor local implementados; **NO** es una prueba real del péndulo con SAM. La Etapa 2 está integrada técnicamente, pero su validación científica todavía está pendiente.

## Qué sí está implementado

- `ui/src/app/sam-video-evidence.ts` utiliza el **parser oficial fijado** `@meta-sam/parser@0.1.0` con `parseVideoStream` y `decodeMaskToRaster`; no crea solicitudes de red.
- Procesa una respuesta JSON `sam-3.1` marcada como `completed`: reconoce índices `<Nf>`, IDs estables, máscaras y cajas en píxeles fuente; admite orden de frames no monotónico y huecos sin inventar datos.
- Para cada máscara calcula el centroide **de los píxeles positivos**, nunca el centro geométrico de una caja como sustituto. Conserva dimensiones, caja, IDs, confianza opcional, número de píxeles y contorno SVG.
- Rechaza errores de gramática, duplicaciones de objeto en un mismo frame, caja/máscara incompatible, dimensiones mezcladas, estados incompletos y entradas grandes. Un resultado sin objetos permanece `NO_MATCH`.
- `ui/src/app/sam-video-replay.ts` añade en Zona 1 un inspector desplegable **«Etapa 3 · Inspeccionar máscaras SAM (sin costo)»**, cargado bajo demanda. El JSON se procesa en el **navegador** sin subir archivos ni gastar créditos. Las máscaras se dibujan **sobre fondo neutro**, no sobre el video original.
- El inspector se reinicia al cambiar el SHA del video seleccionado; cambiar ES/EN no elimina el resultado.

**Límite explícito:** el JSON importado es material aportado por usuario y **no se autentica**. Ninguna máscara actual se ha obtenido de una llamada al video real; no está vinculada al SHA del clip enviado a SAM. El índice del frame **NO es un timestamp PTS**, ni prueba que SAM siga correctamente la plomada. No calcular velocidad en m/s sin calibrar y no sustituir el tracker local FFmpeg por este resultado aún.

## Cómo probar sin costo

1. Abrir la web local `http://127.0.0.1:4300/` y cargar o recuperar un estudio con video.
2. Pulsar **«Etapa 3 · Inspeccionar máscaras SAM (sin costo)»** al final de Zona 1.
3. Elegir un archivo JSON completo de respuesta SAM 3.1 ya recibido **legalmente** (no pegar claves). Se muestran IDs, índices de frame, máscaras/centroides y errores; ninguna petición sale de ProofLab.
4. Si no hay un JSON real de video, dejar el inspector sin datos. Las pruebas automatizadas utilizan **muestras sintéticas del protocolo** y no acreditan segmentación física.

## Próxima prueba REAL: puente entre Etapas 2 y 3

El objetivo común es verificar la identidad de la plomada en un **clip auténtico** y contrastar las posiciones contra (a) tracker local FFmpeg y (b) puntos humanos revisados. Antes de llamar a Meta:

1. Seleccionar clip corto de video original de 50 cm, con consentimientos de privacidad. Conservar original y SHA; guardar una copia limitada, el SHA de esta copia y la transformación aplicada.
2. Registrar **relación exacta de fotograma a timestamp de la copia enviada** usando FFprobe, y documentar cómo se corresponde (o no) con PTS del MOV original; no inferir la hora `frameIndex/fps` sin esa verificación.
3. Ajustar prompt de un único objeto: nombre corto (`metal pendulum bob`) o caja/punto original revisado; no mezclar concepto con coordenadas.
4. Consultar Billing y Usage de Meta, confirmar saldo/privacidad y obtener permiso explícito para **subir el clip a un proveedor y hacer una solicitud pagada**. Mantener `Meta:AllowPaidRequests=false` hasta entonces. Meta cobra mínimo 50 fotogramas por solicitud; comprobar precio vigente.
5. En respuesta verdadera guardar `response ID`/modelo/versión de parser, huella del JSON, resultado y advertencias en almacenamiento privado; la memoria física SQLite se desarrollará en Etapa 4.
6. Comparar centroides de SAM con puntos revisados manualmente en **los mismos fotogramas y coordenadas**, mostrar desviación en píxeles y casos fallidos. Solo después aprobar identidad y método de la Etapa 2; incertidumbre y calibración siguen siendo independientes.

## Verificación y límites

- Suite Angular ChromeHeadless con los nuevos casos sobre máscaras sintéticas, orden inverso, huecos, IDs, JSON inválido y protección al cambiar video; se deben consultar resultados por commit.
- Ninguna llamada HTTP a Meta, ni transcodificación/persistencia de video, ni evidencia externa de SAM se ejecutó para este incremento.
- Originales MOV, datos científicos históricos, api 5393 y trabajos paralelos Muse quedan intactos.

**Documentación oficial:**
- https://dev.meta.ai/docs/sam/overview
- https://dev.meta.ai/docs/sam/segmenting
- https://github.com/meta-models/meta-sam/blob/main/protocol/sam3.md
- https://dev.meta.ai/docs/pricing-rate-limits

**English:** [SAM-VIDEO-STAGE3.md](SAM-VIDEO-STAGE3.md).
