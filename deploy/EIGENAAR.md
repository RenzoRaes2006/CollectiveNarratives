# Voor de eigenaar: Cloudflare en GitHub

Wat er buiten de VPS moet gebeuren. De VPS zelf: zie [README.md](README.md). Doe dit nadat de VPS-beheerder de gegevens uit stap 7 van die README heeft doorgestuurd.

## 1. Cloudflare: domein en tunnel

De website gaat naar buiten via een **Cloudflare Tunnel**: een uitgaande verbinding van de VPS naar Cloudflare. Cloudflare regelt https, en op de VPS staat geen enkele webpoort open.

1. Maak een gratis account op [dash.cloudflare.com](https://dash.cloudflare.com) en kies **Add a domain** → `collective-narratives.app` → plan **Free**.
2. Cloudflare toont twee nameservers (bv. `xxx.ns.cloudflare.com`). Zet die bij **name.com → My Domains → collective-narratives.app → Nameservers** in de plaats van de `name.com`-nameservers. Het kan tot een paar uur duren voor Cloudflare het domein op **Active** zet.
3. **Zero Trust → Networks → Tunnels → Create a tunnel** → type **Cloudflared**, naam `collective-narratives`. Kopieer het **token** (de lange tekst na `--token` in het voorbeeldcommando). Installeer niets: cloudflared draait al als container.
4. **Public hostnames** van de tunnel:

   | Subdomain | Domain | Service |
   |---|---|---|
   | (leeg) | `collective-narratives.app` | `HTTP` · `web:8080` |
   | `www` | `collective-narratives.app` | `HTTP` · `web:8080` |

   Cloudflare maakt de DNS-records zelf aan.
5. Het token komt op de VPS in `/opt/collective-narratives/.env` (als `deploy`):

   ```bash
   cd /opt/collective-narratives && umask 077 && echo 'TUNNEL_TOKEN=<token>' >> .env
   ```

Controle na de eerste deploy: `docker compose logs tunnel` toont `Registered tunnel connection`, en `https://collective-narratives.app/healthz` geeft `OK`. Zet bij name.com ook **automatisch verlengen** van het domein aan.

## 2. GitHub: environment en secrets

Repo → **Settings → Environments → New environment** `production`.
Optioneel: **Required reviewers** aanzetten, dan moet iemand elke deploy goedkeuren (handig rond speeldagen).

Secrets in dat environment:

| Secret | Waarde |
|---|---|
| `VPS_HOST` | IP van de VPS |
| `VPS_USER` | `deploy` |
| `VPS_SSH_KEY` | de privé deploy-sleutel (hele bestand, inclusief de `BEGIN`- en `END`-regels) |
| `VPS_KNOWN_HOSTS` | de host-key-regel van de VPS-beheerder (begint met het IP, dan `ssh-ed25519 ...`) |

## 3. GitHub: `main` beveiligen

Repo → **Settings → Branches → Add rule** voor `main`: pull request verplicht, status check `test` verplicht, geen directe pushes.

## 4. Eerste deploy

Repo → **Actions → web → Run workflow** op `main` (of gewoon de volgende merge naar `main`).
Klaar als `https://collective-narratives.app/healthz` `OK` geeft.

## 5. Unity

De `GAME_KEY` van de VPS-beheerder komt in `CN_VR/Assets/ApplicationConnection/Resources/GameKey.txt` (één regel). Dat bestand staat in `.gitignore`: deel het privé met wie Quest-builds maakt.
