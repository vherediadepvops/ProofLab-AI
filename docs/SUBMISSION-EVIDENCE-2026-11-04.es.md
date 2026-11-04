# Evidencias para la entrega — ProofLab AI (4 de noviembre de 2026)

**Meta Global AI Developer Hackathon 2026 · Indie Developer · Push the Frontier**

[README en español](../README.es.md) · [English](SUBMISSION-EVIDENCE-2026-11-04.md) · [Protocolo de evaluación](EVALUATION.es.md) · [Resultados](RESULTS.es.md)

## Qué demuestra el video

La [demostración de tres minutos en inglés](https://youtu.be/75i78EO8xZo) presenta el flujo: video de péndulo real → seguimiento con SAM → mediciones físicas de ProofLab → interpretación de Muse Spark → predicción fijada antes de procesar el segundo video → contraste numérico con una tolerancia práctica predefinida.

El período observado se obtiene de mediciones del video, no ajustándolo al período teórico. Estar dentro de una banda práctica no demuestra precisión científica universal ni una ley física nueva.

## Evidencia por niveles, sin mezclar campañas

| Nivel | Evidencia | Qué respalda | Qué no respalda |
| --- | --- | --- | --- |
| A — comparaciones científicas, **30 oct.** | [Campaña de diez casos](RESULTS.es.md): **7 dentro** de bandas predefinidas (incluido **1 control sintético**), **2 inconclusos**, **1 fuera** (control negativo intencional de longitud equivocada). | Integración real Muse/SAM y decisiones falsables, incluso resultados negativos e inconclusos. | Un "70% de precisión", diez experimentos físicos independientes o grabaciones prospectivas ciegas. |
| B — controles de software, **3 nov.** | Revisión de la versión [b651232](https://github.com/vherediadepvops/ProofLab-AI/commit/b6512323e7f72f0d718af30be648c8a59baaf648) con **153 pruebas .NET** y **101 Angular** aprobadas según [Resultados](RESULTS.es.md). | Cobertura de regresión de esa revisión. | Nuevas mediciones, inferencias Meta o validación de commits posteriores. |
| C — registro del propietario, **4 nov.** | Excel local titulado **“28 combinaciones de péndulo”**: contiene **25 parejas registradas**, cada una con un identificador de investigación de 32 caracteres. El propietario informa que esos identificadores alcanzaron el final del flujo. | Índice de trazabilidad y ejecución del flujo informada por el propietario. | 25 veredictos científicos correctos, precisión verificada ni 28 flujos terminados. |

### Alcance del Excel del 4 de noviembre

- Se reutilizan **ocho grabaciones existentes** con longitudes nominales de **10, 20, 30, 40, 50, 60, 70 y 80 cm**; el nombre del archivo no calibra por sí solo la longitud efectiva ni el pivote.
- Las ocho grabaciones permiten **28 parejas teóricas**. Hay **25 parejas registradas**; faltan **10→60 cm, 10→70 cm y 10→80 cm**.
- Las 25 filas de resultado permanecen en estado **“Pendiente”** y no contienen T₁ medido, predicción T₂ congelada, T₂ medido ni veredicto ProofLab.
- Los períodos del Excel son **teóricos**, calculados con \(T=2\pi\sqrt{L/g}\) y \(g=9,81\,\mathrm{m/s^2}\); no son mediciones de los videos.
- **No hemos vinculado aquí los 25 identificadores con los informes privados inmutables o recibos del proveedor.** No se afirma que sean 25 éxitos científicos verificados.
- Las parejas retrospectivas no equivalen a 25/28 grabaciones físicas independientes.

No se publica aquí el Excel ni los videos originales. Los IDs por caso, la información original, los resultados completos de Meta, los recibos y SQLite permanecen privados.

## Evidencia pendiente para verificar la nueva tanda

Cada investigación debería enlazar hashes y marcas de tiempo de ambos videos, seguimiento SAM autenticado, T₁ observado, predicción realmente fijada y su fecha, T₂ observado, tolerancia previa, veredicto guardado (incluidos los inconclusos) y revisión humana. Solo publicar agregados revisados y anonimizados.

**Declaración vigente:** prototipo funcional de investigación mostrado públicamente, con resultados anteriores documentados y limitaciones explícitas. El nuevo registro de 25 IDs es **seguimiento de flujos, no prueba adicional de precisión científica**.

## Recursos

[Aplicación](https://prooflab-ai.com/) · [Demo en inglés](https://youtu.be/75i78EO8xZo) · [Español](https://youtu.be/D1O1ZFvc0q0) · [Português](https://youtu.be/EXuD3RJayIk) · [中文](https://youtu.be/quHyAHKxzZ8)

[Resultados y limitaciones](RESULTS.es.md) · [Protocolo reproducible](EVALUATION.es.md) · [Integración Muse/SAM](MUSE-INVESTIGATOR.es.md)

*Este documento revisa evidencias existentes y un Excel del propietario; no ejecuta inferencias de Meta ni nuevos experimentos.*
