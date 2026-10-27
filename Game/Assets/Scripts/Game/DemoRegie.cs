using System;
using System.Collections;
using System.IO;
using System.Linq;
using Staffel2.World;
using UnityEngine;

namespace Staffel2.Game
{
    /// <summary>
    /// Spielt Episode 0 automatisch mit den vorgesehenen Lösungen durch, so wie ein Mensch:
    /// Menü, zum Arbeitsplatz gehen, Terminal öffnen, Befehle tippen, echte Prüfungen
    /// abwarten, Antwort und Vorwurf in der Fallakte, Abschluss. Dient als Video-Demo und als
    /// Durchspieltest im gebauten Player (Start mit -demo [-aufnahme ordner] [-protokoll datei]).
    /// </summary>
    public sealed class DemoRegie : MonoBehaviour
    {
        private const float ZeichenProSekunde = 22f;
        private GameRoot _spiel;
        private StreamWriter _protokoll;
        private BildAufnahme _aufnahme;
        public bool Fertig { get; private set; }
        public bool Erfolgreich { get; private set; }

        public void Starten(GameRoot spiel, string aufnahmeOrdner, string protokollDatei)
        {
            _spiel = spiel;
            if (!string.IsNullOrEmpty(protokollDatei)) _protokoll = new StreamWriter(protokollDatei, false) { AutoFlush = true };
            if (!string.IsNullOrEmpty(aufnahmeOrdner))
            {
                _aufnahme = gameObject.AddComponent<BildAufnahme>();
                _aufnahme.Starten(aufnahmeOrdner);
            }
            StartCoroutine(Ablauf());
        }

        private void Log(string text)
        {
            string zeile = $"[{Time.realtimeSinceStartup,7:0.0} s] {text}";
            Debug.Log("Demo: " + zeile);
            _protokoll?.WriteLine(zeile);
        }

        private IEnumerator Ablauf()
        {
            float start = Time.realtimeSinceStartup;
            yield return Warte(3.5f);                       // Hauptmenü zeigen
            var episode = _spiel.Daten.Episoden[0];
            Log($"Starte Episode {episode.Ermittlung.Nr}: {episode.Ermittlung.Titel}");
            _spiel.NeueEpisode(episode);
            yield return BisZu(() => _spiel.Ansicht == Ansicht.Raum || _spiel.Sitzung?.Fehler != null, 180);
            if (_spiel.Sitzung?.Fehler != null) { yield return Abbruch("Setup: " + _spiel.Sitzung.Fehler); yield break; }
            Log($"Raum bereit nach {Time.realtimeSinceStartup - start:0.0} s (Linux {_spiel.Linux.StartDauer:0.0} s)");

            yield return Warte(2f);
            yield return GeheZu("terminal", 1.15f);
            yield return Warte(1f);
            _spiel.Zeige(Ansicht.Terminal);
            yield return Warte(2.5f);

            var s = _spiel.Sitzung;
            yield return TippeUndPruefe("ls -l /srv/rechenwerk/uebung/kaffeekasse.csv /srv/rechenwerk/uebung/kaffeekasse.bak > ~/belege/kasse-meta.txt",
                () => s.Zustand.BelegeErkannt.Contains("kasse-meta"));
            yield return TippeUndPruefe("cat ~/belege/kasse-meta.txt", null);
            yield return Warte(3f);

            // Antwort in der Fallakte: wem gehört die Datei?
            _spiel.Zeige(Ansicht.Fallakte);
            yield return Warte(3f);
            Log("Antwort: kaffee");
            var antwort = s.AntwortenAsync("akte", "kaffee");
            yield return BisZu(() => antwort.IsCompleted, 30);
            yield return BisZu(() => s.AktuellerSchritt(s.Ermittlung.Spuren[0]) == null, 20);
            yield return Warte(3f);

            _spiel.Zeige(Ansicht.Terminal);
            yield return Warte(1.5f);
            yield return TippeUndPruefe("grep 2026-04-15 /srv/rechenwerk/uebung/kaffeekasse.csv > ~/belege/kasse-zeilen.txt && grep 2026-04-15 /srv/rechenwerk/uebung/kaffeekasse.bak >> ~/belege/kasse-zeilen.txt",
                () => s.Zustand.BelegeErkannt.Contains("kasse-zeilen"));
            yield return TippeUndPruefe("cp /srv/rechenwerk/uebung/kaffeekasse.bak /srv/rechenwerk/uebung/kaffeekasse.csv",
                () => ErmittlungsFertig(s));
            yield return Warte(3f);

            // Speichern mitten im Fall, wie ein Spieler es über das Pause-Menü tut.
            var speichern = _spiel.SpeichernAsync(true);
            yield return BisZu(() => speichern.IsCompleted, 40);
            Log($"Gespeichert: VM-Zustand {(File.Exists(_spiel.Ablage.VmPfad) ? new FileInfo(_spiel.Ablage.VmPfad).Length / 1024 / 1024 + " MiB" : "fehlt")}");

            _spiel.Zeige(Ansicht.Fallakte);
            yield return Warte(2.5f);
            var taeter = s.Ermittlung.Vorwurf.TaeterId;
            var belege = s.Ermittlung.Vorwurf.Belege.Select(b => b.Id).ToList();
            Log($"Vorwurf gegen {taeter} mit {string.Join(", ", belege)}");
            var vorwurf = s.VorwurfAsync(taeter, belege);
            yield return BisZu(() => vorwurf.IsCompleted, 60);
            Log("Vorwurf: " + vorwurf.Result);
            yield return BisZu(() => _spiel.Ansicht == Ansicht.Abschluss, 20);
            Erfolgreich = _spiel.Stand.Akte.ContainsKey(s.Ermittlung.Id);
            Log(Erfolgreich ? "Episode gelöst und in der Akte gespeichert." : "Episode NICHT gelöst.");
            yield return Warte(7f);
            Ende();
        }

        private static bool ErmittlungsFertig(Ermittlungssitzung s) => s.Ermittlung.Spuren.All(sp => s.AktuellerSchritt(sp) == null);

        private IEnumerator TippeUndPruefe(string befehl, Func<bool> erreicht)
        {
            Log("Tippe: " + befehl);
            var linux = _spiel.Linux;
            foreach (char c in befehl)
            {
                linux.Tippe(System.Text.Encoding.UTF8.GetBytes(c.ToString()));
                yield return Warte(1f / ZeichenProSekunde * UnityEngine.Random.Range(0.6f, 1.4f));
            }
            yield return Warte(0.4f);
            linux.Tippe(new byte[] { (byte)'\r' });
            if (erreicht != null)
            {
                float t0 = Time.realtimeSinceStartup;
                yield return BisZu(erreicht, 60);
                Log(erreicht() ? $"  Prüfung bestanden nach {Time.realtimeSinceStartup - t0:0.0} s" : "  Prüfung NICHT bestanden");
            }
            yield return Warte(2.5f);
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
            float dauer = Mathf.Clamp(Vector3.Distance(von, stand) / 1.6f, 1.5f, 6f);
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
            if (cc != null) cc.enabled = true;
        }

        private IEnumerator Abbruch(string grund)
        {
            Log("ABBRUCH: " + grund);
            yield return Warte(3f);
            Ende();
        }

        private void Ende()
        {
            Log($"Ende. Bilder: {_aufnahme?.Bilder ?? 0}");
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
