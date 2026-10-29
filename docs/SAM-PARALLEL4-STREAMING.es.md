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
