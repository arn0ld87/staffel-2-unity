// Stößt die Prüfung einer Ermittlung an, ohne den Aufrufer warten zu lassen (Port von pruefLauf.ts):
// höchstens ein Bündel gleichzeitig in der VM, beliebig viele Anstöße währenddessen ergeben genau
// einen Nachlauf. Das Ergebnis gilt für den Zustand zum Zeitpunkt der Antwort.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Staffel2.Core
{
    public enum PruefFehlerArt
    {
        /// <summary>Agent-Timeout, ERR vom Agenten oder abgelehntes Run. Zustand bleibt unverändert.</summary>
        Agent,
        /// <summary>BuendelSkript hat geworfen (ungültige Prüf-ID): Datenfehler, kein Lauf gesendet.</summary>
        Skript,
        /// <summary>Fehler beim Lesen oder Anwenden des Zustands; kein Agent-Problem.</summary>
        Intern,
    }

    public sealed class PruefFehler
    {
        public PruefFehlerArt Art { get; set; }
        public string Meldung { get; set; }
    }

    public sealed class PruefLaufOptionen
    {
        public Ermittlung Ermittlung { get; set; }
        public Func<string, int, Task<AgentResult>> Run { get; set; }
        public Func<ErmittlungsZustand> LeseZustand { get; set; }
        /// <summary>Wird nur aufgerufen, wenn der Lauf nicht gestoppt wurde. Ereignisse dürfen leer sein.</summary>
        public Action<ErmittlungsZustand, List<ErmittlungsEreignis>> SchreibeZustand { get; set; }
        public Action<PruefFehler> MeldeFehler { get; set; }
        public int TimeoutMs { get; set; } = PruefLauf.PruefTimeoutMs;
    }

    public sealed class PruefLauf
    {
        /// <summary>Wartezeit auf ein Bündel; der Gast bricht laut AgentClient eine Sekunde früher ab.</summary>
        public const int PruefTimeoutMs = 20_000;

        private readonly PruefLaufOptionen _o;
        private readonly object _lock = new object();
        private bool _gestoppt;
        private bool _laufend;
        private bool _nachlauf;
        private TaskCompletionSource<bool> _leerlauf;
        private int _durchlaeufe;
        private bool _letzterOk;

        public PruefLauf(PruefLaufOptionen optionen)
        {
            _o = optionen;
        }

        /// <summary>Kehrt sofort zurück. Startet einen Lauf oder merkt einen Nachlauf vor.</summary>
        public void Anstossen()
        {
            lock (_lock)
            {
                if (_gestoppt) return;
                if (_laufend)
                {
                    _nachlauf = true;
                    return;
                }
                _laufend = true;
            }
            _ = Lauf();
        }

        /// <summary>Danach wird kein Ergebnis mehr angewendet und kein neuer Lauf gestartet. Idempotent.</summary>
        public void Stoppe()
        {
            lock (_lock)
            {
                _gestoppt = true;
                _nachlauf = false;
                _laufend = false;
            }
            LeerlaufErfuellen();
        }

        /// <summary>true, solange ein Bündel unterwegs ist und nicht gestoppt wurde.</summary>
        public bool Laeuft
        {
            get { lock (_lock) return _laufend; }
        }

        /// <summary>Abgeschlossene Durchläufe, auch gescheiterte. Steht fest, bevor Leerlauf() sich erfüllt.</summary>
        public int Durchlaeufe
        {
            get { lock (_lock) return _durchlaeufe; }
        }

        /// <summary>
        /// true, wenn der zuletzt abgeschlossene Durchlauf vollständig geprüft und angewendet hat (oder nichts zu
        /// prüfen war); false nach jedem gemeldeten Fehler. Wer sich auf ein frisches Ergebnis verlassen muss
        /// (Vorwurf), vergleicht <see cref="Durchlaeufe"/> vor dem Anstoßen und prüft danach diesen Wert.
        /// </summary>
        public bool LetzterDurchlaufOk
        {
            get { lock (_lock) return _letzterOk; }
        }

        /// <summary>
        /// Erfüllt sich, sobald kein Lauf mehr unterwegs und kein Nachlauf vorgemerkt ist, oder sofort
        /// nach Stoppe(). Wirft nie.
        /// </summary>
        public Task Leerlauf()
        {
            lock (_lock)
            {
                if (_gestoppt || (!_laufend && !_nachlauf)) return Task.CompletedTask;
                if (_leerlauf == null)
                    _leerlauf = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _leerlauf.Task;
            }
        }

        private void LeerlaufErfuellen()
        {
            TaskCompletionSource<bool> fertig;
            lock (_lock)
            {
                fertig = _leerlauf;
                _leerlauf = null;
            }
            fertig?.TrySetResult(true);
        }

        private bool Gestoppt { get { lock (_lock) return _gestoppt; } }

        // Nach Stoppe() gibt es keine Meldung mehr: Die UI dahinter ist womöglich schon ausgehängt.
        private void Melden(PruefFehlerArt art, string meldung)
        {
            if (Gestoppt || _o.MeldeFehler == null) return;
            try { _o.MeldeFehler(new PruefFehler { Art = art, Meldung = meldung }); }
            catch { /* Ein werfender Rückruf darf den Lauf nicht aus dem Tritt bringen. */ }
        }

        // Ein Durchlauf. Kehrt in jedem Fall normal zurück, damit danach immer die Nachlauf-Prüfung kommt.
        // Ergebnis: true, wenn vollständig geprüft und angewendet (oder nichts offen war).
        private async Task<bool> Durchlauf()
        {
            List<Pruefung> pruefungen;
            try { pruefungen = ErmittlungsEngine.OffenePruefungen(_o.Ermittlung, _o.LeseZustand()); }
            catch (Exception error) { Melden(PruefFehlerArt.Intern, error.Message); return false; }
            if (pruefungen.Count == 0) return true;

            string skript;
            try { skript = ErmittlungsEngine.BuendelSkript(pruefungen); }
            catch (Exception error) { Melden(PruefFehlerArt.Skript, error.Message); return false; }

            if (Gestoppt) return false;
            AgentResult ergebnis;
            try { ergebnis = await _o.Run(skript, _o.TimeoutMs).ConfigureAwait(false); }
            catch (Exception error) { Melden(PruefFehlerArt.Agent, error.Message); return false; }

            // Nach Stoppe() gilt nichts mehr, auch keine bereits eingetroffene Antwort.
            if (Gestoppt) return false;

            if (ergebnis.ExitCode != 0)
            {
                Melden(PruefFehlerArt.Agent, $"Prüfung fehlgeschlagen (exit {ergebnis.ExitCode})");
                return false;
            }

            PruefAnwendung neu;
            try
            {
                neu = ErmittlungsEngine.WendePruefungAn(
                    _o.Ermittlung, _o.LeseZustand(), ErmittlungsEngine.LeseBuendelAusgabe(ergebnis.Output));
            }
            catch (Exception error) { Melden(PruefFehlerArt.Intern, error.Message); return false; }
            if (Gestoppt) return false;
            try { _o.SchreibeZustand(neu.Zustand, neu.Ereignisse); }
            catch { /* Auch ein werfender Rückruf beendet den Durchlauf regulär. */ }
            return true;
        }

        private async Task Lauf()
        {
            try
            {
                for (;;)
                {
                    bool ok = false;
                    try { ok = await Durchlauf().ConfigureAwait(false); }
                    catch { /* Durchlauf fängt selbst alles ab; dies ist nur die letzte Absicherung. */ }
                    lock (_lock)
                    {
                        _durchlaeufe++;
                        _letzterOk = ok;
                        if (_gestoppt || !_nachlauf) break;
                        _nachlauf = false;
                    }
                }
            }
            finally
            {
                // Nach Stoppe() hat Stoppe() schon aufgeräumt; ein später endender Lauf ändert nichts mehr.
                var erfuellen = false;
                lock (_lock)
                {
                    if (!_gestoppt)
                    {
                        _laufend = false;
                        _nachlauf = false;
                        erfuellen = true;
                    }
                }
                if (erfuellen) LeerlaufErfuellen();
            }
        }
    }
}
