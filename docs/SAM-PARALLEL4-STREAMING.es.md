# Ensayo A/B — SAM 3.1 en cuatro fragmentos y cuatro streams (RAMA FEATURE)

**Estado:** prototipo de planificación, concurrencia y verificación de empalmes probado **solo con datos sintéticos**. No está integrado al endpoint pagado. No se ejecutaron solicitudes a Meta ni se modificó `main`.

## Flujo diseñado

1. Spark examina el **video completo una sola vez**, devuelve una escena e identidad de objetivo con evidencias verificables. El mismo SceneKey se exige en los cuatro fragmentos.
2. ProofLab toma PTS nativos (no FPS teóricos), divide los fotogramas en **cuatro bloques centrales contiguos**, con dos fotogramas de solapamiento a cada lado para verificar la identidad al empalmar.
3. Se prepararán cuatro MP4 privados, y antes de autorizar transferencias deberán verificarse **número de fotogramas, PTS, orientación y píxeles** frente al original. **Esta generación física aún no está implementada:** el FFmpeg local no tiene libx264; dispone de h264_nvenc, cuya exactitud todavía debe medirse. Nunca usar cortes por tiempo aproximado como evidencia científica.
4. Un orquestador puede ejecutar cuatro operaciones independientes mediante `Task.WhenAll`, transmitir los contadores de eventos SSE de **cada fragmento** y guardar **cuatro recibos/estados de cobro diferentes** antes de presentar resultados. El prototipo actual acepta funciones simuladas; **NO envía solicitudes ni reserva créditos**.
5. El validador transforma cada índice local al fotograma global y conserva los PTS originales. Permite que SAM cambie ID entre fragmentos solo si el mismo fotograma de solapamiento tiene geometría coincidente: distancia de centroides ≤45% del ancho/alto menor del objeto (mínimo 3 px) e IoU de cajas ≥0,40. Si faltan solapamientos, el empalme es **no verificado**; si se contradicen, se rechaza. Jamás inventa máscaras, centroides o fotogramas.
6. Al reunir **los cuatro resultados completados y autenticados**, se compararán cobertura, huecos y errores de centroides (media y p95 en píxeles) con el análisis SAM completo guardado en SQLite. No fusionar científicamente si faltan recibos o hay discontinuidades. Streaming no equivale a un resultado ya validado.

## Ejemplo 1.800 fotogramas

4 bloques de 450 fotogramas; entradas a SAM de 452, 454, 454 y 452 fotogramas por solapamiento; **1.812 fotogramas agregados**. Estimación aritmética SAM: US$0,3624 frente a US$0,3600 en una sola llamada (US$0,0024 adicionales); no incluye Spark, sobrecarga ni cobro real. Presupuesto combinado de todas las llamadas nunca debe exceder el límite autorizado. Para la grabación de 1.828 fotogramas, las cuatro ventanas contienen 1.840 fotogramas agregados, estimados US$0,3680 en SAM frente a US$0,3656, antes de Spark.

El tiempo **NO** se puede pronosticar como 177/4 sin medir proveedor: diez o cuatro solicitudes pueden serializarse, competir por cuota o aumentar latencia. Las barras SSE informan eventos reales, no porcentajes ficticios. No probar con Meta sin nueva autorización y presupuesto para **cuatro llamadas** explícitas.

## Implementación de la rama

- `src/ProofLab.Api/SamFourWayPlan.cs`: ventanas, SHA-256 de secuencia PTS y presupuesto agregado, asignación global, detecciones y empalmes conservadores.
- `src/ProofLab.Api/SamFourWayStreaming.cs`: cola asíncrona de cuatro tareas, progreso SSE etiquetado, sin reintentos y sin cancelar las demás al fallar una; comparador contra un seguimiento completo.
- `tests/ProofLab.Tests/SamFourWayTests.cs`: fixtures sintéticos que comprueban tiempos, coste, IDs diferentes, discontinuidades, errores, concurrencia, fallos y comparación.
- **La ruta productiva `PendulumSamCache`, transportes Meta, autorizaciones, SQLite y UI permanecen intactos en esta rama.**

## Criterios de aceptación antes de fusionar

1. Cortar el video **real** a cuatro MP4, verificar PTS y decodificación; comparar puntos/píxeles con el video completo. No usar proxy de 12 fps.
2. Extender recibos inmutables por parte (SHA original, SHA parte, PTS y factura) y cachear resultados parciales sin declarar un experimento exitoso.
3. Decodificar máscaras verdaderas con `@meta-sam/parser` para cada fragmento y producir un **único archivo de evidencia final verificable**, incluyendo máscaras y mapa de tiempos. Actualmente solo se empalman observaciones decodificadas, no el payload completo.
4. Benchmark offline con evidencia previa y pruebas UI/servidor de no-gasto; solo después una prueba real **por separado autorizada** para cuatro llamadas y una localización Spark, cotejando costo y duración.
5. No fusionar a `main` sin decisión del propietario. No ejecutar CI rutinariamente.

**English:** [SAM-PARALLEL4-STREAMING.md](SAM-PARALLEL4-STREAMING.md).


## Resultado ampliado del 29/10/2026 — clips REALES y QA SSE aislado

Se implementaron herramientas ejecutables **solo de forma local** en la rama `feature/sam-parallel4-streaming`, sin cambios a los endpoints de pago ni a la UI del producto.

- `SamFourWayClipBuilder.cs` y `SamFourWayOnePassClips.cs`: cortan el MOV original de 50 cm, 1.828 fotogramas, en cuatro MP4 HEVC NVENC sin pérdida mediante selección exacta de cuadro. Los cortes preservan los fotogramas por comparación SHA-256 del raster decodificado y PTS medidos con FFprobe (error máximo observado **1,55 ms**). Cada MP4 pesa 119–160 MB; conjunto **532.387.036 bytes**, frente a **45.708.115 bytes** del MOV original. Este aumento de subida (~11,65×) puede anular ventajas de paralelismo remoto.
- Para 1.828 fotogramas: 459/461/461/459 cuadros por fragmento, solapamiento total 12 cuadros, 1.840 enviados; estimación SAM de **US$0,3680**, antes de cualquier Spark, facturación real o restricciones del proveedor.
- Primera preparación con cuatro decodificaciones: **120,76 s**. Una sola decodificación alimentando cuatro codificadores NVENC simultáneos: **98,24 s**. Verificación optimizada con una sola lectura de hashes del video fuente: **90,208 s** hasta cuatro MP4 validados, **25,3 % menos** que el primer método. La comparación incluye validación pixel-a-pixel, no mide la inferencia.
- Intento de remux/corte HEVC `-c copy` sin recodificación: un fragmento esperado de 56 frames devolvió **115** frames con `-ss` antes de `-i` y **3** con `-ss` después, así que se descartó por no garantizar equivalencia.
- `SamFourWayMaskAssembler.cs`: conserva el payload original de caja y máscara, restituye los índices globales, verifica geometría consistente, exige máscara **exactamente igual** en los fotogramas solapados de la referencia offline (si no coincide, rechaza). El criterio estricto no garantiza que cuatro inferencias reales produzcan máscaras iguales: si divergen, se requieren comparaciones raster IoU y revisión científica, aún no implementadas.
- `scripts/sam-parallel4-reference.mjs` decodificó con `@meta-sam/parser` el archivo real autenticado de una ejecución SAM previa: **1.781 máscaras**, cero advertencias, frames 47–1827.
- `SamFourWayOfflineSseReplay.cs` reconstruye cuatro streams SSE desde **esa misma respuesta antigua**, pasa por `SamVideoSseParser` y posteriormente por `SamFourWayStreaming`. Los cuatro streams se reunieron, 3/3 empalmes y **100 % de igualdad byte por byte** con los 1.781 registros reales originales. Media y p95 de centroides: **0 px por origen idéntico**. **NO prueba calidad ni velocidad de cuatro inferencias autónomas del proveedor**.
- `tools/SamFourWayQa` produce manifiestos y benchmark en `.private/fourclip-qa` sin credenciales. `tools/SamFourWayQaServer` expone solo en `127.0.0.1:5407` un informe de lectura y un SSE `/api/qa/stream` **claramente etiquetado como simulado offline** (16 eventos + fin). No transmite videos ni ofrece endpoints facturables. Consultar `/api/qa/report` para ver duraciones, cobertura, costos estimados, hashes y datos de la validación.
- **QA:** 19/19 pruebas focalizadas, builds CLI/servidor .NET Debug sin errores ni advertencias. No hubo llamadas a Meta, nuevas máscaras auténticas ni modificación de servicios de otros chats.

**Faltan antes de cualquier merge:** integrar recibos inmutables POR FRAGMENTO con presupuesto agregado y autorización de las cuatro solicitudes, decodificación oficial por cada nueva respuesta real, comparación de máscaras distintas por raster-IoU/huecos en empalmes y medición A/B **real** de latencia/coste/calidad. Con 90,2 s de preparación local y ~177 s de SAM completo, el procesamiento remoto de los cuatro fragmentos tendría que tardar menos de ~87 s (excluyendo Spark y sobrecargas) para mejorar la latencia SAM total, si ambas tareas son secuenciales. No se ha demostrado esa mejora ni se autorizó gasto.
