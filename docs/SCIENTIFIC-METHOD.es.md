# Scientific Method Engine — cinco etapas documentadas

**ProofLab AI · Meta Global AI Developer Hackathon 2026 · Código competitivo desarrollado después de la inauguración.**

Scientific Method Engine ofrece un **flujo de investigaciones** con cinco etapas visibles, campos estructurados por informe, borradores persistentes, controles de completitud, confirmación de revisión humana, registros de informes cerrados que no se pueden editar en silencio y descarga de informes Markdown. Actualmente la interfaz admite investigaciones de **matemáticas**. Es un *prototipo alfa de investigación*: no es un sistema de revisión por pares, un laboratorio acreditado ni una demostración de descubrimientos científicos.

## ¿Por qué exactamente cinco etapas?

**No existe un método científico internacional único que imponga cinco pasos universales.** Adoptamos cinco etapas operativas inspiradas en guías de investigación y transparencia reconocidas. La investigación real es iterativa y cada disciplina necesita sus propios controles y autorizaciones.

| Etapa | Informe | Requisitos mínimos para avanzar | Referencias |
| --- | --- | --- | --- |
| 1. Problema y antecedentes | Informe de planteamiento | Pregunta delimitada, antecedentes, brecha y DOI/URL rastreable | Introducción IMRaD; transparencia TOP |
| 2. Hipótesis y objetivos | Informe de hipótesis | Hipótesis principal, alternativas y criterios de refutación | Prerregistro OSF; plan de análisis TOP |
| 3. Metodología y protocolo | Protocolo de investigación | Procedimiento reproducible, controles, plan de análisis previo y ética/seguridad | Prerregistro OSF; protocolos TOP |
| 4. Ejecución y evidencias | Bitácora y manifiesto | Referencias a datos originales, registro de intentos y procedencia | FAIR; W3C PROV-O |
| 5. Análisis y verificación | Informe científico y expediente | Resultados, estado de validación y conclusión con limitaciones | IMRaD; TOP; JCGM/GUM si hay mediciones |

Al completar las cinco etapas se habilita un **informe global consolidado**, estructurado de forma similar a IMRaD. No afirmamos que reúna automáticamente los requisitos de publicación de todas las revistas científicas.

### Estados y aprobación humana

- La investigación comienza con la etapa 1 en **DRAFT**; el resto permanece **LOCKED**.
- El investigador puede editar y guardar borradores.
- Para avanzar, todos los campos obligatorios deben superar una comprobación básica y la persona debe declarar expresamente que revisó la documentación.
- Cada aprobación genera hora UTC y huella SHA-256 del informe; después se habilita la siguiente etapa.
- Un informe aprobado no puede modificarse por esta API. La función de **abrir una nueva revisión** está pendiente de desarrollar; cuando se agregue, conservará los informes anteriores.
- **Aprobar las cinco etapas significa documentación completa, no verdad científica confirmada.** La declaración humana no equivale a auditoría independiente, aprobación ética, revisión por pares, prueba formal matemática ni acreditación ISO.

**Limitaciones actuales:** Los validadores revisan longitud y presencia de información y formato básico de una fuente, pero no si lo escrito es correcto u original. Por ahora el manifiesto solo almacena **referencias textuales**; aún no subimos ni comprobamos realmente archivos de video, imágenes o hashes de datos. La bitácora local `.private/research/audit.jsonl` es un registro añadido por la aplicación, **no un almacén a prueba de alteraciones** ni implementación completa de W3C PROV-O. La API es un prototipo local sin autenticación multiusuario.

## Adaptación para la Misión 1: matemáticas

Una conjetura es una propuesta matemática que podemos explorar; los cálculos numéricos ayudan a buscar patrones o contraejemplos, pero **no prueban un enunciado universal**. Cuando se afirme una demostración formal, deberá pasar por **Lean 4**. Si Lean rechaza una estrategia, esa prueba no está verificada; no significa que la conjetura sea falsa. Para afirmar un resultado realmente nuevo necesitamos enunciado correcto, verificación rigurosa, análisis de originalidad y revisión especializada.

El planificador Muse Spark permanece **desactivado** mientras no se confirme el crédito de Meta Model API. Este motor metodológico todavía **no redacta automáticamente ni aprueba informes con IA**.

## Rutas de la API local

- `GET /api/research/stages` — cinco etapas con campos y referencias bilingües.
- `POST /api/research` — `{"title":"Pregunta de investigación matemática","discipline":"Mathematics"}`.
- `GET /api/research/{id}` — consulta de la investigación.
- `POST /api/research/{id}/stages/{n}/draft` — `{"fields":{"question":"...",...}}` en la etapa activa.
- `POST /api/research/{id}/stages/{n}/approve` — `{"reviewedByHuman":true}`; falla si hay campos incompletos o se intenta saltar etapas.
- `GET /api/research/{id}/reports/{n}?lang=es` — informe Markdown de la etapa.
- `GET /api/research/{id}/paper?lang=es` — informe consolidado **solo tras aprobar las cinco etapas**.

Los datos se guardan únicamente en `src/ProofLab.Api/.private/research/`, excluido de Git. El navegador recuerda localmente el último ID. No debemos ingresar datos clínicos, sensibles o identificables en esta API de prueba sin autenticación.

## Referencias y alcance de cada estándar

- [TOP Guidelines (versión 2025)](https://www.cos.io/initiatives/top-guidelines): registro del estudio, protocolos, transparencia del código/datos y verificaciones.
- [Prerregistro OSF](https://help.osf.io/article/330-welcome-to-registrations): plan fechado **antes** de obtener/analizar resultados; los cambios deben declararse.
- [Principios FAIR](https://www.go-fair.org/fair-principles/): datos fáciles de encontrar, accesibles, interoperables y reutilizables.
- [W3C PROV-O](https://www.w3.org/TR/prov-o/): modelo de procedencia para entidades, agentes, actividades y relaciones de datos.
- [JCGM 100:2008 / GUM](https://www.bipm.org/en/doi/10.59161/jcgm100-2008e): tratamiento de incertidumbre de medición, especialmente relevante para **física**.
- [ICMJE](https://www.icmje.org/recommendations/browse/manuscript-preparation/preparing-for-submission.html): estructura IMRaD, documentación metodológica, uso de IA y limitaciones.
- [Red EQUATOR](https://www.equator-network.org/about-us/what-is-a-reporting-guideline/): guías específicas por tipo de investigación, como CONSORT, STROBE o PRISMA.
- [ISO/IEC 17025](https://www.iso.org/es/contents/data/standard/06/69/66912.html): competencia de **laboratorios de ensayos y calibración**; **no** es una plantilla general de cinco pasos. ProofLab AI no afirma cumplir esa norma.

**English:** [SCIENTIFIC-METHOD.md](SCIENTIFIC-METHOD.md).
