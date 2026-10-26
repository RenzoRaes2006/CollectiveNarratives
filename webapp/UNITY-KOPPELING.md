# Koppeling website ↔ Unity (Alone at Christmas VR)

Dit document is de afspraak tussen de website (map `webapp/`) en het Unity-project (`CN_VR/`).
**Verander je iets aan de hub? Pas dan eerst dit document aan en laat het het andere team weten.**

## Wie doet wat

| Onderdeel | Taak |
|---|---|
| **Game** (Quest) | Stuurt het verhaal. Maakt de sessie, toont de code in de bril, bepaalt wanneer een keuze begint en voert de uitslag uit. |
| **Server** (`collective-narratives.app`, ASP.NET Core + SignalR) | Sessies en codes, stemmen tellen, alles doorgeven. Kent **geen** verhaallogica. |
| **Groot scherm** (browser op de projectorlaptop, `/screen`) | Koppelt met de code uit de bril. Toont daarna QR, code, kijkers, stemming en einde. Kan zelf niets sturen. |
| **Telefoons** (publiek, `/poll/{code}`) | Stemmen en drukken. |

```
 Telefoons ──┐
             ├──SignalR──▶ Server /hubs/session ◀──SignalR── Game (Quest)
 Groot scherm┘
```

## Verloop van een sessie

1. **Start:** de game verbindt met de game key en roept `CreateSession()` aan. Antwoord: een code van 6 cijfers. De game toont die in de bril.
2. **Koppelen:** iemand opent op de projectorlaptop `collective-narratives.app/screen` en typt de code. De game krijgt `ScreenPaired`.
3. **Tutorial en wachtkamer:** de game roept `StartTutorial(60)` aan en start de tutorial. Het grote scherm toont de QR, de code, het aantal kijkers en een aftelling. Telefoons joinen.
4. **Spel start:** zodra de tutorial klaar is **en** er minstens 60 s voorbij zijn, roept de game `StartGame()` aan. De 60 s houdt de game zelf bij; de server gebruikt ze alleen voor de aftelling op het scherm. Met de knop **Skip countdown** op het grote scherm (of A/X in de bril) krijgt de game `LobbySkipped` en wacht ze niet langer.
5. **Spel:** `StartVote`, `StartTogether`, `SetEnding` zoals voorheen.
6. **Einde:** `EndSession()`. De code werkt meteen niet meer. Telefoons houden hun eindscherm.

Telefoons kunnen pas joinen na stap 2 (daarvoor staat de code alleen in de bril). Late kijkers kunnen tijdens het spel nog joinen: de code staat klein onderaan het grote scherm.

**Storingen:** valt de verbinding met de game weg (crash, wifi weg), dan zet de server `gameConnected` op `false` en toont het grote scherm na 3 s "Technical difficulties". Komt de game terug (`ResumeSession`), dan verdwijnt de melding. Komt ze niet terug, dan vervalt de sessie na 2 minuten en vraagt het scherm om de game te herstarten. Stopt het spelerzicht langer dan 6 s terwijl de game verbonden is (vastgelopen, bril afgezet), dan toont het scherm dezelfde melding. Heeft het scherm zelf 5 s geen verbinding met de server, dan toont het "Connection lost".

Alle teksten voor spelers en publiek (website en game) zijn in het Engels. Id's (`telefoon`, `opnemen`, `held`, ...) blijven zoals ze zijn.

## Lokaal draaien

Met Docker (geen .NET SDK nodig):

```bash
cd webapp
docker compose up --build
```

Of met de .NET 10 SDK: `dotnet run --project webapp/src/CollectiveNarratives.Web` (poort 5000).

Daarna:

| Adres | Wat |
|---|---|
| `http://localhost:5000/host` | **Nep-game**: doet wat Unity doet (sessie maken, keuzes starten, events zien). Handig zonder headset. |
| `http://localhost:5000/screen` | Groot scherm: code invullen |
| `http://localhost:5000/` | Telefoon: code invullen |

Lokaal is de game key `dev-game-key`. Telefoons op hetzelfde wifi: start met `PUBLIC_URL=http://<wifi-IP-van-de-laptop>:5000 docker compose up --build`, anders wijst de QR naar `localhost`.

Tests: `dotnet test webapp/CollectiveNarratives.slnx` (of in Docker, zie `.github/workflows/web.yml`).

## Unity: wat staat er al

In `CN_VR/Assets/ApplicationConnection/` (scene `Assets/Scenes/ApplicationConnection.unity`):

| Bestand | Wat |
|---|---|
| `Scripts/SessionClient.cs` | De koppeling. Coroutines: `StartSession`, `StartTutorial`, `SkipLobby`, `StartGame`, `RunVote`, `Decide`, `StartTogether`, `SetEnding`, `EndSession`. Eigenschappen: `Code`, `ScreenPaired`, `Viewers`, `LobbySkipped`, `LiveVote`, `Connected`, `LastError`. Verbindt zelf opnieuw en hervat de sessie. |
| `Scripts/Net/SignalRConnection.cs` | Kleine SignalR-client (JSON over WebSockets), zonder packages. Getest vanuit `webapp/tests`. |
| `Scripts/Net/MiniJson.cs` | Haalt SignalR-berichten uit elkaar; de inhoud gaat daarna naar `JsonUtility`. |
| `Scripts/ServerConfig.cs` + `Resources/ServerConfig.asset` | Serveradres en game key. In de Editor `http://localhost:5000`, in een build `https://collective-narratives.app`. |
| `Scripts/Tutorial.cs` | Plek voor de tutorial. Nu leeg (meteen klaar). |
| `Scripts/ApplicationConnection.cs` | Test-flow: code in de bril → koppelen → tutorial + 60 s → teststemming "telefoon". Paneel in de bril dat je blik volgt, debug-info linksboven op de pc. |
| `Scripts/PlayerViewStreamer.cs` | Zicht van de speler naar het grote scherm: extra camera, 1280×720 met 4× MSAA, 15 beelden per seconde als JPEG. Zet `streamEnabled` uit als de Quest het niet trekt. |
| `Scripts/XRRigSetup.cs` | Hoofdtracking in de Quest voor een scene zonder XR-rig (maakt er zelf een XR Origin bij). In de Editor zonder bril doet hij niets. |

**Bediening in de testscene:**

| Wat | In de bril (controllers) | In de Editor |
|---|---|---|
| Zonder groot scherm spelen | A/X, verschijnt na 2 minuten op het codescherm | S |
| Wachttijd (60 s) overslaan | A/X tijdens het wachten, of de knop op het grote scherm | K |
| Kevin beslist bij gelijkspel | Naar de telefoon kijken + trigger = opnemen. 8 s niets doen = laten rinkelen | 1 = opnemen, 2 = laten rinkelen |
| Opnieuw (nieuwe code) | A/X op het uitslagscherm | R |

**Game key voor de online server:** zet hem in `CN_VR/Assets/ApplicationConnection/Resources/GameKey.txt` (één regel, staat in `.gitignore`). Zonder dat bestand gebruikt de game `dev-game-key`, en dat werkt alleen lokaal. De sleutel krijg je van de VPS-beheerder (`/opt/collective-narratives/.env`).

**Waarom een eigen SignalR-client?** De officiële NuGet-client sleept veel afhankelijkheden mee en moet met `link.xml` tegen IL2CPP-stripping beschermd worden. Het protocol is klein (handshake, JSON-berichten met `0x1E` erachter, pings), dus ~250 regels zonder packages is eenvoudiger. **Nog te testen op een echte Quest-build** (fase 0 uit `docs/PLAN-ONLINE.md`).

### Gebruik

```csharp
yield return session.StartSession();                    // Code staat klaar
toonInBril(session.Code);
yield return new WaitUntil(() => session.ScreenPaired);

float start = Time.unscaledTime;
yield return session.StartTutorial(60);
yield return tutorial.Speel();                           // eigen code
yield return new WaitUntil(() => Time.unscaledTime - start >= 60 || session.LobbySkipped);
yield return session.StartGame();

VoteResult uitslag = null;
yield return session.RunVote("telefoon", 12, r => uitslag = r);
string keuze = uitslag != null ? uitslag.winner : "";
if (keuze == "")
{
    // Gelijkspel, niemand stemde of server weg: Kevin beslist.
    yield return WachtTotKevinIetsAanraakt(gekozen => keuze = gekozen); // eigen code
    if (uitslag != null) yield return session.Decide(uitslag.voteId, keuze);
}
```

Is de server niet bereikbaar, dan geeft `RunVote` `null` en speelt het spel gewoon door: Kevin beslist.

## De hub: `/hubs/session`

SignalR, JSON-protocol. Velden in camelCase. Alles is leesbaar met `JsonUtility` (geen dictionaries).

### Game → server

De game verbindt met de game key als `access_token` (querystring bij WebSockets, of de header `Authorization: Bearer <key>`). Zonder geldige key weigeren `CreateSession` en `ResumeSession`.

| Methode | Antwoord | Opmerking |
|---|---|---|
| `CreateSession()` | `{ code, gameToken, joinUrl, screenUrl }` | Bewaar `gameToken` voor `ResumeSession`. |
| `ResumeSession(code, gameToken)` | `bool` | Na een verbroken verbinding, binnen 2 minuten. `false`: sessie vervallen, maak een nieuwe. |
| `StartTutorial(seconds)` | – | Na `ScreenPaired`. Het scherm toont de aftelling. Fout als er nog geen scherm gekoppeld is. |
| `SkipLobby()` | – | Wachttijd overslaan (zoals de knop op het grote scherm). De game krijgt `LobbySkipped` terug. |
| `StartGame()` | – | Einde wachtkamer. |
| `StartVote(id, question, options, seconds, message)` | `voteId` (int) | Zie hieronder. Een nieuwe stemming rondt een lopende eerst af. |
| `StopVote()` | uitslag of `null` | Meteen tellen (normaal niet nodig). |
| `Decide(voteId, option)` | – | Kevin koos zelf: zet `winner` en `decidedBy: "kevin"`. |
| `StartTogether(label, seconds)` | `{ active, label, seconds, remaining, presses, needed }` | Samen-actie. `needed` = aantal kijkers bij de start. |
| `SetEnding(ending)` | – | `held`, `weg` of `cadeau`. |
| `EndSession()` | – | Code vervalt meteen. |

**`StartVote`**: geef `question` en `options` (`[{ "id": "a", "label": "..." }]`, 2 tot 6 opties) zelf mee, dan zit alle verhaalinhoud in Unity. Geef je `null` mee, dan neemt de server de ingebouwde keuze met dat `id`:

| id | Vraag | Opties (id → tekst) |
|---|---|---|
| `telefoon` | The phone is ringing. What does Kevin do? | `opnemen` Answer it · `rinkelen` Let it ring |
| `hond` | Scratching at the back door. It's Marley's dog. | `binnen` Let the dog in · `buiten` Leave it outside |
| `stroom` | The power goes out. What does Kevin do? | `kelder` Go to the basement · `zaklamp` Find a flashlight |
| `inbreker` | The back door opens. What does Kevin do? | `kast` Hide in the closet · `raam` Escape through the window |

`seconds` ≤ 0 betekent 12 (standaard). `message` is een extra regel op de telefoons, bv. "They're faster than expected."

Fouten (onbekende keuze, sessie afgelopen, ...) komen terug als foutmelding op de aanroep. In `SessionClient` staan ze in `LastError`.

### Server → game (events)

| Event | Inhoud |
|---|---|
| `ScreenPaired` | `{ screens }`. Het grote scherm is gekoppeld: door naar de tutorial. Kan vaker komen (tweede scherm). |
| `ViewersChanged` | `{ viewers }` |
| `LobbySkipped` | `{ viewers }`. De wachttijd is overgeslagen: start zodra de tutorial klaar is. |
| `VoteTally` | De lopende stemming: `{ voteId, id, question, message, seconds, remaining, total, options: [{ id, label, count }] }` |
| `VoteEnded` | `{ voteId, id, question, winner, decidedBy, total, options }`. **`winner == ""`: gelijkspel of niemand stemde, Kevin beslist.** `decidedBy` is `publiek` of `kevin`. |
| `TogetherEnded` | `{ attempt, success, presses, needed }`. Gelukt als iedereen binnen de tijd drukte. |
| `SessionEnded` | Sessie voorbij (door `EndSession`, of vervallen). |

### Telefoon en groot scherm

| Wie | Methode | Antwoord |
|---|---|---|
| Telefoon | `JoinAsPhone(code, clientId)` | `{ ok, error }` |
| Telefoon | `CastVote(voteId, option)` | – (de laatste druk telt) |
| Telefoon | `Press()` | – |
| Groot scherm | `JoinAsScreen(code)` | `{ ok, error }`. Het eerste scherm koppelt de sessie. |
| Groot scherm | `SkipLobby()` | – (knop "Skip countdown") |

`error`: `invalid` (geen 6 cijfers), `unknown`, `ended`, `notReady` (telefoon vóór de koppeling) of `rateLimited` (te veel foute codes vanaf hetzelfde IP: 20 per minuut).
`clientId` is een vaste id per telefoon (localStorage): opnieuw verbinden geeft geen extra kijker of dubbele stem.

**Server → telefoons en scherm: `State`**, bij elke verandering:

```json
{
  "code": "365895",
  "joinUrl": "https://collective-narratives.app/poll/365895",
  "phase": "lobby",
  "lobbyRemaining": 42.5,
  "screens": 1,
  "viewers": 5,
  "active": true,
  "vote": { "voteId": 3, "id": "telefoon", "question": "...", "message": "", "seconds": 12, "remaining": 7, "total": 4, "options": [{ "id": "opnemen", "label": "Answer it", "count": 3 }] },
  "last": { "voteId": 2, "id": "hond", "question": "...", "winner": "binnen", "decidedBy": "publiek", "total": 5, "options": [] },
  "together": { "active": false, "label": "", "seconds": 0, "remaining": 0, "presses": 0, "needed": 0 },
  "lastTogether": { "attempt": 0, "success": false, "presses": 0, "needed": 0 },
  "ending": "",
  "history": [],
  "gameConnected": true
}
```

`phase`: `pairing` → `lobby` → `playing` → `ended`. `lobbyRemaining` (seconden) is `null` tot `StartTutorial`. `gameConnected` is `false` zolang de game weg is (crash of verbinding kwijt). De rest heeft dezelfde vorm als `/api/state` van de oude Node-server.

## HTTP

| URL | Wat |
|---|---|
| `GET /api/sessions/{code}` | `{ status }`: `active` (200), `unknown` (404), `ended` (410), `invalid` (400), `rateLimited` (429) |
| `GET /api/qr/{code}.png` | QR naar `/poll/{code}` |
| `GET /api/choices` | De ingebouwde keuzes (voor de nep-game) |
| `GET /healthz` | `OK` |

## Sessies

- Code: 6 cijfers, willekeurig (cryptografisch), nooit een code die nog actief is of minder dan 24 uur geleden gebruikt werd.
- Game weg: na 2 minuten zonder verbinding vervalt de sessie (binnen die tijd: `ResumeSession`).
- Maximum 2 uur per sessie.
- Alles in het geheugen van de server: een herstart wist lopende sessies. **Niet deployen tijdens een voorstelling.**

## Zicht van de speler (HTTP, niet via de hub)

De beelden gaan niet via SignalR: dat zou base64 en grote berichten betekenen. Gewone HTTP is eenvoudiger en sneller.

| URL | Wie | Wat |
|---|---|---|
| `POST /api/sessions/{code}/frame` | game | Body: één JPEG (`Content-Type: image/jpeg`, max. 300 KB). Header `X-Game-Token: <gameToken>`. Antwoord 204; 429 = te snel na het vorige beeld (max. 25 per seconde), gewoon het volgende sturen. |
| `GET /api/sessions/{code}/frame.jpg` | groot scherm | Het nieuwste beeld, of 204 als er al 5 s geen is. |

`/screen` haalt ongeveer 8 beelden per seconde op en toont ze groot. Komt er 3 s niets, dan weer het titelbeeld. Samen-actie en einde liggen er bovenop. Testen zonder Unity: op `/host` het vakje “Nep-spelerzicht sturen” aanvinken.

## Nog niet gebouwd

- **Minimap** (fase 6): de plek staat al op `/screen`.
- **Vloeiender spelerzicht via WebRTC** (fase 5b), alleen als 8 beelden per seconde te schokkerig blijkt.
- **Kevin beslist door de telefoon echt vast te pakken** (XR Interaction Toolkit). Nu: kijken + trigger.
