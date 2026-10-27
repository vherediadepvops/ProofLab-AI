# ProofLab AI — Preparación segura de Meta Model API (Muse Spark 1.3 y SAM 3.1)

**Estado:** integración de código preparada y probada SIN solicitudes reales; **Meta continúa inactivo** hasta verificar créditos y autorización explícita. Fecha: 27/10/2026.

## 1. Conseguir el token de Meta

1. Entra tú mismo en **https://dev.meta.ai/** e inicia sesión con la cuenta de Meta Model API del hackathon.
2. Abre **API keys** desde el panel de Model API. Si ya creaste una clave válida, puedes reutilizarla; de lo contrario pulsa **Create API key**, escribe por ejemplo `ProofLab-AI-Hackathon` y créala.
3. **Copia el valor secreto una sola vez** y guárdalo de forma segura. No lo pongas en ChatGPT, capturas de pantalla, código, Angular, GitHub, logs, README, Slack ni memoria compartida.
4. **La misma clave** autentica Muse Spark y SAM 3.1. Variable esperada por el backend: `MODEL_API_KEY`. Meta utiliza el encabezado `Authorization: Bearer ...`.

Guías oficiales: https://dev.meta.ai/docs/authentication · https://dev.meta.ai/docs/quickstart · https://dev.meta.ai/help/api-keys/create-api-key .

## 2. Dónde poner el token en el único equipo autorizado

**Equipo:** `DESKTOP-ESGSLJH` · **Código:** `C:\PROOFLAB-AI-HACKATHON` · **API:** `http://127.0.0.1:5391`.

**Opción temporal recomendada (solo en una consola PowerShell TUYA):** el siguiente código pide el token con entrada oculta y lo deja en `MODEL_API_KEY` **solo en esa terminal y sus procesos hijos**. No hay que sustituir ningún literal por el secreto en el comando (evita historial de PowerShell).

```powershell
$claveSegura = Read-Host 'Pega MODEL_API_KEY' -AsSecureString
$puntero = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($claveSegura)
try {
    $env:MODEL_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($puntero)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($puntero)
    Remove-Variable claveSegura, puntero -ErrorAction SilentlyContinue
}
# SOLO confirma presencia, no muestra el valor:
[bool]$env:MODEL_API_KEY
```

**Importante:** la API ya se ejecuta en puerto 5391. Para que **ese proceso** herede la clave, se debe reiniciar **solo el backend de ProofLab AI** desde esa misma terminal, después de verificar su PID y ejecutable. No arranques otra instancia que compita por el puerto. Configurar la clave en una terminal **no cambia** un backend que ya está arrancado.

**Alternativa persistente (menos privada):** Windows → «Editar variables de entorno de tu cuenta» → «Variables de usuario» → «Nueva»: nombre `MODEL_API_KEY`, valor el secreto. Se aplicará a los **procesos nuevos** del mismo usuario tras reiniciar el backend. Esta alternativa guarda el valor en la configuración de usuario de Windows; no es un almacén cifrado de secretos. Evita variables de sistema y nunca envíes el valor por el chat.

## 3. Comprobar créditos y permiso ANTES de llamar

Abre **https://dev.meta.ai/billing** con la sesión Meta, completa personalmente la confirmación de identidad si aparece y verifica el **saldo promocional realmente aplicado a tu equipo**, su vencimiento y si hay cargos al superar el crédito. La promoción de US$150 mencionada durante el hackathon **no está verificada en ProofLab**. Visita también Usage/Spend limits si están disponibles; las alertas de gasto por correo **no son límites duros**.

La API exige **cuatro controles simultáneos**:
- `MODEL_API_KEY` presente en el proceso del backend.
- `Meta:CreditsConfirmedByOwner=true` únicamente tras tu verificación real de Billing.
- `Meta:OwnerApprovedPaidRequests=true` únicamente tras tu autorización explícita.
- `Meta:AllowPaidRequests=true` como último interruptor.

**Por defecto los tres interruptores son `false`.** La activación se haría mediante variables de entorno del backend `Meta__CreditsConfirmedByOwner`, `Meta__OwnerApprovedPaidRequests`, `Meta__AllowPaidRequests`; **no modifiques Git ni enciendas nada hasta verificar saldo y autorizar los cargos**. El código no puede comprobar automáticamente tu crédito en Meta: son confirmaciones declaradas por el propietario, no una conexión con Billing.

Además, cada proceso de la API tiene **un máximo inicial de dos solicitudes reales**, compartido por Muse y SAM. Es un límite de peticiones, **no un límite monetario de la cuenta**. Reiniciar el proceso lo reinicia; consulta el Usage de Meta para el costo real.

## 4. Consultar el estado SIN gastar créditos

En PowerShell, sin introducir una clave en la URL:

```powershell
Invoke-RestMethod http://127.0.0.1:5391/api/meta/readiness
```

Muestra **booleanos**, modelo y cupo restante, nunca el token. También aparecerá el estado en la vista de Goldbach de la web `http://127.0.0.1:4300/`. Cuando todos los requisitos estén completos, se habilitará «Preparar con Muse Spark (puede gastar créditos Meta)» y cada solicitud requerirá confirmación.

## 5. Qué hace cada modelo

- **Muse Spark 1.3:** `POST https://api.meta.ai/v1/chat/completions`. ProofLab prepara propuestas de borradores de las cinco etapas, con esquema JSON; **los informes siguen sin aprobarse automáticamente**. Las etapas 4–5 conservan los IDs/hashes de evidencia del servidor. El texto de Muse permanece **NO VERIFICADO** hasta revisión.
- **SAM 3.1:** `POST https://api.meta.ai/v1/responses` para imágenes/video. El adaptador actual sólo devuelve la respuesta bruta; **aún no existe decodificación validada de máscaras ni seguimiento para la Misión 2**. No usar datos personales ni videos sensibles sin decisión expresa.
- Ambos endpoints leen el mismo `MODEL_API_KEY`, exclusivamente en C# del backend. El frontend **jamás** recibe ni guarda el secreto.

Documentación actual: https://dev.meta.ai/docs/protocols/chat-completions · https://dev.meta.ai/docs/sam/overview · https://dev.meta.ai/docs/pricing-rate-limits .

## 6. Qué fue verificado y qué NO

- **Verificado sin Meta:** compilación, pruebas unitarias de los cuatro interruptores, límite de dos solicitudes concurrentes, estado local de solo lectura y rechazo HTTP 409 de peticiones pagadas con interruptores apagados. CI no debe llevar `MODEL_API_KEY`.
- **No verificado:** autenticación real con token, permisos reales de tu equipo, crédito promocional disponible, respuestas Muse Spark/SAM de pago, costo por uso y generación científica autónoma. No declarar integración Meta operativa antes de esas pruebas.
- **Regla científica:** ninguna inferencia Muse es una prueba Lean. Lean sólo verifica objetivos conocidos y tácticas curadas hasta que se valide un sandbox independiente.

**English:** [META-API-CONNECTION.md](META-API-CONNECTION.md).
