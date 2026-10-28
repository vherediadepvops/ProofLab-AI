# ProofLab AI — Etapa 4: dos modos SAM y SQLite

**Fecha:** 28/10/2026 · **Misión:** Autonomous Physics Discovery / péndulo · **Estado:** integración técnica implementada; validación local con datos sintéticos. No se ejecutó una inferencia Meta nueva ni se validó científicamente la plomada.

## Modos y contrato

| Selección | Comportamiento |
|---|---|
| **API Meta** (`api-meta`) | Solicita una inferencia SAM nueva, aunque exista un análisis previo. No presenta la caché como ejecución nueva. Requiere autorización explícita, créditos y admisión del servidor; cada ejecución conserva su propio ID e historial. |
| **API Meta + caché** (`api-meta-cache`, predeterminado) | Consulta SQLite por identidad exacta de video/modelo/protocolo/prompt y verifica los archivos. Un HIT recupera evidencia sin token, permiso de cobro ni llamada a Meta. Un MISS solo ofrece una llamada manual cuando el servidor la permite. |

El selector principal tiene exactamente estas dos opciones. El token se introduce en **Configuración**; la selección de puerto local queda allí en **Conexión avanzada**, cerrada por defecto; 5391 sigue siendo el puerto principal. Elegir un puerto o pegar una clave no habilita pagos.

- `GET /api/science/director/pendulum/sessions/{id}/sam/{role}/status?mode=api-meta-cache` consulta el estado. También acepta `mode=api-meta`.
- `POST .../sam/{role}/manual-one-shot` y `POST .../sam/{role}/analyze` reciben `mode`; omitirlo conserva el modo caché. La UI utiliza el primero con la acción manual existente.
- El estado añade `analysisId`, `mode`, `cacheHit` y `storage`. Un HIT conserva `CACHED_META_...`. El modo nuevo usa `READY_FOR_FRESH_META_ANALYSIS` cuando corresponde; el permiso final depende de los controles del servidor.
- `GET .../sam/{role}/evidence` recupera la última evidencia verificada. La UI recarga el reproductor mediante GET tras un HIT o una ejecución completada; nunca inicia un POST como consecuencia de reproducir.
- Una ejecución nueva completada devuelve su ID y `cacheHit=false`. La presencia de metadatos de un análisis anterior en el estado no demuestra que se haya ejecutado uno nuevo.

## Persistencia y procedencia

`SamVideoEvidenceIndex` usa Microsoft.Data.Sqlite bajo `.private/physics/sam-video-evidence.sqlite`, separado de la memoria matemática de Lean. SQLite guarda **solo metadatos**: SHA del video, modelo/protocolo, hash del prompt, hashes de respuesta y recibo, ruta privada relativa, ID de análisis, fecha, cantidad de fotogramas y procedencia. No guarda tokens, videos, máscaras, coordenadas, prompts en texto claro ni respuestas completas.

`PendulumSamCache` conecta ese índice con los archivos privados de respuesta y recibo. Cada ejecución se guarda en `.private/physics/sam-cache/<key>/runs/<analysisId>/`; una nueva inferencia no sobrescribe el archivo histórico anterior. El backend vuelve a comprobar identidad, hashes, recibo y tiempos del video antes de reutilizarlo. Puede recuperar un resultado completo conservado en disco tras una interrupción sin volver a llamar al proveedor. Una fila SQLite aislada o un JSON importado manualmente no certifican origen Meta.

La identidad incluye video original SHA-256, `sam-3.1`, protocolo `PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2`, clave de caché y hash del prompt. Las rutas se restringen al archivo privado permitido; los conflictos de identidad o procedencia se rechazan.

## Autorización y concurrencia

Se conservan las comprobaciones de transferencia del video privado, créditos, autorización de cobro, límite estimado, SHA/fotogramas/PTS e intentos previos. El servidor mantiene guardias entre procesos para evitar solicitudes simultáneas incompatibles y no hace reintentos automáticos. La reconciliación manual de un intento posiblemente facturado sigue siendo explícita. Un fallo de análisis nuevo devuelve `NEW_ANALYSIS_REQUIRES_MANUAL_RECONCILIATION` y no habilita el reintento excepcional del intento histórico.

La clave se configura en el campo de contraseña de **Configuración**, que permanece montado al navegar. Solo su disponibilidad se comparte con Laboratorio; el valor se lee y borra al ejecutar manualmente y nunca se persiste. Las comprobaciones GET no consumen el token. Cambiar modo o puerto limpia consentimientos y descarta comprobaciones anteriores. Durante un envío se bloquea ese cambio. El inspector JSON tiene un contador independiente y no puede dejar la consulta o el envío bloqueados.

## Verificación local

- Angular: **27/27 pruebas de integración inicial y 14/14 del control manual final correctas** (28 casos únicos), con HTTP simulado: dos opciones, HIT solo GET, MISS con controles cerrados, consentimiento antes de un único POST, ausencia de reintentos, cambios de modo/puerto, respuestas obsoletas, inspector independiente y recarga GET del reproductor.
- `npm run build`: correcto. Advertencia de tamaño inicial: 528,07 kB frente al umbral de 500 kB; no impide compilar.
- Backend: **181/181 pruebas .NET correctas**; incluye 15 casos de integración Stage 4 y 11 del índice (26 en total). Compilación API: **0 errores y 0 advertencias**. Las pruebas usan archivos y transporte sintéticos; cubren persistencia, identidad exacta, conservación de históricos, recuperación tras interrupción, conflictos, rutas y controles de ejecución.
- La validación se ejecuta en la PC física autorizada. **CI es manual y solo se ejecuta cuando el usuario lo solicita**; subir cambios a GitHub no exige ejecutarlo.

Estas pruebas no son segmentaciones reales del péndulo. La inferencia SAM del video, identificación humana de la plomada, calibración e incertidumbre siguen pendientes. No se declara gasto Meta nuevo ni descubrimiento científico. La validación técnica de la etapa 4 no completa esas etapas científicas.

**English:** [SAM-TWO-MODES-SQLITE.md](SAM-TWO-MODES-SQLITE.md).