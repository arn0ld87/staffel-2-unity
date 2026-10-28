using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Staffel2.Core.Tests
{
    public class SpielstandTests
    {
        private static AkteEintrag Eintrag(long dauer, bool loesung = false, int tipps = 0, long fertig = 1000) =>
            new AkteEintrag { DurationMs = dauer, WithSolution = loesung, HintsUsed = tipps, FinishedAt = fertig, VorwurfVersuche = 1 };

        [Test]
        public void Roundtrip_MitVersionUndLaufend()
        {
            var stand = new Spielstand();
            stand.LoeseEpisode("probelauf", Eintrag(61_000, false, 2, 1_700_000_000_000));
            var z = ErmittlungsEngine.StartZustand();
            z.Erledigt["rechte"] = 1;
            z.BelegeErkannt.Add("b-log");
            z.SperreBis = 2;
            z.SperreRestPruefungen = 2;
            z.VorwurfVersuche = 1;
            z.LetzteDiagnose = new Dictionary<string, string> { ["rechte/s2"] = "Falscher Besitzer" };
            stand.Laufend = new LaufendeErmittlung
            {
                EpisodeId = "fremder-login",
                Zustand = z,
                GezeigteTipps = new Dictionary<string, int> { ["rechte/s1"] = 2 },
                HintsUsed = 2,
                WithSolution = false,
                ElapsedMs = 90_500,
                PagerVerlauf = new List<string> { "p1", "p2" },
            };

            var json = SpielstandSerializer.Serialize(stand);
            Assert.That(JObject.Parse(json)["version"].Value<int>(), Is.EqualTo(1));
            var neu = SpielstandSerializer.Deserialize(json);

            Assert.That(neu.Version, Is.EqualTo(Spielstand.AktuelleVersion));
            Assert.That(neu.Akte["probelauf"].DurationMs, Is.EqualTo(61_000));
            Assert.That(neu.Akte["probelauf"].FinishedAt, Is.EqualTo(1_700_000_000_000));
            Assert.That(neu.Akte["probelauf"].HintsUsed, Is.EqualTo(2));
            Assert.That(neu.Akte["probelauf"].VorwurfVersuche, Is.EqualTo(1));
            var l = neu.Laufend;
            Assert.That(l.EpisodeId, Is.EqualTo("fremder-login"));
            Assert.That(l.Zustand.Erledigt["rechte"], Is.EqualTo(1));
            Assert.That(l.Zustand.BelegeErkannt, Is.EqualTo(new[] { "b-log" }));
            Assert.That(l.Zustand.SperreBis, Is.EqualTo(2));
            Assert.That(l.Zustand.SperreRestPruefungen, Is.EqualTo(2));
            Assert.That(l.Zustand.LetzteDiagnose["rechte/s2"], Is.EqualTo("Falscher Besitzer"));
            Assert.That(l.GezeigteTipps["rechte/s1"], Is.EqualTo(2));
            Assert.That(l.ElapsedMs, Is.EqualTo(90_500));
            Assert.That(l.PagerVerlauf, Is.EqualTo(new[] { "p1", "p2" }));
            Assert.That(SpielstandSerializer.Serialize(neu), Is.EqualTo(json));
        }

        [Test]
        public void Json_HatCamelCaseSchluesselUndErhaeltDictionarySchluessel()
        {
            var stand = new Spielstand { ZurueckgesetztAm = 5 };
            stand.LoeseEpisode("Probe-Lauf", Eintrag(1));
            var o = JObject.Parse(SpielstandSerializer.Serialize(stand));
            Assert.That(o["akte"]["Probe-Lauf"]["durationMs"], Is.Not.Null);
            Assert.That(o["zurueckgesetztAm"].Value<long>(), Is.EqualTo(5));
        }

        [Test]
        public void NeuereVersionWirftAeltereOhneMigrationWirft()
        {
            Assert.Throws<SpielstandVersionException>(() => SpielstandSerializer.Deserialize("{\"version\":2,\"akte\":{}}"));
            Assert.Throws<SpielstandVersionException>(() => SpielstandSerializer.Deserialize("{\"version\":0,\"akte\":{}}"));
        }

        [Test]
        public void MigrationsHakenHebtVersionSchrittweise()
        {
            var vorher = SpielstandSerializer.Migrationen.Count;
            SpielstandSerializer.Migrationen[0] = alt =>
            {
                alt["akte"] = alt["alteAkte"];
                alt.Remove("alteAkte");
                return alt;
            };
            try
            {
                var s = SpielstandSerializer.Deserialize(
                    "{\"version\":0,\"alteAkte\":{\"e0\":{\"durationMs\":5,\"withSolution\":false,\"hintsUsed\":0,\"finishedAt\":9}}}");
                Assert.That(s.Version, Is.EqualTo(1));
                Assert.That(s.Akte["e0"].DurationMs, Is.EqualTo(5));
            }
            finally
            {
                SpielstandSerializer.Migrationen.Remove(0);
            }
            Assert.That(SpielstandSerializer.Migrationen.Count, Is.EqualTo(vorher));
        }

        [Test]
        public void FehlendeVersionGiltAlsEins_KaputteEintraegeFallenWeg()
        {
            var s = SpielstandSerializer.Deserialize(
                "{\"akte\":{\"gut\":{\"durationMs\":5,\"withSolution\":true,\"hintsUsed\":1,\"finishedAt\":9}," +
                "\"schlecht\":{\"durationMs\":\"x\"},\"leer\":null}}");
            Assert.That(s.Akte.Keys, Is.EqualTo(new[] { "gut" }));
            Assert.That(s.Akte["gut"].VorwurfVersuche, Is.Null);
        }

        [Test]
        public void LeererOderKaputterTextErgibtLeerenStand()
        {
            foreach (var text in new[] { "", "   ", "nicht json", "[1,2]" })
            {
                var s = SpielstandSerializer.Deserialize(text);
                Assert.That(s.Akte, Is.Empty, text);
                Assert.That(s.Laufend, Is.Null);
            }
        }

        [Test]
        public void LaufendOhneSperreRestBekommtDefaultNachSperre()
        {
            var s = SpielstandSerializer.Deserialize(
                "{\"version\":1,\"laufend\":{\"episodeId\":\"e\",\"zustand\":{\"sperreBis\":3,\"erledigt\":{},\"belegeErkannt\":[]}}}");
            Assert.That(s.Laufend.Zustand.SperreRestPruefungen, Is.EqualTo(ErmittlungsEngine.SperrePruefungen));
        }

        [Test]
        public void BessererLauf_OhneLoesungSchlaegtMitLoesung_SonstKuerzereZeit()
        {
            var s = new Spielstand();
            s.LoeseEpisode("e", Eintrag(100_000, loesung: true));
            s.LoeseEpisode("e", Eintrag(200_000, loesung: false));
            Assert.That(s.Akte["e"].DurationMs, Is.EqualTo(200_000), "ohne Lösung schlägt mit Lösung trotz längerer Zeit");
            s.LoeseEpisode("e", Eintrag(50_000, loesung: true));
            Assert.That(s.Akte["e"].DurationMs, Is.EqualTo(200_000), "mit Lösung ersetzt ohne Lösung nie");
            s.LoeseEpisode("e", Eintrag(150_000, loesung: false));
            Assert.That(s.Akte["e"].DurationMs, Is.EqualTo(150_000));
            s.LoeseEpisode("e", Eintrag(150_000, loesung: false, tipps: 5));
            Assert.That(s.Akte["e"].HintsUsed, Is.EqualTo(0), "gleiche Zeit ist nicht besser");
        }

        [Test]
        public void LoeseEpisode_BeendetLaufendeDerselbenEpisode()
        {
            var s = new Spielstand { Laufend = new LaufendeErmittlung { EpisodeId = "a" } };
            s.LoeseEpisode("b", Eintrag(1));
            Assert.That(s.Laufend, Is.Not.Null);
            s.LoeseEpisode("a", Eintrag(1));
            Assert.That(s.Laufend, Is.Null);
        }

        [Test]
        public void Zuruecksetzen_LoeschtAkteUndLaufendUndSetztZeitstempel()
        {
            var s = new Spielstand { Laufend = new LaufendeErmittlung { EpisodeId = "a" } };
            s.LoeseEpisode("x", Eintrag(1));
            s.Zuruecksetzen(1234);
            Assert.That(s.Akte, Is.Empty);
            Assert.That(s.Laufend, Is.Null);
            Assert.That(s.ZurueckgesetztAm, Is.EqualTo(1234));
            var neu = SpielstandSerializer.Deserialize(SpielstandSerializer.Serialize(s));
            Assert.That(neu.ZurueckgesetztAm, Is.EqualTo(1234));
        }

        [Test]
        public void ZuEintrag_UebernimmtTippNutzungUndVorwurfVersuche()
        {
            var l = new LaufendeErmittlung { HintsUsed = 3, WithSolution = true };
            l.Zustand.VorwurfVersuche = 2;
            var e = l.ZuEintrag(42_000, 99);
            Assert.That((e.DurationMs, e.WithSolution, e.HintsUsed, e.FinishedAt, e.VorwurfVersuche), Is.EqualTo((42_000L, true, 3, 99L, (int?)2)));
        }
    }

    public class FixtureTests
    {
        [Test]
        public void LaedtVierEpisodenMitSchrittzahlen()
        {
            var d = TestDaten.Export();
            Assert.That(d.Format, Is.EqualTo(1));
            Assert.That(d.Quelle.Repo, Is.EqualTo("alexle135.de"));
            Assert.That(d.Episoden, Has.Count.EqualTo(4));
            var schritte = new List<int>();
            foreach (var ep in d.Episoden)
            {
                var n = 0;
                foreach (var spur in ep.Ermittlung.Spuren) n += spur.Schritte.Count;
                schritte.Add(n);
            }
            Assert.That(schritte, Is.EqualTo(new[] { 4, 6, 4, 4 }));
        }

        [Test]
        public void Episodenkopf_UndOptionaleFelder()
        {
            var d = TestDaten.Export();
            Assert.That(d.Episoden[0].Ermittlung.Id, Is.EqualTo("probelauf"));
            Assert.That(d.Episoden[0].Ermittlung.Vorwurf.OhneSperre, Is.True);
            Assert.That(d.Episoden[1].Ermittlung.Vorwurf.OhneSperre, Is.False);
            Assert.That(d.Episoden[0].Ermittlung.Bildschirm, Is.False);
            Assert.That(d.Episoden[2].Ermittlung.Bildschirm, Is.True);
            Assert.That(d.Episoden[1].Ermittlung.Einsatzrahmen.SudoBefehle, Has.Count.EqualTo(6));
            Assert.That(d.Episoden[0].Ermittlung.Einsatzrahmen.SudoBefehle, Is.Empty);
            Assert.That(d.Episoden[1].Ermittlung.Ansichten[0].Art, Is.EqualTo(AnsichtArt.Raum));
            Assert.That(d.Episoden[1].Ermittlung.Ansichten[0].Hotspots, Is.Not.Empty);
            Assert.That(d.Episoden[0].Ermittlung.Ansichten[0].Art, Is.EqualTo(AnsichtArt.Aktenwand));
            Assert.That(d.Episoden[0].Eintrag.Werkzeuge, Has.Count.EqualTo(7));
            Assert.That(d.Episoden[0].RahmenSetup, Does.StartWith("set -e"));
            Assert.That(d.EpisodeMitId("probelauf"), Is.SameAs(d.Episoden[0]));
        }

        [Test]
        public void SchritteHabenArtTippsUndAblesenAntwort()
        {
            foreach (var ep in TestDaten.Export().Episoden)
                foreach (var spur in ep.Ermittlung.Spuren)
                    foreach (var s in spur.Schritte)
                    {
                        Assert.That(s.Tipps, Has.Count.EqualTo(3), $"{spur.Id}/{s.Id}");
                        Assert.That(s.Verify, Is.Not.Empty);
                        Assert.That(s.Antwort != null, Is.EqualTo(s.Art == Schrittart.Ablesen), $"{spur.Id}/{s.Id}");
                    }
        }

        [Test]
        public void PagerUndGlossar()
        {
            var d = TestDaten.Export();
            Assert.That(d.PagerPosen, Has.Count.EqualTo(10));
            Assert.That(d.PagerPosen[0], Is.EqualTo("neutral"));
            Assert.That(d.PagerTakt.MsJeZeichen, Is.EqualTo(12));
            Assert.That(d.PagerTakt.MsJeZeile, Is.EqualTo(200));
            Assert.That(d.PagerTakt.MsPause, Is.EqualTo(1500));
            Assert.That(d.Glossar, Has.Count.EqualTo(62));
            Assert.That(d.Glossar["ls -l"].Beispiel, Is.EqualTo("ls -l /etc/hostname"));
        }

        [Test]
        public void AlleEpisodenSindGueltigNachPruefeDaten()
        {
            foreach (var ep in TestDaten.Export().Episoden)
                Assert.That(ErmittlungsEngine.PruefeDaten(ep.Ermittlung), Is.Empty, ep.Ermittlung.Id);
        }

        [Test]
        public void AlleEpisodenBuendelSkriptWirftNicht()
        {
            foreach (var ep in TestDaten.Export().Episoden)
            {
                var skript = ErmittlungsEngine.BuendelSkript(ErmittlungsEngine.OffenePruefungen(ep.Ermittlung, ErmittlungsEngine.StartZustand()));
                Assert.That(skript, Does.EndWith("'\n"));
            }
        }

        [Test]
        public void DurchspielenEinerEpisodeMitDerEngine()
        {
            var e = TestDaten.Export().Episoden[0].Ermittlung;
            var z = ErmittlungsEngine.StartZustand();
            var ergebnisse = new Dictionary<string, bool>();
            for (var runde = 0; runde < 10 && !z.Geloest; runde++)
            {
                ergebnisse.Clear();
                foreach (var p in ErmittlungsEngine.OffenePruefungen(e, z))
                    if (!p.Diagnose && p.Id != ErmittlungsEngine.PruefIdVerstoss) ergebnisse[p.Id] = true;
                var aus = new PruefungsAusgabe { Ergebnisse = ergebnisse, Vollstaendig = true };
                z = ErmittlungsEngine.WendePruefungAn(e, z, aus).Zustand;
                if (runde == 1)
                {
                    var alle = z.BelegeErkannt;
                    var r = ErmittlungsEngine.PruefeVorwurf(e, z, new VorwurfEingabe { VerdaechtigeId = e.Vorwurf.TaeterId, BelegIds = alle });
                    z = r.Zustand;
                }
            }
            Assert.That(z.Geloest, Is.True);
            Assert.That(ErmittlungsEngine.AlleSpurenErledigt(e, z), Is.True);
        }
    }
}
