using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Staffel2.World;
using UnityEngine;

namespace Staffel2.Game
{
    /// <summary>
    /// Spielt Episoden automatisch mit den vorgesehenen Lösungen durch, so wie ein Mensch:
    /// zum Arbeitsplatz gehen, Terminal öffnen, Befehle tippen, echte Prüfungen abwarten,
    /// Antworten und Vorwurf in der Fallakte, Abschluss. Jede Erwartung wird geprüft und
    /// protokolliert; der Exit-Code des Players meldet Fehler. Dient als Video-Demo und als
    /// Durchspieltest im gebauten Player:
    ///   -demo [-episoden 0,1,2,3] [-schnell] [-aufnahme ordner] [-protokoll datei] [-spielstand ordner]
    /// </summary>
    public sealed class DemoRegie : MonoBehaviour
    {
        private GameRoot _spiel;
        private StreamWriter _protokoll;
        private BildAufnahme _aufnahme;
        private List<int> _episoden;
        private bool _schnell;
        private int _fehler, _bestanden;
        private readonly List<float> _bildzeiten = new List<float>();

        public bool Fertig { get; private set; }
        public bool Erfolgreich => _fehler == 0 && _bestanden > 0;

        public void Starten(GameRoot spiel, string aufnahmeOrdner, string protokollDatei, IEnumerable<int> episoden, bool schnell)
        {
            _spiel = spiel;
            _episoden = episoden.ToList();
            _schnell = schnell;
            if (!string.IsNullOrEmpty(protokollDatei)) _protokoll = new StreamWriter(protokollDatei, false) { AutoFlush = true };
            if (!string.IsNullOrEmpty(aufnahmeOrdner))
            {
                _aufnahme = gameObject.AddComponent<BildAufnahme>();
                _aufnahme.Starten(aufnahmeOrdner);
            }
            Log($"Demo: Episoden {string.Join(",", _episoden)}, {(schnell ? "schnell" : "normal")}, {Screen.width}×{Screen.height}, {SystemInfo.graphicsDeviceName}, Qualität {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            StartCoroutine(Ablauf());
        }

        private void Update() => _bildzeiten.Add(Time.unscaledDeltaTime);

        /// <summary>Bildrate seit dem letzten Aufruf (Echtzeit), für das Protokoll.</summary>
        private string Bildrate()
        {
            if (_bildzeiten.Count < 2) return "keine Messung";
            var z = _bildzeiten.Skip(1).OrderBy(x => x).ToList();
            float mittel = z.Average();
            float p95 = z[Mathf.Min(z.Count - 1, (int)(z.Count * 0.95f))];
            _bildzeiten.Clear();
            return $"{1f / mittel:0} fps im Mittel, 95 % der Bilder unter {p95 * 1000:0} ms ({z.Count} Bilder)";
        }

        private void Log(string text)
        {
            string zeile = $"[{Time.realtimeSinceStartup,7:0.0} s] {text}";
            Debug.Log("Demo: " + zeile);
            _protokoll?.WriteLine(zeile);
        }

        private void Pruefe(bool ok, string was)
        {
            if (ok) _bestanden++; else _fehler++;
            Log($"  {(ok ? "OK    " : "FEHLER")} {was}");
        }

        private IEnumerator Ablauf()
        {
            yield return Warte(_schnell ? 1f : 3.5f);
            Log("Hauptmenü: " + Bildrate());
            foreach (int nr in _episoden)
            {
                var episode = _spiel.Daten.Episoden.FirstOrDefault(e => e.Ermittlung.Nr == nr);
                var plan = Durchspielplaene.Fuer(nr);
                if (episode == null || plan == null) { Pruefe(false, $"Episode {nr} oder Plan fehlt"); continue; }
                yield return Episode(episode, plan);
            }
            Log($"Ergebnis: {_bestanden} Prüfungen bestanden, {_fehler} fehlgeschlagen.");
            Ende();
        }

        private IEnumerator Episode(Staffel2.Core.Episode episode, List<PlanSchritt> plan)
        {
            float start = Time.realtimeSinceStartup;
            Log($"Episode {episode.Ermittlung.Nr}: {episode.Ermittlung.Titel}{(episode.Ermittlung.Bildschirm ? " (Bildschirm-Image)" : "")}");
            var alte = _spiel.Sitzung;
            void Mitschreiben(string text) => Log("    " + text);
            _spiel.Protokoll += Mitschreiben;
            _spiel.NeueEpisode(episode);
            // Auf die NEUE Sitzung warten; Zustand der vorigen Episode zählt nicht.
            yield return BisZu(() => _spiel.Sitzung != alte && (_spiel.Ansicht == Ansicht.Raum || _spiel.Sitzung?.Fehler != null) || _spiel.Linux.Phase == LinuxPhase.Fehler && _spiel.Sitzung != alte, 300);
            var s = _spiel.Sitzung;
            bool bereit = _spiel.Ansicht == Ansicht.Raum && s != null && s.Bereit;
            Pruefe(bereit, $"Linux und Setup bereit nach {Time.realtimeSinceStartup - start:0.0} s (Linux-Start {_spiel.Linux.StartDauer:0.0} s){(bereit ? "" : ": " + (s?.Fehler ?? _spiel.Linux.FehlerText))}");
            Log("  Laden: " + Bildrate());
            if (!bereit) { _spiel.Protokoll -= Mitschreiben; yield break; }

            yield return Warte(_schnell ? 0.5f : 2f);
            yield return GeheZu("terminal", 1.15f);
            Log("  Raum: " + Bildrate());

            foreach (var schritt in plan)
            {
                switch (schritt.Art)
                {
                    case PlanArt.Terminal: _spiel.Zeige(Ansicht.Terminal); yield return Warte(_schnell ? 0.5f : 2f); break;
                    case PlanArt.Fallakte: _spiel.Zeige(Ansicht.Fallakte); yield return Warte(_schnell ? 0.5f : 2.5f); break;
                    case PlanArt.Raum: _spiel.Zeige(Ansicht.Raum); yield return Warte(1f); break;
                    case PlanArt.Tippe: yield return Tippe(s, schritt); break;
                    case PlanArt.Antwort:
                    {
                        var t = s.AntwortenAsync(schritt.SpurId, schritt.Text);
                        yield return BisZu(() => t.IsCompleted, 30);
                        yield return Erwarte(s, schritt);
                        break;
                    }
                    case PlanArt.Vorwurf:
                    {
                        var belege = s.Ermittlung.Vorwurf.Belege.Select(b => b.Id).ToList();
                        var t = s.VorwurfAsync(schritt.Text, belege);
                        yield return BisZu(() => t.IsCompleted, 60);
                        Log($"  Vorwurf gegen {schritt.Text}: {(t.IsCompleted ? t.Result?.ToString() : "keine Antwort")}");
                        yield return Erwarte(s, schritt);
                        break;
                    }
                    case PlanArt.Fortsetzen:
                    {
                        var vorher = s.Zustand;
                        var belegeVorher = string.Join(",", vorher.BelegeErkannt.OrderBy(x => x));
                        _spiel.ZumHauptmenue();
                        yield return BisZu(() => _spiel.Ansicht == Ansicht.Menue && _spiel.Sitzung == null, 60);
                        Pruefe(_spiel.KannFortsetzen, "Hauptmenü bietet Fortsetzen an");
                        yield return Warte(_schnell ? 0.5f : 2f);
                        _spiel.Fortsetzen();
                        yield return BisZu(() => _spiel.Sitzung != null && _spiel.Sitzung != s && _spiel.Ansicht == Ansicht.Raum, 120);
                        s = _spiel.Sitzung;
                        if (s == null) { Pruefe(false, "Fortsetzen: keine Sitzung"); yield break; }
                        Pruefe(string.Join(",", s.Zustand.BelegeErkannt.OrderBy(x => x)) == belegeVorher && s.Pager.Count > 0, $"Zustand nach Fortsetzen (Belege {belegeVorher}, Pager {s.Pager.Count})");
                        // Frische Prüfung in der fortgesetzten VM: Belege müssen als Dateien noch da sein.
                        s.StandPruefen();
                        yield return Warte(_schnell ? 6f : 8f);
                        Pruefe(string.Join(",", s.Zustand.BelegeErkannt.OrderBy(x => x)) == belegeVorher, "Belegdateien in der fortgesetzten VM vorhanden");
                        _spiel.Zeige(Ansicht.Terminal);
                        yield return Warte(_schnell ? 0.5f : 2f);
                        break;
                    }
                    case PlanArt.Speichern:
                    {
                        var t = _spiel.SpeichernAsync(true);
                        yield return BisZu(() => t.IsCompleted, 60);
                        bool vm = File.Exists(_spiel.Ablage.VmPfad);
                        Pruefe(vm && _spiel.Stand.Laufend != null, $"Speichern mit VM-Zustand ({(vm ? new FileInfo(_spiel.Ablage.VmPfad).Length / 1024 / 1024 + " MiB" : "fehlt")})");
                        break;
                    }
                }
            }
            Log("  Ermittlung: " + Bildrate());
            yield return BisZu(() => _spiel.Ansicht == Ansicht.Abschluss, 20);
            Pruefe(_spiel.Stand.Akte.ContainsKey(episode.Ermittlung.Id), $"Episode {episode.Ermittlung.Nr} gelöst und in der Akte, Dauer {Time.realtimeSinceStartup - start:0} s Echtzeit");
            _spiel.Protokoll -= Mitschreiben;
            yield return Warte(_schnell ? 1.5f : 7f);
        }

        private IEnumerator Tippe(Ermittlungssitzung s, PlanSchritt schritt)
        {
            Log("  Tippe: " + schritt.Text);
            var linux = _spiel.Linux;
            // Auf langsamen Rechnern (Software-Rendering, ~1 fps) erst Terminal-Layout und stty
            // für die neue Größe abwarten: gerenderte Bilder zählen, nicht nur Sekunden.
            int bild0 = Time.frameCount;
            float t0 = Time.realtimeSinceStartup;
            yield return BisZu(() => Time.frameCount - bild0 >= 3 && Time.realtimeSinceStartup - t0 >= 0.3f, 15);
            if (_schnell)
            {
                linux.Tippe(System.Text.Encoding.UTF8.GetBytes(schritt.Text));
            }
            else
            {
                foreach (char c in schritt.Text)
                {
                    linux.Tippe(System.Text.Encoding.UTF8.GetBytes(c.ToString()));
                    yield return Warte(1f / 22f * UnityEngine.Random.Range(0.6f, 1.4f));
                }
                yield return Warte(0.4f);
            }
            linux.Tippe(new byte[] { (byte)'\r' });
            if (schritt.Erwartung != null) yield return Erwarte(s, schritt);
            else yield return Warte(_schnell ? 1.5f : 3f);
        }

        private IEnumerator Erwarte(Ermittlungssitzung s, PlanSchritt schritt)
        {
            float t0 = Time.realtimeSinceStartup;
            yield return BisZu(() => schritt.Erwartung(s), schritt.MaxSekunden);
            // Gegenproben erwarten, dass etwas NICHT eintritt: kurz nachwarten, ob es stabil bleibt.
            if (schritt.Beschreibung != null && schritt.Beschreibung.StartsWith("Gegenprobe")) yield return Warte(1f);
            bool ok = schritt.Erwartung(s);
            Pruefe(ok, $"{schritt.Beschreibung} ({Time.realtimeSinceStartup - t0:0.0} s)");
            if (!ok)
            {
                Log("    Letzte Pager-Meldung: " + string.Join(" ", s.Pager.LastOrDefault()?.Zeilen ?? new List<string>()));
                var zeilen = (_spiel.Linux.Bildschirm?.Text() ?? "").TrimEnd().Split('\n');
                Log("    Terminal (letzte Zeilen):");
                foreach (var z in zeilen.Skip(Math.Max(0, zeilen.Length - 8))) Log("      | " + z.TrimEnd());
            }
            yield return Warte(_schnell ? 0.5f : 2.5f);
        }

        /// <summary>Spieler gleichmäßig vor ein Interactable führen und es anschauen.</summary>
        private IEnumerator GeheZu(string id, float abstand)
        {
            var ziel = FindObjectsByType<Interactable>(FindObjectsSortMode.None).FirstOrDefault(i => i.Id == id);
            var spieler = FindFirstObjectByType<PlayerController>();
            if (ziel == null || spieler == null) yield break;
            var cc = spieler.GetComponent<CharacterController>();
            var cam = spieler.GetComponentInChildren<Camera>();
            Vector3 zielPunkt = ziel.transform.position;
            Vector3 richtung = spieler.transform.position - zielPunkt;
            richtung.y = 0;
            Vector3 stand = zielPunkt + richtung.normalized * abstand;
            stand.y = spieler.transform.position.y;
            Vector3 von = spieler.transform.position;
            Quaternion vonRot = spieler.transform.rotation;
            Vector3 blick = zielPunkt - stand; blick.y = 0;
            Quaternion zuRot = Quaternion.LookRotation(blick);
            float dauer = _schnell ? 0.5f : Mathf.Clamp(Vector3.Distance(von, stand) / 1.6f, 1.5f, 6f);
            Quaternion camVon = cam.transform.localRotation;
            float neigung = -Mathf.Atan2(zielPunkt.y - (stand.y + cam.transform.localPosition.y), abstand) * Mathf.Rad2Deg;
            Quaternion camZu = Quaternion.Euler(Mathf.Clamp(neigung, -30, 30), 0, 0);
            if (cc != null) cc.enabled = false;
            for (float t = 0; t < 1f; t += Time.deltaTime / dauer)
            {
                float k = Mathf.SmoothStep(0, 1, t);
                spieler.transform.SetPositionAndRotation(Vector3.Lerp(von, stand, k), Quaternion.Slerp(vonRot, zuRot, Mathf.SmoothStep(0, 1, Mathf.Min(1, t * 1.6f))));
                cam.transform.localRotation = Quaternion.Slerp(camVon, camZu, k);
                yield return null;
            }
            spieler.transform.SetPositionAndRotation(stand, zuRot);
            cam.transform.localRotation = camZu;
            if (cc != null) cc.enabled = true;
        }

        private void Ende()
        {
            Log($"Ende. {(Erfolgreich ? "ERFOLGREICH" : "MIT FEHLERN")}. Bilder: {_aufnahme?.Bilder ?? 0}");
            _aufnahme?.Stoppen();
            _protokoll?.Dispose();
            Fertig = true;
            if (!Application.isEditor) Application.Quit(Erfolgreich ? 0 : 1);
        }

        private static IEnumerator Warte(float sekunden)
        {
            for (float t = 0; t < sekunden; t += Time.deltaTime) yield return null;
        }

        private static IEnumerator BisZu(Func<bool> bedingung, float maxEchtzeit)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!bedingung() && Time.realtimeSinceStartup - t0 < maxEchtzeit) yield return null;
        }
    }
}
