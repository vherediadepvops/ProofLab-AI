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
| C — contrastes recuperados de AWS, **4 nov.** | [25 evaluaciones almacenadas](RESULTS.es.md): **18 dentro de ±5 %**, **6 fuera**, **1 inconclusa**; predicciones congeladas y ambos videos presentes. | Persistencia y recuperación de todo el flujo y sus veredictos, incluso negativos. | 25 predicciones correctas, 25 filmaciones independientes o precisión calibrada. |

### Alcance comprobado de la tanda del 4 de noviembre

- El Excel «28 combinaciones de péndulo» contiene **25 parejas y 25 identificadores privados**. Sus celdas de resultados aún dicen «Pendiente», pero los resultados guardados **sí fueron recuperados** de la producción AWS y están resumidos por pareja en [Resultados](RESULTS.es.md).
- Por HTTPS GET se verificaron **25/25 registros con ambos videos, predicción Muse congelada y evaluación de contraste `COMPLETED`**: **18 dentro de ±5 %**, **6 fuera** y **1 inconcluso**. No hubo nuevas inferencias.
- Se reutilizaron **ocho grabaciones** existentes con longitudes nominales de 10 a 80 cm. Las tres parejas teóricas no incluidas fueron 10→60, 10→70 y 10→80 cm; el propietario eligió registrar solamente 25 casos.
- La pareja de archivos 50→60 cm declaró por error 40 cm como longitud inicial (−11,23 % fuera de banda); la pareja 40→80 cm fue inconclusa porque el segundo seguimiento SAM cubrió solo el **3,07 %**. Ninguna incidencia se reescribió.
- La consulta se hizo mediante los endpoints del sistema respaldados por SQLite/archivos, **no** mediante auditoría de archivos de BD o recibos Meta. La compatibilidad con ±5 % no establece precisión universal ni validez física calibrada.

Los códigos de recuperación, medios no públicos, respuestas crudas, recibos y filas SQLite permanecen privados. No se modificó ni publicó el Excel.

## Próxima evidencia científica necesaria

Medir de forma independiente longitud, pivote y perspectiva; cuantificar errores sistemáticos; grabar prospectivamente el segundo video después de congelar la predicción y conciliar recibos/hashes en privado. Los 25 casos documentan funcionamiento real, incluidos los resultados adversos, **no** 25 predicciones científicas correctas ni 25 filmaciones independientes.

## Recursos

[Aplicación](https://prooflab-ai.com/) · [Demo en inglés](https://youtu.be/75i78EO8xZo) · [Español](https://youtu.be/D1O1ZFvc0q0) · [Português](https://youtu.be/EXuD3RJayIk) · [中文](https://youtu.be/quHyAHKxzZ8)

[Resultados y limitaciones](RESULTS.es.md) · [Protocolo reproducible](EVALUATION.es.md) · [Integración Muse/SAM](MUSE-INVESTIGATOR.es.md)

*Este documento revisa evidencias existentes y un Excel del propietario; no ejecuta inferencias de Meta ni nuevos experimentos.*
