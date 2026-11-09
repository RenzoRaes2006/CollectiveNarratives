#!/usr/bin/env bash
# Voert bletchley.md stap 1-8 uit op de VPS. Veilig om opnieuw te draaien.
# Gebruik (als root):  bash /root/setup-vps.sh
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

ADMIN_USER="louis"
ADMIN_KEY="ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAAsdMhJ1rzQlFOYZX0KhMk4lh7OG3ZQOCH8Xzm8cieA boulezlouis@gmail.com"
APP_DIR="/opt/collective-narratives"

say() { printf '\n\033[1;32m== %s\033[0m\n' "$*"; }
[ "$(id -u)" -eq 0 ] || { echo "Draai dit als root."; exit 1; }

say "1. Systeem"
apt-get update
apt-get -y -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold upgrade
apt-get install -y unattended-upgrades fail2ban curl ca-certificates openssl
cat > /etc/apt/apt.conf.d/20auto-upgrades <<EOF
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
systemctl enable --now fail2ban

say "2. Docker"
if ! command -v docker >/dev/null || ! docker compose version >/dev/null 2>&1; then
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" > /etc/apt/sources.list.d/docker.list
  apt-get update
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
docker --version; docker compose version

say "3. Gebruiker deploy"
id deploy >/dev/null 2>&1 || adduser --disabled-password --gecos "" deploy
usermod -aG docker deploy
mkdir -p /home/deploy/.ssh
if [ ! -s /home/deploy/.ssh/authorized_keys ]; then
  [ -f /root/deploy_key ] || ssh-keygen -t ed25519 -C "github-deploy collective-narratives" -f /root/deploy_key -N ""
  cat /root/deploy_key.pub > /home/deploy/.ssh/authorized_keys
fi
chown -R deploy:deploy /home/deploy/.ssh
chmod 700 /home/deploy/.ssh && chmod 600 /home/deploy/.ssh/authorized_keys

say "4. Beheerder $ADMIN_USER"
id "$ADMIN_USER" >/dev/null 2>&1 || adduser --disabled-password --gecos "" "$ADMIN_USER"
usermod -aG sudo "$ADMIN_USER"
H="/home/$ADMIN_USER"
mkdir -p "$H/.ssh"; touch "$H/.ssh/authorized_keys"
grep -qxF "$ADMIN_KEY" "$H/.ssh/authorized_keys" || echo "$ADMIN_KEY" >> "$H/.ssh/authorized_keys"
chown -R "$ADMIN_USER:$ADMIN_USER" "$H/.ssh"
chmod 700 "$H/.ssh" && chmod 600 "$H/.ssh/authorized_keys"
if passwd -S "$ADMIN_USER" | awk '{exit !($2=="L"||$2=="NP")}'; then
  if [ -t 0 ]; then
    echo "Kies een wachtwoord voor $ADMIN_USER (nodig voor sudo):"
    until passwd "$ADMIN_USER"; do echo "Opnieuw."; done
  else
    echo "!! $ADMIN_USER heeft nog geen wachtwoord. Start opnieuw met 'ssh -t'. Gestopt voor SSH dichtgaat."; exit 1
  fi
fi

say "6. Firewall (eerst, zodat SSH zeker open blijft)"
apt-get install -y ufw
SSH_PORTS=$(sshd -T | awk '$1=="port"{print $2}')
for p in $SSH_PORTS; do ufw allow "$p/tcp"; done
ufw allow 80/tcp; ufw allow 443/tcp; ufw allow 443/udp
ufw --force enable
ufw status verbose

say "5. SSH dichtzetten (met automatisch terugdraaien na 10 min)"
rm -f /etc/ssh/sshd_config.d/99-hardening.conf
cat > /etc/ssh/sshd_config.d/00-hardening.conf <<EOF
PermitRootLogin no
PasswordAuthentication no
EOF
sshd -t
systemctl stop ssh-revert.timer 2>/dev/null || true
systemctl reset-failed ssh-revert.service ssh-revert.timer 2>/dev/null || true
systemd-run --unit=ssh-revert --on-active=10min /bin/sh -c 'rm -f /etc/ssh/sshd_config.d/00-hardening.conf; systemctl restart ssh'
systemctl restart ssh
sshd -T | grep -E '^(permitrootlogin|passwordauthentication) '

say "7. Map en geheime sleutel"
mkdir -p "$APP_DIR"; chown deploy:deploy "$APP_DIR"
if [ ! -f "$APP_DIR/.env" ]; then
  KEY=$(openssl rand -base64 48 | tr -d '/+=\n' | head -c 40)
  sudo -u deploy sh -c "umask 077; printf 'GAME_KEY=%s\nIMAGE_TAG=latest\n' '$KEY' > $APP_DIR/.env"
fi

say "8. Doorgeven aan de eigenaar van de repo (PRIVÉ!)"
IP4=$(ip -4 -o addr show scope global | awk '{print $4}' | cut -d/ -f1 | head -1)
echo "--- IP-adressen";  ip -4 -o addr show scope global | awk '{print $4}'; ip -6 -o addr show scope global | awk '{print $4}'
echo "--- Host key";     ssh-keyscan -t ed25519 localhost 2>/dev/null | sed "s/^localhost/$IP4/"
echo "--- GAME_KEY";     grep GAME_KEY "$APP_DIR/.env"
if [ -f /root/deploy_key ]; then
  echo "--- Privé deploy-sleutel (daarna: rm /root/deploy_key /root/deploy_key.pub)"; cat /root/deploy_key
else
  echo "--- Deploy-sleutel al doorgegeven en verwijderd."
fi

cat <<EOF

=====================================================================
 BELANGRIJK: root kan nu niet meer via SSH inloggen.
 Test NU in een tweede PowerShell-venster:   ssh $ADMIN_USER@$IP4
 Lukt dat, bevestig dan daar:                sudo systemctl stop ssh-revert.timer
 Doe je niets, dan zet de server SSH over 10 minuten zelf weer open
 (root-login terug aan), zodat je nooit buitengesloten raakt.
=====================================================================
EOF
