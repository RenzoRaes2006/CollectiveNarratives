# Git-workflow Unity – Collective Narratives (Fase C)

Oct 12, 2026

Elke wijziging komt op een eigen branch, wordt gecontroleerd en gaat via een Pull Request naar `main`. Niemand werkt rechtstreeks op `main`.

## Overzicht

1. `main` bijwerken
2. Eigen branch maken
3. Werken in Unity
4. Controleren
5. Committen
6. `main` in je branch halen
7. Pushen en Pull Request openen
8. Review door een teamgenoot en merge
9. Opruimen

## Voorwaarden (eenmalig)

Deze cyclus werkt pas als de setup af is:

- [ ] Unity: Project Settings > Version Control > Visible Meta Files (in oudere versies onder Editor)
- [ ] Unity: Asset Serialization staat op Force Text
- [ ] Officiële Unity `.gitignore` in de root van de repo (de map met `Assets/`, `Packages/`, `ProjectSettings/`)
- [ ] `.gitattributes` met de LFS-regels, vóór de eerste commit van assets
- [ ] `git lfs install` uitgevoerd op elke laptop
- [ ] Eerste commit staat op `main` op GitHub
- [ ] Elk teamlid heeft de repo gekloond (`git clone`)

## De stappen

**1. Start vanaf een actuele main**

```bash
git switch main
git pull
```

**2. Eigen branch per taak**

```bash
git switch -c feature/<naam>-<omschrijving>
# bv. feature/gerben-voting-ui
```

**3. Werk in Unity.** Sla daarna alles op (Ctrl+S).

**4. Controleer vóór je commit** (zie de checklist hieronder)

```bash
git status
git diff --stat
```

**5. Commit (klein en vaak)**

```bash
git add -A
git commit -m "feat: stemmen-UI toegevoegd"
```

**6. Haal main in je branch.** Conflicten los je hier op, niet op main.

```bash
git fetch origin
git merge origin/main
```

**7. Push en open een Pull Request**

```bash
git push -u origin feature/<naam>-<omschrijving>
gh pr create --fill --base main
# of: de knop "Compare & pull request" op github.com
```

**8. Review en merge.** Een teamgenoot kijkt de PR na. Daarna "Squash and merge" op GitHub.

**9. Opruimen**

```bash
git switch main
git pull
git branch -d feature/<naam>-<omschrijving>
```

Opmerkingen: `gh` (GitHub CLI) is optioneel, de webknop doet hetzelfde. `git switch` vraagt Git 2.23 of nieuwer, de oudere variant is `git checkout -b`.

## Controle vóór je commit (stap 4)

- [ ] `git status`: staat er alleen in wat je verwachtte?
- [ ] `git diff --stat`: geen onverwacht grote of talrijke wijzigingen (bv. tientallen scenes)?
- [ ] Elk nieuw bestand staat samen met zijn `.meta`-bestand in de commit
- [ ] Project opent in Unity en de Console toont geen errors
- [ ] Korte Play-test van het stuk dat je aanpaste
- [ ] Geen `Library/`, `Temp/` of `UserSettings/` in de lijst

## Teamregels Unity

Dit zijn gangbare best practices, geen officiële Unity-regels.

- Nooit rechtstreeks op `main` werken.
- Eén persoon per scene tegelijk. Scene-conflicten zijn de grootste pijn.
- Splits de scene op in prefabs en additieve scenes (VR-rig, verhaallogica, stem-UI, audio), zodat jullie minder snel hetzelfde bestand raken.
- Verwijder en hernoem assets altijd in Unity, zodat het `.meta`-bestand meegaat.
- Kleine, frequente commits en korte levensduur van branches.
- Zware assets (audio, modellen, textures) alleen via Git LFS, en niet onnodig opnieuw pushen: elke gewijzigde versie telt volledig mee in het LFS-quotum.

## Als er iets misgaat

| Probleem | Wat te doen |
| --- | --- |
| Push wordt geweigerd | Doe stap 6 opnieuw (`git fetch origin`, `git merge origin/main`) en push daarna weer. |
| Conflict in een scene of prefab | Stop en overleg met wie dat bestand ook aanpaste. Kies samen welke versie blijft. Is UnityYAMLMerge ingesteld, dan kan `git mergetool` een deel automatisch oplossen. |
| Teamgenoot ziet ontbrekende assets of lege verwijzingen | Controleer of de `.meta`-bestanden gecommit zijn en of die persoon `git lfs install` deed. |
| Assets zijn enkel kleine pointer-bestandjes | Het LFS-quotum van de repo-eigenaar kan op zijn. Controleer het gebruik in de GitHub-instellingen. |
| Per ongeluk gewerkt op main, nog niet gecommit | Maak alsnog een branch met `git switch -c feature/<naam>-<omschrijving>`. Je wijzigingen gaan mee. |

De laatste twee rijen en de LFS-opmerking in de tabel komen deels uit eigen kennis. Controleer ze bij `git-scm.com/docs` en `docs.github.com` voor je erop vertrouwt.
