# ProofLab AI — Pruebas manuales de Muse Spark 1.3 y SAM 3.1

**Estado: interfaz instalada; sin pruebas reales nuevas desde el frontend. Fecha: 2026-10-27.**

## Contexto real

El propietario confirmó visualmente en Meta Billing que aparecían **US$150 de créditos gratuitos en uso** (no garantiza el saldo restante ni impide futuros cargos) y configuró `MODEL_API_KEY` en Windows. La primera operación remota fue bloqueada por seguridad; **el reintento autorizado llegó a la API temporal**, pero `POST /api/math/plan` devolvió **HTTP 502** y registró **1 solicitud reservada**. No quedó evidencia suficiente para distinguir respuesta Meta HTTP 400/401/403/429, problema de transporte o error de formato. El servidor temporal se detuvo y el principal permanece con los pagos desactivados. No se repitió.

## Cómo iniciar manualmente una nueva sesión, sin exponer el secreto

1. Trabaja **solo** en `DESKTOP-ESGSLJH`, carpeta `C:\PROOFLAB-AI-HACKATHON`.
2. Consulta [Meta Billing](https://dev.meta.ai/billing) para comprobar créditos, gastos, posibilidad de cargo a tarjeta y avisos sobre el uso de contenido enviado para mejora de productos. No confundir los créditos visibles con un límite monetario duro.
3. Abre desde el Explorador de Windows `C:\PROOFLAB-AI-HACKATHON\scripts\Start-MetaTest.cmd`. El script no contiene claves y **no realiza peticiones a Meta por sí solo**. Requiere que `MODEL_API_KEY` ya esté disponible en esa nueva terminal. Si no está, cierra la sesión Windows e inicia una nueva; no pegues el token en el chat, script o consola pública.
4. Lee la advertencia de posibles cargos y escribe **`ACTIVAR`** personalmente para abrir **solo** el servidor temporal `http://127.0.0.1:5393`. La API de trabajo `http://127.0.0.1:5391` continúa desactivada para pagos. El servidor de pruebas habilita **máximo dos llamadas a Meta por arranque**, compartidas Muse y SAM; **no es un límite duro de dinero ni se debe reiniciar para evadir el presupuesto**.
5. Abre `http://127.0.0.1:4300/`, actualiza con `Ctrl+F5` y elige la navegación lateral **«Pruebas Meta API»**. Pulsa **«Comprobar conexión de prueba»** hasta ver el estado de las cuatro puertas y solicitudes disponibles. No pegues la clave en esta interfaz.

## Prueba independiente de cada modelo

**Muse Spark 1.3:** escribe una pregunta científica pública breve, p. ej. «En una frase, explica qué es un número primo». Pulsa **«Enviar UNA solicitud a Muse»**, revisa la confirmación y acepta solo si quieres que se utilicen tus créditos. Se mostrará texto real **NO VERIFICADO** o un mensaje con HTTP de nuestra API y, si existe, **HTTP del proveedor Meta** y categoría (sin divulgar respuesta bruta de error, clave o credenciales). No hay reintentos automáticos.

**SAM 3.1:** pulsa **«Preparar imagen sintética de una pelota»**. El navegador dibuja una pelota roja en un fondo simple y genera un PNG **local** que no es evidencia científica. Deja `red ball` como descripción y pulsa **«Enviar UNA imagen a SAM»**, confirmando la posible facturación. También puedes escoger una imagen PNG/JPEG/WebP menor de 1,5 MB **sin personas ni datos privados**. Una respuesta exitosa mostrará **JSON de segmentación bruto** (máximo una vista corta); todavía no está decodificada ni dibujada la máscara, ni hay seguimiento de video.

Al terminar, pulsa **Ctrl+C o cierra la ventana de consola temporal**. Comprueba Usage/Billing de Meta para estimar costos (pueden demorarse en reflejarse). Nunca asumas que un error HTTP 502 implica cero uso.

## Salvaguardas

- Ninguna prueba modifica expedientes matemáticos, aprendizaje SQLite ni los cinco informes. **No demuestra Goldbach**.
- `MODEL_API_KEY` continúa solo en el entorno del backend; la UI no la lee, muestra ni conserva.
- La consola temporal exige aceptación del usuario y usa tres variables **sólo de proceso**: `Meta__CreditsConfirmedByOwner=true`, `Meta__OwnerApprovedPaidRequests=true`, `Meta__AllowPaidRequests=true`. No modifica `appsettings.json`, variables globales, Git ni el servicio principal.
- Las llamadas reales exigen confirmación visible en el navegador **por separado** y están sujetas a los controles del backend. La segunda petición puede ser rechazada si no quedan solicitudes.
- Si aparece un bloqueo de seguridad del sistema, **no intentar sortearlo**. Si falta la clave o hay errores del proveedor, revisar configuración y permisos por vías permitidas antes de gastar más.

**English:** [META-MANUAL-TESTS.md](META-MANUAL-TESTS.md).
