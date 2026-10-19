# Koppeling webapp ↔ Unity (Home Alone VR)

Dit document is de afspraak tussen de stem-webapp (map `webapp/`) en het Unity-project (`CN_VR/`).
Iemand kan hiermee de Unity-kant bouwen (QR-code, stemmen starten, resultaat gebruiken) terwijl de webapp verder wordt gebouwd.
**Verander je iets aan de API? Pas dan eerst dit document aan en laat het het andere team weten.**

## Wie doet wat

| Onderdeel | Taak |
|---|---|
| **Unity** (headset + groot scherm) | Stuurt het verhaal. Bepaalt wanneer een keuze begint, voert het resultaat uit, berekent het einde. Toont de QR-code en "x kijkers verbonden" op het grote scherm. |
| **Webapp-server** (laptop, Node.js) | Telt stemmen. Kent **geen** verhaallogica: alleen de vraag, de opties, de tijd en de uitslag. |
| **Telefoons** (publiek) | Openen de stempagina via de QR-code en drukken op een knop. |

```
 Telefoons ──WebSocket──▶ Server (laptop :3000) ◀──HTTP── Unity (Quest / pc)
                             ▲
                     /host (testpagina, browser)
```

Unity praat **alleen via gewone HTTP** met de server (`UnityWebRequest`). Geen WebSocket-package nodig.

## Server starten

```bash
cd webapp
npm install
npm start
```

De console toont de adressen, bv.:

```
Telefoons:  http://192.168.1.10:3000/
Host/test:  http://192.168.1.10:3000/host
```

- Laptop, Quest en telefoons moeten op **hetzelfde wifi-netwerk** zitten. Schoolwifi blokkeert vaak verkeer tussen toestellen: gebruik dan een eigen router of een hotspot.
- Windows vraagt de eerste keer of Node door de firewall mag: kies **Privénetwerken toestaan**.
- Pakt de server het verkeerde IP (bv. door VirtualBox of een VPN), start dan met een vast adres:
  `PUBLIC_URL=http://192.168.1.10:3000 npm start` (PowerShell: `$env:PUBLIC_URL="http://192.168.1.10:3000"; npm start`).
- `/host` is een testpagina: daarmee kun je stemmingen starten en volgen zonder Unity.

## Instellingen in Unity (eenmalig)

- **Project Settings > Player > Other Settings > Allow downloads over HTTP** op **Always allowed** zetten. Nu staat het op *Not allowed*, en dan blokkeert Unity elke `http://`-request.
- Maak het server-adres instelbaar in de Inspector (bv. `http://192.168.1.10:3000`). Het IP verandert per netwerk.

## De keuzes (id's)

Unity start een keuze met één van deze id's. De teksten staan in `webapp/choices.js`.

| id | Vraag | Opties (id → tekst) |
|---|---|---|
| `telefoon` | De telefoon gaat. | `opnemen` Opnemen · `rinkelen` Laten rinkelen |
| `hond` | Gekrab aan de achterdeur. | `binnen` Hond binnenlaten · `buiten` Buiten laten |
| `stroom` | De stroom valt uit. | `kelder` Naar de kelder · `zaklamp` Zaklamp zoeken |
| `inbreker` | De achterdeur gaat open. | `kast` Verstoppen in de kast · `raam` Vluchten door het raam |

Eindes (voor `/api/end`): `held` (De held van de straat), `weg` (Net op tijd weg), `cadeau` (Het cadeau is weg).

## API

Alle antwoorden zijn JSON. Alle `POST`-bodies zijn JSON met `Content-Type: application/json`.
De velden zijn zo gekozen dat `JsonUtility` ze kan lezen: geen dictionaries, alleen arrays en gewone velden.

### `GET /api/info`

Adres voor de QR-code.

```json
{ "joinUrl": "http://192.168.1.10:3000/" }
```

### `GET /api/qr.png`

Kant-en-klare QR-code (PNG) die naar `joinUrl` wijst. Unity laadt die als texture, zie het voorbeeld hieronder.

### `POST /api/start` — keuze starten

```json
{ "id": "inbreker", "seconds": 8, "message": "Ze zijn sneller dan verwacht." }
```

- `seconds` is optioneel (standaard **12**). Unity geeft 8 mee als de telefoon niet werd opgenomen.
- `message` is optioneel: extra regel die de telefoons tonen.
- Een nieuwe start stopt een stemming die nog liep.

Antwoord: de nieuwe stemming (zelfde vorm als `vote` in `/api/state`). **Onthoud `voteId`.**

### `GET /api/state` — alles in één keer (pollen, bv. 4× per seconde)

```json
{
  "viewers": 5,
  "active": true,
  "vote": {
    "voteId": 3,
    "id": "telefoon",
    "question": "De telefoon gaat. Wat doet Kevin?",
    "message": "",
    "seconds": 12,
    "remaining": 7,
    "total": 4,
    "options": [
      { "id": "opnemen", "label": "Opnemen", "count": 3 },
      { "id": "rinkelen", "label": "Laten rinkelen", "count": 1 }
    ]
  },
  "last": {
    "voteId": 2,
    "id": "hond",
    "winner": "binnen",
    "decidedBy": "publiek",
    "total": 5,
    "options": [
      { "id": "binnen", "label": "Hond binnenlaten", "count": 4 },
      { "id": "buiten", "label": "Buiten laten", "count": 1 }
    ]
  },
  "together": { "active": false, "label": "", "seconds": 0, "remaining": 0, "presses": 0, "needed": 0 },
  "lastTogether": { "attempt": 0, "success": false, "presses": 0, "needed": 0 },
  "ending": "",
  "history": [ { "...": "zelfde vorm als last" } ]
}
```

| Veld | Betekenis |
|---|---|
| `viewers` | Aantal verbonden telefoons. Voor "5 van 5 kijkers verbonden". |
| `active` | `true` zolang er gestemd wordt. Bij `false` is `vote` `null`. |
| `last` | De laatst afgeronde keuze, of `null` als er nog geen is. |
| `last.winner` | Optie-id van de winnaar. **Leeg (`""`) bij gelijkspel of als niemand stemde: dan beslist Kevin** (wat hij het eerst aanraakt). |
| `last.decidedBy` | `"publiek"`, of `"kevin"` (gelijkspel/geen stemmen, of nadat Unity `/api/decide` stuurde). |
| `history` | Alle afgeronde keuzes van deze speelronde, in volgorde. Handig voor het einde en de verhaalboom. |

Let op: `JsonUtility` maakt van `null` een leeg object. Kijk daarom naar `active` en naar `last.voteId` (`0` = nog geen resultaat), niet naar `null`.

### Wachten op het resultaat

1. `POST /api/start` → bewaar `voteId` uit het antwoord.
2. Poll `/api/state` tot `last.voteId == voteId`.
3. `last.winner` is gevuld → voer die optie uit. `last.winner == ""` → laat Kevin kiezen en stuur zijn keuze met `/api/decide`.

### `POST /api/decide` — Kevin heeft zelf gekozen

```json
{ "voteId": 3, "option": "opnemen" }
```

Zet `winner` in `last`/`history` op die optie (`decidedBy: "kevin"`). Zo kloppen de keuzes op de telefoons aan het einde.

### `POST /api/stop`

Stopt de lopende stemming meteen en telt de stemmen (normaal niet nodig, de timer doet dit).

### `POST /api/together` — samen-actie in de finale

```json
{ "label": "Laat de hond blaffen", "seconds": 5 }
```

Alle telefoons krijgen één grote knop met `label`. Unity kiest de tekst zelf op basis van de eerdere keuzes.

- `together.presses` / `together.needed`: voor "3 van 5 drukken..." en de balk op het grote scherm. `needed` is het aantal kijkers op het moment van starten.
- Gelukt zodra **iedereen** binnen de tijd gedrukt heeft. Anders mislukt na `seconds` (standaard 5).
- Resultaat in `lastTogether` (`attempt` telt op per poging, `success` true/false).
- Tweede kans: stuur gewoon nog een `POST /api/together`.

### `POST /api/end` — einde tonen op de telefoons

```json
{ "ending": "held" }
```

Telefoons tonen "Einde x van 3", de naam van het einde en hun keuzes.

### `POST /api/reset` — nieuwe speelronde

Wist stemmingen, history, samen-actie en einde. De telefoons blijven verbonden. Roep dit aan bij het begin van elke ronde.

## Wat de telefoons zien

| Server-staat | Telefoon toont |
|---|---|
| Geen stemming | "Jullie zijn het lot. Kevin hoort jullie niet. Praat dus gerust hardop." + wachten |
| `active` | Vraag, `message`, 2 knoppen, aftelbalk. Wisselen mag, de laatste druk telt. |
| Net afgerond | De uitslag ("Het publiek koos: …" of "Gelijkspel: Kevin beslist") |
| `together.active` | Eén grote knop met het label |
| `ending` gezet | "Einde x van 3", naam van het einde, lijst met keuzes |

## Voorbeeldcode Unity (C#)

```csharp
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[Serializable] public class VoteOption { public string id; public string label; public int count; }
[Serializable] public class Vote { public int voteId; public string id; public string question; public string message; public int seconds; public int remaining; public int total; public VoteOption[] options; }
[Serializable] public class VoteResult { public int voteId; public string id; public string winner; public string decidedBy; public int total; public VoteOption[] options; }
[Serializable] public class Together { public bool active; public string label; public int seconds; public int remaining; public int presses; public int needed; }
[Serializable] public class TogetherResult { public int attempt; public bool success; public int presses; public int needed; }
[Serializable] public class VoteState { public int viewers; public bool active; public Vote vote; public VoteResult last; public Together together; public TogetherResult lastTogether; public string ending; public VoteResult[] history; }

[Serializable] class StartBody { public string id; public int seconds; public string message; }
[Serializable] class DecideBody { public int voteId; public string option; }

public class VoteClient : MonoBehaviour
{
    public string serverUrl = "http://192.168.1.10:3000";
    public RawImage qrImage; // op het grote scherm

    public VoteState State { get; private set; }

    IEnumerator Start()
    {
        yield return LoadQr();
        while (true)
        {
            yield return Poll();
            yield return new WaitForSeconds(0.25f);
        }
    }

    IEnumerator Poll()
    {
        using var req = UnityWebRequest.Get(serverUrl + "/api/state");
        req.timeout = 2;
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success)
            State = JsonUtility.FromJson<VoteState>(req.downloadHandler.text);
    }

    IEnumerator LoadQr()
    {
        using var req = UnityWebRequestTexture.GetTexture(serverUrl + "/api/qr.png");
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success && qrImage != null)
            qrImage.texture = DownloadHandlerTexture.GetContent(req);
    }

    IEnumerator PostJson(string path, string json, Action<string> onDone = null)
    {
        using var req = UnityWebRequest.Post(serverUrl + path, json, "application/json");
        req.timeout = 2;
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success) onDone?.Invoke(req.downloadHandler.text);
        else Debug.LogWarning($"{path} mislukt: {req.error}");
    }

    // Start een keuze en geeft de uitslag terug (null = server niet bereikbaar).
    // uitslag.winner == "" betekent: Kevin beslist.
    public IEnumerator RunVote(string id, int seconds, Action<VoteResult> onResult, string message = "")
    {
        int voteId = 0;
        var body = JsonUtility.ToJson(new StartBody { id = id, seconds = seconds, message = message });
        yield return PostJson("/api/start", body, text => voteId = JsonUtility.FromJson<Vote>(text).voteId);
        if (voteId == 0) { onResult(null); yield break; }

        yield return new WaitUntil(() => State != null && State.last != null && State.last.voteId == voteId);
        onResult(State.last);
    }

    public IEnumerator Decide(int voteId, string option) =>
        PostJson("/api/decide", JsonUtility.ToJson(new DecideBody { voteId = voteId, option = option }));
}
```

Gebruik, bv. in hoofdstuk 1:

```csharp
VoteResult uitslag = null;
yield return voteClient.RunVote("telefoon", 12, r => uitslag = r);

string keuze = uitslag != null ? uitslag.winner : "";
if (keuze == "")
{
    // Gelijkspel, niemand stemde of server weg: Kevin beslist.
    yield return WachtTotKevinIetsAanraakt(gekozen => keuze = gekozen); // eigen code
    if (uitslag != null) yield return voteClient.Decide(uitslag.voteId, keuze);
}

if (keuze == "opnemen") NeemOp();
else LaatRinkelen();
```

Als de server niet bereikbaar is, loopt het spel zo gewoon door: Kevin beslist dan elke keuze zelf.

## Nog niet in de webapp (later afspreken)

- **Eigen camera per kijker** (hoofdstuk 3): Unity moet dan beelden naar de telefoons sturen. Nog uit te zoeken; voorlopig doet het grote scherm dit.
- **Scares vanaf de telefoon** (vloer kraakt, licht knippert, hond blaft): idee is een knop op de telefoon → event dat Unity ophaalt. Nog niet gebouwd.
