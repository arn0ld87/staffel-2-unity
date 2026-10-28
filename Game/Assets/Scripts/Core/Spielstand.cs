// Spielstand von Staffel 2 (Akte je Episode aus staffel.ts/staffelStore.ts) plus eine native
// Erweiterung: `Laufend` hält die gerade laufende Episode, damit das Spiel mittendrin fortsetzen kann.
// Reine Daten und Regeln; Dateizugriff liegt in der Unity-Schicht. Zeiten sind Millisekunden.
using System;
using System.Collections.Generic;

namespace Staffel2.Core
{
    /// <summary>Eine gelöste Episode in der Ermittlungsakte.</summary>
    public class AkteEintrag
    {
        public long DurationMs { get; set; }
        /// <summary>Tippstufe 3 genutzt.</summary>
        public bool WithSolution { get; set; }
        /// <summary>Abgerufene Tipps über alle Schritte.</summary>
        public int HintsUsed { get; set; }
        public long FinishedAt { get; set; }
        /// <summary>Vorwürfe, die der gelöste Lauf bis zum richtigen brauchte.</summary>
        public int? VorwurfVersuche { get; set; }
    }

    /// <summary>Die gerade laufende Episode; wird mit jedem Zustandswechsel neu gespeichert.</summary>
    public class LaufendeErmittlung
    {
        public string EpisodeId { get; set; }
        public ErmittlungsZustand Zustand { get; set; } = new ErmittlungsZustand();
        /// <summary>Je Schritt (`spurId/schrittId`) die Zahl bereits aufgedeckter Tipps (0 bis 3).</summary>
        public Dictionary<string, int> GezeigteTipps { get; set; } = new Dictionary<string, int>();
        /// <summary>Abgerufene Tipps dieses Laufs (wird zu AkteEintrag.HintsUsed).</summary>
        public int HintsUsed { get; set; }
        /// <summary>Stufe 3 aufgedeckt (wird zu AkteEintrag.WithSolution).</summary>
        public bool WithSolution { get; set; }
        /// <summary>Bisherige Spielzeit der Episode; die Unity-Schicht zählt sie fort.</summary>
        public long ElapsedMs { get; set; }
        /// <summary>IDs der bereits gezeigten Pager-Nachrichten, in Reihenfolge.</summary>
        public List<string> PagerVerlauf { get; set; } = new List<string>();

        /// <summary>
        /// Deckt den nächsten Tipp des Schritts auf und gibt ihn zurück (Richtung, Befehl, Lösung); null, wenn
        /// alle drei schon gezeigt sind. Jeder neue Tipp zählt, Stufe 3 gilt als „mit Lösung“.
        /// </summary>
        public string TippAufdecken(string spurId, string schrittId, ErmittlungsSchritt schritt)
        {
            var key = $"{spurId}/{schrittId}";
            var anzahl = GezeigteTipps.TryGetValue(key, out var n) ? n : 0;
            if (anzahl >= 3 || anzahl >= schritt.Tipps.Count) return null;
            var stufe = anzahl + 1;
            GezeigteTipps[key] = stufe;
            HintsUsed += 1;
            WithSolution = WithSolution || stufe == 3;
            return schritt.Tipps[anzahl];
        }

        /// <summary>Der AkteEintrag für den Abschluss dieses Laufs.</summary>
        public AkteEintrag ZuEintrag(long durationMs, long finishedAt) => new AkteEintrag
        {
            DurationMs = durationMs,
            WithSolution = WithSolution,
            HintsUsed = HintsUsed,
            FinishedAt = finishedAt,
            VorwurfVersuche = Zustand.VorwurfVersuche,
        };
    }

    public class Spielstand
    {
        public const int AktuelleVersion = 1;

        public int Version { get; set; } = AktuelleVersion;
        public Dictionary<string, AkteEintrag> Akte { get; set; } = new Dictionary<string, AkteEintrag>();
        /// <summary>Zeitpunkt des letzten Zurücksetzens.</summary>
        public long? ZurueckgesetztAm { get; set; }
        public LaufendeErmittlung Laufend { get; set; }

        /// <summary>Lauf a ist besser als b: ohne Lösung schlägt mit Lösung, sonst zählt die kürzere Zeit.</summary>
        public static bool Besser(AkteEintrag a, AkteEintrag b)
        {
            if (a.WithSolution != b.WithSolution) return !a.WithSolution;
            return a.DurationMs < b.DurationMs;
        }

        /// <summary>
        /// Trägt den Abschluss einer Episode ein (staffelStore.loeseEpisode): der bessere Lauf bleibt. Ein
        /// laufender Stand derselben Episode endet damit.
        /// </summary>
        public void LoeseEpisode(string episodeId, AkteEintrag eintrag)
        {
            if (!Akte.TryGetValue(episodeId, out var vorher) || Besser(eintrag, vorher)) Akte[episodeId] = eintrag;
            if (Laufend != null && Laufend.EpisodeId == episodeId) Laufend = null;
        }

        /// <summary>Löscht den Stand; der Zeitstempel bleibt stehen (loescheStaffel).</summary>
        public void Zuruecksetzen(long jetztMs)
        {
            Akte = new Dictionary<string, AkteEintrag>();
            Laufend = null;
            ZurueckgesetztAm = jetztMs;
        }
    }
}
