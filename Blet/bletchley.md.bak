# VPS-setup voor collective-narratives.app

Voor wie de VPS beheert. Alles hieronder gebeurt **op de VPS zelf**, één keer. Daarna zet de pipeline van de repo de website automatisch live; op de VPS hoeft dan niets meer te gebeuren.

Wat er straks draait: twee Docker-containers, `web` (de website) en `caddy` (https). Geen database.

## 1. Systeem

Ubuntu 24.04 LTS. Minimum 1 vCPU, 1 GB RAM, 10 GB schijf. Als root of met sudo:

```bash
apt update && apt upgrade -y
apt install -y unattended-upgrades fail2ban
dpkg-reconfigure -plow unattended-upgrades   # kies "Yes": veiligheidsupdates automatisch
```

## 2. Docker

Uit de officiële repo van Docker (niet het `docker.io`-pakket van Ubuntu):

```bash
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" > /etc/apt/sources.list.d/docker.list
apt update
apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

## 3. Gebruiker `deploy` met een eigen SSH-sleutel

De pipeline logt in als `deploy`, met een sleutel die alleen daarvoor dient.

```bash
adduser --disabled-password --gecos "" deploy
usermod -aG docker deploy
mkdir -p /home/deploy/.ssh
ssh-keygen -t ed25519 -C "github-deploy collective-narratives" -f /root/deploy_key -N ""
cat /root/deploy_key.pub > /home/deploy/.ssh/authorized_keys
chown -R deploy:deploy /home/deploy/.ssh
chmod 700 /home/deploy/.ssh && chmod 600 /home/deploy/.ssh/authorized_keys
```

`/root/deploy_key` (de privésleutel) gaat in stap 7 naar de eigenaar van de repo. Daarna verwijderen.

Let op: wie in de groep `docker` zit, is in de praktijk root. Daarom een aparte gebruiker met een aparte sleutel.

## 4. SSH dichtzetten

```bash
cat > /etc/ssh/sshd_config.d/99-hardening.conf <<EOF
PermitRootLogin no
PasswordAuthentication no
EOF
systemctl restart ssh
```

**Test eerst in een tweede terminal dat je nog kan inloggen met je eigen sleutel**, voor je de eerste sessie sluit.

## 5. Firewall

```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw enable
```

Poort 80 en 443 moeten open zijn: daarmee haalt Caddy het https-certificaat.

## 6. Map en geheime sleutel

```bash
mkdir -p /opt/collective-narratives
chown deploy:deploy /opt/collective-narratives
KEY=$(openssl rand -base64 48 | tr -d '/+=\n' | head -c 40)
sudo -u deploy sh -c "umask 077; printf 'GAME_KEY=%s\nIMAGE_TAG=latest\n' '$KEY' > /opt/collective-narratives/.env"
cat /opt/collective-narratives/.env
```

`docker-compose.yml` en `Caddyfile` zet de pipeline zelf in deze map. `IMAGE_TAG` past de pipeline ook zelf aan.

## 7. Doorgeven aan de eigenaar van de repo

Stuur dit **privé** door (niet in een groepskanaal, niet in git):

| Wat | Hoe je het krijgt |
|---|---|
| IP-adres van de VPS (IPv4, en IPv6 als die er is) | `ip -4 addr` / `ip -6 addr` |
| De privé deploy-sleutel | `cat /root/deploy_key`, daarna `rm /root/deploy_key /root/deploy_key.pub` |
| De host key van de VPS | `ssh-keyscan -t ed25519 localhost \| sed "s/^localhost/<IP-van-de-VPS>/"` |
| De `GAME_KEY` | `grep GAME_KEY /opt/collective-narratives/.env` |

Daarna is de VPS klaar. De eerste deploy start de eigenaar vanuit GitHub.

## Later: beheer op de VPS

| Wat | Commando (als `deploy`, in `/opt/collective-narratives`) |
|---|---|
| Logs bekijken | `docker compose logs -f web` |
| Welke versie staat live | `grep IMAGE_TAG .env` |
| Terugrollen | in `.env` `IMAGE_TAG` op een oudere commit-sha zetten, dan `docker compose up -d` |
| Herstarten | `docker compose restart web` (lopende sessies zijn dan weg: **niet tijdens een voorstelling**) |

Niet doen: het volume `caddy_data` verwijderen (daar staan de certificaten).
