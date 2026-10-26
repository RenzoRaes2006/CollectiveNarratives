# CollectiveNarratives

Alone at Christmas VR: één speler in VR, het publiek stemt mee met de telefoon, alles live op een groot scherm.

| Map | Wat |
|---|---|
| `CN_VR/` | Unity-project (Quest, OpenXR). Koppeling met de website: scene `Assets/Scenes/ApplicationConnection.unity`. |
| `webapp/` | Website + SignalR-hub (ASP.NET Core, .NET 10): telefoons, groot scherm, sessies. Afspraken met Unity: [`webapp/UNITY-KOPPELING.md`](webapp/UNITY-KOPPELING.md). |
| `deploy/` | Docker Compose + Cloudflare Tunnel voor de VPS. VPS-setup: [`deploy/README.md`](deploy/README.md). Domein en GitHub: [`deploy/EIGENAAR.md`](deploy/EIGENAAR.md). |
| `docs/` | Plannen, bv. [`docs/PLAN-ONLINE.md`](docs/PLAN-ONLINE.md). |

Website lokaal starten: `cd webapp && docker compose up --build`, daarna `http://localhost:5000/host` (nep-game) en `http://localhost:5000/screen`.
