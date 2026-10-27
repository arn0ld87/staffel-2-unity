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
10. 2026-10-27: Alex-Büste mit Shape Keys (Blinzeln_L/R, Mund_Auf, Mund_Schmal, Neutral) aus `tools/blender/alex_gesicht.py`; Vorschau `docs/alex/vorschau_uebersicht.png`. Neutral wirkt noch leicht schief.
