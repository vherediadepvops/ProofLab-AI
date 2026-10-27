# Misión 1 — Contrato de herramientas para el futuro agente Muse Spark

**Estado 2026-10-27: preparado y probado como validador local. NO existe todavía un bucle autónomo conectado al modelo.** La verificación de créditos y la inferencia real son requisitos previos.

## Bucle supervisado propuesto

1. Recuperar expediente y revisión de la literatura; declarar el estatus actual del problema.
2. Muse Spark propone una pregunta precisa, un subobjetivo y **una acción JSON** de la lista cerrada.
3. Validar esquema, campos, tipos, límites y estado de la investigación. Rechazar cualquier campo extra, código arbitrario o comando.
4. Invocar herramienta independiente solo cuando se autorice la acción: (a) observación Goldbach finita o (b) Lean de un teorema conocido con táctica preaprobada.
5. Guardar propuesta, decisión, intento, datos, errores, hashes, versión y hora; no borrar resultados negativos.
6. Devolver los resultados a Muse para que corrija su hipótesis o indique inconclusión. Repetir como máximo **3 llamadas a herramientas** por ciclo.
7. Generar los informes de cinco etapas en ES/EN con IDs reales. **No autoaprobar** informes ni inferir novedad a partir de una salida de modelo.

## Acciones permitidas (validador actual)

`CHECK_GOLDBACH_FINITE`:
```json
{"tool":"CHECK_GOLDBACH_FINITE","conjectureId":"GOLDBACH-STRONG","evenLimit":100}
```
Limita a pares 4–2000; cálculo C# determinista, no prueba Lean.

`VERIFY_KNOWN_BENCHMARK`:
```json
{"tool":"VERIFY_KNOWN_BENCHMARK","problemId":"NAT-ADD-COMM","strategy":"omega"}
```
Únicamente teoremas fijos y `rfl`, `omega` o `simp`; no se acepta `leanSource`.

`ResearchAgentToolPolicy.ValidateProposal` rechaza comandos, campos inesperados, límites inválidos, objetivos ajenos al catálogo y estrategias no aprobadas. `GET /api/math/agent/capabilities` revela el estado **SCHEMA_VALIDATION_ONLY_NO_MODEL_INFERENCE**.

## Pendiente / puertas

- Confirmar el crédito Meta y obtener autorización **antes** de habilitar peticiones facturables; mantener `Meta:AllowPaidRequests=false`.
- Construir orquestador de decisiones e integrar salida real de Muse Spark, con coste máximo, límite de intentos y trazabilidad del prompt/versionado.
- Validar un entorno desechable para Lean libre; mientras no exista, no permitirlo.
- Revisar una formalización fiel de Goldbach, distinguir prueba finita, lemmas conocidos y conjetura universal.
- Probar evaluación ciega con un baseline e inspección humana del informe.

**English:** [MISSION-001-AGENT.md](MISSION-001-AGENT.md).