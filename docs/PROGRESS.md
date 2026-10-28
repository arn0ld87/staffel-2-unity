# Fortschrittslog

1. 2026-10-27: Tooling geprüft.
   - Unity CLI 1.0.0-beta.14 ist installiert, vorher weder Editor noch Login noch Lizenz.
   - Installiert wird Unity 6000.3.27f1 LTS mit `mac-il2cpp`, `linux-mono` und `linux-il2cpp` (Freigabe von Alex).
   - code-review-graph kennt `alexle135.de`, der Graph steht aber auf einem Branch 63 Commits hinter `origin/main`. Deshalb wurde ein Referenz-Worktree `main-ref` angelegt und das Inventar per ctx erstellt.
2. 2026-10-27: Inventar Staffel 2 erstellt: E0–E3, Datenmodell, Agent-Protokoll, Spielstand.
3. 2026-10-27: Runtime-Spike bestanden auf macOS (Node, Bun-Binary) und auf Linux x86_64 (gns3). Siehe `runtime-spike.md`.
4. 2026-10-27: Core-Port (Engine, Agent-Protokoll, Spielstand) – 129 dotnet-Tests grün, Bündelskript byte-identisch zur TS-Referenz.
5. 2026-10-27: VT-Emulator, Prozessbrücke, Terminal-UI – 12 EditMode-Tests grün (`unity command test run`).
6. 2026-10-27: Speichern/Fortsetzen mit VM-Zustand (`linux-host` save/--state): Roundtrip-Test bestanden (13,9 MiB, Fortsetzen 0,6 s).
7. 2026-10-27: Erster spielbarer Abschnitt. Im Editor-Play-Mode E0 Schritte 1–3 mit echten Prüfungen gelöst, falsche Antwort abgewiesen.
8. 2026-10-27: Erster nativer macOS-Build (`Builds/macOS/LinuxKrimi.app`, Mono, arm64, 467 MB, 0 Fehler). Demo-Modus im Build: E0 komplett gelöst, Speichern mitten im Fall (15 MiB), Vorwurf richtig, Exit 0. Video: `docs/demo/staffel2-demo-episode0.mp4`.
   Offen: Im Hauptmenü dauerten während der Aufnahme einzelne Bilder mehrere Sekunden (Ursache ungeklärt); die angezeigte Falldauer im Demo-Lauf ist Spielzeit der Aufnahme, nicht Echtzeit.
9. 2026-10-27: Durchspielpläne E0–E3 (Lösungswege und Gegenproben wörtlich aus den E2E-Specs der Webversion) im Demo-Modus.
   Gefundene und behobene Fehler: Prüflauf-Rückrufe auf Hintergrundthreads (Unity-APIs warfen, Ereignisse gingen verloren);
   Speichern-Warteschlange blieb hängen (Reihenfolgefehler); „–“ im Spielstandpfad kam über stdin als „?“ an;
   Setups über 55 s (Grenze des mission-agent im Image) laufen jetzt losgelöst im Gast mit Abfrage des Exit-Codes.
   Nachweise (Editor-Play-Mode, Mac unter hoher Systemlast, Load 90–230):
   E0, E1, E3 je komplett inkl. Gegenproben (`docs/tests/editor-durchlauf-e0-e3.txt`), E2 komplett (`editor-durchlauf-e2-e0.txt`),
   E0 mit Speichern → Hauptmenü → Fortsetzen → Belege in der fortgesetzten VM 12/12 (`editor-durchlauf-e0-speichern.txt`).
   EditMode 13/13, dotnet 129/129.
10. 2026-10-27: Alex-Büste mit Shape Keys (Blinzeln_L/R, Mund_Auf, Mund_Schmal, Neutral) aus `tools/blender/alex_gesicht.py`; Vorschau `docs/alex/vorschau_uebersicht.png`. Neutral in `162cd43` symmetrisch korrigiert.
11. 2026-10-27: Raum-Ausbau (`154f2ae`, `6762a11`): reproduzierbarer Szenenbau (`SpielSzeneBuilder`), detaillierte Racks mit Einschüben/LED-Reihen/Kabeltrassen, emissive Leuchten, Doppelboden, Aktenwand, Volume-Profil (`ServerraumProfil.asset`: ACES, Bloom, Vignette, Belichtung). Im Editor-Play-Mode gespielt; Raum-Helligkeit im echten Build noch ungetestet.
12. 2026-10-27: Windows-x64-Build (`db58fae`, `c78eaf6`, `Builds/Windows/`): `linux-host` als Windows-Binary (`bun-windows-x64`), `CommandLineToArgvW`-Quotierung (EditMode-Tests 14/14). Auf `win-server` (`C:\staffel2-test\`) getestet: Lauf 1 war 8/12 (Demo tippte bei Software-Rendering zu früh, `docs/tests/windows-build-e0-win-server-lauf1.txt`), nach Warten auf gerenderte Bilder in Lauf 2 E0 12/12 bestanden (`docs/tests/windows-build-e0-win-server.txt`). Ohne GPU (1–2 fps) und ohne Audiogerät: keine FPS- oder Sound-Aussage.
13. 2026-10-27: Linux-Build-Diagnose auf `gns3` (`~/staffel-2-unity-test/`, Stand vor `c78eaf6`): lief 5 h in Docker (Xvfb, llvmpipe) und hing nach genau 1 gerendertem Bild (`ergebnis/bilder/bild_00000.jpg`, Protokoll nur Startzeile, kein `linux-host`-Prozess gestartet, 329 Threads in `futex_do_wait`, `ergebnis/player.log` nur ALSA/FMOD-Fehler). Container entfernt; Ursache ungelöst.
14. 2026-10-27: Kino-Modus (`76407a5`, `-kino`): geführter Rundgang durch den Raum, Menü mit Befehlsübersicht und Einstellungen, menschliches Tippen mit Vertippern, sichtbare Gegenproben (`grpe`, falsche Antwort, falscher Vorwurf), Speichern über Pausemenü und Fortsetzen per Knopf. Kompiliert, noch ungetestet.
15. 2026-10-28: Linux-/Headless-Hänger-Ursache behoben (`GameRoot.cs`, `SceneBuilder.cs`): `PlayerSettings.runInBackground = true`, `Application.runInBackground = true` und Auswertung von `-screen-fullscreen 0` in `GameRoot.Awake()`, damit `Screen.fullScreen = Einstellungen.Vollbild` unter Xvfb keinen zweiten Vollbild-Request mehr auslöst.
16. 2026-10-28: Kino-Modus im Editor-Play-Mode durchgelaufen (`docs/tests/editor-kino-e0.txt`): 13/13 Prüfungen bestanden, inkl. Rundgang, Gegenproben (`grpe`, falsche Antwort, falscher Vorwurf), Speichern über Pausemenü und Fortsetzen.
17. 2026-10-28: Ladebildschirm-Fortschritt (`GameRoot.cs`, `LadeBildschirm.cs`), Raum-Helligkeit (`SceneBuilder.cs`: Ambient, Punkt-/Spotlichter, `postExposure = 1.75`, mittlere Luminanz im Play-Mode von `0.099` auf `0.251`), `THIRD_PARTY_ASSETS.md` und `README.md` umgesetzt.
18. 2026-10-28: Nativer Bildschirm-Modus für Episode 2 (Slice S8): `linux-host.mjs` streamt den v86-VGA-Textpuffer (80×25 Zeichen + Attributbyte) bei Änderung als Base64-Event `{"ev":"screen","cols":80,"rows":25,"data":"..."}`; `ProcessBridge.cs`, `LinuxSitzung.cs`, `BildschirmAnsicht.cs` und `TerminalAnsicht.cs` rendern den Live-VGA-Monitor mit Modus-Umschalter (`F3`, `TERMINAL (SSH)` / `BILDSCHIRM (VGA)`). EditMode-Tests 16/16 grün.
19. 2026-10-28: Vollständiger 3D-Charakter von Alex (1,80 m, `tools/blender/alex_ganzkoerper_live.py`, `Game/Assets/Art/Alex/Alex_Bueste.fbx`, `Alex_Ganzkoerper.blend`) in Blender modelliert (Kapuzenpulli mit Kordeln/Kängurutasche, zeigende rechte 3D-Hand, Jeans, weiße Sneaker, 9-Bone-Skelett-Rig, 6 Gesichts-Shape-Keys, 3 Animationen `Alex_Idle`, `Alex_Explain`, `Alex_Listening`) und als ansprechbare (`E`) `AlexRaumFigur` im Serverraum platziert (`AlexBuilder.cs`, `AlexDialogStudio.cs`, `GameRoot.cs`), plus 4 gerahmte Wandposter mit Referenzen auf `alex-schneider.dev` (`SceneBuilder.cs`, `Game/Assets/Art/Brand/`). Im Editor-Play-Mode geprüft (`docs/alex/serverraum_alex_und_poster.png`).
