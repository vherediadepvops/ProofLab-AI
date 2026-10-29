# Reglas de trabajo — ProofLab AI

## Meta: autorización persistente del propietario (29/10/2026)

Estas instrucciones aplican a `main`, todas las ramas feature y worktrees de este proyecto. Consultar [permisos y pruebas Meta](docs/META-API-CONNECTION.es.md) ([English](docs/META-API-CONNECTION.md)).

- Mantener `Meta:AllowPaidRequests`, `Meta:CreditsConfirmedByOwner` y `Meta:OwnerApprovedPaidRequests` en `true`, conforme a la autorización del propietario. Crear una rama, desarrollar o terminar pruebas no revoca esa autorización.
- No deshabilitar el servicio del propietario ni introducir restricciones de gasto/cupo adicionales por iniciativa del agente. Los envíos manuales mantienen `Meta:UnlimitedManualRequests=true`.
- Antes de consumir Meta en pruebas del agente, solicitar **cuántas pruebas reales autoriza** el propietario; explicar las llamadas por prueba y el costo estimado disponible. Respetar las autorizaciones ya dadas dentro de su alcance; registrar consumo y pedir ampliación al agotarlo. Finalizar una tanda no apaga los permisos del usuario.
- Las pruebas sin proveedor usan mocks o configuración aislada; nunca cambian las banderas del servicio compartido ni las variables globales. Los datos y contadores del propietario se conservan.
- No confundir estos permisos de la aplicación con las políticas de herramientas/OpenAI. Si una herramienta rechaza una acción, explicar cuál y por qué; no eludir el rechazo ni atribuir a OpenAI una regla de mantener Meta apagado sin una fuente concreta.
- Preservar secretos, validación de credenciales, consentimiento de cada operación, integridad de resultados y límites reales del proveedor. No publicar tokens ni datos privados.
- No iniciar, reintentar ni esperar CI por rutina; usar verificaciones locales pertinentes.
- Antes de modificar, comprobar rama/HEAD y cambios pendientes. Usar procesos y directorios propios; no detener ni reconfigurar otros chats.

English: keep the owner's three Meta permission flags enabled across branches. Ask for an authorized number of agent-initiated live tests, track consumption and stop the agent's batch when exhausted; do not disable the owner's service. Offline tests use isolated configuration or mocks. Existing tool/platform restrictions still apply.
