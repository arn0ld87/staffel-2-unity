using System;
using System.Collections.Generic;
using System.IO;
using Staffel2.Core;

namespace Staffel2.Core.Tests
{
    /// <summary>Frei erfundene Episode wie in ermittlung.test.ts und der echte Export.</summary>
    public static class TestDaten
    {
        public static ErmittlungsSchritt Schritt(string id, string verify, Schrittart art = Schrittart.Sichern) =>
            new ErmittlungsSchritt
            {
                Art = art,
                Id = id,
                Title = $"Schritt {id}",
                Pager = new List<string> { $"Schritt {id}" },
                Verify = verify,
                Tipps = new List<string> { "Richtung", "Befehl", "Lösung" },
            };

        public static Ermittlung Episode() => new Ermittlung
        {
            Nr = 1,
            Id = "episode-1",
            Titel = "Fremder Login",
            Hostname = "alex-schneider",
            Setup = "true",
            Ansichten = new List<Ansicht>
            {
                new Ansicht
                {
                    Id = "raum", Art = AnsichtArt.Raum, Titel = "Büro", Abfrage = "printf 'host\\t%s\\n' srv",
                    Hotspots = new List<Hotspot> { new Hotspot { Id = "tuer", Label = "Tür", X = 10, Y = 20, W = 30, H = 40, Schluessel = "host" } },
                },
            },
            Spuren = new List<Spur>
            {
                new Spur
                {
                    Id = "rechte", Titel = "Rechte",
                    Schritte = new List<ErmittlungsSchritt> { Schritt("s1", "true"), Schritt("s2", "false", Schrittart.Eingreifen) },
                },
                new Spur
                {
                    Id = "log", Titel = "Logbuch",
                    Schritte = new List<ErmittlungsSchritt> { Schritt("s1", "test -f ~/belege/log", Schrittart.Reparieren) },
                },
            },
            Vorwurf = new VorwurfRegel
            {
                Verdaechtige = new List<Verdaechtige>
                {
                    new Verdaechtige { Id = "timo", Name = "Timo", Rolle = "Admin", Portraet = "p-timo" },
                    new Verdaechtige { Id = "nadia", Name = "Nadia", Rolle = "Praktikantin", Portraet = "p-nadia" },
                    new Verdaechtige { Id = "margit", Name = "Margit", Rolle = "Chefin", Portraet = "p-margit" },
                },
                TaeterId = "timo",
                Belege = new List<BelegRegel>
                {
                    new BelegRegel { Id = "b-sudoers", Beschreibung = "Timo steht in der sudoers-Datei.", SpurId = "rechte", Stuetzt = "timo", Verify = "true" },
                    new BelegRegel { Id = "b-stat", Beschreibung = "Die Datei gehört Timo.", SpurId = "rechte", Stuetzt = "timo", Verify = "test -f ~/belege/stat" },
                    new BelegRegel { Id = "b-log", Beschreibung = "Der Login steht im Log.", SpurId = "log", Stuetzt = "timo", Verify = "test -f ~/belege/log" },
                    new BelegRegel { Id = "b-nadia", Beschreibung = "Nadia hat den Schlüssel.", SpurId = "log", Stuetzt = "nadia", Verify = "test -f ~/belege/nadia" },
                },
                MindestBelege = 2,
                JedeSpur = true,
                Antworten = new VorwurfAntworten
                {
                    Richtig = new List<string> { "Der Vorwurf sitzt." },
                    FalschePerson = new List<string> { "Das war es nicht." },
                    ZuWenigBelege = new List<string> { "Das reicht nicht." },
                    Gesperrt = new List<string> { "Erst weiter ermitteln." },
                },
            },
            Einsatzrahmen = new Einsatzrahmen
            {
                Text = new List<string> { "Nur lesen." },
                Verstoss = "false",
                Folge = new List<string> { "Grenze überschritten." },
            },
            Beweisstueck = new Beweisstueck { Id = "beweis-1", Titel = "Karte", Beschreibung = "Eine Karte.", Code = "ABC123" },
            Szene = new List<string> { "Szene." },
        };

        public static ErmittlungsZustand Zustand(
            Dictionary<string, int> erledigt = null,
            string[] belege = null,
            int? sperreBis = null,
            int sperreRest = 0,
            bool geloest = false)
        {
            var z = new ErmittlungsZustand { SperreBis = sperreBis, SperreRestPruefungen = sperreRest, Geloest = geloest };
            if (erledigt != null) z.Erledigt = erledigt;
            if (belege != null) z.BelegeErkannt = new List<string>(belege);
            return z;
        }

        public static PruefungsAusgabe Ausgabe(Dictionary<string, bool> ergebnisse, bool vollstaendig = true) =>
            new PruefungsAusgabe { Ergebnisse = ergebnisse, Vollstaendig = vollstaendig };

        public static string FixturePfad(string datei) => Path.Combine(AppContext.BaseDirectory, datei);

        public static Staffel2Daten Export() => Staffel2Daten.Laden(File.ReadAllText(FixturePfad("staffel2.fixture.json")));
    }
}
