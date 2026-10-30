# Pruebas de ProofLab en Microsoft Edge
Actualizado: 29/10/2026. [English](EDGE-TESTING.en.md).

## Herramientas usadas
- **Remote Desktop Commander**: el chat verifica el equipo con list_devices y ejecuta Node/PowerShell mediante start_process; read_process_output obtiene el resultado. read_file/write_file sirven para scripts y evidencias. Se opera exclusivamente en el equipo autorizado por la memoria compartida, desde C:\PROOFLAB-AI-HACKATHON.
- **Microsoft Edge + DevTools Protocol (CDP)**: navegador real, DOM, clics, red, capturas y tamaños de pantalla. CDP en Edge comparte las API de Chromium. [Referencia de Microsoft](https://learn.microsoft.com/en-us/microsoft-edge/devtools/protocol/).
- **Node.js**: los scripts .mjs usan fetch y WebSocket incorporados. Entorno comprobado: Node 24.21.0, Edge 155.0.4283.62, protocolo 1.3; son versiones observadas, no requisitos fijados.
- Esta ruta de pruebas no usa Selenium, Playwright, msedgedriver ni una extensión. El aviso de Adblock de una ventana de Edge no es una herramienta de ProofLab; no hace falta habilitar esa extensión.

## 1. Preparar o reutilizar Edge
Leer AGENTS.md y el contexto vigente de la memoria. Verificar dispositivo, rama y cambios pendientes si se va a editar. No iniciar otro servidor ni reiniciar la API solo para inspeccionar una pantalla.

| Servicio | Dirección actual |
|---|---|
| Angular | http://127.0.0.1:4300 |
| API principal | http://127.0.0.1:5391 |
| DevTools del Edge de pruebas | http://127.0.0.1:9237 |
| API 5393 | Sesión ajena a estas pruebas; conservar |

Perfil separado existente: .private\muse-browser-profile. No copiar ni usar el perfil personal de Edge/Chrome. Comprobar primero:

~~~powershell
Set-Location 'C:\PROOFLAB-AI-HACKATHON'
Get-NetTCPConnection -LocalPort 9237 -State Listen -ErrorAction SilentlyContinue
Invoke-RestMethod 'http://127.0.0.1:9237/json/version'
Invoke-RestMethod 'http://127.0.0.1:9237/json/list' |
  Select-Object id,type,url
~~~

Si responde, verificar que es Edge de QA, que el perfil corresponde al directorio anterior y que el listener es loopback. Reutilizar solo una pestaña de ProofLab del perfil dedicado; no adjuntarse a cualquier navegador personal ni ejecutar varios scripts sobre la misma pestaña.

Solo si no existe esa instancia, abrirla con este comando; si el puerto está ocupado, detener el procedimiento y averiguar quién lo usa, sin matar procesos:

~~~powershell
$qaRoot='C:\PROOFLAB-AI-HACKATHON'
$qaProfile=Join-Path $qaRoot '.private\muse-browser-profile'
$qaEdge='C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if(Get-NetTCPConnection -LocalPort 9237 -State Listen -ErrorAction SilentlyContinue) {
  throw '9237 ocupado: identificar y reutilizar la instancia autorizada.'
}
if(!(Test-Path -LiteralPath $qaEdge)) { throw 'Localizar la instalación de Edge.' }
Start-Process -FilePath $qaEdge -WindowStyle Hidden -ArgumentList @(
  '--remote-debugging-address=127.0.0.1',
  '--remote-debugging-port=9237',
  ('--user-data-dir="'+$qaProfile+'"'),
  '--no-first-run','--no-default-browser-check','--disable-extensions',
  'http://127.0.0.1:4300'
)
~~~

Verificar de nuevo el listener y /json/version. No abrir el puerto a la red. No cerrar todas las ventanas de Edge ni usar Stop-Process con un nombre genérico. No cambiar ExecutionPolicy: se puede ejecutar PowerShell en línea y node directamente.

## 2. Inspección rápida reproducible
El repositorio incluye [scripts/edge-inspect.mjs](../scripts/edge-inspect.mjs):

~~~powershell
Set-Location 'C:\PROOFLAB-AI-HACKATHON'
node scripts/edge-inspect.mjs
~~~

Selecciona una pestaña cuyo origen sea exactamente http://127.0.0.1:4300, inspecciona la pantalla actual y guarda PNG/JSON con fecha en .private/edge-inspection/. El JSON incluye dimensiones, desbordamiento, vistas/video presentes, errores observados y contador Meta antes/después. No navega, carga videos, pulsa Procesar ni introduce tokens.

Durante su conexión permite solo GET/HEAD/OPTIONS locales; bloquea mutaciones y solicitudes externas de esa pestaña. Un bloqueo inesperado, error o cambio de contador produce salida 1. La intercepción termina al desconectar: no es un cortafuegos permanente ni controla otras pestañas/procesos. Ejecutarlo cuando nadie esté usando simultáneamente la pestaña de QA.

Validación del ejemplo: ejecutado el 29/10/2026 en Inicio, 1763×795, sin desbordamiento/errores, contador 37→37. Cero vistas de video es normal en Inicio; esta inspección **no prueba** máscaras, reproducción ni exactitud física.

## 3. Pruebas funcionales que ya se han usado
Los scripts de campañas y sus videos/resultados son **privados y locales**, no se descargan al clonar Git:

- .private/suspension-review-browser.mjs: última campaña de reproducción desde caché; selecciona tres estudios locales, comprueba valores dinámicos/inconclusos y explicación Spark.
- .private/suspension-review-browser.json y .private/suspension-review-browser-*.png: resultados y capturas.
- .private/pendulum-values-browser.mjs: campaña anterior, con expectativas históricas; no ejecutarla ciegamente porque un análisis posterior puede cambiar el resultado esperado.

Para repetir la campaña actual, leer primero el script, comprobar que existen .private/pendulum-new-videos-live.json y los estudios/archivos que referencia, y confirmar que las expectativas corresponden al análisis actual. Si hay un MISS, revisión nueva con costo o evidencia ausente, parar la prueba de caché; no obtener datos pagando automáticamente.

~~~powershell
Set-Location 'C:\PROOFLAB-AI-HACKATHON'
node .private/suspension-review-browser.mjs
~~~

La campaña espera tres resultados; un script que termina sin probar los estudios esperados no demuestra éxito. Revisar results.length, blocked, errors y cada aserción, además del código de salida. Para otro video, preparar una copia de prueba bajo .private/ con expectativas explícitas y datos existentes; no cambiar los resultados científicos para satisfacer una aserción.

## 4. Cómo automatizar un recorrido nuevo
El cliente lee /json/list, conecta al webSocketDebuggerUrl de la pestaña y envía mensajes {id,method,params}. Cada respuesta se asocia por id con su promesa y un timeout. Atender también errores/cierre del WebSocket y liberar recursos en finally.

| Acción | Método/condición usados |
|---|---|
| Leer DOM o pulsar un botón de la app | Runtime.evaluate; returnByValue y awaitPromise |
| Abrir el laboratorio | Page.navigate al origen local; botones ES y Laboratorio |
| Esperar carga | Elemento presente, readyState del video, estado de costo/procesamiento; no solo un sleep fijo |
| Probar otro estudio local | Fetch.fulfillRequest para /demo-video.json, con su studyId de fixture; no cambia el archivo en disco |
| Escritorio/móvil | Emulation.setDeviceMetricsOverride, 1440×1000 y 390×844 |
| Captura | Page.captureScreenshot, PNG guardado en .private/ |
| Errores JavaScript | Runtime.exceptionThrown, más comprobación de exceptionDetails de evaluate |
| Red | Fetch.enable → Fetch.requestPaused → continueRequest/failRequest/fulfillRequest |
| Finalizar | Resolver solicitudes pendientes, desactivar Fetch, restaurar emulación/foco modificados y desconectar solo el cliente |

Para reproducción: esperar video.readyState>=2; en caché exigir modo API Meta + caché, precio cero y botón habilitado antes de Procesar. Tras cargar, mover currentTime y esperar !video.seeking y actualización del fotograma presentado antes de leer θ/ω. Las campañas usan 3, 4 y 5 segundos y comprueban que ω cambia donde hay mediciones válidas; un caso ambiguo debe mostrar — con explicación. No exigir números cuando la evidencia es inconclusa.

En Angular de desarrollo existe window.ng.getComponent(...): se usó para comparar frameTime() con el video (tolerancia 0,04 s en esa campaña). No existe necesariamente en producción ni es una tolerancia científica universal. Para otro build o FPS, usar eventos del video/observaciones del DOM y criterio temporal adecuado. Comprobar cuatro vistas, overlays alineados, reproducción/pausa/seek, ES/EN, ausencia de desbordamiento y capturas. Ver [CDP Fetch](https://chromedevtools.github.io/devtools-protocol/tot/Fetch/).

## 5. Separar caché, simulación de errores y llamadas reales
- **UI/caché, por defecto:** sin token. Interceptar antes de navegar o hacer clic. Permitir lecturas locales necesarias y, únicamente si el recorrido lo exige, POST /api/science/pendulum/mathematical-model (cálculo local). Bloquear por defecto otros POST/PUT/PATCH/DELETE y destinos externos, incluidos los endpoints locales que podrían llamar a Meta.
- **Error simulado:** responder un 502 mediante Fetch.fulfillRequest sin reenviar la solicitud; etiquetar fixture y forwarded:false. Sirve para probar spinner/error, no demuestra una respuesta de Meta.
- **Meta real:** flujo separado con autorización vigente y límite de pruebas, presupuesto y recibos. No retirar el bloqueo de una prueba visual para hacerla pasar. Nunca imprimir/persistir token, cabeceras Authorization ni cuerpos con credenciales.

Comparar usedRequests de GET /api/meta/readiness antes y después. El contador es evidencia auxiliar: una solicitud simultánea del propietario puede cambiarlo; revisar rutas interceptadas y no afirmar gasto propio solo por esa diferencia. No reiniciar el servidor ni restablecer contadores para obtener igualdad. Los controles del servidor siguen aplicándose; CDP solo protege el recorrido conectado.

## 6. Fallos frecuentes y alcance
- **No hay 9237/pestaña:** comprobar instancia/perfil y URL; no iniciar otra API.
- **Spinner detenido:** revisar solicitud/error y DOM; no pulsar Procesar repetidamente.
- **No reproduce HEVC/MOV:** revisar video.error y soporte del navegador. No reemplazar silenciosamente el original por un proxy reducido para declarar éxito.
- **Extensión bloqueada:** no es dependencia de la prueba. Conservar políticas del navegador; para nuevas instancias de QA usar el perfil separado sin extensiones.
- **Selector/fixture cambió:** revisar el DOM y el código actual; actualizar la prueba específica, sin una batería completa por rutina.
- **Móvil emulado:** comprueba disposición en Edge de escritorio; no equivale a probar un teléfono real.
- **Captura correcta:** no certifica medidas ni una inferencia nueva. Registrar el alcance comprobado.

## 7. Traspaso al próximo chat
Entregar commit/build, dispositivo autorizado, perfil/puerto, script y comando, estudios/analysisId privados, aserciones y resultados, rutas de PNG/JSON, contador antes/después y limitaciones. Mantener videos, perfiles, IDs y evidencias privados fuera del repositorio público. Publicar únicamente código/documentación general. No ejecutar CI por estas pruebas locales.

La guía se enlaza desde los README y desde AGENTS/PROJECT-CONTEXT de la memoria compartida; consultar solo cuando la tarea requiera navegador. No se necesita instalar otro plugin si Remote Desktop Commander ya permite ejecutar Node en la PC autorizada.

Comprobación adicional de códec (30/10/2026): readyState >= 2 no basta. Exigir videoWidth > 0 y videoHeight > 0; Edge puede aceptar solo la pista de audio de un MOV/HEVC y reportar 0×0 sin error. Ese caso debe mostrar error de reproducción original, nunca declararse prueba visual exitosa. El período puede calcularse desde la geometría SAM autenticada sin depender del decodificador. Probar H.264 y originales HEVC por separado; no sustituir silenciosamente el original ni afirmar que H.264 demuestra reproducción HEVC.
