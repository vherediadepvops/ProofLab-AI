# Misión 1 — Evidencia de Lean 4 vinculada con informes científicos

**Desarrollo competitivo:** 26 de octubre de 2026, después de la inauguración oficial.
**Madurez:** banco de pruebas matemático controlado. No hemos resuelto un problema matemático abierto.

## ¿Por qué importa este avance?

Antes, Scientific Method Engine permitía completar y cerrar cinco informes escritos. Ahora exige **intentos formales reales generados por el servidor** en las etapas matemáticas de ejecución y verificación. Así, no basta escribir que una demostración fue correcta para avanzar.

## Demostración reproducible (TODAVÍA SIN IA)

1. Crea una investigación `Mathematics` desde la sección Misión 1.
2. Completa y confirma la revisión de las etapas 1, 2 y 3: problema, hipótesis y protocolo. Esta confirmación valida **completitud documental**, no verdad científica.
3. En la **etapa 4: Ejecución y evidencias**, elige un teorema **conocido** del catálogo, por ejemplo conmutatividad de la suma natural.
4. Pulsa **Ejecutar ciclo de control**. El backend prueba solo `rfl` y luego `omega`, deteniéndose cuando Lean acepta una prueba. En el control de conmutatividad, `rfl` falla y `omega` es aceptado. Los resultados provienen de **Lean 4 real**.
5. Pulsa **Adjuntar al informe actual**. El sistema inserta el ID del intento, resultado y SHA-256 del código Lean en el manifiesto. Completa los campos restantes y confirma la revisión.
6. En la **etapa 5: Análisis y verificación**, adjunta nuevamente un intento registrado al campo de verificación; analiza su resultado y sus limitaciones. Solo después puedes cerrar el informe.
7. Descarga el informe de cada fase o el informe global tipo IMRaD, que incluyen la bitácora de resultados reales de Lean.

## Reglas obligatorias en el backend

- No se pueden ejecutar intentos antes de completar las etapas 1–3 ni después de terminar el expediente.
- Los intentos solo admiten identificadores de problemas predefinidos y las tácticas `rfl`, `omega` y `simp`. No se ejecuta código Lean arbitrario, comandos ni código suministrado por la IA.
- Cada expediente conserva sus intentos en JSONL local privado con hora, resultado, error, huella SHA-256 y etiqueta de teorema conocido.
- La etapa 4 **exige un intento real** (fallido o correcto) y su ID en el manifiesto. Una cadena inventada o un ID de otra investigación no sirve.
- La etapa 5 también exige una referencia real en el campo de verificación.
- Se distinguen tres conceptos: **teorema conocido verificado**, **intento de demostración fallido** y **descubrimiento original (NO declarado)**.
- El registro no es a prueba de manipulación: alguien con acceso a los archivos locales podría alterarlo. Esta API todavía no tiene autenticación y debe usarse solo en loopback.

## Rutas HTTP

- `GET /api/research/{id}/math/attempts` — consulta de intentos.
- `POST /api/research/{id}/math/attempts` — `{"problemId":"NAT-ADD-COMM","strategy":"rfl"}` (prueba individual permitida).
- `POST /api/research/{id}/math/cycle` — `{"problemId":"NAT-ADD-COMM"}` (ciclo determinista).
- `POST /api/research/{id}/stages/4/approve` y `.../5/approve` — nuevos bloqueos por evidencia real.
- `GET /api/research/{id}/reports/{stage}?lang=es` y `GET /api/research/{id}/paper?lang=es` incorporan el registro matemático al informe.

El script público de regresión `tests/smoke-math-evidence.ps1` ejecuta un expediente matemático etiquetado **solo como control** y comprueba: rechazo de ejecución temprana, rechazo de datos sin evidencia, fallo de `rfl`, éxito de `omega`, referencias de intentos obligatorias e inclusión de registros en el informe final. GitHub Actions repite el proceso con Lean 4 fijado y .NET 10 en un ejecutor efímero.

No se publican claves API ni datos originales del usuario en GitHub. Los registros se almacenan en `src/ProofLab.Api/.private/research/math-attempts/` y Git los excluye.

## Siguiente fase: razonamiento real con IA

El ciclo de control se identifica expresamente como `DETERMINISTIC_BASELINE_NOT_AI`. **No utiliza Muse Spark ni constituye un descubrimiento.** Para avanzar hacia investigación autónoma tenemos que comprobar los créditos de Meta, conectar un bucle de herramientas controladas y preparar un entorno aislado para código Lean más expresivo antes de intentar conjeturas abiertas. Una demostración aceptada por Lean no prueba por sí sola originalidad ni que el enunciado formalizado represente correctamente el problema: también necesitamos revisar literatura y equivalencia matemática.

Referencia Lean 4: https://lean-lang.org/doc/reference/latest/Tactic-Proofs/Tactic-Reference
Tutorial Meta sobre llamadas a herramientas: https://dev.meta.ai/docs/cookbook/tool-function-calling

**English:** [MISSION-001-EVIDENCE.md](MISSION-001-EVIDENCE.md).
