# Misión 1 — Primera conjetura abierta curada

**Registro:** 2026-10-27. **Estado:** catálogo y observación computacional finita; NO demostración formal ni descubrimiento nuevo.

## Problema GOLDBACH-STRONG

**Conjetura fuerte de Goldbach:** todo entero par mayor que 2 es suma de dos primos.
**Estado en la revisión del 27/10/2026:** OPEN_RESEARCH (no resuelta).
**Fuentes:**
- Wolfram MathWorld, [Goldbach Conjecture](https://mathworld.wolfram.com/GoldbachConjecture.html), consultada 2026-10-27. Distinguir la versión fuerte de la débil (ternaria).
- T. Oliveira e Silva, S. Herzog y S. Pardi, *Mathematics of Computation* 83 (2014), 2033–2060. [DOI:10.1090/S0025-5718-2013-02787-1](https://doi.org/10.1090/S0025-5718-2013-02787-1). Su comprobación computacional llegó hasta 4×10^18. Es trabajo anterior, NO de ProofLab.
- [Formal Conjectures (DeepMind)](https://github.com/google-deepmind/formal-conjectures): catálogo de enunciados Lean, ejemplo de buenas prácticas de versionado y advertencias de fidelidad de formalización. **ProofLab no ha importado ni verificado allí un enunciado Lean de Goldbach.**

## Herramienta computacional (M1.1)

API: `GET /api/math/conjectures` y `GET /api/math/conjectures/GOLDBACH-STRONG`.
Tras aprobar las etapas 1–3, API `POST /api/research/{id}/math/conjectures/GOLDBACH-STRONG/finite-check` con `{"evenLimit":100}`.
El límite debe ser par entre **4 y 2000**; el motor aplica un tamiz de números primos y encuentra una descomposición prima para cada par de `4..evenLimit`.

Cada observación se guarda en un **ledger JSONL privado y aditivo**, con ID, momento, algoritmo `goldbach-sieve-v1`, rango, recuentos, ejemplos, hash SHA-256 de entrada y del resumen. El hash del resumen es reproducible excluyendo hora e ID del intento. `GET /api/research/{id}/math/finite-checks` recupera el historial. Informes 4–5 y documento consolidado incluyen la evidencia en ES/EN.

**Importante:** encontrar testigos hasta 2000 no demuestra la afirmación universal; ese intervalo ya estaba cubierto por investigaciones anteriores. Tampoco equivale a una prueba formal Lean. El resultado solo es evidencia computacional acotada y sirve para evaluar trazabilidad, herramientas y control de afirmaciones. La exigencia existente de **ID real Lean** para aprobar informes matemáticos 4–5 permanece vigente; estos registros son complementarios.

## Cinco etapas y próximo ciclo del agente

1. **Problema:** registrar Goldbach, revisión del estado, DOI y limitaciones.
2. **Hipótesis:** enunciado universal, subproblemas/ejemplos, predicción falsable para intervalo finito.
3. **Protocolo:** explicar método de primalidad, cotas, versión y cómo reproducirlo.
4. **Experimentación:** ejecutar herramienta finita, registrar testigos/errores/ID/SHA; también adjuntar intento Lean de control mientras no exista un aislamiento validado para pruebas abiertas.
5. **Verificación:** examinar incertidumbre y sesgos, comparar literatura, indicar expresamente `INCONCLUSIVE` para la conjetura universal, documentar el siguiente intento.

**Siguiente fase (NO implementada todavía):** Muse Spark vivo seleccionará acciones de una lista cerrada, con límite de iteraciones y costos; un validador independiente comprobará la salida, y el ejecutor aislado recibirá código Lean libre solo después de su validación y revisión de la formalización del objetivo. Ningún texto de un LLM es una prueba.

Véase también [seguridad de Lean](LEAN-SANDBOX.es.md) y [Misión 1](MISSION-001-MATH.es.md).
