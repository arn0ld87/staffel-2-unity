# Plan: Staffel 2 als natives Unity-Spiel

Quelle der Inhalte ist `alexle135.de` auf `origin/main`. Die Webversion bleibt unverändert.

## Grenzen

- **Episodendaten**: werden aus den TypeScript-Episoden der Webversion nach JSON exportiert (`tools/export-episoden`), nicht von Hand abgeschrieben.
- **Ermittlungs-Engine (C#, ohne Unity)**: Prüfbündel, Auswertung, Belege, Rahmenverstoß, Sperre, Vorwurf. Port von `ermittlung.ts`.
- **Linux-Runtime**: Kindprozess `linux-host` (v86), Agent-Client, serielle Warteschlange. Siehe `runtime-spike.md`.
- **Spielstand**: JSON mit Versionsfeld unter `Application.persistentDataPath`.
- **UI**: Terminal mit eigenem VT-Emulator, Pager, Fallakte, Vorwurf, Befehlsübersicht und Einstellungen. Alles als Bildschirm-UI, nicht auf schrägen 3D-Flächen.
- **3D**: Serverraum, Interaktion, Zustandsanzeigen, Alex.

## Slices

| Nr | Inhalt | Akzeptanz |
|---|---|---|
| S0 | Inventar, Tooling, Repo | Inventar belegt, Editor installiert |
| S1 | Runtime-Spike | Prüfung vor/nach Lösung korrekt auf macOS und Linux |
| S2 | Unity-Projekt (URP, Pipeline), Core-Assembly mit Datenmodell und Engine | EditMode-Tests für Bündel, Auswertung, Vorwurf, Sperre grün |
| S3 | Episoden-Export E0–E3 nach JSON | Export deterministisch, Konsistenztest grün |
| S4 | Linux-Runtime in C# und Terminal-UI | PlayMode: Befehl tippen, Ausgabe lesen, Prüfung löst aus |
| S5 | Erster Abschnitt: Menü → Raum → Monitor → E0-Schritt → Pager → Beleg → Speichern/Fortsetzen, ein Sound, Platzhalterfigur | im gebauten macOS-Player durchgespielt |
| S6 | Fallakte, Hinweise, Vorwurf, Abschluss, Episodenwahl, Befehlsübersicht, Einstellungen | E0, E1, E3 per Skript durchgespielt |
| S7 | Raum-Assets, Licht, Rack-LEDs aus Spielzustand, Audio-Mixer | Lizenzinventar vollständig |
| S8 | Bildschirm-Image für E2 (Framebuffer, Maus) | E2 durchgespielt |
| S9 | Alex final mit Animationen (nach Freigabe) | Idle, Blinzeln, Sprechen, Zeigen, Tippen |
| S10 | Tests, Linux-Build auf gns3, Messung FPS/Ladezeit | Nachweise in `docs/TESTS.md` |
| S11 | Übergabe, Startanleitung, PR | |

## Risiken

- Episode 2 (grafisches Image) bleibt bis S8 offen.
- Ladezeit des ersten 9p-Zugriffs auf macOS (bis ca. 10 s): wird im Ladebildschirm verdeckt.
- Die Büste hat keine Gesichts-Shape-Keys, Blinzeln und Sprechen fehlen. Siehe `ALEX.md`.
- Auf gns3 gibt es keine GPU. Der Linux-Player-Test läuft dort unter Xvfb mit Software-Rendering, das ist kein Nachweis für die Bildrate.
