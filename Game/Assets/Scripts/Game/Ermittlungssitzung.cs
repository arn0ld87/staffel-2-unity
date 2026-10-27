using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Staffel2.Core;

namespace Staffel2.Game
{
    /// <summary>Eine Pager-Nachricht wie in der Webversion: Art auftrag | erledigt | szene.</summary>
    public sealed class PagerNachricht
    {
        public int Id;
        public string Art;
        public List<string> Zeilen = new List<string>();
        public string Pose;

        /// <summary>Kompakte Form für den Spielstand: "art\tpose\tzeile1\u001ezeile2".</summary>
        public string Kodieren() => Art + "\t" + (Pose ?? "") + "\t" + string.Join("\u001e", Zeilen);

        public static PagerNachricht Dekodieren(int id, string text)
        {
            var teile = text.Split(new[] { '\t' }, 3);
            if (teile.Length < 3) return null;
            return new PagerNachricht
            {
                Id = id,
                Art = teile[0],
                Pose = teile[1].Length > 0 ? teile[1] : null,
                Zeilen = teile[2].Split('\u001e').ToList(),
            };
        }
    }

    /// <summary>
    /// Ablauf einer laufenden Episode, nachgebaut nach ErmittlungApp.tsx der Webversion:
    /// Rahmen- und Episoden-Setup in der VM, Prüflauf nach jedem Prompt, Antworten auf
    /// Ablesen-Schritte, Vorwurf mit vorheriger Frischprüfung, Pager-Meldungen, Tipps und
    /// Abschluss. Ohne Unity: läuft gegen jeden <see cref="IAgentRunner"/>.
    /// </summary>
    public sealed class Ermittlungssitzung
    {
        public const string RahmenFehler = "Der Rahmen für Staffel 2 konnte nicht gesetzt werden.";
        public const string PruefHinweis = "Prüfung gerade nicht erreichbar. Der nächste Befehl versucht es erneut.";
        public const string MissionHinweis = "In Staffel 2 gibt es mission noch nicht. Schau in den Pager.";
        public const string AntwortZeichenHinweis = "Bitte nur Buchstaben, Ziffern und . : _ / -";
        public const string AntwortLeerHinweis = "Bitte trag erst eine Antwort ein.";
        public const string AntwortFalschHinweis = "Das passt nicht zu dem, was das System zeigt. Schau noch mal genau hin.";
        public const string AntwortFehlerHinweis = "Die Antwort konnte gerade nicht geprüft werden. Versuch es gleich noch einmal.";
        public const string BelegUngueltig = "Ein ausgewählter Beleg ist nicht mehr gültig. Aktualisiere die Aktenwand.";
        public const string VorwurfFehler = "Belege konnten gerade nicht geprüft werden. Bitte erneut versuchen.";
        private const int SetupTimeoutMs = 60_000;

        private static readonly HashSet<string> MissionBefehle = new HashSet<string> { "status", "hint", "check", "solve", "flag", "next", "reset" };

        public Episode Episode { get; }
        public Ermittlung Ermittlung => Episode.Ermittlung;
        public ErmittlungsZustand Zustand { get; private set; } = ErmittlungsEngine.StartZustand();
        public LaufendeErmittlung Laufend { get; private set; }
        public List<PagerNachricht> Pager { get; } = new List<PagerNachricht>();
        public bool Bereit { get; private set; }
        public string Fehler { get; private set; }
        public string PruefHinweisText { get; private set; }
        public bool PrueftAntwort { get; private set; }
        public bool PrueftVorwurf { get; private set; }

        /// <summary>Zustand, Pager oder Hinweise haben sich geändert (für UI und 3D-Anzeigen).</summary>
        public event Action Geaendert;
        public event Action<PagerNachricht> NeueNachricht;
        public event Action<ErmittlungsEreignis> EreignisEingetreten;
        /// <summary>Fall gelöst: Akte-Eintrag bereit zum Speichern.</summary>
        public event Action<AkteEintrag> Geloest;

        private readonly ErmittlungsRun _run = new ErmittlungsRun();
        private readonly Func<long> _jetztMs;
        private IAgentRunner _client;
        private Func<int, string, int, string, Task> _reply;
        private PruefLauf _lauf;
        private bool _promptAusstehend;
        private int _version;
        private long _startMs;

        public Ermittlungssitzung(Episode episode, Func<long> jetztMs)
        {
            Episode = episode ?? throw new ArgumentNullException(nameof(episode));
            _jetztMs = jetztMs;
        }

        /// <summary>
        /// Startet die Episode in einer bereiten VM. Mit <paramref name="fortsetzen"/> läuft
        /// kein Setup: die VM kommt dann aus dem gespeicherten Zustand und hat die Dateien schon.
        /// </summary>
        /// <param name="reply">Antwort an `mission` in der VM: (requestId, exitCode, text).</param>
        public async Task StarteAsync(IAgentRunner client, Func<string, int, string, Task> reply, LaufendeErmittlung fortsetzen = null)
        {
            int version = ++_version;
            _client = client;
            _reply = (v, id, code, text) => v == _version ? reply(id, code, text) : Task.CompletedTask;
            _lauf?.Stoppe();
            _lauf = null;
            Bereit = false;
            Fehler = null;
            PruefHinweisText = null;
            bool IstAktuell() => version == _version;

            if (fortsetzen == null || fortsetzen.EpisodeId != Ermittlung.Id)
            {
                try
                {
                    foreach (var skript in new[] { Episode.RahmenSetup, Episode.EinsatzrahmenSetup, Ermittlung.Setup })
                    {
                        var res = await _run.RunAsync(client, skript, SetupTimeoutMs, IstAktuell);
                        if (!IstAktuell()) return;
                        if (res.ExitCode != 0) throw new AgentException(RahmenFehler);
                    }
                }
                catch (Exception)
                {
                    if (IstAktuell()) { Fehler = RahmenFehler; Geaendert?.Invoke(); }
                    return;
                }
                Zustand = ErmittlungsEngine.StartZustand();
                Laufend = new LaufendeErmittlung { EpisodeId = Ermittlung.Id, Zustand = Zustand };
                Pager.Clear();
                _startMs = _jetztMs();
                var startSpur = Ermittlung.Spuren.FirstOrDefault(s => ErmittlungsEngine.AktuellerSchritt(s, Zustand) != null);
                var erster = startSpur != null ? ErmittlungsEngine.AktuellerSchritt(startSpur, Zustand) : null;
                if (erster != null) Melde("auftrag", erster.Pager, erster.Pose);
            }
            else
            {
                Laufend = fortsetzen;
                Zustand = ErmittlungsEngine.MitZustandsDefaults(fortsetzen.Zustand, null);
                Pager.Clear();
                for (int i = 0; i < fortsetzen.PagerVerlauf.Count; i++)
                {
                    var n = PagerNachricht.Dekodieren(i + 1, fortsetzen.PagerVerlauf[i]);
                    if (n != null) Pager.Add(n);
                }
                // Die bisherige Spielzeit läuft weiter.
                _startMs = _jetztMs() - fortsetzen.ElapsedMs;
            }

            _lauf = new PruefLauf(new PruefLaufOptionen
            {
                Ermittlung = Ermittlung,
                Run = (skript, timeoutMs) => _run.RunAsync(client, skript, timeoutMs, IstAktuell),
                LeseZustand = () => Zustand,
                SchreibeZustand = (neu, ereignisse) => { if (IstAktuell()) SchreibeZustand(neu, ereignisse); },
                MeldeFehler = _ => { if (IstAktuell()) { PruefHinweisText = PruefHinweis; Geaendert?.Invoke(); } },
            });
            Bereit = true;
            if (_promptAusstehend)
            {
                _promptAusstehend = false;
                _lauf.Anstossen();
            }
            Geaendert?.Invoke();
        }

        /// <summary>OSC 7771 aus dem Terminal: "anfrage-id;befehl".</summary>
        public void PromptSignal(string payload)
        {
            var teile = payload.Split(';');
            if (teile.Length != 2 || !AgentProtocol.IsValidId(teile[0])) return;
            string befehl = teile[1];
            if (befehl == "__prompt")
            {
                // Der erste Prompt der frischen Shell kann während des Setups eintreffen.
                if (_lauf != null && Bereit) _lauf.Anstossen();
                else _promptAusstehend = true;
            }
            else if (MissionBefehle.Contains(befehl))
            {
                _ = _reply?.Invoke(_version, teile[0], 1, MissionHinweis);
            }
        }

        /// <summary>„Stand prüfen“: derselbe volle Prüflauf wie nach einem Befehl.</summary>
        public void StandPruefen() => _lauf?.Anstossen();

        /// <summary>Aktueller Schritt einer Spur oder null, wenn die Spur erledigt ist.</summary>
        public ErmittlungsSchritt AktuellerSchritt(Spur spur) => ErmittlungsEngine.AktuellerSchritt(spur, Zustand);

        public async Task AntwortenAsync(string spurId, string roh)
        {
            var spur = Ermittlung.Spuren.FirstOrDefault(s => s.Id == spurId);
            var schritt = spur != null ? AktuellerSchritt(spur) : null;
            if (!Bereit || spur == null || schritt == null || schritt.Art != Schrittart.Ablesen || schritt.Antwort == null) return;
            if (PrueftAntwort || Zustand.Geloest) return;
            string text = (roh ?? "").Trim();
            if (text.Length == 0) { Melde("auftrag", new[] { Satz(AntwortLeerHinweis) }); return; }
            if (!ErmittlungsEngine.IstGueltigeAntwort(text)) { Melde("auftrag", new[] { Satz(AntwortZeichenHinweis) }); return; }
            int version = _version;
            PrueftAntwort = true;
            Geaendert?.Invoke();
            try
            {
                var res = await _run.RunAsync(_client, ErmittlungsEngine.AntwortSkript(schritt.Antwort.Pruefung, text), ErmittlungsEngine.AntwortTimeoutMs, () => version == _version);
                if (version != _version) return;
                bool richtig = res.ExitCode == 0;
                var neu = ErmittlungsEngine.WendeAntwortAn(Ermittlung, Zustand, spur.Id, schritt.Id, richtig);
                if (neu.Ereignisse.Count > 0) SchreibeZustand(neu.Zustand, neu.Ereignisse);
                else if (!richtig) Melde("auftrag", new[] { Satz(AntwortFalschHinweis) });
            }
            catch (Exception)
            {
                if (version == _version) Melde("auftrag", new[] { Satz(AntwortFehlerHinweis) });
            }
            finally
            {
                if (version == _version) { PrueftAntwort = false; Geaendert?.Invoke(); }
            }
        }

        /// <summary>
        /// Vorwurf erheben. Vorher läuft eine frische Prüfung, damit die Belege dem aktuellen
        /// Stand der VM entsprechen (wie erstelleVorwurfAblauf der Webversion).
        /// </summary>
        public async Task<VorwurfErgebnis?> VorwurfAsync(string verdaechtigeId, IReadOnlyList<string> belegIds)
        {
            if (!Bereit || PrueftVorwurf || Zustand.Geloest) return null;
            int version = _version;
            PrueftVorwurf = true;
            Geaendert?.Invoke();
            try
            {
                _lauf.Anstossen();
                await _lauf.Leerlauf();
                if (version != _version) return null;
                var auswertung = ErmittlungsEngine.PruefeVorwurf(Ermittlung, Zustand, new VorwurfEingabe { VerdaechtigeId = verdaechtigeId, BelegIds = belegIds });
                bool warGeloest = Zustand.Geloest;
                Zustand = auswertung.Zustand;
                Laufend.Zustand = Zustand;
                var antwort = auswertung.Antwort ?? new string[0];
                switch (auswertung.Ergebnis)
                {
                    case VorwurfErgebnis.BelegUnbekannt: Melde("auftrag", new[] { Satz(BelegUngueltig) }); break;
                    case VorwurfErgebnis.Richtig:
                        if (antwort.Count > 0) Melde("erledigt", antwort);
                        _lauf.Stoppe();
                        break;
                    case VorwurfErgebnis.RichtigOffen: Melde("erledigt", antwort); break;
                    case VorwurfErgebnis.BereitsGeloest: break;
                    default: if (antwort.Count > 0) Melde("auftrag", antwort); break;
                }
                if (!warGeloest && Zustand.Geloest) Abschliessen();
                return auswertung.Ergebnis;
            }
            catch (Exception)
            {
                if (version == _version) Melde("auftrag", new[] { Satz(VorwurfFehler) });
                return null;
            }
            finally
            {
                if (version == _version) { PrueftVorwurf = false; Geaendert?.Invoke(); }
            }
        }

        /// <summary>Nächsten Tipp eines Schritts aufdecken (Stufe 3 ist die Lösung).</summary>
        public string TippAufdecken(Spur spur)
        {
            var schritt = AktuellerSchritt(spur);
            if (schritt == null || Laufend == null) return null;
            var tipp = Laufend.TippAufdecken(spur.Id, schritt.Id, schritt);
            Geaendert?.Invoke();
            return tipp;
        }

        public int GezeigteTipps(Spur spur)
        {
            var schritt = AktuellerSchritt(spur);
            if (schritt == null || Laufend == null) return 0;
            return Laufend.GezeigteTipps.TryGetValue(spur.Id + "/" + schritt.Id, out var n) ? n : 0;
        }

        /// <summary>Aktuellen Stand für den Spielstand übernehmen (vor dem Speichern).</summary>
        public LaufendeErmittlung StandFuerSpielstand()
        {
            if (Laufend == null) return null;
            Laufend.Zustand = Zustand;
            Laufend.ElapsedMs = Math.Max(0, _jetztMs() - _startMs);
            Laufend.PagerVerlauf = Pager.Select(p => p.Kodieren()).ToList();
            return Laufend;
        }

        public void Beenden()
        {
            _version++;
            _lauf?.Stoppe();
            _lauf = null;
            Bereit = false;
        }

        private void SchreibeZustand(ErmittlungsZustand neu, List<ErmittlungsEreignis> ereignisse)
        {
            bool warGeloest = Zustand.Geloest;
            Zustand = neu;
            if (Laufend != null) Laufend.Zustand = neu;
            PruefHinweisText = ereignisse.Any(e => e.Art == EreignisArt.PruefungUnvollstaendig) ? PruefHinweis : null;
            foreach (var ereignis in ereignisse)
            {
                switch (ereignis.Art)
                {
                    case EreignisArt.SchrittErledigt:
                    {
                        var schritt = Ermittlung.Spuren.FirstOrDefault(s => s.Id == ereignis.SpurId)?.Schritte.FirstOrDefault(s => s.Id == ereignis.SchrittId);
                        if (schritt != null) Melde("erledigt", new[] { Satz("Erledigt: " + schritt.Title) });
                        break;
                    }
                    case EreignisArt.SpurErledigt:
                    {
                        var spur = Ermittlung.Spuren.FirstOrDefault(s => s.Id == ereignis.SpurId);
                        if (spur != null) Melde("erledigt", new[] { Satz("Spur abgeschlossen: " + spur.Titel) });
                        break;
                    }
                    case EreignisArt.Diagnose: Melde("auftrag", new[] { Satz(ereignis.Text) }); break;
                    case EreignisArt.EinsatzrahmenVerletzt: Melde("auftrag", Ermittlung.Einsatzrahmen.Folge); break;
                    case EreignisArt.SperreAufgehoben: Melde("erledigt", new[] { Satz("Der Vorwurf ist wieder möglich.") }); break;
                    case EreignisArt.FallGeloest: Melde("erledigt", Ermittlung.Vorwurf.Antworten.Richtig); break;
                }
                EreignisEingetreten?.Invoke(ereignis);
            }
            if (!warGeloest && Zustand.Geloest) Abschliessen();
            Geaendert?.Invoke();
        }

        private void Abschliessen()
        {
            _lauf?.Stoppe();
            if (Ermittlung.Szene != null && Ermittlung.Szene.Count > 0) Melde("szene", Ermittlung.Szene, Ermittlung.SzenePose ?? "zeigt");
            var eintrag = Laufend.ZuEintrag(Math.Max(0, _jetztMs() - _startMs), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            eintrag.VorwurfVersuche = Zustand.VorwurfVersuche;
            Geloest?.Invoke(eintrag);
        }

        private void Melde(string art, IEnumerable<string> zeilen, string pose = null)
        {
            var n = new PagerNachricht { Id = Pager.Count + 1, Art = art, Zeilen = zeilen.ToList(), Pose = pose };
            Pager.Add(n);
            NeueNachricht?.Invoke(n);
            Geaendert?.Invoke();
        }

        /// <summary>Wie satz() der Webversion: Punkt am Ende, ohne doppelte Satzzeichen.</summary>
        public static string Satz(string text)
        {
            string t = (text ?? "").TrimEnd();
            return t.Length > 0 && ".!?…:".IndexOf(t[t.Length - 1]) >= 0 ? t : t + ".";
        }
    }
}
