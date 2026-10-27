# Staffel2.World

3D-Welt des Serverraums. Eine MonoBehaviour pro Datei; Assembly `Staffel2.World` (Editor-Builder: `Staffel2.World.Editor`).

## Komponenten

| Komponente | Zweck | Öffentliche API |
|---|---|---|
| `PlayerController` | Ego-Steuerung (WASD, Maus, Shift = Sprint, Schwerkraft) auf einem `CharacterController`; Kamera als Kind | `bool InputBlocked` (friert ein, gibt Cursor frei), `float MouseSensitivity` (Grad je Zählschritt), `bool ReducedMotion` (kein Wippen/Pendeln), `WalkSpeed`, `SprintSpeed`, `const AugenHoehe` |
| `Interactable` | Anvisierbares Objekt mit Collider; Hervorhebung per MaterialPropertyBlock | `Id`, `Prompt`, `event Action<Interactable> Interacted`, `Configure(id, prompt, renderers)`, `Interact()`, `SetHighlighted(bool)` |
| `PlayerInteractor` | An der Kamera: Raycast bis 2,2 m, E oder Linksklick löst aus; ruht bei `InputBlocked` | `event Action<string> HintChanged` (null = nichts anvisiert), `Current`, `Reach`, `Mask` |
| `RackStatusLights` | LED-Würfel am Rack; Zustand zusätzlich im Muster, nicht nur in der Farbe | `SetState(RackState)`, `State`; `RackState { Ruhe, Aktiv, Warnung, Erledigt }` |
| `MonitorScreen` | Leuchtende Monitorfläche mit großem TMP-Schriftzug | `SetLabel(string)`, `SetState(MonitorState)`, `State`; `MonitorState { Aus, Bereit, Aktiv, Fehler }` |
| `FootstepAudio` | Schritte am Boden, Pitch 0,95–1,05 | `Clips[]`, `MixerGroup`, `StrideLength`, `Volume` |

Muster der Rack-Zustände: Ruhe = ein schwaches blaues Lämpchen, Aktiv = cyanfarbenes Lauflicht, Warnung = alle blinken bernsteinfarben (1,5 Hz), Erledigt = alle grün, dauerhaft an.

## Szene bauen

Menü „Staffel 2/Serverraum bauen“ oder `Staffel2.World.Editor.SceneBuilder.BuildServerRoom()`.
Erzeugt `Assets/Scenes/Serverraum.unity` und die Materialien unter `Assets/Materials/World/`, setzt die Build-Szenen
(Start.unity auf Index 0, falls vorhanden, danach Serverraum; SampleScene entfällt).

Interactable-Ids: `terminal`, `fallakte`, `serverschrank`, `pager`.
Racks heißen `Rack_A1..4` und `Rack_B1..4` (`Rack_B1` = `Serverschrank`); das Monitorobjekt heißt `Monitorflaeche`.
