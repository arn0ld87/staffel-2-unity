// Laufzeitzustand einer Ermittlung und deren Ereignisse. Die Engine verändert übergebene Zustände nie,
// sie liefert Kopien (wie die Objekt-Spreads in ermittlung.ts).
using System;
using System.Collections.Generic;

namespace Staffel2.Core
{
    public class ErmittlungsZustand
    {
        /// <summary>Je Spur-ID: Anzahl erledigter Schritte (von vorn). Fehlt ein Eintrag, gilt 0.</summary>
        public Dictionary<string, int> Erledigt { get; set; } = new Dictionary<string, int>();
        /// <summary>IDs der Belegregeln, deren verify bei der letzten vollständigen Prüfung Exit 0 hatte; sortiert.</summary>
        public List<string> BelegeErkannt { get; set; } = new List<string>();
        public bool EinsatzrahmenVerletzt { get; set; }
        /// <summary>Ein Vorwurf ist erst wieder möglich, wenn BelegeErkannt.Count >= SperreBis; null = frei.</summary>
        public int? SperreBis { get; set; }
        /// <summary>Vollständige Prüfungen, nach denen eine aktive Sperre spätestens endet; 0 = keine Sperre.</summary>
        public int SperreRestPruefungen { get; set; }
        public int VorwurfVersuche { get; set; }
        /// <summary>Ein richtiger Vorwurf liegt vor, aber noch nicht alle Spuren sind erledigt.</summary>
        public bool VorwurfRichtig { get; set; }
        /// <summary>Vorwurf richtig und alle Spuren erledigt.</summary>
        public bool Geloest { get; set; }
        /// <summary>Je Schritt (`spurId/schrittId`) die zuletzt gemeldete Diagnose; null, solange keine steht.</summary>
        public Dictionary<string, string> LetzteDiagnose { get; set; }

        public ErmittlungsZustand Kopie()
        {
            return new ErmittlungsZustand
            {
                Erledigt = new Dictionary<string, int>(Erledigt),
                BelegeErkannt = new List<string>(BelegeErkannt),
                EinsatzrahmenVerletzt = EinsatzrahmenVerletzt,
                SperreBis = SperreBis,
                SperreRestPruefungen = SperreRestPruefungen,
                VorwurfVersuche = VorwurfVersuche,
                VorwurfRichtig = VorwurfRichtig,
                Geloest = Geloest,
                LetzteDiagnose = LetzteDiagnose == null ? null : new Dictionary<string, string>(LetzteDiagnose),
            };
        }
    }

    public class ErmittlungsEreignis : IEquatable<ErmittlungsEreignis>
    {
        public EreignisArt Art { get; set; }
        public string SpurId { get; set; }
        public string SchrittId { get; set; }
        public string BelegId { get; set; }
        public string Text { get; set; }

        public static ErmittlungsEreignis SchrittErledigt(string spurId, string schrittId) =>
            new ErmittlungsEreignis { Art = EreignisArt.SchrittErledigt, SpurId = spurId, SchrittId = schrittId };
        public static ErmittlungsEreignis SpurErledigt(string spurId) =>
            new ErmittlungsEreignis { Art = EreignisArt.SpurErledigt, SpurId = spurId };
        public static ErmittlungsEreignis BelegErkannt(string belegId) =>
            new ErmittlungsEreignis { Art = EreignisArt.BelegErkannt, BelegId = belegId };
        public static ErmittlungsEreignis BelegVerloren(string belegId) =>
            new ErmittlungsEreignis { Art = EreignisArt.BelegVerloren, BelegId = belegId };
        public static ErmittlungsEreignis Einfach(EreignisArt art) => new ErmittlungsEreignis { Art = art };
        public static ErmittlungsEreignis Diagnose(string spurId, string schrittId, string text) =>
            new ErmittlungsEreignis { Art = EreignisArt.Diagnose, SpurId = spurId, SchrittId = schrittId, Text = text };

        public bool Equals(ErmittlungsEreignis other) =>
            other != null && Art == other.Art && SpurId == other.SpurId && SchrittId == other.SchrittId
            && BelegId == other.BelegId && Text == other.Text;

        public override bool Equals(object obj) => Equals(obj as ErmittlungsEreignis);

        public override int GetHashCode() => HashCode.Combine((int)Art, SpurId, SchrittId, BelegId, Text);

        public override string ToString() =>
            $"{Art}({SpurId}/{SchrittId}/{BelegId}/{Text})";
    }

    public class PruefAnwendung
    {
        public ErmittlungsZustand Zustand { get; set; }
        public List<ErmittlungsEreignis> Ereignisse { get; set; } = new List<ErmittlungsEreignis>();
    }

    public class VorwurfAuswertung
    {
        public ErmittlungsZustand Zustand { get; set; }
        public VorwurfErgebnis Ergebnis { get; set; }
        public IReadOnlyList<string> Antwort { get; set; }
    }

    /// <summary>Vorwurf des Spielers: Verdächtige und die genannten Belege.</summary>
    public class VorwurfEingabe
    {
        public string VerdaechtigeId { get; set; }
        public IReadOnlyList<string> BelegIds { get; set; } = new string[0];
    }
}
