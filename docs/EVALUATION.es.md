# Evaluación — flujo científico del péndulo

**Meta Global AI Developer Hackathon 2026 · Indie Developer · Push the Frontier**
**Evidencia revisada hasta el 3 de noviembre de 2026.** [English](EVALUATION.md) · [Resultados](RESULTS.es.md).

Este documento explica **qué se evaluó**, el protocolo, los controles, la procedencia y lo que puede reproducir un tercero. No sostiene que se haya descubierto una ley física nueva. La campaña de diez casos ocurrió el **30 de octubre de 2026**, antes de posteriores cambios en el procesamiento de video.

## Pregunta de investigación

¿Puede un laboratorio asistido por IA usar observaciones de un péndulo grabado, registrar una predicción falsable para otra longitud y compararla con mediciones **independientes** de una segunda grabación, sin adaptar el período medido al valor esperado?

Se utiliza la relación conocida de pequeñas oscilaciones `T = 2π√(L/g)`. Al variar la longitud, la predicción configurada es `T₂ = T₁√(L₂/L₁)`. Se evalúa **el sistema de medición y el método experimental**, no la originalidad de la teoría.

## Casos y controles (campaña del 30 de octubre)

| Grupo | Casos | Qué se evaluó | Aclaración |
|---|---:|---|---|
| Grabaciones reales anteriores | 8 | Tres pares principales 50 cm → 80 cm y cinco combinaciones cruzadas de seis videos (50CM_1/2/3 y 80CM_1/2/3) | Se reutilizan grabaciones y ciertas observaciones SAM; **no son ocho filmaciones independientes** |
| Control negativo deliberado | 1 | Un video de 50 cm declarado intencionalmente como seguimiento de 80 cm | Verifica que el sistema detecte discrepancias, no mide precisión normal |
| Control sintético de período conocido | 1 | Dos videos/trayectorias generados con períodos predefinidos | Control de software/geometría, **no evidencia física** |
| **Total** | **10** | Hipótesis y contraste Muse nuevos por caso | **No son diez experimentos físicos prospectivos independientes** |

Los seis videos reales **ya existían** antes de esta tanda. La predicción se congeló con hash **antes de subir el segundo video a la aplicación**, pero eso **no demuestra** que la filmación haya ocurrido después de la predicción. No se afirma una adquisición prospectiva a ciegas.

## Protocolo y criterios

1. Registrar original, parámetros físicos declarados, SHA-256, tiempos del video e identidad del análisis; conservar intactos los bytes originales.
2. Localizar el objeto suspendido con Muse Spark y obtener máscaras/trayectorias de video con SAM 3.1. Rechazar o declarar incierta la evidencia ausente o contradictoria; nunca inventar cuadros.
3. Calcular períodos desde tiempos originales y geometría observada. El estimador **no recibe el período teórico**. Los huecos de seguimiento separan intervalos y no se interpolan.
4. Muse Spark revisa la evidencia y propone otra prueba o marca inconclusa. Una comprobación numérica independiente valida y congela la predicción con identidad/hash antes de la carga posterior.
5. Contrastar el segundo análisis autenticado con la predicción congelada. La **banda práctica predeclarada** determina dentro/fuera; una suspensión dudosa puede producir **inconcluso**. La banda **no equivale** a intervalo estadístico de confianza.
6. Conservar intentos, decisiones, resultados negativos y procedencia en registros privados. Recuperar resultados guardados no vuelve a llamar a Meta.

Documentación pública: [estimador SAM](SAM-PERIOD.es.md), [investigador y contraste Muse](MUSE-INVESTIGATOR.es.md), [protocolo físico](EXPERIMENTS.es.md).

## Alcance de los controles

- **Casos que coinciden:** prueban concordancia con la banda práctica bajo las observaciones y longitudes declaradas; no certifican calibración ni geometría exacta.
- **Control de longitud falsa:** prueba el rechazo de una discrepancia introducida deliberadamente; no representa una contradicción física espontánea.
- **Control sintético:** valida cálculos contra trayectorias conocidas; no elimina sesgos de cámara, pivote o identidad en videos reales.
- **Incertidumbre:** incertidumbres tipo A e intervalos exploratorios del 95 % cubren solo las fuentes declaradas. No incluyen completamente perspectiva, longitud, reloj y errores sistemáticos.

## Reproducibilidad y acceso a evidencia

**Público:** protocolo, [resultados](RESULTS.es.md), [pruebas .NET](../tests/ProofLab.Tests), [pruebas del flujo](../tests/ProofLab.Workflow.Tests) y documentación de los algoritmos. Permiten inspección de código y pruebas locales sin Meta. Los requisitos y comandos están en [README](../README.es.md). Ejecutarlos **no equivale** a repetir las diez inferencias reales.

**Privado:** videos originales, respuestas integrales de Meta, credenciales, recibos, bases SQLite y bitácora completa de cada ejecución. Se preservan por privacidad y seguridad. Por tanto, **un tercero no puede repetir los diez casos originales únicamente con el repositorio público**.

La versión [`b651232`](https://github.com/vherediadepvops/ProofLab-AI/commit/b6512323e7f72f0d718af30be648c8a59baaf648), verificada el 3 de noviembre, superó pruebas locales y comprobaciones de UI en producción, **sin nuevas inferencias Meta**. Commits posteriores no quedan validados por esa campaña antigua.

## Pendientes para afirmar mayor precisión

- Medir de forma independiente pivote, centro del objeto y geometría de cámara en videos reales.
- Incorporar incertidumbre de longitud, perspectiva, reloj y segmentación sistemática.
- Validar con Meta el flujo nuevo de preparación en segundo plano y revisión sin zoom.
- Filmar realmente la segunda prueba después de congelar la predicción, con cronometraje manual separado.
- Preparar evidencia pública autorizada o controles sintéticos reproducibles sin compartir videos privados.

**Tablas y limitaciones:** [RESULTS.es.md](RESULTS.es.md). Este informe no ejecutó experimentos ni consumió Meta.
