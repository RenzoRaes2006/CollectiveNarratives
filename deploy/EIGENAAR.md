# Voor de eigenaar: domein en GitHub

Wat er buiten de VPS moet gebeuren. De VPS zelf: zie [README.md](README.md). Doe dit nadat de VPS-beheerder de gegevens uit stap 7 van die README heeft doorgestuurd.

## 1. DNS bij name.com

| Type | Host | Waarde |
|---|---|---|
| A | `@` | IPv4 van de VPS |
| AAAA | `@` | IPv6 van de VPS (alleen als die er is) |
| CNAME | `www` | `collective-narratives.app` |

Controle: `nslookup collective-narratives.app` geeft het IP van de VPS. Zet ook **automatisch verlengen** van het domein aan.

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
