# Runtime-Spike: echtes Linux im nativen Spiel

Stand 2026-10-27. Quelle der Webversion: `alexle135.de` auf `origin/main` (`acd3ad8`).

## Ausgangslage

Die Webversion führt Alpine Linux 3.21.8 (i386, Kernel 6.12 `linux-virt`) in v86 0.5.471
aus. Das Dateisystem kommt per 9p aus inhaltsadressierten Dateien, der Start erfolgt aus
einem Savestate. `ttyS0` ist das Spielerterminal. Auf `ttyS1` läuft als root der
`mission-agent` mit Zeilenprotokoll (`PING`, `RUN`, `REPLY` → `RES`, `ERR`). Alle Setups
und Prüfungen sind Bash-Skripte, die über diesen Agenten laufen.

## Verglichene Wege

| Kriterium | A: v86 headless im Kindprozess (gewählt) | B: QEMU als Kindprozess |
|---|---|---|
| Linux-Semantik | identisch zur Webversion: gleiches Image, gleicher Savestate, gleicher Agent | gleicher Userspace möglich, Kernel/9p müssten auf Blockgerät umgebaut werden, eigener Boot |
| Isolation | Emulator ohne Netz-Relay, Dateisystem aus Image-Dateien, Schreibzugriffe nur im Speicher. Belegt: Host-Pfade fehlen, `ping` scheitert | gut. Aber 9p über virtio wäre ein Host-Mount (verboten), also Disk-Image nötig |
| Offline | ja | ja |
| Portabilität | eine JS-Datei, als Bun-Einzelbinary für `darwin-arm64` und `linux-x64` gebaut und getestet | x86-Gast auf Apple Silicon nur per TCG; eigene QEMU-Builds je Plattform |
| Abhängigkeiten | v86 (BSD-2), Bun-Runtime eingebettet | QEMU plus Firmware je Plattform |
| Lizenz | v86 BSD-2-Clause, SeaBIOS/VGABIOS LGPL, Bun MIT mit statisch gelinktem JavaScriptCore (LGPL-2): Hinweis und Quellenangebot nötig | QEMU GPL-2.0: Quellangebot nötig |
| Paketgröße | Host 71 MB (macOS) bzw. 91 MB (Linux), wasm 2 MB, Image 177 MB, Bildschirm-Image +52 MB | QEMU ca. 50–100 MB je Plattform plus neu gebautes Image |
| Start | aus Savestate (gemessen, siehe unten) | Kaltstart, kein Savestate der Webversion verwendbar |

B wurde nicht ausgeführt. Die Tabelle stützt sich dort auf die bekannten
Eigenschaften von QEMU. A ist durch den Spike belegt, B müsste für dieselbe Semantik
ein neues Image und einen eigenen Boot-Pfad bekommen.

## Spike-Messungen (`runtime/spike.mjs`)

Ablauf: Host als Kindprozess starten, `PING` bis zur Antwort, `RUN` mit Systeminfo, dann
ein Ausschnitt aus dem E0-Setup. Die Prüfung von `kasse-zurueck` läuft zuerst vor der
Lösung (Exit 1). Danach wird `cp …bak …csv` ins Terminal getippt, und die Prüfung läuft
erneut (Exit 0). Zum Schluss folgt ein Isolationstest.

| Host | Emulator gestartet | Agent bereit | Prüfung vor / nach Lösung | Netz |
|---|---|---|---|---|
| macOS M3, Node 26 | 1,87 s | 3,92 s | Exit 1 / Exit 0 | keins |
| macOS M3, Bun-Binary | 3,45 s | 3,71 s | Exit 1 / Exit 0 | keins |
| gns3 Ubuntu 26.04 x86_64, Bun-Binary | 0,39 s | 0,63 s | Exit 1 / Exit 0 | keins |

Der erste `RUN` nach dem Start dauert auf dem Mac bis ca. 10 s, weil 9p die Binärdateien
erst beim ersten Zugriff lädt. Das Spiel führt deshalb das Rahmen-Setup während des
Ladebildschirms aus.

Die VM hat das Gerät `eth0` (Teil des Savestates), aber kein Relay. Die Geräteliste darf
sich nicht ändern, sonst passt der Savestate nicht mehr.

## Entscheidung

Weg A. Das Spiel startet `linux-host` als Kindprozess mit fester Argumentliste, ohne
Shell, und spricht JSON-Zeilen über stdin/stdout. Das Agent-Protokoll, die Prüfbündel,
die Sperren und die Vorwurflogik liegen in C# und sind mit EditMode-Tests abgedeckt.
Endet das Spiel, schließt sich stdin, und der Host beendet die VM.

Offenes Risiko: Episode 2 braucht das grafische Bildschirm-Image. Der Framebuffer muss
headless aus v86 ausgelesen werden und Maus- und Tastatureingaben zurückgeben. Das ist
eigener Slice S8 mit eigenem Spike.
