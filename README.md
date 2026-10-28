# Ein Linux-Krimi – Staffel 2 (Native Unity-Portierung)

Native 3D-Portierung des Lernspiels **„Ein Linux-Krimi – Staffel 2: Die erste Schicht bei der Rechenwerk Ostwind GmbH“** auf Basis von **Unity 6000.3.27f1 (URP 17.3)**.

Spielerinnen und Spieler bewegen sich in der Ich-Perspektive durch einen begehbaren 3D-Serverraum, nehmen Aufträge von **Alex** (animierte 3D-Büste im Pager) entgegen und lösen vier aufeinander aufbauende Linux-Forensik-Episoden (**Episode 0 bis Episode 3**) an einem echten, vom Host-System isolierten Linux-Terminal (v86-VM über `linux-host`).

---

## 1. Projektstruktur

- `core/` – dotnet-Projekt für den reinen C#-Kern (`Staffel2.Core`, `.NET Standard 2.1`); die Quellen liegen unter `Game/Assets/Scripts/Core/` (Episodendaten, Beleg- und Antwortprüfer, Vorwurf-Logik, Spielstand-Serialisierung, Protokoll für den `linux-host`).
- `core/tests/` – 129 xUnit-Tests für `Staffel2.Core`.
- `Game/` – Unity-6-Projekt (URP 17.3), Szenen-Builder (`SceneBuilder.cs`, `SpielSzeneBuilder.cs`), 3D-Serverraum, Terminal-Emulator (`VtScreen.cs`, `TerminalAnsicht.cs`), grafischer Bildschirm für Episode 2 (`BildschirmAnsicht.cs`), UI (Hauptmenü, Fallakte, Pager, Pausemenü, Ladebildschirm, Abschlusskarte) und Demo-/Kino-Regie (`DemoRegie.cs`).
- `tools/prepare-runtime.sh` – Bereitet die isolierte Linux-Laufzeit (Bun-kompilierte `linux-host`-Programme je Plattform, `v86.wasm`, VM-Image `alpine-fs.json`/`alpine-state.bin.zst` samt `bildschirm/`, BIOS-Dateien) unter `Game/Assets/StreamingAssets/linux/` vor.
- `tools/dev/uc` – Wrapper für das Unity-CLI (`--project-path Game --caller plugin --skill unity-cli`).
- `tools/blender/alex_gesicht.py` – Erzeugt deterministisch die Shape Keys (`Blinzeln_L`, `Blinzeln_R`, `Mund_Auf`, `Mund_Schmal`, `Neutral`) und Materialien der Alex-Büste.
- `docs/` – Architektur (`docs/runtime-spike.md`), Plan (`docs/PLAN.md`), Fortschrittsprotokoll (`docs/PROGRESS.md`) und Testnachweise (`docs/tests/`).

---

## 2. Voraussetzungen & Vorbereitung der Linux-Laufzeit

1. **Unity Editor**: Version `6000.3.27f1` mit URP 17.3 sowie den Build-Modulen für macOS, Linux und Windows (Mono).
2. **.NET SDK**: für die Ausführung der Core-Tests (`dotnet test`).
3. **Linux-Laufzeit vorbereiten** (einmalig nach dem Klonen bzw. vor dem ersten Start/Build, da `Game/Assets/StreamingAssets/linux/` gitignored ist):

```bash
./tools/prepare-runtime.sh
```

---

## 3. Starten und Spielen

### Im Unity Editor
1. Projekt `Game/` in Unity `6000.3.27f1` öffnen.
2. Szene `Assets/Scenes/Serverraum.unity` öffnen (oder per CLI neu aufbauen: `./tools/dev/uc script eval --code 'return Staffel2.Game.Editor.SpielSzeneBuilder.Alles();'`).
3. **Play (▶)** drücken (bzw. `./tools/dev/uc editor.playmode start`).
4. Tipp für volle Ansicht im Editor: Maus über die **Game**-Ansicht bewegen, **Shift + Leertaste** drücken und die Auflösung oben auf **1920×1080** stellen.

### Im gebauten Spiel (macOS / Linux / Windows)
- **macOS**: `Builds/macOS/LinuxKrimi.app` starten.
- **Linux**: `Builds/Linux/LinuxKrimi.x86_64` ausführen. *Ungetestet:* Im Test unter Xvfb ohne GPU blieb der Player nach dem ersten Bild stehen (siehe `docs/PROGRESS.md`).
- **Windows**: `Builds/Windows/LinuxKrimi.exe` starten.

*Hinweis zur Ladezeit*: Beim Start einer Episode wird ein echtes Linux-System initialisiert und in drei Schritten eingerichtet (`Rahmen`, `Einsatzrahmen`, `Episode`). Der Ladebildschirm zeigt den aktuellen Schritt (`1 von 3` bis `3 von 3`) und die verstrichene Zeit in Sekunden an. Bei normaler Systemlast dauert dies ca. 11–15 Sekunden; unter hoher Systemlast entsprechend länger.

---

## 4. Steuerung im Spiel

| Taste / Aktion | Funktion |
|---|---|
| **Mausklick ins Bild** | Mauszeiger für die Ich-Perspektive einfangen |
| **W, A, S, D** + **Maus** | Durch den Serverraum gehen und umsehen |
| **E** | Mit dem anvisierten Objekt im Raum interagieren (Monitor → Linux-Terminal, Aktenwand → Fallakte, Pager → Alex-Nachrichten) |
| **F** (im Raum) / **F2** (im Terminal) | Fallakte öffnen (Auftrag, Spuren, Belege prüfen, Fragen beantworten, Vorwurf erheben) |
| **P** | Pager öffnen (Dialoge und Hinweise von Alex mit animierter 3D-Büste) |
| **F10** (im Terminal) | Vom Terminal zurück in den 3D-Serverraum treten |
| **Esc** | Aktuelles Fenster schließen bzw. im Raum das Pausemenü öffnen (Fortsetzen, Speichern, Befehlsübersicht, Einstellungen, Hauptmenü) |

---

## 5. Bauen (macOS, Linux, Windows)

Über den Unity-CLI-Wrapper `tools/dev/uc` (setzt einen geöffneten Unity Editor voraus):

```bash
# macOS (StandaloneOSX)
./tools/dev/uc build run --target StandaloneOSX --outputPath /Volumes/T7/Projekte/staffel-2-unity/Builds/macOS/LinuxKrimi.app

# Linux x86_64 (StandaloneLinux64)
./tools/dev/uc build run --target StandaloneLinux64 --outputPath /Volumes/T7/Projekte/staffel-2-unity/Builds/Linux/LinuxKrimi.x86_64

# Windows x64 (StandaloneWindows64)
./tools/dev/uc build run --target StandaloneWindows64 --outputPath /Volumes/T7/Projekte/staffel-2-unity/Builds/Windows/LinuxKrimi.exe
```

Den Build-Fortschritt prüft man mit `./tools/dev/uc build status`.

---

## 6. Automatische Tests & Demo-/Kino-Modus

### C#-Kern-Tests (`Staffel2.Core.Tests`)
```bash
dotnet test core/tests/Staffel2.Core.Tests.csproj
```

### Automatisierter Durchlauf im Build (`-demo`)
Jeder Build unterstützt einen automatisierten Durchlauf einer oder mehrerer Episoden samt Prüfprotokoll und FPS-Statistik:

```bash
Builds/macOS/LinuxKrimi.app/Contents/MacOS/Ein\ Linux-Krimi\ -\ Staffel\ 2 \
  -demo -episoden 0 -schnell \
  -protokoll docs/tests/macos-build-e0.txt \
  -spielstand /tmp/staffel2-stand \
  -aufnahme /tmp/staffel2-bilder \
  -logFile /tmp/staffel2-player.log
```

- `-episoden 0,1,2,3`: Zu spielende Episoden (kommagetrennte Nummern, Standard `0`).
- `-schnell`: Kompakter Prüfdurchlauf.
- `-kino`: Ausführlicher Vorführmodus (Menü-Rundgang mit Befehlsübersicht und Einstellungen, Kamerafahrt durch den Serverraum, menschliches Tippen mit Tippfehlern, Gegenproben für falschen Befehl `grpe`, falsche Antwort und falschen Vorwurf, Speichern über das Pausemenü und Fortsetzen).

---

## 7. Lizenzen

- **Code:** MIT (`LICENSE`).
- **Inhalte** (Episodentexte, Alex-Büste, eigene Klänge, Renderings, Demo-Video): CC BY-SA 4.0, ohne Persönlichkeitsrechte am Abbild (`LICENSE-INHALTE.md`).
- **Ausgenommen:** Figurenporträts (Herkunft offen) und Drittanbieter-Assets.

Alle verwendeten Drittanbieter-Assets (Kenney CC0, JetBrains Mono OFL 1.1, v86 BSD-2-Clause u. a.) sowie Eigenproduktionen sind in [`THIRD_PARTY_ASSETS.md`](THIRD_PARTY_ASSETS.md) aufgeführt.
