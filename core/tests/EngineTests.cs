using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using static Staffel2.Core.ErmittlungsEngine;
using static Staffel2.Core.Tests.TestDaten;

namespace Staffel2.Core.Tests
{
    public class EngineTests
    {
        private static readonly Ermittlung E = Episode();

        private static PruefAnwendung Lauf(ErmittlungsZustand z, Dictionary<string, bool> ergebnisse) =>
            WendePruefungAn(E, z, Ausgabe(ergebnisse));

        private static VorwurfAuswertung Vorwurf(ErmittlungsZustand z, string wer, params string[] belege) =>
            PruefeVorwurf(E, z, new VorwurfEingabe { VerdaechtigeId = wer, BelegIds = belege });

        private static string Json(object o) => JsonConvert.SerializeObject(o);

        private static void Ereignisse(PruefAnwendung r, params ErmittlungsEreignis[] erwartet) =>
            Assert.That(r.Ereignisse, Is.EqualTo(erwartet));

        // ---- offene Prüfungen, Bündel, Ausgabe ----

        [Test]
        public void OffenePruefungen_JeSpurAktuellerSchrittAlleBelegeUndRahmen()
        {
            var ids = OffenePruefungen(E, StartZustand()).Select(p => p.Id).ToArray();
            Assert.That(ids, Is.EqualTo(new[]
            {
                "s.rechte.s1", "s.log.s1", "b.b-sudoers", "b.b-stat", "b.b-log", "b.b-nadia", "e.verstoss",
            }));
        }

        [Test]
        public void OffenePruefungen_NachVerstossOhneRahmen_NachLoesungLeer()
        {
            var z = StartZustand();
            z.EinsatzrahmenVerletzt = true;
            Assert.That(OffenePruefungen(E, z).Select(p => p.Id), Does.Not.Contain("e.verstoss"));
            Assert.That(OffenePruefungen(E, Zustand(geloest: true)), Is.Empty);
        }

        [Test]
        public void OffenePruefungen_DiagnoseDirektNachVerify()
        {
            var e = Episode();
            e.Spuren[0].Schritte[0].Diagnose = "echo hinweis";
            var p = OffenePruefungen(e, StartZustand());
            Assert.That(p[0].Id, Is.EqualTo("s.rechte.s1"));
            Assert.That(p[1].Id, Is.EqualTo("d.rechte.s1"));
            Assert.That(p[1].Diagnose, Is.True);
        }

        [Test]
        public void BuendelSkript_LeereListeNurSchlusszeile()
        {
            Assert.That(BuendelSkript(new Pruefung[0]), Is.EqualTo("printf 'ende\\t%s\\n' '0'\n"));
        }

        [Test]
        public void BuendelSkript_WirftBeiUngueltigerId()
        {
            Assert.Throws<System.ArgumentException>(() => BuendelSkript(new[] { new Pruefung { Id = "a b", Skript = "true" } }));
            Assert.Throws<System.ArgumentException>(() => BuendelSkript(new[] { new Pruefung { Id = "a'", Skript = "true" } }));
        }

        [Test]
        public void BuendelSkript_IdentischesSkriptNurEinmal()
        {
            var skript = BuendelSkript(new[]
            {
                new Pruefung { Id = "schritt", Skript = "echo MARKER >&2; exit 0" },
                new Pruefung { Id = "andere", Skript = "false" },
                new Pruefung { Id = "beleg", Skript = "echo MARKER >&2; exit 0" },
            });
            Assert.That(skript.Split(new[] { "MARKER" }, System.StringSplitOptions.None).Length, Is.EqualTo(2));
        }

        [Test]
        public void LeseBuendelAusgabe_IgnoriertFremdeZeilen()
        {
            var a = LeseBuendelAusgabe("quatsch\npruefung\ta\t0\nnoch was\npruefung\tb\t1\nende\t2\n");
            Assert.That(a.Ergebnisse, Is.EqualTo(new Dictionary<string, bool> { ["a"] = true, ["b"] = false }));
            Assert.That(a.Vollstaendig, Is.True);
        }

        [Test]
        public void LeseBuendelAusgabe_OhneSchlusszeileOderFalscheZahl_Unvollstaendig()
        {
            Assert.That(LeseBuendelAusgabe("pruefung\ta\t0\n").Vollstaendig, Is.False);
            Assert.That(LeseBuendelAusgabe("pruefung\ta\t0\nende\t2\n").Vollstaendig, Is.False);
        }

        [Test]
        public void LeseBuendelAusgabe_CrlfUndDoppelteIdLetzteGewinnt()
        {
            var crlf = LeseBuendelAusgabe("pruefung\ta\t0\r\nende\t1\r\n");
            Assert.That(crlf.Vollstaendig, Is.True);
            Assert.That(crlf.Ergebnisse["a"], Is.True);
            var doppelt = LeseBuendelAusgabe("pruefung\ta\t0\npruefung\ta\t1\nende\t2\n");
            Assert.That(doppelt.Ergebnisse["a"], Is.False);
            Assert.That(doppelt.Vollstaendig, Is.True);
        }

        [Test]
        public void LeseBuendelAusgabe_DiagnoseBereinigtUndGekuerzt()
        {
            var lang = new string('x', 300);
            var a = LeseBuendelAusgabe("diagnose\td.a.b\tHal\u0001lo\tWelt\npruefung\ts.a.b\t1\ndiagnose\td.x.y\t" + lang + "\nende\t1\n");
            Assert.That(a.Diagnosen["d.a.b"], Is.EqualTo("HalloWelt"));
            Assert.That(a.Diagnosen["d.x.y"].Length, Is.EqualTo(DiagnoseMaxZeichen));
            Assert.That(a.Vollstaendig, Is.True);
        }

        [Test]
        public void AntwortSkript_TraegtAntwortNurAlsBase64()
        {
            var s = AntwortSkript("test \"$ANTWORT\" = x", "12:34");
            Assert.That(s, Does.Not.Contain("12:34"));
            Assert.That(s, Does.Contain("MTI6MzQ="));
        }

        [Test]
        public void AntwortMuster()
        {
            Assert.That(IstGueltigeAntwort("12:34"), Is.True);
            Assert.That(IstGueltigeAntwort("/var/log-a_b.txt"), Is.True);
            Assert.That(IstGueltigeAntwort("a;b"), Is.False);
            Assert.That(IstGueltigeAntwort("a\n"), Is.False);
            Assert.That(IstGueltigeAntwort(""), Is.False);
        }

        // ---- Zustandsübergänge ----

        [Test]
        public void Pruefung_RuecktSpurUmGenauEinenSchrittVor()
        {
            var r = Lauf(StartZustand(), new Dictionary<string, bool> { [PruefIdSchritt("rechte", "s1")] = true });
            Assert.That(r.Zustand.Erledigt["rechte"], Is.EqualTo(1));
            Ereignisse(r, ErmittlungsEreignis.SchrittErledigt("rechte", "s1"));
        }

        [Test]
        public void Pruefung_HoechstensEinSchrittJeSpurUndAufruf()
        {
            var r = Lauf(StartZustand(), new Dictionary<string, bool>
            {
                [PruefIdSchritt("rechte", "s1")] = true,
                [PruefIdSchritt("rechte", "s2")] = true,
            });
            Assert.That(r.Zustand.Erledigt["rechte"], Is.EqualTo(1));
            Assert.That(r.Ereignisse, Has.Count.EqualTo(1));
        }

        [Test]
        public void Pruefung_LetzterSchrittMeldetAuchSpur()
        {
            var r = Lauf(Zustand(erledigt: new Dictionary<string, int> { ["rechte"] = 1 }),
                new Dictionary<string, bool> { [PruefIdSchritt("rechte", "s2")] = true });
            Assert.That(r.Zustand.Erledigt["rechte"], Is.EqualTo(2));
            Ereignisse(r, ErmittlungsEreignis.SchrittErledigt("rechte", "s2"), ErmittlungsEreignis.SpurErledigt("rechte"));
        }

        [Test]
        public void Belege_ErkanntVerlorenUndBehalten()
        {
            var erkannt = Lauf(StartZustand(), new Dictionary<string, bool> { [PruefIdBeleg("b-sudoers")] = true });
            Assert.That(erkannt.Zustand.BelegeErkannt, Is.EqualTo(new[] { "b-sudoers" }));
            Ereignisse(erkannt, ErmittlungsEreignis.BelegErkannt("b-sudoers"));

            var verloren = Lauf(erkannt.Zustand, new Dictionary<string, bool> { [PruefIdBeleg("b-sudoers")] = false });
            Assert.That(verloren.Zustand.BelegeErkannt, Is.Empty);
            Ereignisse(verloren, ErmittlungsEreignis.BelegVerloren("b-sudoers"));

            var fehlt = Lauf(erkannt.Zustand, new Dictionary<string, bool>());
            Assert.That(fehlt.Zustand.BelegeErkannt, Is.EqualTo(new[] { "b-sudoers" }));
            Assert.That(fehlt.Ereignisse, Is.Empty);
        }

        [Test]
        public void Belege_SortiertGespeichert_EreignisseInDatenreihenfolge()
        {
            var r = Lauf(StartZustand(), new Dictionary<string, bool>
            {
                [PruefIdBeleg("b-sudoers")] = true,
                [PruefIdBeleg("b-log")] = true,
            });
            Assert.That(r.Zustand.BelegeErkannt, Is.EqualTo(new[] { "b-log", "b-sudoers" }));
            Ereignisse(r, ErmittlungsEreignis.BelegErkannt("b-sudoers"), ErmittlungsEreignis.BelegErkannt("b-log"));
        }

        [Test]
        public void UnvollstaendigePruefung_AendertNichts()
        {
            var z = StartZustand();
            var r = WendePruefungAn(E, z, Ausgabe(new Dictionary<string, bool> { [PruefIdSchritt("rechte", "s1")] = true }, false));
            Assert.That(r.Zustand, Is.SameAs(z));
            Ereignisse(r, ErmittlungsEreignis.Einfach(EreignisArt.PruefungUnvollstaendig));
        }

        [Test]
        public void GeloesterZustand_BleibtUnangetastet()
        {
            var z = Zustand(geloest: true, erledigt: new Dictionary<string, int> { ["rechte"] = 2 });
            var r = WendePruefungAn(E, z, Ausgabe(new Dictionary<string, bool> { [PruefIdVerstoss] = true }, false));
            Assert.That(r.Zustand, Is.SameAs(z));
            Assert.That(r.Ereignisse, Is.Empty);
        }

        [Test]
        public void Einsatzrahmen_VerstossGenauEinmalUndSetztSperre()
        {
            var r = Lauf(StartZustand(), new Dictionary<string, bool> { [PruefIdVerstoss] = true });
            Assert.That(r.Zustand.EinsatzrahmenVerletzt, Is.True);
            Ereignisse(r, ErmittlungsEreignis.Einfach(EreignisArt.EinsatzrahmenVerletzt));
            Assert.That(r.Zustand.SperreBis, Is.EqualTo(1));
            Assert.That(r.Zustand.SperreRestPruefungen, Is.EqualTo(SperrePruefungen));

            var zweite = Lauf(r.Zustand, new Dictionary<string, bool> { [PruefIdVerstoss] = true });
            Assert.That(zweite.Ereignisse, Is.Empty);
        }

        [Test]
        public void Eingaben_BleibenUnveraendert()
        {
            var e = Episode();
            var z = Zustand(erledigt: new Dictionary<string, int> { ["log"] = 0 }, belege: new[] { "b-stat" });
            var a = Ausgabe(new Dictionary<string, bool>
            {
                [PruefIdSchritt("rechte", "s1")] = true,
                [PruefIdBeleg("b-log")] = true,
                [PruefIdBeleg("b-stat")] = false,
                [PruefIdVerstoss] = true,
            });
            var vorher = (Json(e), Json(z), Json(a));
            WendePruefungAn(e, z, a);
            Assert.That((Json(e), Json(z), Json(a)), Is.EqualTo(vorher));
        }

        // ---- Diagnose ----

        private static Ermittlung MitDiagnose()
        {
            var e = Episode();
            e.Spuren[0].Schritte[0].Diagnose = "echo hinweis";
            return e;
        }

        [Test]
        public void Diagnose_NurBeiVerifyFalseUndNichtLeerUndEinmalJeText()
        {
            var e = MitDiagnose();
            var d = new Dictionary<string, string> { [PruefIdDiagnose("rechte", "s1")] = "Falscher Besitzer" };
            var a = new PruefungsAusgabe
            {
                Vollstaendig = true,
                Ergebnisse = new Dictionary<string, bool> { [PruefIdSchritt("rechte", "s1")] = false },
                Diagnosen = d,
            };
            var r = WendePruefungAn(e, StartZustand(), a);
            Ereignisse(r, ErmittlungsEreignis.Diagnose("rechte", "s1", "Falscher Besitzer"));
            Assert.That(r.Zustand.LetzteDiagnose["rechte/s1"], Is.EqualTo("Falscher Besitzer"));
            Assert.That(r.Zustand.Erledigt, Is.Empty);

            var nochmal = WendePruefungAn(e, r.Zustand, a);
            Assert.That(nochmal.Ereignisse, Is.Empty);

            // Verify besteht: Diagnose entfällt, Schritt wird erledigt.
            a.Ergebnisse[PruefIdSchritt("rechte", "s1")] = true;
            var ok = WendePruefungAn(e, r.Zustand, a);
            Assert.That(ok.Zustand.LetzteDiagnose, Is.Null);
            Ereignisse(ok, ErmittlungsEreignis.SchrittErledigt("rechte", "s1"));
        }

        // ---- Antwort ----

        [Test]
        public void Antwort_RichtigErledigtSchrittFalschNichtsVeralteterSchrittNichts()
        {
            var z = StartZustand();
            var r = WendeAntwortAn(E, z, "rechte", "s1", true);
            Assert.That(r.Zustand.Erledigt["rechte"], Is.EqualTo(1));
            Ereignisse(r, ErmittlungsEreignis.SchrittErledigt("rechte", "s1"));

            Assert.That(WendeAntwortAn(E, z, "rechte", "s1", false).Zustand, Is.SameAs(z));
            Assert.That(WendeAntwortAn(E, z, "rechte", "s2", true).Ereignisse, Is.Empty);
            Assert.That(WendeAntwortAn(E, z, "unbekannt", "s1", true).Ereignisse, Is.Empty);
            Assert.That(WendeAntwortAn(E, Zustand(geloest: true), "rechte", "s1", true).Ereignisse, Is.Empty);
        }

        [Test]
        public void Antwort_LetzterSchrittMeldetSpur()
        {
            var r = WendeAntwortAn(E, Zustand(erledigt: new Dictionary<string, int> { ["rechte"] = 1 }), "rechte", "s2", true);
            Ereignisse(r, ErmittlungsEreignis.SchrittErledigt("rechte", "s2"), ErmittlungsEreignis.SpurErledigt("rechte"));
        }

        // ---- Sperre ----

        [Test]
        public void NaechsteSperre_EinenBelegHinterDenStand_NieSackgasse()
        {
            Assert.That(NaechsteSperre(E, StartZustand()), Is.EqualTo(1));
            Assert.That(NaechsteSperre(E, Zustand(belege: new[] { "b-log" }, sperreBis: 3)), Is.EqualTo(3));
            Assert.That(NaechsteSperre(E, Zustand(belege: new[] { "b-log", "b-nadia", "b-stat", "b-sudoers" })), Is.Null);
        }

        [Test]
        public void NaechsteSperre_OhneSperreFlag_Null()
        {
            var e = Episode();
            e.Vorwurf.OhneSperre = true;
            Assert.That(NaechsteSperre(e, StartZustand()), Is.Null);
        }

        [Test]
        public void IstGesperrt_GegenZahlErkannterBelege()
        {
            Assert.That(IstGesperrt(Zustand(belege: new[] { "b-log" }, sperreBis: 2)), Is.True);
            Assert.That(IstGesperrt(Zustand(belege: new[] { "b-log", "b-stat" }, sperreBis: 2)), Is.False);
            Assert.That(IstGesperrt(Zustand()), Is.False);
        }

        private static ErmittlungsZustand Gesperrt()
        {
            var r = Vorwurf(Zustand(belege: new[] { "b-sudoers" }), "nadia", "b-sudoers");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.FalschePerson));
            return r.Zustand;
        }

        [Test]
        public void Sperre_BlockiertZweitenVorwurf()
        {
            Assert.That(Vorwurf(Gesperrt(), "timo", "b-sudoers").Ergebnis, Is.EqualTo(VorwurfErgebnis.Gesperrt));
        }

        [Test]
        public void Sperre_NeuerBelegHebtSieAuf()
        {
            var r = Lauf(Gesperrt(), new Dictionary<string, bool> { [PruefIdBeleg("b-log")] = true });
            Ereignisse(r, ErmittlungsEreignis.BelegErkannt("b-log"), ErmittlungsEreignis.Einfach(EreignisArt.SperreAufgehoben));
            Assert.That(r.Zustand.SperreBis, Is.Null);
            Assert.That(r.Zustand.SperreRestPruefungen, Is.EqualTo(0));
        }

        [Test]
        public void Sperre_EndetNachGenauDreiVollstaendigenPruefungen()
        {
            var start = Gesperrt();
            Assert.That(start.SperreRestPruefungen, Is.EqualTo(SperrePruefungen));
            var eins = Lauf(start, new Dictionary<string, bool>());
            Assert.That(eins.Ereignisse, Is.Empty);
            Assert.That(eins.Zustand.SperreRestPruefungen, Is.EqualTo(2));
            var zwei = Lauf(eins.Zustand, new Dictionary<string, bool>());
            Assert.That(IstGesperrt(zwei.Zustand), Is.True);
            Assert.That(zwei.Zustand.SperreRestPruefungen, Is.EqualTo(1));
            var drei = Lauf(zwei.Zustand, new Dictionary<string, bool>());
            Ereignisse(drei, ErmittlungsEreignis.Einfach(EreignisArt.SperreAufgehoben));
            Assert.That(drei.Zustand.SperreBis, Is.Null);
            Assert.That(IstGesperrt(drei.Zustand), Is.False);
        }

        [Test]
        public void Sperre_UnvollstaendigeZaehltNicht()
        {
            var z = Gesperrt();
            var r = WendePruefungAn(E, z, Ausgabe(new Dictionary<string, bool>(), false));
            Assert.That(r.Zustand, Is.SameAs(z));
            Assert.That(r.Zustand.SperreRestPruefungen, Is.EqualTo(SperrePruefungen));
        }

        [Test]
        public void Sperre_VerstossNeueSperreAusNeuemZustand()
        {
            var r = Lauf(StartZustand(), new Dictionary<string, bool>
            {
                [PruefIdBeleg("b-log")] = true,
                [PruefIdVerstoss] = true,
            });
            Assert.That(r.Zustand.SperreBis, Is.EqualTo(2));
            Assert.That(r.Zustand.SperreRestPruefungen, Is.EqualTo(SperrePruefungen));
        }

        [Test]
        public void Sperre_EreignisReihenfolge_SchrittBelegeVerstoss()
        {
            var r = Lauf(StartZustand(), new Dictionary<string, bool>
            {
                [PruefIdSchritt("rechte", "s1")] = true,
                [PruefIdBeleg("b-log")] = true,
                [PruefIdBeleg("b-sudoers")] = true,
                [PruefIdVerstoss] = true,
            });
            Ereignisse(r,
                ErmittlungsEreignis.SchrittErledigt("rechte", "s1"),
                ErmittlungsEreignis.BelegErkannt("b-sudoers"),
                ErmittlungsEreignis.BelegErkannt("b-log"),
                ErmittlungsEreignis.Einfach(EreignisArt.EinsatzrahmenVerletzt));
        }

        [Test]
        public void Sperre_VerstossBeiLaufenderSperre_OhneAufhebungWennZaehlerTraegt()
        {
            var z = Gesperrt();
            z.SperreRestPruefungen = 2;
            var r = Lauf(z, new Dictionary<string, bool> { [PruefIdVerstoss] = true });
            Ereignisse(r, ErmittlungsEreignis.Einfach(EreignisArt.EinsatzrahmenVerletzt));
            Assert.That(r.Zustand.SperreBis, Is.EqualTo(2));
            Assert.That(r.Zustand.SperreRestPruefungen, Is.EqualTo(SperrePruefungen));
        }

        [Test]
        public void Sperre_AufhebungVorVerstossWennZaehlerFaelltUndNeueSperreAusAbgebautemZustand()
        {
            var z = Gesperrt();
            z.SperreRestPruefungen = 1;
            var r = Lauf(z, new Dictionary<string, bool>
            {
                [PruefIdBeleg("b-sudoers")] = false,
                [PruefIdVerstoss] = true,
            });
            Ereignisse(r,
                ErmittlungsEreignis.BelegVerloren("b-sudoers"),
                ErmittlungsEreignis.Einfach(EreignisArt.SperreAufgehoben),
                ErmittlungsEreignis.Einfach(EreignisArt.EinsatzrahmenVerletzt));
            Assert.That(r.Zustand.BelegeErkannt, Is.Empty);
            Assert.That(r.Zustand.SperreBis, Is.EqualTo(1));
            Assert.That(r.Zustand.SperreRestPruefungen, Is.EqualTo(SperrePruefungen));
        }

        [Test]
        public void MitZustandsDefaults()
        {
            Assert.That(ErmittlungsEngine.MitZustandsDefaults(Zustand(), null).SperreRestPruefungen, Is.EqualTo(0));
            Assert.That(ErmittlungsEngine.MitZustandsDefaults(Zustand(sperreBis: 2), null).SperreRestPruefungen, Is.EqualTo(SperrePruefungen));
            Assert.That(ErmittlungsEngine.MitZustandsDefaults(Zustand(sperreBis: 2), -1).SperreRestPruefungen, Is.EqualTo(SperrePruefungen));
            Assert.That(ErmittlungsEngine.MitZustandsDefaults(Zustand(sperreBis: 2), 1).SperreRestPruefungen, Is.EqualTo(1));
            var z = Zustand(sperreBis: 2);
            Assert.That(ErmittlungsEngine.MitZustandsDefaults(z, null), Is.Not.SameAs(z));
        }

        // ---- Vorwurf: feste Fallreihenfolge ----

        [Test]
        public void Vorwurf_BereitsGeloestVorAllemAnderen()
        {
            var z = Zustand(geloest: true, sperreBis: 3, belege: new string[0]);
            var r = Vorwurf(z, "nadia", "b-unbekannt");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.BereitsGeloest));
            Assert.That(r.Zustand, Is.SameAs(z));
            Assert.That(r.Antwort, Is.Empty);
        }

        [Test]
        public void Vorwurf_RichtigOffenVorSperreUndBelegPruefung()
        {
            var z = Zustand(sperreBis: 3);
            z.VorwurfRichtig = true;
            var r = Vorwurf(z, "nadia", "b-unbekannt");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.RichtigOffen));
            Assert.That(r.Zustand, Is.SameAs(z));
        }

        [Test]
        public void Vorwurf_GesperrtVorBelegUnbekannt()
        {
            var z = Zustand(belege: new[] { "b-sudoers" }, sperreBis: 2, sperreRest: 3);
            var r = Vorwurf(z, "timo", "b-unbekannt");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.Gesperrt));
            Assert.That(r.Antwort, Is.EqualTo(E.Vorwurf.Antworten.Gesperrt));
            Assert.That(r.Zustand, Is.SameAs(z));
        }

        [Test]
        public void Vorwurf_BelegUnbekanntVorFalscherPerson()
        {
            var z = StartZustand();
            var r = Vorwurf(z, "nadia", "b-sudoers");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.BelegUnbekannt));
            Assert.That(r.Zustand, Is.SameAs(z));
            Assert.That(r.Antwort, Is.Empty);
        }

        [Test]
        public void Vorwurf_FalschePersonStraftUndSperrtBisZumNaechstenBeleg()
        {
            var r = Vorwurf(Zustand(belege: new[] { "b-sudoers", "b-log" }), "nadia", "b-sudoers");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.FalschePerson));
            Assert.That(r.Antwort, Is.EqualTo(E.Vorwurf.Antworten.FalschePerson));
            Assert.That(r.Zustand.VorwurfVersuche, Is.EqualTo(1));
            Assert.That(r.Zustand.SperreBis, Is.EqualTo(3));
            Assert.That(r.Zustand.SperreRestPruefungen, Is.EqualTo(SperrePruefungen));
            Assert.That(IstGesperrt(r.Zustand), Is.True);
        }

        [Test]
        public void Vorwurf_FalschePersonVorZuWenigBelegen()
        {
            // Falsche Person mit zu wenig Belegen zählt als falsche Person.
            var r = Vorwurf(Zustand(belege: new[] { "b-sudoers" }), "margit", "b-sudoers");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.FalschePerson));
        }

        [Test]
        public void Vorwurf_ZuWenigBelegeOderFehlendeSpur()
        {
            var r = Vorwurf(Zustand(belege: new[] { "b-sudoers", "b-stat" }), "timo", "b-sudoers", "b-stat");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.ZuWenigBelege));
            Assert.That(r.Antwort, Is.EqualTo(E.Vorwurf.Antworten.ZuWenigBelege));
            Assert.That(r.Zustand.VorwurfVersuche, Is.EqualTo(1));
            Assert.That(r.Zustand.SperreBis, Is.EqualTo(3));
        }

        [Test]
        public void Vorwurf_MindestzahlAuchOhneJedeSpur_DuplikateZaehlenEinmal()
        {
            var e = Episode();
            e.Vorwurf.JedeSpur = false;
            var z = Zustand(belege: new[] { "b-log", "b-nadia", "b-stat", "b-sudoers" },
                erledigt: new Dictionary<string, int> { ["rechte"] = 2, ["log"] = 1 });
            Assert.That(PruefeVorwurf(e, z, new VorwurfEingabe { VerdaechtigeId = "timo", BelegIds = new[] { "b-sudoers" } }).Ergebnis,
                Is.EqualTo(VorwurfErgebnis.ZuWenigBelege));
            Assert.That(PruefeVorwurf(e, z, new VorwurfEingabe { VerdaechtigeId = "timo", BelegIds = new[] { "b-sudoers", "b-sudoers" } }).Ergebnis,
                Is.EqualTo(VorwurfErgebnis.ZuWenigBelege));
            Assert.That(PruefeVorwurf(e, z, new VorwurfEingabe { VerdaechtigeId = "timo", BelegIds = new[] { "b-sudoers", "b-stat" } }).Ergebnis,
                Is.EqualTo(VorwurfErgebnis.Richtig));
        }

        [Test]
        public void Vorwurf_BelegGegenAndereZaehltNichtAlsStuetze()
        {
            var z = Zustand(belege: new[] { "b-log", "b-nadia", "b-stat", "b-sudoers" });
            Assert.That(Vorwurf(z, "timo", "b-sudoers", "b-nadia").Ergebnis, Is.EqualTo(VorwurfErgebnis.ZuWenigBelege));
        }

        [Test]
        public void Vorwurf_RichtigLoestDenFall()
        {
            var z = Zustand(belege: new[] { "b-log", "b-sudoers" }, erledigt: new Dictionary<string, int> { ["rechte"] = 2, ["log"] = 1 });
            var r = Vorwurf(z, "timo", "b-sudoers", "b-log");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.Richtig));
            Assert.That(r.Antwort, Is.EqualTo(E.Vorwurf.Antworten.Richtig));
            Assert.That(r.Zustand.Geloest, Is.True);
            Assert.That(r.Zustand.VorwurfVersuche, Is.EqualTo(1));
            Assert.That(r.Zustand.SperreBis, Is.Null);
        }

        [Test]
        public void Vorwurf_MitAllenBelegenNieSackgasse()
        {
            var z = Zustand(belege: new[] { "b-log", "b-nadia", "b-stat", "b-sudoers" });
            var r = Vorwurf(z, "nadia", "b-sudoers");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.FalschePerson));
            Assert.That(r.Zustand.SperreBis, Is.Null);
            Assert.That(IstGesperrt(r.Zustand), Is.False);
            Assert.That(Vorwurf(r.Zustand, "nadia", "b-sudoers").Ergebnis, Is.EqualTo(VorwurfErgebnis.FalschePerson));
        }

        [Test]
        public void Vorwurf_OhneSperreSperrtNie()
        {
            var e = Episode();
            e.Vorwurf.OhneSperre = true;
            var r = PruefeVorwurf(e, Zustand(belege: new[] { "b-sudoers" }), new VorwurfEingabe { VerdaechtigeId = "nadia", BelegIds = new[] { "b-sudoers" } });
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.FalschePerson));
            Assert.That(r.Zustand.SperreBis, Is.Null);
        }

        [Test]
        public void Vorwurf_EingabenBleibenUnveraendert()
        {
            var z = Zustand(belege: new[] { "b-log", "b-sudoers" });
            var vorher = Json(z);
            Vorwurf(z, "nadia", "b-sudoers");
            Assert.That(Json(z), Is.EqualTo(vorher));
        }

        // ---- Abschluss: Fall gelöst ----

        private static readonly string[] AlleBelege = { "b-log", "b-stat", "b-sudoers" };

        [Test]
        public void Abschluss_RichtigerVorwurfVorLetztenSchrittenBleibtOffen()
        {
            var z = Zustand(belege: AlleBelege, erledigt: new Dictionary<string, int> { ["rechte"] = 1 });
            var r = Vorwurf(z, "timo", "b-sudoers", "b-log");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.RichtigOffen));
            Assert.That(r.Zustand.Geloest, Is.False);
            Assert.That(r.Zustand.VorwurfRichtig, Is.True);
            Assert.That(r.Zustand.VorwurfVersuche, Is.EqualTo(1));
            Assert.That(string.Join(" ", r.Antwort), Does.Contain("Schritt s2 (Spur Rechte); Schritt s1 (Spur Logbuch)"));
            Assert.That(OffenePruefungen(E, r.Zustand).Select(p => p.Id), Does.Contain("s.rechte.s2"));
            var nochmal = Vorwurf(r.Zustand, "timo", "b-sudoers", "b-log");
            Assert.That(nochmal.Ergebnis, Is.EqualTo(VorwurfErgebnis.RichtigOffen));
            Assert.That(nochmal.Zustand, Is.SameAs(r.Zustand));
        }

        [Test]
        public void Abschluss_SchliesstPerPruefungSobaldLetzteSchritteErledigt()
        {
            var z = Vorwurf(Zustand(belege: AlleBelege, erledigt: new Dictionary<string, int> { ["log"] = 1 }), "timo", "b-sudoers", "b-log").Zustand;
            var e = Lauf(z, new Dictionary<string, bool> { ["s.rechte.s1"] = true });
            Assert.That(e.Zustand.Geloest, Is.False);
            Assert.That(e.Ereignisse.Select(x => x.Art), Does.Not.Contain(EreignisArt.FallGeloest));
            e = Lauf(e.Zustand, new Dictionary<string, bool> { ["s.rechte.s2"] = true });
            Assert.That(e.Zustand.Geloest, Is.True);
            Assert.That(e.Ereignisse.Select(x => x.Art), Is.EqualTo(new[] { EreignisArt.SchrittErledigt, EreignisArt.SpurErledigt, EreignisArt.FallGeloest }));
            Assert.That(OffenePruefungen(E, e.Zustand), Is.Empty);
        }

        [Test]
        public void Abschluss_AuchBeiRichtigBeantwortetemLetztenAblesen()
        {
            var z = Vorwurf(Zustand(belege: AlleBelege, erledigt: new Dictionary<string, int> { ["rechte"] = 1, ["log"] = 1 }), "timo", "b-sudoers", "b-log").Zustand;
            var r = WendeAntwortAn(E, z, "rechte", "s2", true);
            Assert.That(r.Zustand.Geloest, Is.True);
            Assert.That(r.Ereignisse.Select(x => x.Art), Does.Contain(EreignisArt.FallGeloest));
        }

        [Test]
        public void Abschluss_SofortWennVorwurfErstNachAllenSpuren()
        {
            var z = Zustand(belege: AlleBelege, erledigt: new Dictionary<string, int> { ["rechte"] = 2, ["log"] = 1 });
            var r = Vorwurf(z, "timo", "b-sudoers", "b-log");
            Assert.That(r.Ergebnis, Is.EqualTo(VorwurfErgebnis.Richtig));
            Assert.That(r.Zustand.Geloest, Is.True);
        }

        [Test]
        public void Abschluss_AlleSpurenOhneVorwurfSchliesstNicht()
        {
            var z = Zustand(erledigt: new Dictionary<string, int> { ["rechte"] = 1 });
            var r = Lauf(z, new Dictionary<string, bool> { ["s.rechte.s2"] = true, ["s.log.s1"] = true });
            Assert.That(AlleSpurenErledigt(E, r.Zustand), Is.True);
            Assert.That(r.Zustand.Geloest, Is.False);
            Assert.That(r.Ereignisse.Select(x => x.Art), Does.Not.Contain(EreignisArt.FallGeloest));
        }

        [Test]
        public void Abschluss_FalscherVorwurfSetztVorwurfRichtigNie()
        {
            var z = Zustand(belege: AlleBelege, erledigt: new Dictionary<string, int> { ["rechte"] = 1 });
            Assert.That(Vorwurf(z, "nadia", "b-sudoers").Zustand.VorwurfRichtig, Is.False);
        }

        // ---- Hinweisstufen ----

        [Test]
        public void Tipps_DreiStufen_StufeDreiZaehltAlsMitLoesung()
        {
            var schritt = E.Spuren[0].Schritte[0];
            var laufend = new LaufendeErmittlung { EpisodeId = E.Id };
            Assert.That(laufend.TippAufdecken("rechte", "s1", schritt), Is.EqualTo("Richtung"));
            Assert.That(laufend.WithSolution, Is.False);
            Assert.That(laufend.TippAufdecken("rechte", "s1", schritt), Is.EqualTo("Befehl"));
            Assert.That(laufend.WithSolution, Is.False);
            Assert.That(laufend.TippAufdecken("rechte", "s1", schritt), Is.EqualTo("Lösung"));
            Assert.That(laufend.WithSolution, Is.True);
            Assert.That(laufend.TippAufdecken("rechte", "s1", schritt), Is.Null);
            Assert.That(laufend.HintsUsed, Is.EqualTo(3));
            Assert.That(laufend.TippAufdecken("rechte", "s2", E.Spuren[0].Schritte[1]), Is.EqualTo("Richtung"));
            Assert.That(laufend.HintsUsed, Is.EqualTo(4));
        }

        // ---- Datenprüfung ----

        [Test]
        public void PruefeDaten_AkzeptiertGueltigeEpisodeUndMeldetFehler()
        {
            var e = Episode();
            e.Spuren[1].Schritte[0].Art = Schrittart.Ablesen;
            e.Spuren[1].Schritte[0].Antwort = new AntwortFrage { Frage = "f", Format = "x", Pruefung = "true" };
            Assert.That(PruefeDaten(e), Is.Empty);

            e.Spuren[0].Id = "Rechte!";
            Assert.That(PruefeDaten(e), Does.Contain("Ungültige ID: Spur \"Rechte!\""));
        }

        [Test]
        public void PruefeDaten_ArtenRegeln()
        {
            var e = Episode();
            e.Spuren[0].Schritte[1].Art = Schrittart.Sichern;
            var fehler = PruefeDaten(e);
            Assert.That(fehler.Any(f => f.Contains("steht zweimal direkt hintereinander")), Is.True);
            Assert.That(fehler.Any(f => f.Contains("Mindestens 3 verschiedene Schrittarten")), Is.True);
            Assert.That(fehler.Any(f => f.Contains("ablesen braucht eine antwort")), Is.False);
        }

        [Test]
        public void IdMuster()
        {
            Assert.That(IstGueltigeId("a-1"), Is.True);
            Assert.That(IstGueltigeId("A"), Is.False);
            Assert.That(IstGueltigeId("a\n"), Is.False);
            Assert.That(IstGueltigeId(new string('a', 41)), Is.False);
        }
    }
}
