# ProofLab AI — API del pivote del péndulo: recuperación y prueba HTTP real

**Estado:** API local verificada. Complementa al motor geométrico publicado en `ProofLab-AI@6d2b15a`. La selección del pivote con el mouse y su visualización Angular continúan PENDIENTES.

## Función y procedencia

- `POST /api/science/director/pendulum/sessions/{id}/kinematics/{role}` recibe JSON:
  `{"pivotXpx":240,"pivotYpx":40,"videoSha256":"<SHA-256 del video original>"}`.
- Solo admite `baseline` o `followup` de una sesión real ya registrada. Rechaza discrepancias de SHA con HTTP 400 y archivos sin `PixelSamples` X/Y.
- Calcula ángulo de imagen, velocidad angular en rad/s, aceleración angular en rad/s², velocidad tangencial aparente en px/s, calidad, pérdidas de seguimiento y máximo teórico ideal separado.
- `GET /api/science/director/pendulum/sessions/{id}/kinematics/{role}` recupera la última revisión de análisis. El servidor guarda hasta 20 revisiones **creadas una sola vez**, bajo `.private/physics/director/{id}/kinematics/{role}/` (gitignored), asociadas a id de video y SHA original. Los medios y los registros originales no se sobrescriben.
- Por construcción el endpoint envía `FrontalCameraAssumptionConfirmed=false`: **NUNCA reporta unidades físicas calibradas en m/s**, plano XY en metros o equivalencia de píxeles a centímetros. El pivote dado es input manual provisional, NO una localización confirmada por SAM.
- No hay inferencia Meta ni acciones facturables. Solo servidor en localhost y origin Angular permitido.

## Verificación local en DESKTOP-ESGSLJH

- La API anterior escuchaba en `5391` hasta que se detuvo legítimamente para compilar; el primer intento de reinicio fue bloqueado por los controles de seguridad. **Un reintento explícitamente solicitado por el propietario fue PERMITIDO**, y el servicio inició normalmente usando `dotnet .\bin\Debug\net10.0\ProofLab.Api.dll --urls http://127.0.0.1:5391` con flags de Meta de pago desactivados.
- Verificado `GET /api/health` **HTTP 200**, web `4300` HTTP 200, `liveCallsPermitted=false` y proceso independiente de prueba del dueño en `5393` intacto.
- Smoke de un expediente QA real `4ccbdae4897a41878d91ad8319b2a60b` con `real_50cm_qa.MOV`: **316 puntos de imagen X/Y**. Antes del alta de pivote GET devolvió 404; POST con SHA falso devolvió 400; POST con SHA real, pivote provisional **(240,40)**, creó una revisión que GET recuperó con SHA e ID idénticos. Resultado del algoritmo: **220 derivadas candidatas**, estado `LOCAL_2D_CANDIDATE_REQUIRES_VISUAL_REVIEW`, `physicalUnitsEnabled=false`. El SHA del video original en disco coincidió; `humanReview` siguió `null`. **Este punto pivote fue seleccionado para probar la API, NO validado científicamente.**
- Ejecutadas **136/136 pruebas .NET Release** y **62/62 Angular** tras restauración. Ambas suites correctas. No se tocaron secretos, videos privados ni sesión 5393.

## Pendiente

1. UI ES/EN para marcar el pivote sobre el mismo frame del reproductor y llamar POST/GET.
2. Superponer ángulo, velocidades y aceleraciones y controlar calidad por frame. Revisar que la trayectoria corresponda realmente a la plomada.
3. Validar pivote real, tiempo PTS, incertidumbre y SAM bajo autorización de créditos. No convertir discrepancias en precisión certificada.
4. Probar nueva versión contra CI, registrar commit e historial. No publicar archivos de Muse ajenos a esta mejora.

**English:** [PENDULUM-PIVOT-API.md](PENDULUM-PIVOT-API.md).
