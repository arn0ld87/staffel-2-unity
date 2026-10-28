// Wurzel des Exports staffel2.fixture.json: { format, quelle, episoden, pagerPosen, pagerTakt, glossar }.
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Staffel2.Core
{
    public class Staffel2Daten
    {
        public int Format { get; set; }
        public QuelleInfo Quelle { get; set; }
        public List<Episode> Episoden { get; set; } = new List<Episode>();
        public List<string> PagerPosen { get; set; } = new List<string>();
        public PagerTakt PagerTakt { get; set; }
        /// <summary>Befehl → Glossar-Eintrag.</summary>
        public Dictionary<string, Werkzeug> Glossar { get; set; } = new Dictionary<string, Werkzeug>();

        public static Staffel2Daten Laden(string json)
        {
            var daten = JsonConvert.DeserializeObject<Staffel2Daten>(json);
            if (daten == null) throw new ArgumentException("Leerer Staffel-2-Export");
            return daten;
        }

        public Episode EpisodeMitId(string id)
        {
            foreach (var episode in Episoden)
                if (episode.Ermittlung != null && episode.Ermittlung.Id == id) return episode;
            return null;
        }
    }
}
