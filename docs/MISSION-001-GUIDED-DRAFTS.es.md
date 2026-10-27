# Misión 1 — Borradores científicos guiados SIN Muse Spark

**Fecha:** 27/10/2026. **Alcance:** experiencia de demostración sobre la conjetura de Goldbach. **No es IA autónoma.**

## Motivación

El usuario no debería escribir manualmente todos los campos de cinco etapas. Esta edición incorpora un generador de **borradores deterministas y revisables**, basado exclusivamente en el catálogo curado y en las evidencias reales almacenadas por la API. No invoca modelos Meta ni facturación.

## Uso en la interfaz

1. Crear o abrir una investigación matemática cuyo título contenga **Goldbach** (por ejemplo «Exploración finita de la conjetura de Goldbach»).
2. En etapas **1, 2 o 3** pulsar **«Preparar borrador guiado (sin IA)»**. Se completan únicamente los campos **vacíos**; se preserva cualquier texto que haya escrito el investigador.
3. Revisar fuentes, hipótesis, método y protocolo. Pulsar «Guardar borrador», marcar personalmente la casilla de revisión de completitud y elegir «Cerrar informe y avanzar». No hay aprobación automática.
4. En la etapa **4**, pulsar **«Ejecutar controles guiados (sin IA)»**. El servidor ejecuta un experimento **finito de Goldbach 4..100** y tácticas Lean 4 autorizadas para un **teorema conocido distinto**, con límite de acciones, ID de intento y SHA-256.
5. Una vez registrados ambos tipos de evidencia, pulsar «Preparar borrador guiado (sin IA)» en la etapa 4. El texto cita **IDs, fechas, resultados y SHA-256 reales**. Revisar y confirmar manualmente el informe.
6. En la etapa **5**, volver a preparar el borrador con los registros ya existentes. Concluir únicamente sobre el intervalo observado y distinguir con claridad el control Lean conocido de la conjetura abierta. Revisar y completar el informe; después exportar Markdown ES/EN.

## API y controles

- `GET /api/research/{id}/stages/{stage}/guided-draft?lang=es|en` produce una **vista previa sin efectos secundarios**, no guarda ni aprueba nada; exige expediente matemático de Goldbach y etapa activa 1–5.
- `GuidedResearchDrafts.cs` genera etapas 1–3 con catálogo Goldbach y fuentes identificadas (MathWorld y DOI de Oliveira e Silva/Herzog/Pardi). La metodología delimita la comprobación a 49 pares, control C# .NET 10 y verificación Lean 4 v4.35.0 de un objetivo **conocido y separado**.
- Etapas 4–5 se bloquean cuando **no existen ambas evidencias** en los registros del expediente: Goldbach finito y al menos un intento Lean auténtico. Nunca se inventan testigos, diagnósticos o verificaciones.
- `POST /api/research/{id}/math/offline-cycle` (preexistente) expuesto en la interfaz como «Ejecutar controles guiados (sin IA)», sin scripts libres, sin peticiones Meta y sin autoaprobación. La selección de acciones es **programada**.
- El botón de borradores **solo rellena campos vacíos en el navegador**, sin sobrescribir texto, guardar archivos, cerrar etapas ni marcar el consentimiento. El investigador debe revisar resultados y decidir si aprobar la completitud.

## Evaluación y límites

El smoke de CI verifica que las etapas 1–3 generen todos los campos ES/EN requeridos, que los informes 4–5 **citen IDs auténticos** y que los flags `isAutonomousAi` e `isScientificDiscovery` permanezcan `false`. El contenido de Goldbach finito **no prueba su afirmación universal** y Lean comprueba únicamente un teorema ya conocido. No se interpreta una plantilla determinista como razonamiento de un modelo.

No se pretende todavía automatizar investigación abierta, lectura autónoma de literatura, revisión humana ni ejecución segura de Lean generado por IA. No afirmar Misión 1 terminada.

**English:** [MISSION-001-GUIDED-DRAFTS.md](MISSION-001-GUIDED-DRAFTS.md).
