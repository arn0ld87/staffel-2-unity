using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Staffel2.Core;
using Staffel2.Game.UI;
using Staffel2.Linux;
using Staffel2.World;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Staffel2.Game
{
    public enum Ansicht { Menue, Raum, Terminal, Fallakte, Pager, Pause, Abschluss, Laden }

    /// <summary>
    /// Spielablauf: Hauptmenü, Episode starten oder fortsetzen, Raum, Terminal, Fallakte,
    /// Pause, Speichern, Abschluss. Verbindet Linux-Sitzung, Ermittlung, Spielstand, UI und
    /// die 3D-Rückmeldungen im Raum. Die Bildschirme sind einfache Klassen ohne eigenes
    /// MonoBehaviour.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instanz { get; private set; }

        public Staffel2Daten Daten { get; private set; }
        public SpielstandAblage Ablage { get; private set; }
        public Spielstand Stand { get; private set; }
        public Einstellungen Einstellungen { get; private set; }
        public LinuxSitzung Linux { get; private set; }
        public Ermittlungssitzung Sitzung { get; private set; }
        public Ansicht Ansicht { get; private set; } = Ansicht.Menue;
        public AlexDialogStudio Alex { get; private set; }
        public string LadeText { get; private set; }
        public float LadeStartZeit { get; private set; }

        public event Action AnsichtGeaendert;
        /// <summary>Diagnosezeilen der laufenden Ermittlung (Testprotokoll).</summary>
        public event Action<string> Protokoll;

        private PlayerController _spieler;
        private PlayerInteractor _interaktor;
        private RackStatusLights[] _racks = new RackStatusLights[0];
        private MonitorScreen _monitor;
        private AlexFigur[] _raumAlex = new AlexFigur[0];
        private SpielAudio _audio;
        private Bildschirme _ui;
        private float _naechstesAutosave;
        private bool _speichertGerade;

        private void Awake()
        {
            Instanz = this;
            // Auch ohne Fensterfokus weiterlaufen (VM-Hintergrundbetrieb und Headless-/Xvfb-Tests).
            Application.runInBackground = true;
            // Im Editor bremsen, damit die emulierte VM genug Rechenzeit behält; im Player regelt VSync.
            Application.targetFrameRate = Application.isEditor ? 60 : -1;
            // -spielstand <ordner> trennt Testläufe vom echten Spielstand.
            var argv = Environment.GetCommandLineArgs();
            int si = Array.IndexOf(argv, "-spielstand");
            string ordner = si >= 0 && si + 1 < argv.Length ? argv[si + 1] : Path.Combine(Application.persistentDataPath, "spielstand");
            Ablage = new SpielstandAblage(ordner);
            Einstellungen = Ablage.LadeEinstellungen();
            int fsi = Array.IndexOf(argv, "-screen-fullscreen");
            if (fsi >= 0 && fsi + 1 < argv.Length) Einstellungen.Vollbild = argv[fsi + 1] != "0";
            Stand = Ablage.Laden(out var warnung);
            if (warnung != null) Debug.LogWarning(warnung);
            Daten = Staffel2Daten.Laden(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "staffel2", "staffel2.json")));

            Linux = gameObject.AddComponent<LinuxSitzung>();
            _audio = gameObject.AddComponent<SpielAudio>();
            Alex = new GameObject("AlexDialogStudio").AddComponent<AlexDialogStudio>();
            _spieler = FindFirstObjectByType<PlayerController>();
            _interaktor = FindFirstObjectByType<PlayerInteractor>();
            _racks = FindObjectsByType<RackStatusLights>(FindObjectsSortMode.None);
            _monitor = FindFirstObjectByType<MonitorScreen>();
            _raumAlex = new[] { "AlexRaumFigur", "AlexRaumFigurNeu" }
                .Select(GameObject.Find).Where(go => go != null)
                .Select(go => go.GetComponent<AlexFigur>()).Where(f => f != null).ToArray();
            foreach (var i in FindObjectsByType<Interactable>(FindObjectsSortMode.None)) i.Interacted += Interagiert;

            UiBaukasten.Schrift = Resources.Load<TMP_FontAsset>("Fonts/Inter SDF") ?? TMP_Settings.defaultFontAsset;
            UiBaukasten.Mono = Resources.Load<TMP_FontAsset>("Fonts/JetBrainsMono SDF") ?? UiBaukasten.Schrift;
            _ui = new Bildschirme(this);
            if (_interaktor != null) _interaktor.HintChanged += h => _ui.Hud.SetzeHinweis(h);
            UiBaukasten.Klick += () => _audio.Klick();
            EinstellungenAnwenden();
            Zeige(Ansicht.Menue);
            RaumAnzeigenAktualisieren();
            DemoAusKommandozeile();
        }

        /// <summary>-demo [-aufnahme ordner] [-protokoll datei] [-spielstand ordner]: automatischer Durchlauf.</summary>
        private void DemoAusKommandozeile()
        {
            var args = Environment.GetCommandLineArgs();
            string Wert(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            if (!args.Contains("-demo")) return;
            var episoden = (Wert("-episoden") ?? "0").Split(',').Select(x => int.TryParse(x, out var n) ? n : -1).Where(n => n >= 0);
            StarteDemo(Wert("-aufnahme"), Wert("-protokoll"), episoden, args.Contains("-schnell"), args.Contains("-kino"));
        }

        /// <summary>Automatischer Durchlauf (auch aus Tests und dem Editor aufrufbar).</summary>
        public DemoRegie StarteDemo(string aufnahme, string protokoll, System.Collections.Generic.IEnumerable<int> episoden, bool schnell, bool kino = false)
        {
            var regie = gameObject.AddComponent<DemoRegie>();
            regie.Starten(this, aufnahme, protokoll, episoden, schnell, kino);
            return regie;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (Ansicht == Ansicht.Terminal && (kb.f10Key.wasPressedThisFrame || (kb.ctrlKey.isPressed && kb.qKey.wasPressedThisFrame))) Zeige(Ansicht.Raum);
                else if (Ansicht == Ansicht.Terminal && kb.f2Key.wasPressedThisFrame) Zeige(Ansicht.Fallakte);
                else if (Ansicht != Ansicht.Terminal && kb.escapeKey.wasPressedThisFrame) Zurueck();
                else if (Ansicht == Ansicht.Raum && kb.fKey.wasPressedThisFrame) Zeige(Ansicht.Fallakte);
                else if (Ansicht == Ansicht.Raum && kb.pKey.wasPressedThisFrame) Zeige(Ansicht.Pager);
            }
            _ui.Aktualisiere();
            if (Alex?.Figur != null)
            {
                Alex.Figur.BewegungReduziert = Einstellungen.BewegungReduziert;
                // Tippt man im Terminal, hört Alex zu (solange er nicht selbst spricht).
                if (Ansicht == Ansicht.Terminal && kb != null && kb.anyKey.wasPressedThisFrame) Alex.Figur.Haltung(AlexHaltung.Zuhoeren, 2.5f);
            }
            foreach (var raumAlex in _raumAlex)
            {
                raumAlex.BewegungReduziert = Einstellungen.BewegungReduziert;
                if (Ansicht == Ansicht.Terminal && kb != null && kb.anyKey.wasPressedThisFrame) raumAlex.Haltung(AlexHaltung.Zuhoeren, 2.5f);
            }
            if (Sitzung != null && Sitzung.Bereit && Time.unscaledTime > _naechstesAutosave)
            {
                _naechstesAutosave = Time.unscaledTime + 120f;
                _ = SpeichernAsync(false);
            }
        }

        private void Zurueck()
        {
            switch (Ansicht)
            {
                case Ansicht.Raum: Zeige(Ansicht.Pause); break;
                case Ansicht.Fallakte:
                case Ansicht.Pager:
                case Ansicht.Pause: Zeige(Sitzung != null ? Ansicht.Raum : Ansicht.Menue); break;
                case Ansicht.Abschluss: Zeige(Ansicht.Menue); break;
            }
        }

        public void Zeige(Ansicht ansicht)
        {
            Ansicht = ansicht;
            bool steuern = ansicht == Ansicht.Raum;
            if (_spieler != null)
            {
                _spieler.InputBlocked = !steuern;
                _spieler.MouseSensitivity = 0.12f * Einstellungen.Mausempfindlichkeit;
                _spieler.ReducedMotion = Einstellungen.BewegungReduziert;
            }
            Cursor.lockState = steuern ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !steuern;
            _ui.ZeigeNur(ansicht);
            AnsichtGeaendert?.Invoke();
        }

        private void Interagiert(Interactable i)
        {
            if (Ansicht != Ansicht.Raum) return;
            _audio.Interaktion();
            switch (i.Id)
            {
                case "terminal": Zeige(Ansicht.Terminal); break;
                case "fallakte": Zeige(Ansicht.Fallakte); break;
                case "pager": Zeige(Ansicht.Pager); break;
                case "serverschrank":
                    _ui.Hud.Meldung(Sitzung == null ? "Der Schrank ist zu." : SchrankText());
                    break;
                case "alex":
                    SprichMitAlexImRaum();
                    break;
            }
        }

        private void SprichMitAlexImRaum()
        {
            string zeile;
            if (Sitzung == null)
            {
                zeile = "Alex: Willkommen im Serverraum! Eine Episode startest du im Hauptmenü.";
            }
            else
            {
                var offeneSpur = Sitzung.Ermittlung.Spuren.FirstOrDefault(s => Sitzung.AktuellerSchritt(s) != null);
                var schritt = offeneSpur != null ? Sitzung.AktuellerSchritt(offeneSpur) : null;
                string hinweis = schritt != null ? (schritt.Pager?.FirstOrDefault() ?? schritt.Title) : null;
                zeile = hinweis != null
                    ? $"Alex ({offeneSpur.Titel}): {hinweis}"
                    : "Alex: Alle Spuren stehen! Jetzt kannst du in der Fallakte (F) den Vorwurf erheben.";
            }
            var nachricht = new PagerNachricht
            {
                Art = "auftrag",
                Zeilen = new System.Collections.Generic.List<string> { zeile },
            };
            _ui.Hud.Pager(nachricht);
            AlexReagiert(nachricht);
            // Gibt es einen offenen Schritt, zeigt Alex auf den Arbeitsplatz-Monitor: dort geht es weiter.
            if (Sitzung != null && _monitor != null && !zeile.StartsWith("Alex: Alle Spuren"))
                foreach (var raumAlex in _raumAlex) raumAlex.ZeigeAuf(_monitor.transform.position, 2.6f);
        }

        /// <summary>Was der Serverschrank zeigt, kommt aus dem echten Spielzustand.</summary>
        private string SchrankText()
        {
            var e = Sitzung.Ermittlung;
            int fertig = e.Spuren.Count(s => Sitzung.AktuellerSchritt(s) == null);
            return $"{e.Hostname}: {fertig} von {e.Spuren.Count} Spuren abgeschlossen, {Sitzung.Zustand.BelegeErkannt.Count} Belege erkannt.";
        }

        // ---------------------------------------------------------------- Episoden

        public bool KannFortsetzen => Ablage.KannFortsetzen(Stand) && Stand.Laufend?.EpisodeId != null && Daten.EpisodeMitId(Stand.Laufend.EpisodeId) != null;

        public async void NeueEpisode(Episode episode)
        {
            Ablage.LaufendVerwerfen(Stand);
            Ablage.Speichern(Stand);
            await StarteAsync(episode, null);
        }

        public async void Fortsetzen()
        {
            if (!KannFortsetzen) { Zeige(Ansicht.Menue); return; }
            var episode = Daten.EpisodeMitId(Stand.Laufend.EpisodeId);
            await StarteAsync(episode, Stand.Laufend);
        }

        private async Task StarteAsync(Episode episode, LaufendeErmittlung laufend)
        {
            Sitzung?.Beenden();
            LadeStartZeit = Time.realtimeSinceStartup;
            LadeText = $"Episode {episode.Ermittlung.Nr}: {episode.Ermittlung.Titel}\nLinux startet …";
            Zeige(Ansicht.Laden);
            var image = episode.Ermittlung.Bildschirm ? LinuxImage.Bildschirm : LinuxImage.Terminal;
            bool ok = await Linux.StartenAsync(image, laufend != null ? Ablage.VmPfad : null);
            if (!ok)
            {
                LadeText = "Linux konnte nicht starten.\n" + Linux.FehlerText;
                _ui.Lade.FehlerAnzeigen(true);
                return;
            }
            string kopf = $"Rechenwerk Ostwind GmbH · {episode.Ermittlung.Hostname}";
            LadeText = laufend == null
                ? $"{kopf}\nErmittlung wird vorbereitet – Schritt 1 von 3 (Rahmen) …"
                : "Ermittlung wird fortgesetzt …";
            var sitzung = new Ermittlungssitzung(episode, () => (long)(Time.realtimeSinceStartupAsDouble * 1000));
            Sitzung = sitzung;
            sitzung.Geaendert += RaumAnzeigenAktualisieren;
            sitzung.Protokoll += text =>
            {
                Debug.Log("Ermittlung: " + text);
                if (laufend == null && sitzung == Sitzung)
                {
                    if (text.StartsWith("Setup Rahmen:"))
                        LadeText = $"{kopf}\nErmittlung wird vorbereitet – Schritt 2 von 3 (Einsatzrahmen) …";
                    else if (text.StartsWith("Setup Einsatzrahmen:"))
                        LadeText = $"{kopf}\nErmittlung wird vorbereitet – Schritt 3 von 3 (Episode) …";
                }
                Protokoll?.Invoke(text);
            };
            sitzung.NeueNachricht += n => { _audio.Pager(n.Art); _ui.Hud.Pager(n); AlexReagiert(n); };
            sitzung.EreignisEingetreten += e =>
            {
                if (e.Art == EreignisArt.SchrittErledigt || e.Art == EreignisArt.BelegErkannt) _ = SpeichernAsync(false);
                if (e.Art == EreignisArt.BelegErkannt) _audio.Erfolg();
                if (e.Art == EreignisArt.EinsatzrahmenVerletzt) _audio.Fehler();
            };
            sitzung.Geloest += eintrag => _ = AbschliessenAsync(eintrag);
            Linux.PromptSignal -= PromptSignal;
            Linux.PromptSignal += PromptSignal;
            await sitzung.StarteAsync(Linux.Agent, Linux.ReplyAsync, laufend);
            if (sitzung != Sitzung) return;
            if (sitzung.Fehler != null)
            {
                LadeText = sitzung.Fehler;
                _ui.Lade.FehlerAnzeigen(true);
                return;
            }
            if (laufend == null) await Linux.BegruessenAsync();
            else Linux.Tippe(new byte[] { (byte)'\r' });
            _ui.Lade.FehlerAnzeigen(false);
            _naechstesAutosave = Time.unscaledTime + 30f;
            Zeige(Ansicht.Raum);
            _ui.Hud.Meldung(laufend == null ? "Geh zum Arbeitsplatz und öffne das Terminal (E)." : "Willkommen zurück.");
            RaumAnzeigenAktualisieren();
        }

        private void PromptSignal(string payload) => Sitzung?.PromptSignal(payload);

        /// <summary>Alex spricht so lange, wie der Pager-Text in der Webversion zum Erscheinen braucht.</summary>
        private void AlexReagiert(PagerNachricht n)
        {
            var takt = Daten.PagerTakt;
            int zeichen = n.Zeilen.Sum(z => z.Length);
            float dauer = (zeichen * (takt?.MsJeZeichen ?? 12) + n.Zeilen.Count * (takt?.MsJeZeile ?? 200)) / 1000f;
            bool ernst = Sitzung != null && (Sitzung.Zustand.EinsatzrahmenVerletzt || ErmittlungsEngine.IstGesperrt(Sitzung.Zustand));
            float ausdruck = ernst ? 1f : n.Art == "erledigt" || n.Art == "szene" ? 0f : 0.45f;
            if (Alex?.Figur != null)
            {
                Alex.Figur.Spreche(dauer);
                Alex.Figur.Ausdruck(ausdruck);
            }
            foreach (var raumAlex in _raumAlex)
            {
                raumAlex.Spreche(dauer);
                raumAlex.Ausdruck(ausdruck);
            }
        }

        private async Task AbschliessenAsync(AkteEintrag eintrag)
        {
            Stand.LoeseEpisode(Sitzung.Ermittlung.Id, eintrag);
            Ablage.LaufendVerwerfen(Stand);
            Ablage.Speichern(Stand);
            _audio.Erfolg();
            await Task.Delay(1800);
            _ui.Abschluss.Setze(Sitzung, eintrag);
            Zeige(Ansicht.Abschluss);
        }

        /// <summary>Spielstand mit VM-Zustand sichern. Ohne laufende Episode nur die Akte.</summary>
        public Task SpeichernAsync(bool meldung)
        {
            // Läuft schon ein Speichern, danach genau eins anhängen: der Stand ist dann aktuell.
            var vorher = _speichern ?? Task.CompletedTask;
            if (_speichernWartet != null) return _speichernWartet;
            var neu = SpeichernNachAsync(vorher, meldung);
            _speichernWartet = neu;
            _speichern = neu;
            return neu;
        }

        private Task _speichern, _speichernWartet;

        private async Task SpeichernNachAsync(Task vorher, bool meldung)
        {
            // Erst zurückgeben, damit SpeichernAsync _speichernWartet setzt, bevor es hier gelöscht wird.
            await Task.Yield();
            try { await vorher; } catch { }
            _speichernWartet = null;
            _speichertGerade = true;
            try
            {
                var sitzung = Sitzung;
                if (sitzung != null && sitzung.Bereit && !sitzung.Zustand.Geloest && Linux.Phase == LinuxPhase.Bereit)
                {
                    await Linux.SpeichernAsync(Ablage.VmPfad);
                    // Während des Wartens kann der Fall gelöst worden sein (AbschliessenAsync hat Laufend
                    // und VM-Datei schon verworfen): den verspätet geschriebenen Stand dann wieder verwerfen,
                    // statt einen gelösten Fall als fortsetzbar zurückzuschreiben.
                    bool geloest = sitzung.Zustand.Geloest || Stand.Akte.ContainsKey(sitzung.Ermittlung.Id);
                    if (geloest) Ablage.LaufendVerwerfen(Stand);
                    else if (Sitzung == sitzung) Stand.Laufend = sitzung.StandFuerSpielstand();
                }
                Ablage.Speichern(Stand);
                if (meldung) _ui.Hud.Meldung("Gespeichert.");
                _ui.Hud.Speicherpunkt();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Speichern fehlgeschlagen: " + e.Message);
                if (meldung) _ui.Hud.Meldung("Speichern fehlgeschlagen: " + e.Message);
            }
            finally { _speichertGerade = false; }
        }

        public async void ZumHauptmenue()
        {
            await SpeichernAsync(false);
            Sitzung?.Beenden();
            Sitzung = null;
            Linux.Stoppen();
            Zeige(Ansicht.Menue);
            RaumAnzeigenAktualisieren();
        }

        public void FortschrittZuruecksetzen()
        {
            Sitzung?.Beenden();
            Sitzung = null;
            Linux.Stoppen();
            Ablage.LaufendVerwerfen(Stand);
            Stand.Zuruecksetzen(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Ablage.Speichern(Stand);
            Zeige(Ansicht.Menue);
        }

        public async void Beenden()
        {
            await SpeichernAsync(false);
            Linux.Stoppen();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- Einstellungen

        public void EinstellungenAnwenden()
        {
            var e = Einstellungen;
            _audio.Lautstaerken(e);
            QualitySettings.vSyncCount = e.VSync ? 1 : 0;
            if (e.Qualitaet >= 0 && e.Qualitaet < QualitySettings.names.Length) QualitySettings.SetQualityLevel(e.Qualitaet, true);
            if (!Application.isEditor && e.AufloesungBreite > 0)
                Screen.SetResolution(e.AufloesungBreite, e.AufloesungHoehe, e.Vollbild ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            else if (!Application.isEditor) Screen.fullScreenMode = e.Vollbild ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            _ui?.Terminal?.SetzeSchrift(e.TerminalSchrift);
            if (_spieler != null)
            {
                _spieler.MouseSensitivity = 0.12f * e.Mausempfindlichkeit;
                _spieler.ReducedMotion = e.BewegungReduziert;
            }
        }

        public void EinstellungenSpeichern()
        {
            Ablage.SpeichereEinstellungen(Einstellungen);
            EinstellungenAnwenden();
        }

        // ---------------------------------------------------------------- Raum

        /// <summary>Rack-Leuchten und Monitor zeigen nur, was der Spielzustand hergibt.</summary>
        private void RaumAnzeigenAktualisieren()
        {
            if (_monitor != null)
            {
                if (Sitzung == null) { _monitor.SetState(MonitorState.Aus); _monitor.SetLabel(""); }
                else if (Linux.Phase == LinuxPhase.Fehler) { _monitor.SetState(MonitorState.Fehler); _monitor.SetLabel("FEHLER"); }
                else if (!Sitzung.Bereit) { _monitor.SetState(MonitorState.Bereit); _monitor.SetLabel("STARTET"); }
                else if (Sitzung.Zustand.Geloest) { _monitor.SetState(MonitorState.Bereit); _monitor.SetLabel("GELÖST"); }
                else if (ErmittlungsEngine.IstGesperrt(Sitzung.Zustand)) { _monitor.SetState(MonitorState.Fehler); _monitor.SetLabel("GESPERRT"); }
                else { _monitor.SetState(MonitorState.Aktiv); _monitor.SetLabel("TERMINAL"); }
            }
            if (_racks.Length == 0) return;
            // Je Spur ein Rack: erledigt, aktiv oder Ruhe; Rahmenverstoß lässt alle warnen.
            var spuren = Sitzung?.Ermittlung.Spuren;
            for (int i = 0; i < _racks.Length; i++)
            {
                RackState s = RackState.Ruhe;
                if (Sitzung != null && Sitzung.Bereit)
                {
                    if (Sitzung.Zustand.EinsatzrahmenVerletzt) s = RackState.Warnung;
                    else if (spuren != null && i < spuren.Count) s = Sitzung.AktuellerSchritt(spuren[i]) == null ? RackState.Erledigt : RackState.Aktiv;
                }
                _racks[i].SetState(s);
            }
        }

        private void OnApplicationQuit()
        {
            // Synchron sichern geht nicht (VM-Zustand kommt asynchron); die Akte ist schon gespeichert,
            // der letzte Autosave enthält die VM.
            Linux?.Stoppen();
        }
    }
}
