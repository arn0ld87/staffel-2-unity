// Datenmodell der Ermittlungen (Port von ermittlungTypes.ts und staffelTypes.ts): reine Daten.
// Die JSON-Namen des Exports (camelCase) liest Newtonsoft ohne Attribute, Groß-/Kleinschreibung
// ist beim Lesen egal.
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Staffel2.Core
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AnsichtArt
    {
        Raum,
        Aktenwand,
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum Schrittart
    {
        Sichern,
        Eingreifen,
        Reparieren,
        Ablesen,
        Ordnen,
        Bearbeiten,
    }

    /// <summary>Befehlskarte (MissionTool): Eintrag aus dem Glossar oder Werkzeug einer Episode.</summary>
    public class Werkzeug
    {
        public string Befehl { get; set; }
        public string Erklaerung { get; set; }
        public string Beispiel { get; set; }
    }

    /// <summary>Anklickbare Stelle in einem Bauplan; Koordinaten in viewBox-Einheiten.</summary>
    public class Hotspot
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        /// <summary>Schlüssel der Abfrage-Ausgabe, dessen Werte die Detailkarte zeigt.</summary>
        public string Schluessel { get; set; }
    }

    public class Ansicht
    {
        public string Id { get; set; }
        public AnsichtArt Art { get; set; }
        public string Titel { get; set; }
        /// <summary>Bash; läuft als Spielerkonto, liest nur; gibt Zeilen `schluessel TAB wert` aus.</summary>
        public string Abfrage { get; set; }
        public List<Hotspot> Hotspots { get; set; } = new List<Hotspot>();
    }

    public class AntwortFrage
    {
        public string Frage { get; set; }
        public string Format { get; set; }
        /// <summary>Bash als root; die Antwort steht in $ANTWORT. Exit 0 = passt zum VM-Zustand.</summary>
        public string Pruefung { get; set; }
    }

    /// <summary>Schritt einer Ermittlung (ErmittlungsSchritt extends VorfallSchritt).</summary>
    public class ErmittlungsSchritt
    {
        public string Id { get; set; }
        public string Title { get; set; }
        /// <summary>Pager-Nachrichten; die erste erscheint, sobald der Schritt aktuell wird.</summary>
        public List<string> Pager { get; set; } = new List<string>();
        /// <summary>Bash als root; Exit-Code 0 = Zustand erreicht. Ändert nichts.</summary>
        public string Verify { get; set; }
        /// <summary>Gestufte Tipps: Richtung, Befehl, Lösung (Stufe 3 zählt als „mit Lösung“).</summary>
        public List<string> Tipps { get; set; } = new List<string>();
        public string Diagnose { get; set; }
        public string Target { get; set; }
        public string FailMessage { get; set; }
        public string Pose { get; set; }
        public Schrittart Art { get; set; }
        /// <summary>Pflicht genau bei Art == Ablesen.</summary>
        public AntwortFrage Antwort { get; set; }
    }

    public class Spur
    {
        public string Id { get; set; }
        public string Titel { get; set; }
        public List<ErmittlungsSchritt> Schritte { get; set; } = new List<ErmittlungsSchritt>();
    }

    public class Verdaechtige
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Rolle { get; set; }
        public string Portraet { get; set; }
    }

    /// <summary>Ein Beleg zählt, wenn Verify (Bash als root) mit Exit 0 endet.</summary>
    public class BelegRegel
    {
        public string Id { get; set; }
        public string Beschreibung { get; set; }
        public string SpurId { get; set; }
        public string Stuetzt { get; set; }
        public string Verify { get; set; }
    }

    public class VorwurfAntworten
    {
        public List<string> Richtig { get; set; } = new List<string>();
        public List<string> FalschePerson { get; set; } = new List<string>();
        public List<string> ZuWenigBelege { get; set; } = new List<string>();
        public List<string> Gesperrt { get; set; } = new List<string>();
    }

    public class VorwurfRegel
    {
        public List<Verdaechtige> Verdaechtige { get; set; } = new List<Verdaechtige>();
        public string TaeterId { get; set; }
        public List<BelegRegel> Belege { get; set; } = new List<BelegRegel>();
        public int MindestBelege { get; set; }
        public bool JedeSpur { get; set; }
        /// <summary>true: Ein falscher Vorwurf oder Rahmenverstoß sperrt nie (Übungsepisode).</summary>
        public bool OhneSperre { get; set; }
        public VorwurfAntworten Antworten { get; set; } = new VorwurfAntworten();
    }

    public class Einsatzrahmen
    {
        public List<string> Text { get; set; } = new List<string>();
        /// <summary>Bash als root; Exit 0 = Verstoß erkannt.</summary>
        public string Verstoss { get; set; }
        public List<string> Folge { get; set; } = new List<string>();
        public List<string> SudoBefehle { get; set; } = new List<string>();
    }

    public class Beweisstueck
    {
        public string Id { get; set; }
        public string Titel { get; set; }
        public string Beschreibung { get; set; }
        public string Code { get; set; }
    }

    public class Ermittlung
    {
        public int Nr { get; set; }
        public string Id { get; set; }
        public string Titel { get; set; }
        public string Hostname { get; set; }
        public string Setup { get; set; }
        public List<Ansicht> Ansichten { get; set; } = new List<Ansicht>();
        public List<Spur> Spuren { get; set; } = new List<Spur>();
        public VorwurfRegel Vorwurf { get; set; } = new VorwurfRegel();
        public Einsatzrahmen Einsatzrahmen { get; set; } = new Einsatzrahmen();
        public Beweisstueck Beweisstueck { get; set; }
        public List<string> Szene { get; set; } = new List<string>();
        public string SzenePose { get; set; }
        /// <summary>Episode braucht das Bildschirm-Image (Bildbelege).</summary>
        public bool Bildschirm { get; set; }
    }

    /// <summary>Eintrag je Episode im Export: Werkzeuge der Akte, Ermittlung und die Setup-Skripte.</summary>
    public class EpisodeEintrag
    {
        public List<Werkzeug> Werkzeuge { get; set; } = new List<Werkzeug>();
    }

    public class Episode
    {
        public EpisodeEintrag Eintrag { get; set; } = new EpisodeEintrag();
        public Ermittlung Ermittlung { get; set; }
        public string RahmenSetup { get; set; }
        public string EinsatzrahmenSetup { get; set; }
    }

    public class PagerTakt
    {
        public int MsJeZeichen { get; set; }
        public int MsJeZeile { get; set; }
        public int MsPause { get; set; }
    }

    public class QuelleInfo
    {
        public string Repo { get; set; }
        public string Commit { get; set; }
    }

    public enum EreignisArt
    {
        SchrittErledigt,
        SpurErledigt,
        BelegErkannt,
        BelegVerloren,
        EinsatzrahmenVerletzt,
        SperreAufgehoben,
        FallGeloest,
        Diagnose,
        PruefungUnvollstaendig,
    }

    public enum VorwurfErgebnis
    {
        Richtig,
        FalschePerson,
        ZuWenigBelege,
        Gesperrt,
        BelegUnbekannt,
        RichtigOffen,
        BereitsGeloest,
    }

    /// <summary>Eine Prüfung im Bündel; Id ist im Bündel eindeutig.</summary>
    public class Pruefung
    {
        public string Id { get; set; }
        public string Skript { get; set; }
        /// <summary>true: Das Skript liefert eine Meldung statt eines Ergebnisses (ändert nichts).</summary>
        public bool Diagnose { get; set; }
    }

    public class PruefungsAusgabe
    {
        /// <summary>id → bestanden (Exit 0). Nur IDs, die in der Ausgabe stehen.</summary>
        public Dictionary<string, bool> Ergebnisse { get; set; } = new Dictionary<string, bool>();
        /// <summary>id → bereinigte Meldung einer Diagnose.</summary>
        public Dictionary<string, string> Diagnosen { get; set; } = new Dictionary<string, string>();
        /// <summary>true nur, wenn die Schlusszeile da ist und ihre Zahl der Zahl gelesener Prüfzeilen entspricht.</summary>
        public bool Vollstaendig { get; set; }
    }

    public class AbfrageAusgabe
    {
        /// <summary>schluessel → Werte in Ausgabereihenfolge.</summary>
        public Dictionary<string, List<string>> Werte { get; set; } = new Dictionary<string, List<string>>();
        public bool Vollstaendig { get; set; }
        /// <summary>Exit-Status von `su`, wenn er weder 0 noch 141 (SIGPIPE) ist; sonst null.</summary>
        public int? FehlerCode { get; set; }
    }
}
