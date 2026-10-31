# Producción Linux / Linux production

La producción autorizada vive en Lightsail Ubuntu 24.04 x64, 4 vCPU, 16 GB y 320 GB.
La PC sigue siendo desarrollo; un push a main **no despliega automáticamente**.
Production is an explicit release, independent of local development and GitHub CI.

## Servicios / Services

- Nginx sirve Angular compilado y `/api` por HTTPS, inicialmente en `https://3.149.236.79`.
- El acceso inicial usa una credencial privada de operador, distinta del token Meta.
  Operator access is authenticated; the application does not implement separate user accounts.
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
- `/etc/prooflab/access.htpasswd`: contraseña cifrada del acceso inicial.
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
IP pública actual: si cambia al detener/iniciar Lightsail, actualizar certificado, origen y Nginx; asignar IP estática o dominio antes de depender de esta URL de forma permanente.

Antes de una migración de datos, detener la API en una ventana acordada y respaldar `/var/lib/prooflab` completo, además de `/etc/prooflab` y `/etc/letsencrypt`.
Nunca copiar solamente el archivo SQLite mientras está escribiendo: puede haber WAL y archivos de evidencia relacionados.
Las copias en el mismo disco no son respaldo externo. No se ha contratado almacenamiento adicional.
