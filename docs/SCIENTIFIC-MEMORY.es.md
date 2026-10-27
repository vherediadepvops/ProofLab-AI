# ProofLab AI — Memoria científica persistente (Misión 1)

**Implementación local del 27/10/2026 — ES.** Guarda experiencias verificadas sin entrenar ningún modelo. El motor continúa **sin Muse Spark**, sin llamadas pagadas y sin ejecutar código Lean arbitrario.

## Objetivo

Recordar errores y estrategias válidas de Lean 4 entre **expedientes de investigación diferentes**, para ahorrar intentos sin reemplazar la verificación independiente. Un rechazo de una táctica nunca refuta un teorema. El primer benchmark es `NAT-ADD-COMM`, un **teorema conocido**; Goldbach sigue abierto y no recibe recomendaciones Lean del benchmark.

## Arquitectura

- `ScientificMemoryStore` usa `Microsoft.Data.Sqlite 10.0.14` y crea `src/ProofLab.Api/.private/research/scientific-experience.sqlite` dentro de la carpeta competitiva. El archivo es privado, `.private/` está excluido de Git y no se conecta con SQLite ni MySQL de otros proyectos.
- La tabla `lean_experience` indexa exclusivamente resultados reales que emite el verificador controlado, asociados a `attempt_id`, `research_id`, `problem_id`, SHA-256 del objetivo y del archivo Lean, táctica, código de resultado, versión de Lean, fecha UTC.
- Los registros originales permanecen en `math-attempts/*.jsonl`. La base es un índice derivado local, **no un registro antimanipulación ni una certificación científica**.
- `MemoryGuidedMathFlow` utiliza el catálogo de teoremas conocidos, recupera solo coincidencias de `problemId + SHA-256 exacto del enunciado + leanprover/lean4:v4.35.0`. Ignora estrategias de objetivos ajenos y no acepta fuente Lean arbitraria.
- Cada ciclo tiene límite de dos intentos Lean; la investigación admite hasta tres ciclos de memoria. Su decisión, fuente recordada, nuevos IDs y conclusiones se guardan en `memory-cycle-traces/*.jsonl`, también privado. Los informes de etapas 4–5 y el documento consolidado incluyen el historial **ES/EN**.

## Esquema SQL versionado en el repositorio principal

- **Código SQL publicado:** [`src/ProofLab.Api/Database/001_scientific_experience.sql`](../src/ProofLab.Api/Database/001_scientific_experience.sql). Contiene únicamente la definición de la tabla `lean_experience` y su índice; no contiene experiencias, identificadores de usuarios ni secretos.
- El backend carga **ese mismo archivo como recurso incrustado** al iniciar, mediante `ScientificMemoryStore`; las instrucciones `CREATE ... IF NOT EXISTS` permiten iniciar una base nueva sin borrar la existente. Las modificaciones futuras del esquema deberán añadirse como migraciones versionadas y probadas, sin sobrescribir registros históricos.
- **Datos reales:** `src/ProofLab.Api/.private/research/scientific-experience.sqlite` permanece exclusivamente local y excluido de Git, junto a los registros JSONL. Subir el esquema `.sql` **no** equivale a publicar la base `.sqlite`.
- Las versiones SQL iniciales no incluyen datos de prueba ni respaldos. Hacer copias de seguridad privadas y protegidas requiere un procedimiento separado.

## Comportamiento validable

1. Investigación A (memoria vacía): consulta `COLD_START`, intenta `rfl`, registra el rechazo real y reintenta `omega` tras leer el error; Lean verifica `omega`.
2. Investigación B: la memoria encuentra el `attempt_id` de A para el **mismo objetivo exacto**, recomienda `omega` y la ejecuta nuevamente con Lean. Se genera un ID y una evidencia **nuevos**, sin repetir innecesariamente `rfl`.
3. Otro teorema `NAT-ADD-ZERO` mantiene `COLD_START` si carece de experiencias propias. `GOLDBACH-STRONG` no está en el catálogo de objetivos Lean conocidos y se rechaza.
4. Reiniciar la API y repetir la consulta conserva la recomendación, los recuentos y los IDs, porque SQLite es persistente.

## API y restricciones

- `GET /api/math/memory/{problemId}`: recuerda la táctica aconsejada y cita ID de evidencia anterior, sin ejecutar.
- `POST /api/research/{id}/math/memory-cycle` con `{"problemId":"NAT-ADD-COMM"}`: permite el ensayo solo durante etapas 4/5 de un expediente matemático.
- `GET /api/research/{id}/math/memory-cycles`: lee el historial. Las etapas permanecen sujetas a revisión humana declarada y a IDs Lean reales.

La política restringida `rfl`/`omega`/`simp` y el máximo de 24 intentos Lean por expediente continúan. Los fallos de compilador, tiempos agotados y indisponibilidad no constituyen demostración. Nunca se afirma originalidad solo por un `VERIFIED` de teorema conocido.

## Seguridad y limitaciones

El índice SQLite y los ledgers JSONL están protegidos por la ubicación privada y por validaciones de código, pero **no incorporan firma digital o atestación externa**. La memoria no reentrena pesos, no razona de modo autónomo ni garantiza transferibilidad a problemas nuevos. Los formularios completos no certifican una conclusión. La UI específica de memoria y el aislamiento de Lean libre siguen pendientes. Solo operar en `DESKTOP-ESGSLJH`.

**English:** [SCIENTIFIC-MEMORY.md](SCIENTIFIC-MEMORY.md).
