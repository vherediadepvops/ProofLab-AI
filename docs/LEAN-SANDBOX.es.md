# Misión 1 — Política de aislamiento para Lean 4 no confiable

**2026-10-27 · DISEÑO, NO HABILITADO.** El verificador actual de los tres teoremas conocidos usa únicamente las tácticas fijas `rfl`, `omega` y `simp`; nunca envía fuente Lean generada por IA al host. El motor Docker **no estaba disponible** al revisar `docker info` en DESKTOP-ESGSLJH.

## Modelo de amenaza

Lean permite metaprogramación y operaciones de E/S si se ejecutan fuentes libres. La salida de Muse Spark, citas de papers, propuestas de terceros y repositorios descargados son **entradas no confiables**. El objetivo es limitar escape al host, lectura de secretos, red, exfiltración, agotamiento de CPU/RAM/disco, persistencia, archivos de sistema y resultados fraudulentos (`sorry`, `admit`, `axiom`, etc.).

## Contrato de ejecución propuesto (requiere validación independiente)

1. Solo una **imagen Linux Lean/mathlib** preconstruida, auditada y fijada por digest SHA-256 (no `latest`); revisar la compatibilidad exacta con Lean 4.35.0 y licencias. Descargar/compilar dependencias **fuera** de la sesión no confiable.
2. Cada intento usa un contenedor descartable; archivo de entrada montado **solo lectura**; sin carpeta completa del proyecto, sin home de Windows, sin `.git`, sin clave `MODEL_API_KEY` ni otras variables de credenciales. No exponer `docker.sock`.
3. Política Docker Linux estricta: `--network none`, `--read-only`, `--cap-drop ALL`, `--security-opt no-new-privileges`, perfil seccomp por defecto, `--pids-limit 64`, `--memory 512m`, `--memory-swap 512m`, `--cpus 1`, usuario sin privilegios, `--tmpfs /tmp:rw,nosuid,noexec,size=64m`, sin dispositivos/privileged. Revisar que todas las restricciones sean realmente soportadas por el motor.
4. Timeout del **contenedor completo**, máximo de caracteres stdout/stderr, entradas pequeñas, máximo de intentos y presupuesto. Siempre detener/eliminar el contenedor al expirar o fallar, sin eliminar artefactos de evidencia externos. No pasar argumentos de shell proporcionados por el modelo.
5. Un verificador posterior comprueba que el enunciado es **exactamente** el problema formalizado y revisado, que Lean acepta la prueba sin placeholders/axiomas nuevos y que los imports/flags son los fijados; guardar imagen, Lean, enunciado, hashes, diagnóstico y resultado. Una compilación verde con `sorry` no es prueba.
6. **Fail closed:** mientras falte imagen fijada, política probada, daemon, aislamiento reforzado o verificación humana del objetivo, el endpoint de código Lean libre continúa DESACTIVADO. No sustituirlo por ejecución host en caso de error. Para amenazas fuertes, evaluar una VM desechable con permisos separados, previa autorización.

## Pruebas de aceptación pendientes

- Verificar `docker info` con motor Linux y seccomp, imagen por digest.
- Probar una demostración válida y una inválida en contenedores sin red.
- Probar intentos de leer secreto del host, contactar Internet, escribir fuera de `/tmp`, ampliar procesos, usar RAM y esperar infinitamente; demostrar rechazo/timeout **sin daños**.
- Verificar logs reproducibles, límites de salida y que los contenedores terminan sin residuos.
- Confirmar que jamás se interpreta una táctica fallida como refutación de la conjetura.

**Situación actual:** ninguna de estas pruebas de aislamiento se ha completado. Está expresamente prohibido ejecutar código Lean generado libremente por IA en Windows. Fuentes: [Docker resource constraints](https://docs.docker.com/engine/containers/resource_constraints/), [run security options](https://docs.docker.com/reference/cli/docker/container/run), [seccomp](https://docs.docker.com/engine/security/seccomp).

**English:** [LEAN-SANDBOX.md](LEAN-SANDBOX.md).
