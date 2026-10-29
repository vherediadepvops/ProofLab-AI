# ProofLab AI — permisos de Meta y pruebas autorizadas

**Vigente: 29/10/2026.** El propietario autorizó mantener Meta habilitado en este proyecto. Esta regla aplica a `main`, ramas feature y worktrees; sustituye las instrucciones antiguas de mantener el servicio principal apagado.

## Configuración del proyecto

`src/ProofLab.Api/appsettings.json` conserva:

```json
{
  "Meta": {
    "UnlimitedManualRequests": true,
    "AllowPaidRequests": true,
    "CreditsConfirmedByOwner": true,
    "OwnerApprovedPaidRequests": true
  }
}
```

Son permisos declarados por el propietario, no una consulta automática al saldo de Meta. No incluyen credenciales ni generan llamadas por sí solos. Una credencial válida y la admisión explícita de cada operación siguen siendo necesarias.

## Regla para todos los agentes y ramas

- Crear una feature, compilar, probar sin red, publicar o terminar una tarea no revoca estos permisos. No cambiar los tres valores a `false`, ni añadir variables de entorno que los deshabiliten en el servicio del propietario, por rutina.
- Antes de pruebas reales iniciadas por el agente, solicitar al propietario **cuántas pruebas autoriza**, explicar cuántas llamadas puede incluir cada prueba (Spark, SAM y lotes) y el costo estimado si está disponible. Una autorización vigente no se vuelve a pedir para cada llamada dentro del mismo alcance.
- Registrar pruebas y llamadas usadas; al consumir lo autorizado, detener las pruebas del agente y solicitar una ampliación. No apagar la API del propietario ni reducir su cupo manual.
- Los clics manuales del propietario siguen el flujo de costo y confirmación de la interfaz, con `UnlimitedManualRequests=true`. El contador compartido no debe limitar esos envíos manuales.
- Usar dobles de prueba/transporte simulado para pruebas sin proveedor. Si una prueba necesita permisos apagados, limitar ese valor a su configuración de prueba aislada; no sobrescribir `appsettings.json`, variables globales ni servicios compartidos.
- La habilitación de este proyecto no modifica políticas de OpenAI ni elimina límites reales del proveedor. Ante un rechazo de herramienta, informar la acción y el motivo exactos; no atribuirlo sin evidencia a una obligación de desactivar Meta y no intentar eludirlo.

## Token y estado

En la interfaz actual, registrar el token en **Configuración**; permanece en memoria de esa sesión. Los envíos manuales pueden usarlo sin una clave cargada en el entorno del servidor. La alternativa para operaciones de backend es `MODEL_API_KEY` en su proceso. Nunca guardar ni publicar secretos en Git, SQLite, logs o documentación.

Comprobación local sin inferencia:

```powershell
Invoke-RestMethod http://127.0.0.1:5391/api/meta/readiness
```

Comprobar `creditsConfirmedByOwner`, `ownerApprovedPaidRequests`, `paidRequestsSwitchEnabled`, `manualTokenCallsPermitted` y `manualRequestsUnlimited`. `liveCallsPermitted=false` puede indicar ausencia de clave de servidor o cupo no manual agotado; por sí solo no significa que los envíos manuales con token estén deshabilitados.

Las variables `Meta__AllowPaidRequests`, `Meta__CreditsConfirmedByOwner` y `Meta__OwnerApprovedPaidRequests` pueden prevalecer sobre el archivo JSON. Revisar esa precedencia si una instancia no coincide con el repositorio. No reiniciar servicios ajenos ni sus contadores para arreglar una instancia de prueba.

## Alcance de las verificaciones

El estado local no certifica autenticación con Meta, saldo disponible ni resultado científico. Esta actualización de permisos se verifica sin llamar al proveedor. Los registros de inferencias anteriores conservan su alcance e historial.

[Reglas para agentes](../AGENTS.md) · [English](META-API-CONNECTION.md)
