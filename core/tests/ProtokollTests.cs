using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Staffel2.Core.Tests
{
    public class ProtokollTests
    {
        private static string B64(string s) => AgentProtocol.EncodeBase64(s);

        [Test]
        public void Encode_PingRunReply()
        {
            Assert.That(AgentProtocol.EncodePing("m1"), Is.EqualTo("PING m1\n"));
            Assert.That(AgentProtocol.EncodeRun("m2", "echo hi"), Is.EqualTo($"RUN m2 {B64("echo hi")}\n"));
            Assert.That(AgentProtocol.EncodeRun("m2", "echo hi", 19), Is.EqualTo($"RUN m2 {B64("echo hi")} 19\n"));
            Assert.That(AgentProtocol.EncodeRun("m2", "x", 5000), Does.EndWith(" 999\n"));
            Assert.That(AgentProtocol.EncodeRun("m2", "x", 0), Does.EndWith(" 1\n"));
        }

        [Test]
        public void Encode_ReplyBereinigtText()
        {
            var line = AgentProtocol.EncodeReply("m3", "r12x3", 300, "a\r\nb\u0001c\td\u001ee\u007f");
            Assert.That(line, Is.EqualTo("REPLY m3 r12x3:255 a\u001eb" + "c\td\u001ee\n"));
            Assert.That(AgentProtocol.EncodeReply("m3", "r1x1", -4, "x"), Is.EqualTo("REPLY m3 r1x1:0 x\n"));
        }

        [Test]
        public void Encode_UngueltigeIdWirft()
        {
            Assert.Throws<ArgumentException>(() => AgentProtocol.EncodePing("a b"));
            Assert.Throws<ArgumentException>(() => AgentProtocol.EncodePing(""));
            Assert.Throws<ArgumentException>(() => AgentProtocol.EncodePing(new string('a', 33)));
            Assert.Throws<ArgumentException>(() => AgentProtocol.EncodeReply("m1", "bad id", 0, "x"));
        }

        [Test]
        public void Base64_RoundtripMitUmlauten()
        {
            Assert.That(AgentProtocol.DecodeBase64(B64("Müller → ok")), Is.EqualTo("Müller → ok"));
        }

        [Test]
        public void Parser_LiestHelloResErr()
        {
            var p = new AgentParser();
            var m = p.Push($"HELLO 1\nRES m1 0 {B64("ausgabe\n")}\nRES m2 3 -\nERR m3 timeout\nERR - kaputt\n");
            Assert.That(m, Has.Count.EqualTo(5));
            Assert.That(m[0].Type, Is.EqualTo(AgentMessageType.Hello));
            Assert.That(m[0].Version, Is.EqualTo(1));
            Assert.That(m[1].Type, Is.EqualTo(AgentMessageType.Result));
            Assert.That(m[1].Output, Is.EqualTo("ausgabe\n"));
            Assert.That(m[2].ExitCode, Is.EqualTo(3));
            Assert.That(m[2].Output, Is.EqualTo(""));
            Assert.That(m[3].Type, Is.EqualTo(AgentMessageType.Error));
            Assert.That(m[3].Id, Is.EqualTo("m3"));
            Assert.That(m[3].Reason, Is.EqualTo("timeout"));
            Assert.That(m[4].Id, Is.Null);
        }

        [Test]
        public void Parser_IgnoriertRauschenUndLiestCrlf()
        {
            var p = new AgentParser();
            var m = p.Push("login: \nEcho PING m1\nRES x\nRES m1 999999 -\nRES m1 0 -\r\nERR bad!id x\nHELLO eins\n");
            Assert.That(m, Has.Count.EqualTo(1));
            Assert.That(m[0].Id, Is.EqualTo("m1"));
        }

        [Test]
        public void Parser_KaputtesBase64IstBadOutput()
        {
            var m = new AgentParser().Push("RES m1 0 abc\nRES m2 0 a=bc\nRES m3 0 ====\n");
            Assert.That(m, Has.Count.EqualTo(3));
            Assert.That(m, Has.All.Matches<AgentMessage>(x => x.Type == AgentMessageType.Error && x.Reason == "bad-output"));
        }

        [Test]
        public void Parser_ZerstueckelteZufuhr()
        {
            var p = new AgentParser();
            var zeile = $"RES m1 0 {B64("hallo")}\n";
            var alle = new List<AgentMessage>();
            foreach (var c in zeile) alle.AddRange(p.Push(c.ToString()));
            Assert.That(alle, Has.Count.EqualTo(1));
            Assert.That(alle[0].Output, Is.EqualTo("hallo"));
        }

        [Test]
        public void Parser_VerwirftUeberlangeZeileBeiLimit()
        {
            var p = new AgentParser();
            Assert.That(p.Push("RES m1 0 " + new string('A', AgentProtocol.MaxLineLength)), Is.Empty);
            // Der Anfang ist verworfen; der Rest einer überlangen Zeile ergibt keine Nachricht.
            Assert.That(p.Push("AAAA\n"), Is.Empty);
            // Danach arbeitet der Parser normal weiter.
            Assert.That(p.Push("RES m2 0 -\n"), Has.Count.EqualTo(1));
        }

        [Test]
        public void Parser_ZeileExaktAmLimitBleibt()
        {
            var p = new AgentParser();
            var payload = new string('A', 4 * 100);
            Assert.That(p.Push("RES m1 0 " + payload), Is.Empty);
            Assert.That(p.Push("\n"), Has.Count.EqualTo(1));
        }
    }

    public class AbfrageTests
    {
        [Test]
        public void Lese_WerteInReihenfolgeVollstaendigMitSchlusszeile()
        {
            var a = Abfrage.Lese("tuer\toffen\ntuer\tzu\nkasse\t5\nende\t3\t0\n");
            Assert.That(a.Werte["tuer"], Is.EqualTo(new[] { "offen", "zu" }));
            Assert.That(a.Werte["kasse"], Is.EqualTo(new[] { "5" }));
            Assert.That(a.Vollstaendig, Is.True);
            Assert.That(a.FehlerCode, Is.Null);
        }

        [Test]
        public void Lese_AltesEndeFormatUndUnvollstaendig()
        {
            Assert.That(Abfrage.Lese("a\tb\nende\t1\n").Vollstaendig, Is.True);
            Assert.That(Abfrage.Lese("a\tb\n").Vollstaendig, Is.False);
            Assert.That(Abfrage.Lese("a\tb\nende\t2\t0\n").Vollstaendig, Is.False);
        }

        [Test]
        public void Lese_FehlerCodeOhne0Und141()
        {
            Assert.That(Abfrage.Lese("a\tb\nende\t1\t0\n").FehlerCode, Is.Null);
            Assert.That(Abfrage.Lese("a\tb\nende\t1\t141\n").FehlerCode, Is.Null);
            Assert.That(Abfrage.Lese("a\tb\nende\t1\t2\n").FehlerCode, Is.EqualTo(2));
        }

        [Test]
        public void Lese_IgnoriertUngueltigeSchluesselUndBereinigtWerte()
        {
            var a = Abfrage.Lese("Gross\tx\nok\ta\u0001b\tc\r\nende\tx\nende\t1\n");
            Assert.That(a.Werte.ContainsKey("Gross"), Is.False);
            Assert.That(a.Werte["ok"], Is.EqualTo(new[] { "ab\tc" }));
            Assert.That(a.Vollstaendig, Is.True);
        }

        [Test]
        public void Skript_WirftBeiUngueltigemKonto()
        {
            Assert.Throws<ArgumentException>(() => Abfrage.Skript("true", "Root;"));
            Assert.That(Abfrage.Skript("true"), Does.Contain("su -s /bin/bash azubi -c"));
        }
    }

    /// <summary>Byteidentisch zu den TypeScript-Funktionen; Referenz: Fixtures/expected.json (bun tests/gen-expected.ts).</summary>
    public class ByteIdentischTests
    {
        private static readonly JObject Erwartet =
            JObject.Parse(File.ReadAllText(TestDaten.FixturePfad("expected.json")));

        private static string Soll(string name) => (string)Erwartet[name];

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void BuendelSkript_Startzustand(int episode)
        {
            var e = TestDaten.Export().Episoden[episode].Ermittlung;
            var skript = ErmittlungsEngine.BuendelSkript(ErmittlungsEngine.OffenePruefungen(e, ErmittlungsEngine.StartZustand()));
            Assert.That(skript, Is.EqualTo(Soll($"buendel-e{episode}-start")));
        }

        [Test]
        public void BuendelSkript_Episode1MitFortschritt()
        {
            var e = TestDaten.Export().Episoden[1].Ermittlung;
            var z = TestDaten.Zustand(new Dictionary<string, int> { ["rechte"] = 2, ["log"] = 0 });
            z.EinsatzrahmenVerletzt = true;
            Assert.That(ErmittlungsEngine.BuendelSkript(ErmittlungsEngine.OffenePruefungen(e, z)), Is.EqualTo(Soll("buendel-e1-fortschritt")));
        }

        [Test]
        public void BuendelSkript_Episode2Halb()
        {
            var e = TestDaten.Export().Episoden[2].Ermittlung;
            var z = TestDaten.Zustand(new Dictionary<string, int> { ["prozess"] = 1, ["zettel"] = 1 });
            Assert.That(ErmittlungsEngine.BuendelSkript(ErmittlungsEngine.OffenePruefungen(e, z)), Is.EqualTo(Soll("buendel-e2-halb")));
        }

        [Test]
        public void BuendelSkript_DuplikateUndDiagnose()
        {
            var skript = ErmittlungsEngine.BuendelSkript(new[]
            {
                new Pruefung { Id = "a", Skript = "true" },
                new Pruefung { Id = "b", Skript = "echo \"x\"; exit 2" },
                new Pruefung { Id = "c", Skript = "true" },
                new Pruefung { Id = "d.diag", Skript = "echo \"Hallo\tWelt\"", Diagnose = true },
                new Pruefung { Id = "e", Skript = "echo \"x\"; exit 2" },
                new Pruefung { Id = "f", Skript = "true" },
                new Pruefung { Id = "g", Skript = "false" },
            });
            Assert.That(skript, Is.EqualTo(Soll("buendel-duplikate")));
        }

        [Test]
        public void BuendelSkript_Leer()
        {
            Assert.That(ErmittlungsEngine.BuendelSkript(new Pruefung[0]), Is.EqualTo(Soll("buendel-leer")));
        }

        [Test]
        public void AntwortSkript()
        {
            Assert.That(ErmittlungsEngine.AntwortSkript("test \"$ANTWORT\" = \"12:34\"", "Müller 12:34/x"), Is.EqualTo(Soll("antwort")));
        }

        [Test]
        public void AbfrageSkript()
        {
            var e = TestDaten.Export().Episoden[1].Ermittlung;
            Assert.That(Abfrage.Skript(e.Ansichten[0].Abfrage), Is.EqualTo(Soll("abfrage-e1")));
            Assert.That(Abfrage.Skript("printf 'a\\tb\\n'", "spieler_1"), Is.EqualTo(Soll("abfrage-konto")));
        }

        [Test]
        public void ProtokollZeilen()
        {
            Assert.That(AgentProtocol.EncodeRun("m1", "echo \"hi\" # äöü\n", 19), Is.EqualTo(Soll("run")));
            Assert.That(AgentProtocol.EncodeRun("m2", "true"), Is.EqualTo(Soll("run-ohne-limit")));
            Assert.That(AgentProtocol.EncodeReply("m3", "r12x3", 300, "a\r\nb\u0001c\td\u001ee\u007f"), Is.EqualTo(Soll("reply")));
            Assert.That(AgentProtocol.EncodePing("m4"), Is.EqualTo(Soll("ping")));
        }
    }
}
