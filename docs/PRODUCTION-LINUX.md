# Producción Linux / Linux production

La producción autorizada vive en Lightsail Ubuntu 24.04 x64, 4 vCPU, 16 GB y 320 GB.
La PC sigue siendo desarrollo; un push a main **no despliega automáticamente**.
Production is an explicit release, independent of local development and GitHub CI.

## Servicios / Services

- Nginx sirve Angular compilado y `/api` en `https://prooflab-ai.com`. La IP redirige al dominio.
- Acceso público directo, sin Basic Auth, por solicitud del propietario (31/10/2026). No reactivar la contraseña de operador al desplegar.
  Public access needs no operator login; the Meta token remains required for paid operations.
- API .NET 10 en `127.0.0.1:5391`, usuario systemd `prooflab`.
- Node 24 ejecuta los workers de medición; FFmpeg/FFprobe prepara videos; Lean 4.35 verifica el catálogo matemático.
- SQLite está integrado. No se instala SQL Server ni se ejecutan modelos Meta en esta VM.
- `Deployment:PublicOrigin` acepta un origen HTTPS exacto; las escrituras exigen ese Origin.
  El proxy conserva el Origin real. No se aceptan encabezados X-Forwarded para otorgar permisos.
- Los cuatro permisos Meta del propietario permanecen habilitados. No se instala una clave Meta global.
  Registrar un token y pulsar Procesar sigue siendo la autorización manual de cada envío.

## Archivos persistentes / Persistent files

- `/opt/prooflab/releases/<release>/{api,web}`: binarios y frontend de cada versión.
- `/opt/prooflab/current`: enlace a la versión activa.
- `/var/lib/prooflab/.private`: sesiones, videos, recibos, caché y SQLite. Nunca dentro del paquete de publicación.
- `/etc/prooflab/production.env`: origen, versión y configuración de ejecución, solo root.
- `/etc/prooflab/access.htpasswd`: archivo histórico del acceso inicial, actualmente no utilizado.
- `/etc/letsencrypt`: certificado y claves; renovación cada seis horas mediante `prooflab-cert-renew.timer`.

Producción comienza vacía por decisión del propietario. No se migra `demo-video.json` ni los videos de desarrollo.
El botón de ejemplo local se omite en producción hasta contar con un conjunto público de demostración.
Production starts empty; upload a new video after generating a recovery code.

## Actualizar / Update

1. Probar cambios localmente. No ejecutar llamadas Meta como parte del despliegue.
2. `npm --prefix ui ci`; `npm --prefix ui run build -- --configuration production`.
3. `dotnet publish src/ProofLab.Api/ProofLab.Api.csproj -c Release -r linux-x64 --self-contained false -o <release>/api`.
4. Copiar `ui/dist/prooflab-comp-web/browser` a `<release>/web`, excluyendo `demo-video*.json`.
5. Subir solo el paquete de esa versión, nunca `.private`, claves SSH ni archivos de la PC.
6. Cambiar el enlace `current`, actualizar `Deployment__Release` y reiniciar `prooflab` en una ventana sin trabajos activos.
7. Verificar HTTPS, salud, conexión Meta sin inferencia, carga de video y recuperación por código.

`deploy/linux/install-production.sh` documenta el aprovisionamiento inicial de esta VM; requiere root y un paquete en `/tmp/prooflab-deploy/release.tar.gz`.
No ejecutar reinicios durante inferencias activas: la recuperación conserva registros, pero no reinicia automáticamente peticiones cobradas.
Keep the previous release for rollback; restore the previous symlink and release setting, then restart only the API.

## Operación / Operations

`systemctl status prooflab nginx`; `journalctl -u prooflab --since '10 minutes ago'`.
`systemctl list-timers prooflab-cert-renew.timer`; `/opt/certbot/bin/certbot renew --dry-run`.
Los certificados por IP son de corta duración; la renovación y la apertura del puerto 80 deben mantenerse.
La VM requiere 80/443 públicos y SSH limitado al acceso administrativo. 5391 permanece en loopback.
Cloudflare publica el registro A de `prooflab-ai.com` en modo DNS only hacia `3.149.236.79`. Si la IP cambia, actualizar DNS y la redirección por IP. El origen público de la API es `https://prooflab-ai.com`.

Antes de una migración de datos, detener la API en una ventana acordada y respaldar `/var/lib/prooflab` completo, además de `/etc/prooflab` y `/etc/letsencrypt`.
Nunca copiar solamente el archivo SQLite mientras está escribiendo: puede haber WAL y archivos de evidencia relacionados.
Las copias en el mismo disco no son respaldo externo. No se ha contratado almacenamiento adicional.

## Verificación inicial / Initial verification — 30/10/2026

Release de aplicación: `16b216b`. Comprobaciones focalizadas: 66 pruebas .NET y 9 Angular, build de producción y publish Linux correctos.
HTTPS validado con certificado de CA; acceso anónimo rechazado, Origin ajeno rechazado, API solo loopback.
Video sintético H.264 de 6 s/60 FPS: carga, poster, descarga con SHA intacto, HTTP Range y recuperación después del reinicio correctos.
Edge con perfil aislado: Inicio/Configuración/Laboratorio, conexión habilitada y recuperación con video decodificado 640×360, sin errores JavaScript.
La sesión sintética se retiró: cero sesiones y cero filas en las tablas de evidencia/Muse. Cero llamadas Meta; no se prueba aquí la precisión científica ni el proveedor en vivo.
`certbot renew --dry-run` correcto; renovación habilitada cada seis horas.

Al terminar la verificación, el servidor escucha en 443, pero la conexión pública sigue bloqueada por la red de Lightsail. Abrir HTTPS/TCP 443 en el firewall IPv4 y comprobar el acceso externo. Las pruebas de Edge se realizaron mediante un túnel SSH privado, conservando la verificación TLS y el origen HTTPS real.
Public reachability is pending the Lightsail HTTPS firewall rule. Browser/server checks passed over SSH; no domain or static IP was assigned, and no external backup was provisioned.

## Interfaz y acceso público / UI and public access — 31/10/2026

HTTPS público, certificado del dominio y `/api/health` comprobados sin credenciales (HTTP 200). La verificación inicial anterior corresponde al estado histórico del aprovisionamiento.
No ejecutar el instalador inicial para actualizar: sus valores iniciales de origen/acceso no representan esta configuración pública.

Para una actualización solo de interfaz, preparar una nueva carpeta de release con la API vigente sin cambios y el nuevo `web` compilado, excluyendo `demo-video*.json`. Conservar también los archivos JS/CSS con hash anteriores para pestañas abiertas. Cambiar `current` atómicamente; no reiniciar la API ni modificar `production.env` o los datos. Registrar por separado el commit de interfaz y la versión de API en el manifiesto de release.

El aviso de token ausente abre un modal ES/EN y pulsa en rojo. Aceptar mueve el token al campo de contraseña de la sesión, limpia el borrador y no llama al proveedor. Procesar pulsa en verde solo con los requisitos existentes satisfechos. Movimiento reducido desactiva las animaciones. Verificación: 77 pruebas Angular en Edge, build de producción y revisión visual a 1440 y 390 px; cero llamadas Meta.

English: production now opens anonymously at the domain. For UI-only releases, keep the existing API and persistent data, deploy the compiled web assets and switch the release symlink atomically without restarting the API. Keep older hashed assets for already-open tabs and record UI/API versions separately. The bilingual token modal clears its draft, keeps the key only in the current session, and never starts analysis on acceptance. Ready-state checks remain enforced. Edge tests (77), production build and desktop/mobile visual checks passed without provider calls.

Despliegue de interfaz verificado: `2455deb-web`, commit `2455debf6ed42a464c94e300ac1669e5c300a191`, bundle `main-JHWIAJIF.js`. API vigente `16b216b`; mismo PID y mismo hash de configuración antes/después. HTTPS anónimo, bundle y salud: HTTP 200. No se reiniciaron servicios ni se modificaron datos.
Verified UI-only deployment: API process/configuration unchanged, public HTTPS and health returned 200, zero Meta calls.
