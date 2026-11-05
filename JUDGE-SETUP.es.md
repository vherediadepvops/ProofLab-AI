# Instalación para el jurado — ProofLab AI (Windows y Linux)

**Meta Global AI Developer Hackathon 2026 · Indie Developer · Push the Frontier**

[English](JUDGE-SETUP.md) · [README del proyecto](README.es.md) · [Datos públicos de 25 casos](demo-data/README.es.md)

Esta guía explica cómo **clonar, compilar y ejecutar ProofLab AI localmente** y revisar los resultados publicados del péndulo **sin clave Meta ni llamadas pagadas**. Es una instalación nueva de desarrollo/evaluación, **no** un despliegue automático en producción ni una restauración de la base privada de AWS.

## 1. Requisitos

| Dependencia | Para qué se usa | Instalación |
| --- | --- | --- |
| Git | Clonar el código | [git-scm.com](https://git-scm.com/downloads) |
| **SDK .NET 10** (no solo runtime) | API ASP.NET Core y motor científico | [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) |
| **Node.js 24 LTS y npm** | Angular 20, worker de video y generador SQLite | [nodejs.org](https://nodejs.org/es/download/) |
| **FFmpeg y FFprobe** | Verificación de videos, miniaturas y preparación | [ffmpeg.org](https://ffmpeg.org/download.html) |
| Navegador moderno | Interfaz local; Chrome solo para pruebas opcionales ChromeHeadless | Edge / Chrome |

**Detalle importante de FFmpeg:** el código de la vista previa utiliza expresamente el codificador `libopenh264`. Hay que confirmar que la distribución instalada lo incluye; tener `ffmpeg` y `ffprobe` no garantiza todas las funciones de video. [Documentación del codificador](https://ffmpeg.org/ffmpeg-codecs.html#libopenh264).

**Opcional:** Lean 4 se requiere solo para el verificador matemático separado, no para el flujo básico del péndulo ni para consultar los 25 casos. Consulta `lean-toolchain`.

## 2. Windows 11 — instalación limpia

Instala Git, SDK .NET 10, Node.js 24 LTS y una distribución de FFmpeg con `ffmpeg.exe`, `ffprobe.exe` y `libopenh264`. Abre otra ventana de PowerShell para actualizar `PATH`. Comprueba:

```powershell
git --version
dotnet --version
node --version
npm.cmd --version
ffmpeg -version
ffprobe -version
ffmpeg -hide_banner -encoders | Select-String "libopenh264"
```

`dotnet --version` debe indicar `10.x` y `node --version` la rama probada `v24.x`. El último comando debe mostrar `libopenh264` para la generación de vistas previas.

Clona en **una carpeta vacía**, no encima de otra instalación:

```powershell
git clone https://github.com/vherediadepvops/ProofLab-AI.git
Set-Location .\ProofLab-AI
git rev-parse --short HEAD
```

**Primero instala dependencias Angular y después compila .NET**, porque la API genera un worker JavaScript desde los archivos de la interfaz:

```powershell
Push-Location .\ui
npm.cmd ci
Pop-Location
dotnet restore .\ProofLabCompetition.slnx
dotnet build .\src\ProofLab.Api\ProofLab.Api.csproj --no-restore
```

Comprueba que estén libres los puertos **5391 (API) y 4300 (web)**:

```powershell
Get-NetTCPConnection -State Listen -LocalPort 5391,4300 -ErrorAction SilentlyContinue
```

Si están ocupados, **no finalices procesos ajenos**. Usa un equipo limpio o libera servicios propios. La aplicación permite solo ciertos orígenes web locales; cambiar el puerto Angular sin adaptar los orígenes puede provocar errores de CORS.

Abre **terminal PowerShell 1** en la raíz del repositorio clonado y ejecuta:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\src\ProofLab.Api --no-launch-profile --urls http://127.0.0.1:5391
```

Abre **terminal PowerShell 2** en la raíz del repositorio clonado y ejecuta:

```powershell
Set-Location .\ui
npm.cmd start -- --host 127.0.0.1 --port 4300
```

Abre **[http://127.0.0.1:4300/](http://127.0.0.1:4300/)**. Mantén abiertas las dos terminales mientras uses la aplicación. Abre cada terminal en la raíz del repositorio clonado antes de ejecutar los comandos.

## 3. Linux — desarrollo local en Ubuntu 24.04

Este procedimiento usa **dos terminales locales**; no es el despliegue especial de Lightsail/Nginx. En Ubuntu 24.04 instala Git, FFmpeg y SDK .NET 10 con los paquetes de la distribución. Instala **Node.js 24 LTS** según las [instrucciones oficiales](https://nodejs.org/es/download/) (no presupongas que el paquete `nodejs` predeterminado de Ubuntu ya es v24):

```bash
sudo apt-get update
sudo apt-get install -y git ffmpeg dotnet-sdk-10.0
git --version
dotnet --version
node --version
npm --version
ffmpeg -version
ffprobe -version
ffmpeg -hide_banner -encoders | grep libopenh264
```

La instalación del paquete `dotnet-sdk-10.0` en Ubuntu 24.04 está documentada por [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install). Si falta `libopenh264`, utiliza una distribución confiable de FFmpeg que lo incluya antes de probar vistas previas.

```bash
git clone https://github.com/vherediadepvops/ProofLab-AI.git
cd ProofLab-AI
npm --prefix ui ci
dotnet restore ProofLabCompetition.slnx
dotnet build src/ProofLab.Api/ProofLab.Api.csproj --no-restore
```

En **terminal 1** (raíz del repositorio):

```bash
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/ProofLab.Api --no-launch-profile --urls http://127.0.0.1:5391
```

En **terminal 2** (raíz del repositorio):

```bash
cd ui
npm run start -- --host 127.0.0.1 --port 4300
```

Abre [http://127.0.0.1:4300/](http://127.0.0.1:4300/) en un navegador del **mismo equipo**. Los comandos Linux derivan del código y de documentación oficial; la instalación limpia verificada en esta entrega se hizo en **Windows 11**, no en una máquina Linux recién preparada.

## 4. Comprobaciones sin enviar solicitudes a Meta

Con ambos servicios activos, abre **una tercera terminal**.

**Windows PowerShell:**

```powershell
Invoke-RestMethod http://127.0.0.1:5391/api/health
Invoke-RestMethod http://127.0.0.1:5391/api/meta/readiness
Invoke-RestMethod -Uri http://127.0.0.1:5391/api/science/predict -Method Post -ContentType "application/json" -Body '{"kind":"Pendulum","lengthMeters":0.5}'
```

**Linux Bash:**

```bash
curl -fsS http://127.0.0.1:5391/api/health
curl -fsS http://127.0.0.1:5391/api/meta/readiness
curl -fsS -X POST http://127.0.0.1:5391/api/science/predict \
  -H 'Content-Type: application/json' \
  -d '{"kind":"Pendulum","lengthMeters":0.5}'
```

Resultado esperado: salud **HTTP 200** y predicción matemática local del péndulo. La consulta de disponibilidad Meta solo informa permisos y presencia de clave, sin devolver el secreto. **Estas comprobaciones no llaman al proveedor Meta.**

La interfaz permite subir videos compatibles y guardar su original en la instalación local; esto es distinto del botón **Procesar/Analizar**, que puede iniciar operaciones **de pago** con Muse Spark/SAM. Sin una clave propia válida no se puede repetir de extremo a extremo el análisis anterior con los modelos reales.

## 5. Generar y consultar la SQLite pública de los 25 casos

El paquete contiene **25 comparaciones guardadas** (18 dentro de banda práctica, 6 fuera, 1 inconclusa) a partir de **ocho videos previamente grabados**. No son 25 ensayos físicos independientes ni una medida certificada de exactitud.

Desde la raíz del repositorio, tanto en **Windows como Linux**, con Node.js 24+:

```sh
node demo-data/build-demo-db.mjs --verify
node demo-data/build-demo-db.mjs prooflab-demo.sqlite
```

El generador no sobrescribe archivos existentes. Abre `prooflab-demo.sqlite` con un visor SQLite y ejecuta:

```sql
SELECT case_id, baseline_length_m, followup_length_m,
       measured_t1_s, predicted_t2_s, measured_t2_s, recorded_verdict
FROM pendulum_cases
ORDER BY case_id;
```

Deben aparecer **25** filas, incluido un caso inconcluso sin período medido válido del segundo video. Otras tablas: `public_recordings` (8 URLs y hashes SHA-256) y `dataset_metadata`.

**Hay dos bases distintas:** la SQLite generada es **un catálogo independiente de consulta**, no la base privada operativa de ProofLab. No añade 25 sesiones al historial de la interfaz ni permite recuperarlas mediante códigos; tampoco incorpora las máscaras SAM ni recibos brutos de Meta. La aplicación crea su almacenamiento SQLite privado por separado. Los videos autorizados están enlazados desde la [galería pública](https://prooflab-ai.com/), sin subirlos a Git. [Guía de datos](demo-data/README.es.md).

## 6. Pruebas locales opcionales

Si deseas revisar la implementación, estas pruebas de software **no requieren llamadas pagadas a Meta**:

```sh
dotnet test tests/ProofLab.Tests/ProofLab.Tests.csproj
dotnet test tests/ProofLab.Workflow.Tests/ProofLab.Workflow.Tests.csproj
```

Para Angular, con Google Chrome instalado, desde `ui/`:

```sh
npm test -- --watch=false --browsers=ChromeHeadless
```

**Verificación realizada (4 de noviembre de 2026):** una copia limpia de `main` en Windows 11 compiló frontend/backend, inició ambos servidores, comprobó salud/predicción, subió un clip sintético H.264, cotejó el SHA-256 y validó parámetros físicos; pasaron **762 pruebas .NET** y **552 Angular**. Es una **validación de software**, no inferencias nuevas de Meta ni auditoría de exactitud científica.

## 7. Resolución de problemas y seguridad

- **No aparece `dotnet` o falla compilación:** instala el **SDK 10** y revisa `dotnet --version`. Ejecuta `npm ci` dentro de `ui` *antes* de compilar la API, ya que el worker usa las dependencias Angular.
- **`FFmpeg unavailable` / no hay vista previa:** comprueba ambos ejecutables en `PATH` y el codificador `libopenh264` en `ffmpeg -hide_banner -encoders`. Tener otro codificador como `libx264` no sustituye este requisito del código actual.
- **No conecta web con API / CORS:** usa los puertos locales documentados **4300/5391** y abre `127.0.0.1` o `localhost`. Otros orígenes o un proxy requieren configuración compatible.
- **No funciona el análisis Meta:** es esperado si no dispones de clave propia válida y permisos reales del proveedor. GitHub no contiene token compartido, créditos ni copia de datos privados de AWS.
- **Llamadas pagadas:** compilar, probar sin conexión y generar SQLite **no consumen Meta**. Nunca guardes claves en Git, comandos históricos ni bases públicas. Para usar Meta voluntariamente, verifica **tus** créditos, costos y consentimiento antes de cada envío manual; los valores JSON no acreditan que una cuenta tenga fondos.
- **Despliegue productivo:** los scripts originales de Lightsail incluyen valores de IP/autenticación particulares; **no** se deben ejecutar sin adaptar en un servidor del jurado. La [guía de producción](docs/PRODUCTION-LINUX.md) aporta contexto, no garantiza despliegue de un comando.

Detén únicamente tus propios procesos con **Ctrl+C** en sus terminales. Esta guía no exige desactivar políticas de seguridad, entrar a AWS del propietario ni ejecutar GitHub Actions.
