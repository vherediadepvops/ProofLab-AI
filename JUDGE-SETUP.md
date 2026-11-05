# Judge setup — ProofLab AI (Windows and Linux)

**Meta Global AI Developer Hackathon 2026 · Indie Developer · Push the Frontier**

[Español](JUDGE-SETUP.es.md) · [Project README](README.md) · [Public 25-case dataset](demo-data/README.md)

This guide explains how to **clone, build, and run ProofLab AI locally** and inspect the published pendulum results **without a Meta API key or paid requests**. It is for an independent development/review installation, **not** an automated production deployment or restoration of the original AWS database.

## 1. Requirements

| Dependency | Required use | Installation |
| --- | --- | --- |
| Git | Clone source | [git-scm.com](https://git-scm.com/downloads) |
| **.NET SDK 10** (not just runtime) | ASP.NET Core API and scientific engine | [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) |
| **Node.js 24 LTS and npm** | Angular 20, compiled video worker, offline SQLite builder | [nodejs.org](https://nodejs.org/en/download) |
| **FFmpeg + FFprobe** | Video probing, thumbnails, original-media preparation | [ffmpeg.org](https://ffmpeg.org/download.html) |
| Modern browser | Local UI; Chrome required only for optional ChromeHeadless tests | Edge / Chrome |

**Important FFmpeg detail:** the current video-preview implementation explicitly invokes the `libopenh264` encoder. Verify that your FFmpeg build includes it; having `ffmpeg` and `ffprobe` alone is not sufficient for every video workflow. [FFmpeg encoder documentation](https://ffmpeg.org/ffmpeg-codecs.html#libopenh264).

**Optional:** Lean 4 is needed only for the separate formal-mathematics verifier, not to inspect the 25 pendulum results or run the basic pendulum UI. See `lean-toolchain`.

## 2. Windows 11 — clean install

Install Git, .NET SDK 10, Node.js 24 LTS and a FFmpeg distribution that includes `ffmpeg.exe`, `ffprobe.exe` and `libopenh264`. Reopen PowerShell after installation so `PATH` is refreshed. Verify:

```powershell
git --version
dotnet --version
node --version
npm.cmd --version
ffmpeg -version
ffprobe -version
ffmpeg -hide_banner -encoders | Select-String "libopenh264"
```

`dotnet --version` should show `10.x`; `node --version` should show `v24.x` (the tested line). The final command must list `libopenh264` for video-preview support.

Choose an **empty directory**, not the existing app/production directory:

```powershell
git clone https://github.com/vherediadepvops/ProofLab-AI.git
Set-Location .\ProofLab-AI
git rev-parse --short HEAD
```

**Install the Angular packages before building .NET**, because the API compiles its JavaScript-based motion worker from the UI sources:

```powershell
Push-Location .\ui
npm.cmd ci
Pop-Location
dotnet restore .\ProofLabCompetition.slnx
dotnet build .\src\ProofLab.Api\ProofLab.Api.csproj --no-restore
```

Check that **ports 5391 (API) and 4300 (web)** are available:

```powershell
Get-NetTCPConnection -State Listen -LocalPort 5391,4300 -ErrorAction SilentlyContinue
```

If there are listeners, do **not** terminate unrelated services. Use a fresh machine or deliberately free ports you control. The current app only permits specific local web origins; merely changing Angular's port may cause CORS/manual-action errors.

Start the API in **PowerShell terminal 1**, from the cloned repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\src\ProofLab.Api --no-launch-profile --urls http://127.0.0.1:5391
```

Start the web UI in **PowerShell terminal 2**, from the cloned repository root:

```powershell
Set-Location .\ui
npm.cmd start -- --host 127.0.0.1 --port 4300
```

**Open [http://127.0.0.1:4300/](http://127.0.0.1:4300/).** Leave both terminals running while you use the UI. Open each terminal in the cloned repository root before running its commands.

## 3. Linux — Ubuntu 24.04 local development

This is a **local two-terminal setup**, not the separate Lightsail/Nginx production procedure. On Ubuntu 24.04, install Git, FFmpeg and .NET SDK 10 using the distribution's trusted packages, and install **Node.js 24 LTS** using the [official Node.js instructions](https://nodejs.org/en/download) (do not assume Ubuntu's default `nodejs` package is v24):

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

The `dotnet-sdk-10.0` package for Ubuntu 24.04 is documented by [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install). If `libopenh264` is absent, use a trusted FFmpeg build with that encoder before testing video previews.

```bash
git clone https://github.com/vherediadepvops/ProofLab-AI.git
cd ProofLab-AI
npm --prefix ui ci
dotnet restore ProofLabCompetition.slnx
dotnet build src/ProofLab.Api/ProofLab.Api.csproj --no-restore
```

In **terminal 1** (repository root):

```bash
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/ProofLab.Api --no-launch-profile --urls http://127.0.0.1:5391
```

In **terminal 2** (repository root):

```bash
cd ui
npm run start -- --host 127.0.0.1 --port 4300
```

Open [http://127.0.0.1:4300/](http://127.0.0.1:4300/) in a browser on the **same computer**. Linux commands are based on the project configuration and official dependencies; this submission's independent clean-install test was performed on **Windows 11**, not a fresh Linux machine.

## 4. Verify the API without Meta requests

In a **third terminal** with both services running:

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

Expected: a **200** health response and a local mathematical pendulum prediction. The readiness endpoint reports permissions/credential presence without returning a token. **These checks do not contact Meta.**

The UI can upload a supported video and retain the original locally; this is different from the **Process/Analyze** action that may initiate **paid Muse Spark/SAM requests**. Without a valid Meta key, the app cannot reproduce the original live-model analysis end to end.

## 5. Rebuild and inspect the 25-case public SQLite

The public data package contains **25 stored comparisons** (18 within a predefined practical band, 6 outside, 1 inconclusive), assembled using **eight previously recorded source videos**. These are not 25 independent physical trials or an externally validated accuracy figure.

From the repository root, on **Windows or Linux** with Node.js 24+:

```sh
node demo-data/build-demo-db.mjs --verify
node demo-data/build-demo-db.mjs prooflab-demo.sqlite
```

The builder refuses to overwrite an existing file. Open `prooflab-demo.sqlite` with any SQLite viewer and run:

```sql
SELECT case_id, baseline_length_m, followup_length_m,
       measured_t1_s, predicted_t2_s, measured_t2_s, recorded_verdict
FROM pendulum_cases
ORDER BY case_id;
```

Expected: **25** rows, including one inconclusive case with missing follow-up measurement. The database also has `public_recordings` (8 links + SHA-256 hashes) and `dataset_metadata`.

**Do not confuse the two databases:** this generated SQLite is a **standalone read-only review catalog**. It does **not** populate ProofLab's private application history, create recoverable study IDs in the UI, or contain raw SAM masks/Meta receipts. The application's internal SQLite storage is created separately in its private runtime data directory. The eight original videos are linked from the existing [public gallery](https://prooflab-ai.com/), not bundled in Git. [Data documentation](demo-data/README.md).

## 6. Optional offline tests

Run only if you wish to inspect the implementation (no paid Meta calls are expected):

```sh
dotnet test tests/ProofLab.Tests/ProofLab.Tests.csproj
dotnet test tests/ProofLab.Workflow.Tests/ProofLab.Workflow.Tests.csproj
```

For Angular tests, with Google Chrome available, run from `ui/`:

```sh
npm test -- --watch=false --browsers=ChromeHeadless
```

**Verification record (4 November 2026):** a separate Windows 11 clean clone of `main` compiled the frontend/backend, started both servers, exercised health/prediction, uploaded a synthetic H.264 clip, verified source SHA-256 and physical-input validation, and passed **762 .NET** plus **552 Angular** tests. This is **software validation**, not a new Meta inference or scientific accuracy audit.

## 7. Troubleshooting and safety

- **`dotnet` not found / build fails:** install **SDK 10**, not only the runtime; check `dotnet --version`. Install `ui` packages with `npm ci` *before* the API build, because its worker uses the Angular dependency tree.
- **`FFmpeg unavailable` / no video preview:** verify both executables on `PATH`, and confirm `libopenh264` appears in `ffmpeg -hide_banner -encoders`. Other encoders (such as `libx264`) do not satisfy this specific implementation.
- **UI cannot reach API / CORS:** keep the documented loopback ports **4300/5391** and access the web via `127.0.0.1` or `localhost`. Changing host/origin or deploying behind a reverse proxy requires matching configuration.
- **Meta analysis unavailable:** expected without your own valid key and corresponding provider permissions. The repository has no shared token, paid allowance or copy of AWS private data.
- **Paid requests:** local builds, offline tests and database import require **no** Meta spending. Never place keys in source, Git, command history or the public database. If independently using Meta, verify your **own** credits, estimated price and consent before each manual submission; do not assume the JSON settings prove an actual funded account.
- **Production deployment:** the initial Lightsail scripts contain environment-specific IP/authentication values and must **not** be used blindly to deploy a judge's server. See [production notes](docs/PRODUCTION-LINUX.md) for background, not a guaranteed one-command deployment.

Stop your own development processes with **Ctrl+C** in their terminals. This guide never requires disabling security policies, accessing the owner's AWS environment or running GitHub Actions.
