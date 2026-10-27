using System;
using System.IO;
using Newtonsoft.Json;
using Staffel2.Core;

namespace Staffel2.Game
{
    /// <summary>
    /// Lokale, versionierte Ablage: stand.json (Akte, laufende Episode), vm.gz (Zustand der
    /// VM, schreibt der Linux-Host) und einstellungen.json. Schreiben immer über eine
    /// temporäre Datei mit anschließendem Umbenennen.
    /// </summary>
    public sealed class SpielstandAblage
    {
        public string Ordner { get; }
        public string StandPfad => Path.Combine(Ordner, "stand.json");
        public string VmPfad => Path.Combine(Ordner, "vm.gz");
        public string EinstellungenPfad => Path.Combine(Ordner, "einstellungen.json");

        public SpielstandAblage(string ordner)
        {
            Ordner = ordner;
            Directory.CreateDirectory(Ordner);
        }

        /// <summary>Liest den Spielstand; fehlt er oder ist er unlesbar, gibt es einen leeren.</summary>
        public Spielstand Laden(out string warnung)
        {
            warnung = null;
            if (!File.Exists(StandPfad)) return new Spielstand();
            try
            {
                return SpielstandSerializer.Deserialize(File.ReadAllText(StandPfad));
            }
            catch (Exception e)
            {
                // Beschädigten Stand nicht überschreiben, sondern beiseitelegen.
                string kaputt = StandPfad + ".unlesbar-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Move(StandPfad, kaputt); } catch { }
                warnung = "Spielstand war unlesbar und wurde beiseitegelegt: " + e.Message;
                return new Spielstand();
            }
        }

        public void Speichern(Spielstand stand) => SchreibeAtomar(StandPfad, SpielstandSerializer.Serialize(stand));

        /// <summary>Gibt es eine fortsetzbare Episode samt VM-Zustand?</summary>
        public bool KannFortsetzen(Spielstand stand) => stand.Laufend != null && File.Exists(VmPfad);

        public void LaufendVerwerfen(Spielstand stand)
        {
            stand.Laufend = null;
            try { if (File.Exists(VmPfad)) File.Delete(VmPfad); } catch { }
        }

        public Einstellungen LadeEinstellungen()
        {
            try
            {
                if (File.Exists(EinstellungenPfad))
                    return JsonConvert.DeserializeObject<Einstellungen>(File.ReadAllText(EinstellungenPfad)) ?? new Einstellungen();
            }
            catch { }
            return new Einstellungen();
        }

        public void SpeichereEinstellungen(Einstellungen e) =>
            SchreibeAtomar(EinstellungenPfad, JsonConvert.SerializeObject(e, Formatting.Indented));

        private static void SchreibeAtomar(string pfad, string inhalt)
        {
            string tmp = pfad + ".tmp";
            File.WriteAllText(tmp, inhalt);
            if (File.Exists(pfad)) File.Replace(tmp, pfad, null);
            else File.Move(tmp, pfad);
        }
    }

    /// <summary>Einstellungen des Spielers, getrennt vom Spielstand.</summary>
    public sealed class Einstellungen
    {
        public int Version = 1;
        public float Gesamt = 0.8f;
        public float Musik = 0.5f;
        public float Effekte = 0.8f;
        public float Sprache = 0.8f;
        public float Umgebung = 0.6f;
        public float Mausempfindlichkeit = 1f;
        public bool BewegungReduziert;
        public int Qualitaet = -1;          // -1 = Standard des Projekts
        public int AufloesungBreite;         // 0 = aktuelle
        public int AufloesungHoehe;
        public bool Vollbild = true;
        public int TerminalSchrift = 22;
        public bool VSync = true;
    }
}
