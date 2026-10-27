# Misión 001 — Descubrimiento matemático

**Edición de competencia:** código nuevo desarrollado el 26 de octubre de 2026 después de la inauguración oficial del hackathon Meta.
**Alcance:** primera capa de un agente investigador científico. Es un *banco de pruebas de verificación formal*, no una afirmación de haber resuelto una conjetura abierta.

## Objetivo

Dotar al futuro agente científico de un mecanismo independiente de verificación. Una propuesta no es un descubrimiento matemático solamente porque un modelo de lenguaje la considere convincente. Las afirmaciones formales deben superar el verificador de Lean 4. Determinar originalidad y fidelidad del enunciado formalizado exige además revisión externa.

## Prototipo implementado

- Tres teoremas **ya conocidos** sobre números naturales: suma con cero, conmutatividad e incremento no negativo.
- Selección de una estrategia preaprobada: `rfl`, `omega` o `simp`. Se genera el archivo Lean 4 a partir de enunciados inmutables.
- Ejecución local de Lean con límite de 25 segundos. Un éxito se informa como **VERIFIED: teorema conocido**; un fallo como **REJECTED**. Si falta Lean se informa **UNAVAILABLE**.
- Cálculo de huella SHA-256 y registro local de resultados en `.private/math/verification-attempts.jsonl` (excluido de Git).
- Interfaz web bilingüe con selección de problema, estrategia, diagnóstico e identificador del intento.

El repositorio fija `leanprover/lean4:v4.35.0` en `lean-toolchain`. La carpeta `formal/` incluye teoremas correctos y una demostración intencionadamente incorrecta; un flujo separado de GitHub Actions verifica independientemente que los primeros pasan y la segunda falla.

**Seguridad:** No ejecutar código Lean arbitrario generado por una IA o enviado por un usuario directamente en la computadora. Esta API solo admite IDs de problemas curados y tres tácticas fijas. Para verificar pruebas abiertas en el futuro se necesitará un entorno aislado y endurecido, sin acceso a secretos, red o archivos personales y con límites de recursos.

## Ejecución local

En Windows se puede instalar Lean 4 con `winget install --id Lean.Elan -e`, luego `elan default leanprover/lean4:stable` y `elan toolchain install leanprover/lean4:stable`. Versión probada en el equipo el 26 de octubre: Lean **4.35.0**. El backend busca Lean por PATH, carpeta elan o alias de winget. El repositorio ya fija la versión 4.35.0 para reproducir los ejemplos.

Los comandos .NET y Angular anteriores permanecen vigentes. Rutas HTTP nuevas:

- `GET /api/math/problems` — catálogo de problemas conocidos con procedencia.
- `GET /api/math/status` — disponibilidad del verificador y estado real del desarrollo.
- `POST /api/math/verify` — `{"problemId":"NAT-ADD-COMM","strategy":"omega"}`.
- `POST /api/math/plan` — planificación de estrategias con Muse Spark, bloqueada por créditos; sus sugerencias no verificadas nunca se ejecutan automáticamente.

Los estados posibles son `VERIFIED`, `REJECTED`, `UNAVAILABLE` y `TIMED_OUT`. En esta etapa `isNewDiscovery` siempre es falso.

## Criterios de aceptación

1. Una demostración correcta de un teorema conocido pasa por un proceso Lean real.
2. Una estrategia equivocada es rechazada por Lean, sin confundirlo con un contraejemplo matemático.
3. Código arbitrario, `sorry`, `admit` y problemas inexistentes se bloquean *antes* de ejecutarse.
4. El registro conserva identificador de intento, hora, huella y resultado.
5. Las pruebas formales locales no gastan créditos de Meta Model API.

## De la Misión 1 a la Misión 4

1. **Mathematical Discovery:** verificador estable; luego elegir conjeturas abiertas documentadas (por ejemplo [Formal Conjectures](https://github.com/google-deepmind/formal-conjectures)), revisar sus enunciados, aislar ejecución e integrar Muse Spark para proponer estrategias de investigación.
2. **Autonomous Physics Discovery:** reutilizar el registro de evidencia y el verificador de hipótesis con videos reales y SAM 3.1.
3. **Historical & Big Data Discovery:** datos científicos filtrados por fecha, predicciones ciegas y evaluación independiente.
4. **Autonomous Research Director:** elegir problemas originales valiosos, pedir mediciones al ser humano solo cuando sean necesarias y retomar investigaciones guardadas.

**No afirmamos** haber demostrado conjeturas abiertas, generado matemáticas originales, entrenado un gran modelo propio ni verificado la integración real con Meta en esta etapa.

**English:** [MISSION-001-MATH.md](MISSION-001-MATH.md).
