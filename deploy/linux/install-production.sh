#!/usr/bin/env bash
set -euo pipefail
release=${1:?Release identifier required}
[[ "$release" =~ ^[a-zA-Z0-9._-]+$ ]] || exit 1
source_dir=/tmp/prooflab-deploy
id prooflab >/dev/null 2>&1 || useradd --system --home-dir /var/lib/prooflab --shell /usr/sbin/nologin prooflab
install -d -o prooflab -g prooflab -m 700 /var/lib/prooflab
install -d -m 755 /opt/prooflab/releases /etc/prooflab
install -d -m 755 "/opt/prooflab/releases/$release"
tar -xzf "$source_dir/release.tar.gz" -C "/opt/prooflab/releases/$release"
chown -R root:root "/opt/prooflab/releases/$release"
chmod -R a+rX "/opt/prooflab/releases/$release"
ln -sfn "/opt/prooflab/releases/$release" /opt/prooflab/current
cat > /etc/prooflab/production.env <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5391
Deployment__PublicOrigin=https://3.149.236.79
Deployment__Release=$release
AllowedHosts=3.149.236.79
Meta__AllowPaidRequests=true
Meta__CreditsConfirmedByOwner=true
Meta__OwnerApprovedPaidRequests=true
Meta__UnlimitedManualRequests=true
Media__FfmpegDirectory=/usr/bin
Research__NodeExecutable=/usr/local/bin/node
PROOFLAB_LEAN_PATH=/opt/lean-4.35.0-linux/bin/lean
PATH=/usr/local/bin:/usr/bin:/bin
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
EOF
chmod 600 /etc/prooflab/production.env
# A private initial operator login; no application or Meta token is installed.
if [[ ! -f /etc/prooflab/access.htpasswd ]]; then
    python3 - <<'PY'
import secrets, subprocess, pathlib, os
password=secrets.token_urlsafe(24)
subprocess.run(['htpasswd','-i','-B','-c','/etc/prooflab/access.htpasswd','prooflab'],input=password+'\n',text=True,check=True)
p=pathlib.Path('/home/ubuntu/prooflab-access.txt')
p.write_text('URL: https://3.149.236.79/\nUsuario: prooflab\nContraseña: '+password+'\n\nAcceso privado al ambiente de producción. No es el token de Meta.\n')
os.chmod(p,0o600)
PY
    chown ubuntu:ubuntu /home/ubuntu/prooflab-access.txt
fi
chown root:www-data /etc/prooflab/access.htpasswd
chmod 640 /etc/prooflab/access.htpasswd
install -m 644 "$source_dir/prooflab.service" /etc/systemd/system/prooflab.service
install -m 644 "$source_dir/prooflab-cert-renew.service" /etc/systemd/system/
install -m 644 "$source_dir/prooflab-cert-renew.timer" /etc/systemd/system/
printf '#!/bin/sh\n/usr/sbin/nginx -t && /usr/bin/systemctl reload nginx\n' > /usr/local/sbin/prooflab-reload-tls
chmod 755 /usr/local/sbin/prooflab-reload-tls
install -m 644 "$source_dir/production.nginx" /etc/nginx/sites-available/prooflab
if [[ -L /etc/nginx/sites-enabled/default ]]; then unlink /etc/nginx/sites-enabled/default; fi
ln -sfn /etc/nginx/sites-available/prooflab /etc/nginx/sites-enabled/prooflab
nginx -t
systemctl daemon-reload
systemctl enable --now prooflab.service prooflab-cert-renew.timer
systemctl restart prooflab.service
systemctl reload nginx
systemctl is-active prooflab nginx prooflab-cert-renew.timer
