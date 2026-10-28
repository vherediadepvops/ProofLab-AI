# ProofLab AI — Dos modos SAM y base SQLite física (incremento sin inferencias)

**Fecha:** 28/10/2026 · **Misión:** Autonomous Physics Discovery / péndulo · **Estado:** índice SQLite offline implementado y probado; integración con las llamadas a Meta y el selector UI AÚN BLOQUEADA por control de seguridad.

## Contrato funcional solicitado

| Selección | Objetivo | Reglas y estado real |
|---|---|---|
| **API Meta** | Solicitar una inferencia SAM 3.1 **nueva** incluso si el video ya fue procesado | No volver a presentar un resultado de caché como una ejecución nueva. Si el usuario lo autoriza y el backend permite la solicitud, debe guardarse un nuevo `analysis_id`, un nuevo recibo y un nuevo marcador de intento. **No implementado:** la modificación del flujo pagado/forzado fue rechazada por controles de seguridad. |
| **API Meta + caché** | Consultar SQLite por video original SHA-256 + versión de modelo/protocolo + hash del prompt; usar la evidencia auténtica guardada si existe; con ausencia, solicitar SAM solamente tras permisos y presupuesto | Índice SQLite físico preparado, **pero NO conectado** al servicio ni al reproductor. El cache previo de archivos con `receipt.json` sigue separado. Aún no existe un caché SAM real para el péndulo de 50cm. |

No sustituir la autorización del backend por una casilla del frontend: la UI recaba consentimientos, pero el backend verifica identidad, video/PTS, presupuesto y seguridad. Una clave temporal pegada en el navegador nunca debe desactivar una protección técnica o de organización.

## Componente implementado en este incremento seguro

`src/ProofLab.Api/SamVideoEvidenceIndex.cs` implementa un índice **offline** con Microsoft.Data.Sqlite `10.0.14` bajo `.private/physics/sam-video-evidence.sqlite`, separado de `.private/research/scientific-experience.sqlite` (Lean 4). Almacena SOLO metadatos: SHA de video original, identidad de procesamiento/modelo, hash de prompt, SHA del archivo de respuesta, SHA del recibo, ruta privada relativa sin traversal, fecha, `analysis_id`, recuento de fotogramas y estado de procedencia. **No almacena** token, video, máscaras, coordenadas, prompts en texto claro ni respuestas del proveedor.

La clase exige formato estricto de SHA/IDs, modelo `sam-3.1`, protocolo `PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2`, ruta permitida y estado `PROVIDER_RECEIPT_PREVIOUSLY_VERIFIED`. La inserción es idempotente solo si no hay conflicto en identidad/procedencia; las inconsistencias se rechazan. La recuperación exige coincidencia exacta de video, cacheKey, prompt y protocolo. Una coincidencia SQLite **no es prueba suficiente de autenticidad**: cuando se conecte al backend, este deberá comprobar sobre disco tanto recibo como respuesta, hashes y origen real de Meta **antes** de renderizar SAM. Un JSON importado manualmente no puede promoverse a proveedor válido.

**No hay llamada a Meta ni lógica de reintento pagado en este componente.** Tampoco se creó un índice de video en la base matemática existente.

## Verificación local

Pruebas en `tests/ProofLab.Tests/SamVideoEvidenceIndexTests.cs`: ausencia de entrada, índice persistente entre reaperturas, coincidencia exacta de hash/modelo/prompt, duplicado idéntico, rechazo de datos en conflicto, rutas `../`/traversal, metadatos falsos o incompletos, y SHA de consulta inválido. Son **fixtures sintéticos**; NO son segmentaciones de plomada. Suite .NET **165/165 PASS**, build API aislado Debug **0 errores/0 warnings**. La compilación Release normal intentó escribir en un DLL bloqueado por el servicio del titular `5393`; no se detuvo ese servicio. Una compilación a `.private/sqlite-index-qa` pasó.

El repositorio de trabajo del dueño conserva modificaciones Muse no relacionadas y el intento SAM anterior (`attempt-started.json` y `sam-upload.mp4`) sin máscaras. **No se cambiaron** `Program.cs`, `PendulumSamCache.cs`, `sam-video-replay.ts` ni configuración de cobros: los cambios de integración de la conversación anterior fueron expresamente rechazados por la herramienta de seguridad; no se intentó reproducirlos con otra vía.

## Trabajo pendiente, sujeto a procedimiento permitido

1. Obtener un procedimiento autorizado para la integración bloqueada: enlace del índice a respuestas provider verificadas y selector de exactamente **API Meta / API Meta + caché**.
2. En cada reproducción, exigir recibo, hashes y coincidencia exacta con versión y tiempos reales; declarar cache MISS sin inventar resultados. No usar SQLite de Lean.
3. Para API Meta, diseñar nuevos `analysis_id` y recibos sin pisar históricos; no iniciar llamadas mientras los gates del proveedor estén bloqueados y no haya autorización nueva conforme a las protecciones.
4. Verificar CI del commit, y después el flujo real de SAM y revisión manual de plomada/errores. **Etapas científicas 2 y 3 pendientes.**

**English:** [SAM-TWO-MODES-SQLITE.md](SAM-TWO-MODES-SQLITE.md).
