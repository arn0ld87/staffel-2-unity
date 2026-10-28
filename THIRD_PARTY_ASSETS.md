# Drittanbieter-Assets und Lizenzen (`THIRD_PARTY_ASSETS.md`)

Diese Datei dokumentiert alle im Projekt „Ein Linux-Krimi – Staffel 2“ verwendeten externen Modelle, Klänge, Schriften, Laufzeitkomponenten und Eigenproduktionen mitsamt Lizenz, Herkunft und Ablageort im Repository.

---

## 1. 3D-Modelle (CC0 1.0 Universal)

| Paket / Asset | Urheber / Quelle | Lizenz | Lizenzdatei im Repo | Verwendete Dateien im Projekt |
|---|---|---|---|---|
| **Kenney Furniture Kit** | Kenney Vleugels (`https://kenney.nl/assets/furniture-kit`) | CC0 1.0 Universal (Public Domain) | `Game/Assets/ThirdParty/Kenney/Furniture/License.txt`, `third_party_staging/kenney-furniture-kit/License.txt` | `Game/Assets/ThirdParty/Kenney/Furniture/` (`desk.fbx`, `chairDesk.fbx`, `computerScreen.fbx`, `computerKeyboard.fbx`, `computerMouse.fbx`, `laptop.fbx`, `bookcaseClosedWide.fbx`, `cabinetBed.fbx`, `lampSquareFloor.fbx`, `trashcan.fbx`) |
| **Kenney Blocky Characters (2.0)** | Kenney Vleugels (`https://kenney.nl/assets/blocky-characters`) | CC0 1.0 Universal (Public Domain) | `Game/Assets/ThirdParty/Kenney/Characters/License.txt`, `third_party_staging/kenney-blocky-characters/License.txt` | `Game/Assets/ThirdParty/Kenney/Characters/character-a.fbx` sowie Texturen `Textures/texture-a.png` bis `texture-r.png` |

---

## 2. Eigene 3D-Modelle (Alex-Büste)

| Asset | Urheber / Quelle | Lizenz / Status | Verarbeitung im Repo | Verwendete Dateien im Projekt |
|---|---|---|---|---|
| **Alex-Büste (`Alex_Bueste.fbx`, `Alex_BaseColor.png`)** | Alexander Schneider; Ausgangsmodell `alex-dialog-bueste.fbx` erstellt mit Tripo AI (`https://www.tripo3d.ai`, kostenloser Plan) | CC BY-NC-SA 4.0, ohne Persönlichkeitsrechte (siehe `LICENSE-INHALTE.md`). Grundlage: Tripo stellt Modelle aus dem kostenlosen Plan laut Preisseite öffentlich unter CC BY 4.0, kommerzielle Nutzung nur in bezahlten Plänen (Tripo-AGB selbst nicht eingesehen, Abruf blockiert) | Nachbearbeitet über `tools/blender/alex_gesicht.py` (Shape Keys `Blinzeln_L`, `Blinzeln_R`, `Mund_Auf`, `Mund_Schmal`, `Neutral`) | `Game/Assets/Art/Alex/`, `Game/Assets/Resources/Alex/` |

---

## 2b. Figurenporträts

| Asset | Quelle | Lizenz / Status | Verwendete Dateien |
|---|---|---|---|
| Porträts Timo, Nadia, Margit, Kaffeeautomat | Alexander Schneider, erzeugt mit Google Gemini | CC BY-SA 4.0 (siehe `LICENSE-INHALTE.md`) | `Game/Assets/StreamingAssets/staffel2/bilder/*.webp` |

---

## 2c. Ganzkörper-Figur und Poster

| Asset | Quelle | Lizenz / Status | Verwendete Dateien |
|---|---|---|---|
| Ganzkörper-Alex (Körper, Rig, Clips) | Alexander Schneider, erstellt mit Google Gemini über `tools/blender/alex_ganzkoerper_live.py` auf Basis der Tripo-Büste | CC BY-NC-SA 4.0 (abgeleitet von der Tripo-Büste) | `Game/Assets/Art/Alex/Alex_Bueste.fbx`, `Alex_Body_BaseColor.png`, `art-source/alex/` |
| Proportionsvorlage `alexander-ganz-zeigt.webp` | Alexander Schneider | nicht im Repo; Pfad per `ALEX_VORLAGE` | — |
| Zweiter Alex (TRELLIS) | Vorlagen: Zeichnungen von Alexander Schneider, erzeugt mit Google Gemini (`art-source/alex/referenz/`); 3D-Modell erzeugt mit TRELLIS (Microsoft, MIT-Lizenz, Space `trellis-community/TRELLIS` auf Hugging Face); Gesicht, Rig und Clips über `tools/blender/alex_trellis_gesicht.py` und `alex_trellis_rig.py` | CC BY-SA 4.0 (keine Tripo-Anteile) | `Game/Assets/Art/Alex/Alex_Trellis*`, `art-source/alex/trellis/` |
| Poster- und Banner-Grafiken | Alexander Schneider, erzeugt mit Google Gemini | CC BY-SA 4.0; Name/Domain alex-schneider.dev nicht lizenziert | `Game/Assets/Art/Brand/` |

---

## 3. Audio und Klänge

### 3.1 Kenney Audio Packs (CC0 1.0 Universal)

| Paket | Urheber / Quelle | Lizenz | Lizenzdatei im Repo | Verwendete Dateien in `Game/Assets/Resources/Audio/` |
|---|---|---|---|---|
| **Kenney Interface Sounds** | Kenney Vleugels (`https://kenney.nl/assets/interface-sounds`) | CC0 1.0 Universal (Public Domain) | `Game/Assets/ThirdParty/Kenney/Audio/License.txt`, `third_party_staging/kenney-interface-sounds/License.txt` | `click_002.ogg`, `select_001.ogg`, `confirmation_001.ogg`, `error_004.ogg`, `bong_001.ogg`, `tick_001.ogg`, `glitch_001.ogg` |
| **Kenney Impact Sounds** | Kenney Vleugels (`https://kenney.nl/assets/impact-sounds`) | CC0 1.0 Universal (Public Domain) | `Game/Assets/ThirdParty/Kenney/Audio/License.txt`, `third_party_staging/kenney-impact-sounds/License.txt` | `footstep_concrete_000.ogg` bis `footstep_concrete_004.ogg` |
| **Kenney UI Audio** | Kenney Vleugels (`https://kenney.nl/assets/ui-audio`) | CC0 1.0 Universal (Public Domain) | `third_party_staging/kenney-ui-audio/License.txt` | Im Staging-Ordner `third_party_staging/kenney-ui-audio/` vorgehalten |

### 3.2 Prozedural erzeugte Klänge (Eigenproduktion)

| Datei | Erzeugung | Lizenz | Beschreibung |
|---|---|---|---|
| `Game/Assets/Resources/Audio/rack_luefter.wav` | `tools/erzeuge-audio.py` | CC BY-SA 4.0 (prozedural synthetisiert, keine Drittquelle; das Skript selbst steht unter MIT) | Nahtlose 8-Sekunden-Schleife für das 3D-Rack-Grundrauschen im Serverraum |
| `Game/Assets/Resources/Audio/ermittlung_teppich.wav` | `tools/erzeuge-audio.py` | CC BY-SA 4.0 (prozedural synthetisiert, keine Drittquelle; das Skript selbst steht unter MIT) | Nahtlose 16-Sekunden-Schleife für den leisen Ermittlungs-Klangteppich |

---

## 4. Schriftarten (SIL Open Font License 1.1)

| Schriftart | Urheber / Quelle | Lizenz | Lizenzdatei im Repo | Verwendete Dateien im Projekt |
|---|---|---|---|---|
| **JetBrains Mono (v2.304)** | JetBrains s.r.o. (`https://github.com/JetBrains/JetBrainsMono`) | SIL Open Font License 1.1 (OFL-1.1) | `Game/Assets/ThirdParty/Fonts/JetBrainsMono/OFL.txt`, `third_party_staging/jetbrains-mono/OFL.txt` | `Game/Assets/ThirdParty/Fonts/JetBrainsMono/JetBrainsMono-Regular.ttf`, `JetBrainsMono-Bold.ttf`, `Game/Assets/Resources/Fonts/JetBrainsMono SDF.asset` |

---

## 5. Eingebettete Linux-Laufzeit (`StreamingAssets/linux`, gitignored, gebaut via `tools/prepare-runtime.sh`)

`tools/prepare-runtime.sh` baut die Laufzeit und legt sie unter `Game/Assets/StreamingAssets/linux/` ab: die Host-Programme `linux-host-darwin-arm64`, `linux-host-linux-x64`, `linux-host-windows-x64.exe`, dazu `v86.wasm` und unter `assets/` das VM-Image samt BIOS. Das VM-Image stammt aus der Webversion (`alexle135.de`, `scripts/mission-image/build.sh`); das Skript liest es standardmäßig aus `runtime/assets/linux` (Variable `IMAGE_DIR`).

| Komponente | Quelle | Lizenz | Hinweis / Status |
|---|---|---|---|
| **v86 (`v86.wasm`, im Host gebündeltes `libv86.js`)** | Fabian Hemmer / copy (`https://github.com/copy/v86`), npm-Paket `v86` unter `runtime/node_modules/v86` | BSD-2-Clause | x86-Emulator in WebAssembly/JavaScript für die isolierte Linux-VM |
| **SeaBIOS (`seabios.bin`) & VGA-BIOS (`vgabios.bin`)** | SeaBIOS-Projekt, über das VM-Image der Webversion | LGPLv3 (unmodifizierte Firmware-Binärdateien) | **Zu prüfen**: Bei Weitergabe von Builds LGPLv3-Lizenztext und Verweis auf den SeaBIOS-Quellcode beilegen |
| **Alpine-Linux-Abbild** (`alpine-fs.json`, `alpine-rootfs-flat/`, `alpine-state.bin.zst`, dazu `bildschirm/` für Episode 2) | Alpine Linux Project, gebaut von `scripts/mission-image/build.sh` der Webversion | Sammelwerk aus Open-Source-Paketen (Linux-Kernel GPLv2, BusyBox GPLv2, musl MIT u. a.; das Bildschirm-Image zusätzlich X11/Openbox/PCManFM) | **Zu prüfen**: Bei Weitergabe von Builds Paketliste (`apk info -v`) und GPLv2-Quellcode-Hinweis beifügen |
| **Bun-Laufzeit** (in die Host-Programme eingebettet, `bun build --compile`) | Oven (`https://bun.sh`) | MIT; Bun enthält JavaScriptCore/WebKit unter LGPL-2.1 | **Zu prüfen**: LGPL-Hinweise für die eingebettete JavaScriptCore-Engine bei Weitergabe von Builds |
