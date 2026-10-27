# ProofLab AI — Política bilingüe de respuestas (ES/EN)

**Misión 1, edición de competencia del Meta Global AI Developer Hackathon 2026.**

## Comportamiento implementado

- La selección **ES / EN** controla los textos de la interfaz, las explicaciones del verificador Lean 4 y los resultados de las pruebas matemáticas, así como las advertencias y errores de interfaz de la Misión 1. La preferencia queda guardada en el navegador.
- **ES:** `VERIFIED` aparece como **VERIFICADO**; `REJECTED` como **RECHAZADO**; las explicaciones de `rfl`, `simp` y de las pruebas aceptadas aparecen en español.
- **EN:** `VERIFIED` y `REJECTED` se muestran en inglés junto con sus explicaciones.
- Cambiar de idioma vuelve a presentar **el mismo resultado ya verificado**, sin ejecutar Lean nuevamente ni alterar las pruebas. Los identificadores de intentos, las tácticas `rfl`, `omega`, `simp`, las expresiones matemáticas y las huellas SHA-256 no se traducen.
- La salida original de Lean, que normalmente está en inglés, se conserva como evidencia técnica y solo aparece cuando el usuario despliega «Ver diagnóstico original».
- Los expedientes exportados con `?lang=es` o `?lang=en` incorporan el historial de verificaciones en ese idioma. Las observaciones **redactadas manualmente** se conservan en su idioma original; no se traducen silenciosamente.
- Muse Spark recibe una instrucción explícita para responder en el idioma activo y la API permanece desactivada hasta confirmar créditos. Cambiar de idioma no genera llamadas pagadas; los textos antiguos producidos por un modelo se ocultan, evitando mostrar una respuesta desactualizada en el idioma incorrecto.

## Ejemplo de la Misión 1

En el teorema conocido de conmutatividad de la suma:

| Táctica | ES | EN |
| --- | --- | --- |
| `rfl` | **RECHAZADO** — los miembros de la igualdad no son idénticos por definición. No se refuta el teorema. | **REJECTED** — sides are not equal by definition. This does not disprove the theorem. |
| `omega` | **VERIFICADO** — demostración de un teorema ya conocido, no un descubrimiento. | **VERIFIED** — a previously known theorem, not a novel discovery. |
| `simp` | **RECHAZADO** — la simplificación no pudo demostrar el objetivo. | **REJECTED** — simplification did not prove the goal. |

La información técnica original puede ser consultada, pero no sustituye la explicación en el idioma elegido.

## Límites

La localización de mensajes está definida para los resultados conocidos de las pruebas Lean y la interfaz actual; futuros tipos de experimentos y errores especializados necesitarán nuevas traducciones. Tampoco afirmamos que los informes redactados por usuarios estén traducidos automáticamente. Las pruebas automatizadas verifican ambas opciones de idioma, y ninguna validación científica o demostración matemática se modifica al traducir su descripción.

**English:** [LOCALIZATION.md](LOCALIZATION.md).
