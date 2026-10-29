# Misión 1 — Validación del flujo científico SIN Muse Spark

> **Permisos vigentes (29/10/2026):** las referencias a pagos apagados en este documento describen el ensayo o estado histórico de esa instancia. No son instrucciones para desactivar la API del propietario ni las ramas nuevas. Aplicar la [política actual de autorización y pruebas](META-API-CONNECTION.es.md).

**Fecha:** 27/10/2026. **Estado:** ensayo determinista con herramientas reales, no agente de IA.
**No se habilitan modelos Meta ni llamadas facturables.** Conservar `Meta:AllowPaidRequests=false`.

## Objetivo y separación de resultados

Validar el futuro circuito `investigar → formular hipótesis → elegir acción → validar parámetros → ejecutar → leer resultado/error → reintentar → registrar → informar` sin depender de Muse Spark. Las decisiones del ensayo están fijadas en código y **no son razonamiento autónomo**.

**Problema abierto:** conjetura fuerte de Goldbach, aún sin demostración en ProofLab y sin objetivo Lean revisado.
**Herramientas seguras:** cálculo finito de Goldbach en C# y verificación Lean de un **teorema distinto, conocido** (conmutatividad de suma natural).

## Condiciones

- Crear expediente `Mathematics` y completar/revisar etapas 1, 2 y 3 de manera independiente. El ensayo solo acepta expedientes con etapa activa 4 o 5.
- `POST /api/research/{id}/math/offline-cycle` sin parámetros ni scripts de usuario. No ejecuta fuente Lean libre ni códigos externos.
- `GET /api/research/{id}/math/offline-events`: historial local aditivo JSONL de propuestas validadas, resultados, diagnósticos, IDs y SHA-256.
- **No se aprueban informes automáticamente**; siguen obligatorias la revisión humana declarada, campos completos y cita de ID Lean real para cerrar etapas 4 y 5.
- Límite: **3 acciones de herramientas/ciclo** y hasta **3 ciclos por expediente**; se respetan los límites de los verificadores subyacentes y no hay fallback de ejecución libre.

## Secuencia fija de prueba

1. **Hipótesis finita preregistrada:** todos los 49 pares de 4 a 100 disponen de un testigo de suma prima. Enviar acción JSON tipada `CHECK_GOLDBACH_FINITE`, con `evenLimit=100`. Guardar resultado computacional (no prueba formal), testigos e ID/hash.
2. **Control Lean débil:** `VERIFY_KNOWN_BENCHMARK` en `NAT-ADD-COMM` con táctica `rfl`. El diagnóstico auténtico debería ser `REJECTED`; no se interpreta como refutación del teorema.
3. **Reintento condicional:** únicamente si Lean respondió `REJECTED`, intentar `omega` en ese **mismo teorema conocido**. Esperado `VERIFIED`. Ante `UNAVAILABLE`/`TIMED_OUT` o un resultado inesperado, no reintentar.
4. Concluir `OPEN_CONJECTURE_INCONCLUSIVE` para Goldbach en todos los casos. No afirmar descubrimiento, revisión de pares externos ni prueba Lean de Goldbach.
5. Agregar bitácora de decisiones a reportes 4–5 e informe consolidado **ES/EN**, junto a los intentos Lean y resultados numéricos de los otros ledgers.

Cada evento almacena `cycleId`, secuencia, acción JSON de origen **SCRIPTED**, resultado de herramienta real, ID de evidencia, SHA-256, fecha y explicaciones ES/EN. Los fallos de Lean permanecen en el ledger original incluso cuando el flujo se interrumpe; el ledger de eventos guarda cada paso completado (no hay atomicidad transaccional entre ledgers).

## Pruebas de aceptación

- Antes de etapa 4: HTTP 400 (bloqueo real).
- Después de aprobar las primeras 3 etapas en una **fixture de CI etiquetada como simulación documental**, ejecutar 3 herramientas: `NO_COUNTEREXAMPLE_IN_FINITE_RANGE`, `REJECTED`, `VERIFIED`; cotejar IDs con sus registros.
- El expediente sigue en **etapa 4** después del ensayo, sin autoaprobación.
- Etapas 4–5 todavía rechazan aprobación sin referencia a intento Lean registrado; las fixtures CI inyectan documentación de prueba explícitamente no científica.
- Informes Markdown ES/EN incluyen el `cycleId` y explican por qué Goldbach sigue abierto.
- CI debe validar .NET, Angular y Lean 4 real sin utilizar Muse Spark ni llamar Meta.

**Límites actuales:** no hay selección de objetivos por IA, lectura autónoma de artículos, formulación Lean de Goldbach ni sandbox para código Lean libre. Validar este flujo **no concluye Misión 1**. La interfaz del ensayo permanece pendiente; la API y la CI son las superficies de prueba.

**English:** [MISSION-001-OFFLINE-REHEARSAL.md](MISSION-001-OFFLINE-REHEARSAL.md).
