# VPS-setup voor collective-narratives.app

Eenmalig, door wie de VPS beheert. Daarna zet de pipeline (`.github/workflows/web.yml`) elke merge naar `main` die `webapp/` raakt automatisch live.
Wat er draait: twee containers, `web` (de website en de SignalR-hub, ASP.NET Core) en `caddy` (https en reverse proxy). Er is geen database: sessies zitten in het geheugen.

> **Kort:** Ubuntu 24.04, Docker, gebruiker `deploy`, firewall 22/80/443, map `/opt/collective-narratives` met `.env`, DNS naar de VPS, en vier secrets in GitHub. Alle stappen staan hieronder.

## 1. Server

Ubuntu 24.04 LTS, als root of met sudo:

```bash
apt update && apt upgrade -y
apt install -y unattended-upgrades fail2ban
dpkg-reconfigure -plow unattended-upgrades   # "Yes": veiligheidsupdates automatisch
```

Minimum: 1 vCPU, 1 GB RAM, 10 GB schijf. Meer is niet nodig.

## 2. Docker

Officiële repo van Docker (niet het `docker.io`-pakket van Ubuntu):

```bash
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" > /etc/apt/sources.list.d/docker.list
apt update
apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

## 3. Gebruiker `deploy` (voor de pipeline)

```bash
adduser --disabled-password --gecos "" deploy
usermod -aG docker deploy
mkdir -p /home/deploy/.ssh && chmod 700 /home/deploy/.ssh
```

Maak een **aparte SSH-sleutel alleen voor de pipeline** (op je eigen pc of op de VPS, maakt niet uit):

```bash
ssh-keygen -t ed25519 -C "github-deploy collective-narratives" -f deploy_key -N ""
```

- `deploy_key.pub` → toevoegen aan `/home/deploy/.ssh/authorized_keys` (rechten `600`, eigenaar `deploy`).
- `deploy_key` (privé) → wordt het GitHub-secret `VPS_SSH_KEY` (zie stap 8). Daarna het bestand verwijderen.

Let op: wie in de groep `docker` zit, is in de praktijk root op die machine. Daarom is dit een aparte gebruiker met een aparte sleutel die alleen in GitHub staat.

## 4. SSH dichtzetten

In `/etc/ssh/sshd_config` (of een bestand in `/etc/ssh/sshd_config.d/`):

```
PermitRootLogin no
PasswordAuthentication no
```

Daarna `systemctl restart ssh`. **Test eerst in een tweede terminal dat inloggen met je sleutel nog werkt.**

## 5. Firewall

```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp     # HTTP/3, optioneel
ufw enable
```

Later, voor het spelerzicht via WebRTC (fase 5b), komen er nog 3478 en een UDP-range bij. Nu nog niet.

## 6. Map en geheimen

```bash
mkdir -p /opt/collective-narratives
chown deploy:deploy /opt/collective-narratives
sudo -u deploy sh -c 'umask 077; cat > /opt/collective-narratives/.env' <<EOF
GAME_KEY=$(openssl rand -base64 33 | tr -d '/+=' | head -c 40)
IMAGE_TAG=latest
EOF
cat /opt/collective-narratives/.env
```

- `GAME_KEY` is de sleutel waarmee alleen de game sessies mag aanmaken. **Geef hem veilig door aan het Unity-team** (niet via git, niet in een openbaar kanaal). In Unity komt hij in `CN_VR/Assets/ApplicationConnection/Resources/GameKey.txt` (staat in `.gitignore`).
- `IMAGE_TAG` past de pipeline zelf aan bij elke deploy.

De pipeline kopieert `docker-compose.yml` en `Caddyfile` zelf naar deze map.

## 7. DNS (bij name.com)

| Type | Host | Waarde |
|---|---|---|
| A | `@` | IPv4 van de VPS |
| AAAA | `@` | IPv6 van de VPS (alleen als de VPS IPv6 heeft) |
| CNAME | `www` | `collective-narratives.app` |

Controle: `dig +short collective-narratives.app` geeft het IP van de VPS. `.app` werkt alleen via https; Caddy haalt het certificaat zelf zodra DNS klopt en poort 80/443 open staan.

## 8. GitHub (repo-beheerder)

**Settings → Environments → New environment `production`.** Optioneel: "Required reviewers" aanzetten, dan moet iemand elke deploy goedkeuren (handig rond speeldagen). Zet in dat environment deze **secrets**:

| Secret | Waarde |
|---|---|
| `VPS_HOST` | IP of hostnaam van de VPS |
| `VPS_USER` | `deploy` |
| `VPS_SSH_KEY` | de inhoud van `deploy_key` (privé, uit stap 3) |
| `VPS_KNOWN_HOSTS` | de uitvoer van `ssh-keyscan -t ed25519 <VPS_HOST>`, **gecontroleerd** tegen de fingerprint op de VPS (`ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`) |

Het image komt uit GHCR (`ghcr.io/renzoraes2006/collectivenarratives-web`). De pipeline logt bij elke deploy tijdelijk in met zijn eigen token, dus er is **geen PAT of `docker login` op de VPS nodig**.

**Branch-beveiliging** (Settings → Branches → `main`): pull request verplicht, status check `web / test` verplicht, geen directe pushes.

## 9. Eerste deploy

Zodra stap 1 t/m 8 klaar zijn: in GitHub **Actions → web → Run workflow** op `main`. Of gewoon de volgende merge naar `main`.
Klaar als `https://collective-narratives.app/healthz` `OK` geeft.

Liever eerst met de hand? Als `deploy` op de VPS, met `docker-compose.yml` en `Caddyfile` uit deze map in `/opt/collective-narratives/`:

```bash
cd /opt/collective-narratives
docker login ghcr.io          # alleen nodig zolang het image privé is: GitHub-gebruiker + PAT met read:packages
docker compose pull && docker compose up -d
curl https://collective-narratives.app/healthz
```

## Beheer

| Wat | Hoe |
|---|---|
| Logs | `cd /opt/collective-narratives && docker compose logs -f web` |
| Wat staat er live | `grep IMAGE_TAG /opt/collective-narratives/.env` |
| Terugrollen | GitHub **Actions → web → Run workflow** met `image_tag` = oude commit-sha. Of op de VPS: `.env` aanpassen en `docker compose up -d` |
| Herstarten | `docker compose restart web` (lopende sessies zijn dan weg: **niet tijdens een voorstelling**) |
| Uptime | Gratis monitor (UptimeRobot of Uptime Kuma) op `https://collective-narratives.app/healthz` |
| Certificaten | Doet Caddy zelf. Volume `caddy_data` niet verwijderen (anders opnieuw aanvragen, met rate limits van Let's Encrypt) |
