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
    ///   -demo [-episoden 0,1,2,3] [-schnell | -kino] [-aufnahme ordner] [-protokoll datei] [-spielstand ordner]
    /// </summary>
    public sealed class DemoRegie : MonoBehaviour
    {
        private GameRoot _spiel;
        private StreamWriter _protokoll;
        private BildAufnahme _aufnahme;
        private List<int> _episoden;
        private bool _schnell;
        private bool _kino;
        private readonly System.Random _zufall = new System.Random(135);
        private int _fehler, _bestanden;
        private readonly List<float> _bildzeiten = new List<float>();

        public bool Fertig { get; private set; }
        public bool Erfolgreich => _fehler == 0 && _bestanden > 0;

        public void Starten(GameRoot spiel, string aufnahmeOrdner, string protokollDatei, IEnumerable<int> episoden, bool schnell, bool kino = false)
        {
            _spiel = spiel;
            _episoden = episoden.ToList();
            _schnell = schnell;
            _kino = kino && !schnell;
            if (!string.IsNullOrEmpty(protokollDatei)) _protokoll = new StreamWriter(protokollDatei, false) { AutoFlush = true };
            if (!string.IsNullOrEmpty(aufnahmeOrdner))
            {
                _aufnahme = gameObject.AddComponent<BildAufnahme>();
                _aufnahme.Starten(aufnahmeOrdner);
            }
            Log($"Demo: Episoden {string.Join(",", _episoden)}, {(schnell ? "schnell" : _kino ? "kino" : "normal")}, {Screen.width}×{Screen.height}, {SystemInfo.graphicsDeviceName}, Qualität {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
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
            if (_kino) yield return MenueZeigen();
            foreach (int nr in _episoden)
            {
                var episode = _spiel.Daten.Episoden.FirstOrDefault(e => e.Ermittlung.Nr == nr);
                var plan = (_kino ? Durchspielplaene.Kino(nr) : null) ?? Durchspielplaene.Fuer(nr);
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
            if (_kino) yield return Rundgang();
            yield return GeheZu("terminal", 1.15f);
            Log("  Raum: " + Bildrate());

            foreach (var schritt in plan)
            {
                switch (schritt.Art)
                {
                    case PlanArt.Terminal: _spiel.Zeige(Ansicht.Terminal); yield return Warte(_schnell ? 0.5f : 2f); break;
                    case PlanArt.Fallakte: _spiel.Zeige(Ansicht.Fallakte); yield return Warte(_schnell ? 0.5f : 2.5f); break;
                    case PlanArt.Raum: _spiel.Zeige(Ansicht.Raum); yield return Warte(1f); break;
                    case PlanArt.Pause:
                    {
                        // Pausemenü wie ein Spieler: Einstellungen ansehen, dann über den Knopf speichern.
                        _spiel.Zeige(Ansicht.Pause);
                        yield return Warte(2f);
                        yield return Klicke("Einstellungen");
                        yield return Warte(4f);
                        yield return Klicke("Fertig");
                        yield return Warte(1f);
                        File.Delete(_spiel.Ablage.VmPfad);
                        yield return Klicke("Speichern");
                        yield return BisZu(() => File.Exists(_spiel.Ablage.VmPfad), 60);
                        yield return Warte(1.5f);
                        Pruefe(File.Exists(_spiel.Ablage.VmPfad), "Speichern über das Pausemenü (VM-Zustand)");
                        break;
                    }
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
                        if (_kino && _spiel.Ansicht == Ansicht.Pause) yield return Klicke("Speichern und Hauptmenü");
                        else _spiel.ZumHauptmenue();
                        yield return BisZu(() => _spiel.Ansicht == Ansicht.Menue && _spiel.Sitzung == null, 60);
                        Pruefe(_spiel.KannFortsetzen, "Hauptmenü bietet Fortsetzen an");
                        yield return Warte(_schnell ? 0.5f : _kino ? 3f : 2f);
                        if (_kino) yield return Klicke("Fortsetzen");
                        else _spiel.Fortsetzen();
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
            else if (_kino)
            {
                yield return TippeMenschlich(linux, schritt.Text);
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
            else yield return Warte(_schnell ? 1.5f : _kino ? 4f : 3f);
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
            yield return Warte(_schnell ? 0.5f : _kino ? 3.5f : 2.5f);
        }

        /// <summary>Spieler gleichmäßig vor ein Interactable führen und es anschauen.</summary>
        private IEnumerator GeheZu(string id, float abstand)
        {
            var ziel = FindObjectsByType<Interactable>(FindObjectsSortMode.None).FirstOrDefault(i => i.Id == id);
            var spieler = FindFirstObjectByType<PlayerController>();
            if (ziel == null || spieler == null) yield break;
            Vector3 zielPunkt = ziel.transform.position;
            Vector3 richtung = spieler.transform.position - zielPunkt;
            richtung.y = 0;
            Vector3 stand = zielPunkt + richtung.normalized * abstand;
            yield return GehePunkt(stand, zielPunkt);
        }

        /// <summary>
        /// Kamerafahrt: Spieler auf einer Geraden zu <paramref name="stand"/> führen (Höhe bleibt)
        /// und dabei den Blick weich auf <paramref name="blickZiel"/> drehen.
        /// </summary>
        private IEnumerator GehePunkt(Vector3 stand, Vector3 blickZiel, float dauer = -1)
        {
            var spieler = FindFirstObjectByType<PlayerController>();
            if (spieler == null) yield break;
            var cc = spieler.GetComponent<CharacterController>();
            var cam = spieler.GetComponentInChildren<Camera>();
            Vector3 von = spieler.transform.position;
            stand.y = von.y;
            float augen = cam.transform.position.y - von.y;
            float yawVon = spieler.transform.eulerAngles.y;
            float pitchVon = cam.transform.localEulerAngles.x;
            if (pitchVon > 180) pitchVon -= 360;
            Vector3 blick = blickZiel - (stand + Vector3.up * augen);
            float yawZu = Mathf.Atan2(blick.x, blick.z) * Mathf.Rad2Deg;
            float pitchZu = Mathf.Clamp(-Mathf.Atan2(blick.y, new Vector2(blick.x, blick.z).magnitude) * Mathf.Rad2Deg, -35, 35);
            if (dauer < 0) dauer = _schnell ? 0.5f : Mathf.Clamp(Vector3.Distance(von, stand) / (_kino ? 1.3f : 1.6f), 1.5f, 7f);
            if (cc != null) cc.enabled = false;
            for (float t = 0; t < 1f; t += Time.deltaTime / dauer)
            {
                float k = Mathf.SmoothStep(0, 1, t);
                float d = Mathf.SmoothStep(0, 1, Mathf.Min(1, t * 1.4f));
                spieler.transform.position = Vector3.Lerp(von, stand, k);
                spieler.SetLook(Mathf.LerpAngle(yawVon, yawZu, d), Mathf.Lerp(pitchVon, pitchZu, d));
                yield return null;
            }
            spieler.transform.position = stand;
            spieler.SetLook(yawZu, pitchZu);
            if (cc != null) cc.enabled = true;
        }

        /// <summary>Hauptmenü für das Vorführvideo: Befehlsübersicht und Einstellungen öffnen.</summary>
        private IEnumerator MenueZeigen()
        {
            Log("  Menü: Befehlsübersicht und Einstellungen");
            yield return Warte(2f);
            yield return Klicke("Befehlsübersicht");
            yield return Warte(4.5f);
            yield return Klicke("Schließen");
            yield return Warte(1.5f);
            yield return Klicke("Einstellungen");
            yield return Warte(5f);
            yield return Klicke("Fertig");
            yield return Warte(2f);
        }

        /// <summary>
        /// Einen sichtbaren Knopf wie ein Spieler bedienen: erst hervorheben, dann auslösen.
        /// Knöpfe heißen "Knopf &lt;Beschriftung&gt;" (UiBaukasten); der Anfang der Beschriftung genügt.
        /// </summary>
        private IEnumerator Klicke(string beschriftung)
        {
            var knopf = FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
                .Where(b => b.isActiveAndEnabled && b.interactable && b.name.StartsWith("Knopf " + beschriftung))
                .OrderBy(b => b.name.Length).FirstOrDefault();
            if (knopf == null) { Pruefe(false, $"Knopf „{beschriftung}“ sichtbar"); yield break; }
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(knopf.gameObject);
            yield return Warte(_schnell ? 0.1f : 0.7f);
            knopf.onClick.Invoke();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }

        /// <summary>Langsamer Rundgang für das Vorführvideo: Racks, Aktenwand, Serverschrank, Pager.</summary>
        private IEnumerator Rundgang()
        {
            Log("  Rundgang durch den Serverraum");
            var spieler = FindFirstObjectByType<PlayerController>();
            if (spieler == null) yield break;
            Vector3 P(float x, float z) => new Vector3(x, 0, z);
            Vector3 hier = spieler.transform.position;
            // Umschauen am Eingang, dann Aktenwand mit Fallakte.
            yield return GehePunkt(hier, new Vector3(-1f, 1.4f, 2f), 3f);
            yield return Warte(1.5f);
            yield return GehePunkt(P(-3.4f, -0.5f), new Vector3(-4.98f, 1.55f, -0.5f));
            yield return Warte(2.5f);
            _spiel.Zeige(Ansicht.Fallakte);
            yield return Warte(4f);
            _spiel.Zeige(Ansicht.Raum);
            yield return Warte(1f);
            // Durch den Gang zwischen den Rack-Reihen; die LEDs zeigen den Spielzustand.
            yield return GehePunkt(P(-3.5f, 2f), new Vector3(0.5f, 1.2f, 2f));
            yield return Warte(1.5f);
            yield return GehePunkt(P(-2.4f, 2f), new Vector3(-2.4f, 1.2f, 1f));
            yield return Warte(3f);
            yield return GehePunkt(P(-1f, 2f), new Vector3(-1f, 1.3f, 3f));
            yield return Warte(2f);
            yield return GehePunkt(P(1.4f, 2f), new Vector3(4.3f, 1f, -1.6f));
            // Arbeitsplatz: Pager mit dem Auftrag.
            var pager = FindObjectsByType<Interactable>(FindObjectsSortMode.None).FirstOrDefault(i => i.Id == "pager");
            if (pager != null)
            {
                yield return GehePunkt(P(3.5f, -1.0f), pager.transform.position);
                yield return Warte(2f);
                _spiel.Zeige(Ansicht.Pager);
                yield return Warte(4.5f);
                _spiel.Zeige(Ansicht.Raum);
                yield return Warte(1f);
            }
        }

        /// <summary>Tippen in menschlichem Takt (~8 Zeichen/s), mit Denkpausen und gelegentlich einem korrigierten Vertipper.</summary>
        private IEnumerator TippeMenschlich(LinuxSitzung linux, string text)
        {
            int fehlerAn = -1;
            if (text.Length > 25 && _zufall.NextDouble() < 0.5)
            {
                fehlerAn = _zufall.Next(8, text.Length - 4);
                while (fehlerAn < text.Length && !char.IsLetter(text[fehlerAn])) fehlerAn++;
            }
            yield return Warte(0.7f + Zufall(0.8f));
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (i == fehlerAn)
                {
                    Sende(linux, Nachbartaste(c));
                    yield return Warte(0.35f + Zufall(0.25f));
                    linux.Tippe(new byte[] { 0x7f });
                    yield return Warte(0.2f + Zufall(0.15f));
                }
                Sende(linux, c);
                float takt = 0.06f + Zufall(0.12f);
                if (c == ' ') takt += 0.06f + Zufall(0.18f);
                if (c == '>' || c == '|' || c == '&') takt += 0.25f;
                yield return Warte(takt);
            }
            yield return Warte(0.5f + Zufall(0.5f));
        }

        private float Zufall(float bis) => (float)_zufall.NextDouble() * bis;

        private static void Sende(LinuxSitzung linux, char c) => linux.Tippe(System.Text.Encoding.UTF8.GetBytes(c.ToString()));

        private static char Nachbartaste(char c)
        {
            const string reihen = "qwertzuiop asdfghjkl yxcvbnm";
            int i = reihen.IndexOf(char.ToLowerInvariant(c));
            if (i < 0) return 'x';
            return i + 1 < reihen.Length && reihen[i + 1] != ' ' ? reihen[i + 1] : reihen[i - 1];
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
