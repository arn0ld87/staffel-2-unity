# Übergabe: Staffel 2 als natives Unity-Spiel

Stand: 2026-10-28, Branch `feat/alex-trellis` (Commit `d73ea0c` plus diese Datei).
Zielgruppe: der nächste Agent. Erst diese Datei, dann `README.md`, `docs/PLAN.md`, `docs/PROGRESS.md` lesen.
Details stehen dort und in der Git-Historie; hier steht nur, was man zum Weitermachen braucht und nicht ohne Weiteres findet.

## Kurz: wo das Projekt steht

- Spielbar: Episoden 0–3 mit echtem Linux (v86/Alpine als abgeschotteter Kindprozess, ohne Netz), Terminal, Pager, Fallakte,
  Vorwurf, Speichern/Fortsetzen mit VM-Zustand, Bildschirm-Modus für E2, Demo- und Kino-Modus. Belege: `docs/tests/`.
- Builds: macOS (getestet), Windows x64 (auf einem Windows-11-Rechner im Demo-Modus E0 12/12), Linux x86_64 (gebaut;
  Headless-Hänger laut `PROGRESS.md` Nr. 15 behoben, **erneuter Lauf steht aus** → ungetestet).
- Alex: neue Ganzkörper-Figur aus TRELLIS mit Porträt-Gesicht, Blinzeln, Mund, vier Clips. Sie ist die einzige Figur im Raum
  und im Pager. Die Tripo-Büste ist nur noch Rückfall, Geminis Ganzkörper-Figur wird beim Szenenaufbau entfernt.

## Offene PRs (Stack, von unten nach oben mergen, jeweils Merge-Commit)

| PR | Branch → Basis | Inhalt |
|---|---|---|
| [#1](https://github.com/arn0ld87/staffel-2-unity/pull/1) | `codex/staffel-2-unity` → `main` | das ganze Spiel, Lizenzen, Codex-Findings behoben |
| [#2](https://github.com/arn0ld87/staffel-2-unity/pull/2) | `feat/alex-ganzkoerper` → `codex/staffel-2-unity` | Geminis Ganzkörper-Figur, Poster, Aufräumen |
| [#3](https://github.com/arn0ld87/staffel-2-unity/pull/3) | `feat/alex-trellis` → `feat/alex-ganzkoerper` | neuer TRELLIS-Alex, ersetzt Raumfigur und Pager, Zeigen |

Nichts mergen ohne Freigabe von Alex. Merge-Strategie beim Ansagen begründen (Stack → Merge-Commit).
Das Repo ist **öffentlich**: keine Hostnamen-Details, Tokens, IPs oder privaten Pfade in Commits und Doku.

## Pipeline der neuen Alex-Figur (reproduzierbar)

1. Vorlagen (Zeichnungen, mit Gemini erzeugt): `art-source/alex/referenz/alex-hoodie-front.png` (Ganzkörper, A-Haltung),
   `alex-portraet-front.png` (Gesicht frontal, 1254²).
2. Bild → 3D: Hugging-Face-Space `trellis-community/TRELLIS` über `gradio_client`, Seed 42, `mesh_simplify=0.7`,
   `texture_size=2048` → `art-source/alex/trellis/alex-hoodie-trellis-fein.glb`. Das Skript dazu lag nur im Scratchpad;
   bei Bedarf neu schreiben (Endpunkte `/start_session`, `/preprocess_image`, `/generate_and_extract_glb`).
   Anonymes ZeroGPU-Kontingent reicht für etwa einen Lauf am Tag; mit HF-Token des Nutzers mehr (Token nie ausgeben,
   nur als Umgebungsvariable `HF_TOKEN` für den einen Aufruf setzen).
3. Gesicht: `tools/blender/alex_trellis_gesicht.py <fein.glb> <portraet.png> <aus.glb> [vorschau]` — Doppelpunkte
   verschmelzen, Stirn glätten, frontale Projektion mit Sichtbarkeitstest (BVH), in die Textur einbrennen.
4. Rig: `tools/blender/alex_trellis_rig.py <gesicht.glb> Game/Assets/Art/Alex/Alex_Trellis.fbx <vorschau>` — 1,80 m,
   21 Knochen, Lid- und Mund-Patches als Shape Keys (`Blinzeln_L/R`, `Mund_Auf`, `Mund_Schmal`), Clips
   `Alex_Idle/Explain/Listening/Zeigen`. Nutzt Blender-5-Slot-Actions (läuft nicht auf 4.x).
5. Unity: `AlexTrellisBuilder.Einrichten()` (läuft auch in `SpielSzeneBuilder.Alles()`) → Materialien, Controller,
   `Resources/Alex/AlexTrellis.prefab`, Objekt `AlexRaumFigurNeu` im Serverraum.

Vorschauen und Unity-Kontrollbilder: `art-source/alex/trellis/vorschau/`. Die Zwischen-GLB aus Schritt 3 ist nicht
versioniert (reproduzierbar).

## Arbeitsweise mit Unity (wichtig, sonst Zeitverlust)

- Shell ist **fish**. Immer `tools/dev/uc <gruppe> <befehl>` nutzen (setzt Projektpfad und `--caller plugin --skill unity-cli`).
  Verfügbare Befehle über die Kommandosuche der CLI ermitteln, nichts raten (`editor stop` z. B. gibt es nicht).
- Play Mode starten/stoppen: `tools/dev/uc script eval --code 'UnityEditor.EditorApplication.isPlaying = true; return "x";'`.
- Nach Code-Änderungen: `AssetDatabase.Refresh()` per `script eval`, dann warten, bis `editor status`
  `"status":"ready","compiling":false,"domainReloadInProgress":false` meldet (Domain-Reload dauert oft > 30 s).
  Danach `EditorUtility.scriptCompilationFailed` prüfen.
- Szene nur über Builder bauen (`Staffel2.Game.Editor.SpielSzeneBuilder.Alles()`), nie Scene-YAML von Hand.
  Szenenbau scheitert im Play Mode (`EditorSceneManager.NewScene`) → vorher stoppen.
- Tests: `tools/dev/uc test run --mode EditMode` (zuletzt 14/14), Kern: `dotnet test` in `core/` (zuletzt 130/130).
- Unity-„Fake-Null“: bei Komponenten kein `??`/`?.`, sondern explizit `== null` prüfen.
- Nach jedem Play-Mode-Lauf: `git checkout -- 'Game/Assets/TextMesh Pro'` falls sich Schrift-Atlanten geändert haben
  (eigentlich fest gebacken, siehe `SchriftAtlas.Backen()`).
- Der Szenenbau schreibt `ServerraumProfil.asset` und `AlexDialog.controller` jedes Mal neu (neue GUIDs) — mitcommitten,
  weil die Szene darauf verweist.
- Nur **ein** Agent steuert den Editor. Blender-Arbeit kann parallel laufen (headless, `-b --factory-startup`,
  Engine heißt `BLENDER_EEVEE`).
- Konsole lesen: `tools/dev/uc console get --tail 50 --level error`; Stacktraces stehen im JSON-Feld `stackTrace`.
  Zeitstempel beachten: alte Einträge bleiben stehen.

## Nächste Schritte (Vorschlag, in dieser Reihenfolge)

1. **macOS-Build mit neuem Alex** bauen und im Demo-Modus E0 durchspielen; Kontrollbild der Raumfigur beim Zeigen
   aufnehmen (bisher nur per Winkelmessung belegt, Kontrollkamera hatte die Figur verfehlt).
2. **Linux-Build** neu bauen und auf dem Linux-Testhost (Docker, Xvfb, `tools/linux-test/`) laufen lassen; Ergebnis in
   `PROGRESS.md` nachtragen. Ohne GPU: keine FPS-Aussage.
3. **FPS/Ladezeiten** auf einem nicht ausgelasteten Mac bei 1080p messen; bei Last als ungültig vermerken.
4. Figur-Feinschliff (nur auf Wunsch): Hände ohne Finger und fleckig; Lider einfarbig; Mund minimal asymmetrisch;
   leichte Facetten am Hals.
5. `PROGRESS.md` Nr. 18 beschreibt den Bildschirm-Modus als VGA-**Textpuffer**; geprüft wurde im Editor ein grafischer
   Openbox-Desktop als Bild. Beschreibung gegen den Code (`runtime/linux-host.mjs`, `BildschirmAnsicht.cs`) abgleichen.
6. Kleinere Altlasten: Warnungen CS0067 (`LinuxSitzung.Gespeichert`) und CS0414 (`GameRoot._speichertGerade`);
   Schrittgeräusche `footstep_concrete_*.ogg` fehlen (Warnung beim Szenenbau); `Zeigen` wird nur beim Ansprechen
   im Raum ausgelöst.

## Entscheidungen, die nicht im Code stehen

- Lizenz: Code MIT; Inhalte CC BY-SA 4.0; alles mit Tripo-Anteil (alte Büste, Geminis Ganzkörper-Figur) CC BY-NC-SA 4.0,
  weil die Büste im kostenlosen Tripo-Plan entstand. Die TRELLIS-Figur hat keine Tripo-Anteile → CC BY-SA 4.0.
  Keine Persönlichkeitsrechte am Abbild eingeräumt; Name/Domain alex-schneider.dev nicht lizenziert.
  Siehe `LICENSE`, `LICENSE-INHALTE.md`, `THIRD_PARTY_ASSETS.md`.
- Gezeichneter Stil statt Foto für Alex (einheitlich mit Pager und Webversion, Datenschutz beim Hochladen).
- Hawaii-Variante der Zeichnung existiert, ist aber nur als spätere Kostüm-/Easter-Egg-Idee vorgesehen.
- Die Webversion im Website-Repo bleibt unverändert und ist Quelle für Episoden und Linux-Image
  (`tools/prepare-runtime.sh` braucht sie; die Laufzeitdateien sind nicht im Repo).

## Vorgeschlagene Skills

- `unity:unity-cli` — vor jeder Editor-Steuerung (Befehle, Verifikationsablauf, Play Mode, Builds).
- `context-mode:context-mode` — für lange Build-, Test- und Log-Ausgaben.
- `code-review-graph` — prüfen, ob das Repo registriert ist; C# wird ggf. nicht abgedeckt, dann gezielt lesen.
- `unity:build-live-game` bzw. `run` — für den Build-und-Start-Test.
- `review-changes` / `mattpocock-skills:code-review` — vor dem Mergen des Stacks, kurz halten (nur Blocker).
- `coderabbit` — falls am PR Bot-Findings abgeholt werden sollen.
