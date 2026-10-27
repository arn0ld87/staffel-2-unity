using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Staffel2.Core.Tests
{
    public class AgentClientTests
    {
        /// <summary>Gast-Attrappe: sammelt gesendete Zeilen; Antworten füttert der Test per OnLine.</summary>
        private sealed class FakeTransport
        {
            public readonly List<string> Zeilen = new List<string>();
            public Action<string> Beim;
            public void Schreibe(string zeile)
            {
                lock (Zeilen) Zeilen.Add(zeile);
                Beim?.Invoke(zeile);
            }
            public string[] Teile(int index) => Zeilen[index].TrimEnd('\n').Split(' ');
        }

        private static string Res(string id, int code, string output) =>
            $"RES {id} {code} {(output.Length == 0 ? "-" : AgentProtocol.EncodeBase64(output))}";

        [Test]
        public async Task RunAsync_SendetRunMitGastLimitUndLiefertErgebnis()
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            var task = client.RunAsync("echo hallo", 20_000);
            Assert.That(t.Teile(0)[0], Is.EqualTo("RUN"));
            Assert.That(t.Teile(0)[1], Is.EqualTo("m1"));
            Assert.That(AgentProtocol.DecodeBase64(t.Teile(0)[2]), Is.EqualTo("echo hallo"));
            Assert.That(t.Teile(0)[3], Is.EqualTo("19"));
            client.OnLine(Res("m1", 7, "ausgabe\n"));
            var r = await task;
            Assert.That(r.ExitCode, Is.EqualTo(7));
            Assert.That(r.Output, Is.EqualTo("ausgabe\n"));
        }

        [TestCase(20_000, "19")]
        [TestCase(120_000, "55")]
        [TestCase(56_000, "55")]
        [TestCase(57_000, "55")]
        [TestCase(30_000, "29")]
        [TestCase(1_000, "1")]
        public void RunAsync_GastLimitEineSekundeFruehHoechstens55(int timeoutMs, string erwartet)
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            _ = client.RunAsync("true", timeoutMs);
            Assert.That(t.Teile(0)[3], Is.EqualTo(erwartet));
        }

        [Test]
        public void Ids_ZaehlenAlsM1M2()
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            _ = client.RunAsync("a");
            _ = client.RunAsync("b");
            _ = client.ReplyAsync("r1x1", 0, "ok");
            Assert.That(t.Zeilen.Select((_, i) => t.Teile(i)[1]), Is.EqualTo(new[] { "m1", "m2", "m3" }));
            Assert.That(t.Zeilen[2], Does.StartWith("REPLY m3 r1x1:0 ok"));
        }

        [Test]
        public async Task Antwort_SynchronImSendeaufrufWirdZugeordnet()
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            t.Beim = zeile => client.OnLine(Res(zeile.Split(' ')[1], 0, "sofort"));
            var r = await client.RunAsync("true");
            Assert.That(r.Output, Is.EqualTo("sofort"));
        }

        [Test]
        public async Task Feed_ZerstueckeltUndRauschenWirdIgnoriert()
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            var task = client.RunAsync("true");
            client.Feed("Echo RUN m1\nHELLO 1\nRES m9 0 -\nRES m1 0 ");
            Assert.That(task.IsCompleted, Is.False);
            client.Feed(AgentProtocol.EncodeBase64("fertig") + "\n");
            Assert.That((await task).Output, Is.EqualTo("fertig"));
        }

        [Test]
        public void ErrVomAgenten_LehntAbMitAgentException()
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            var task = client.RunAsync("true");
            client.OnLine("ERR m1 bad-script");
            var ex = Assert.ThrowsAsync<AgentException>(async () => await task);
            Assert.That(ex.Message, Does.Contain("bad-script"));
        }

        [Test]
        public void Zeitueberschreitung_WirftTimeoutException()
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            var task = client.RunAsync("true", 50);
            var ex = Assert.ThrowsAsync<TimeoutException>(async () => await task);
            Assert.That(ex.Message, Does.Contain("m1"));
            // Eine späte Antwort ist wirkungslos.
            Assert.DoesNotThrow(() => client.OnLine(Res("m1", 0, "")));
        }

        [Test]
        public void Abbruch_BrichtLaufAb()
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            using var cts = new CancellationTokenSource();
            var task = client.RunAsync("true", 30_000, cts.Token);
            cts.Cancel();
            Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        }

        [Test]
        public void SendeFehler_LandetImTask()
        {
            var client = new AgentClient(_ => throw new InvalidOperationException("kein Kanal"));
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await client.RunAsync("true"));
            Assert.That(ex.Message, Is.EqualTo("kein Kanal"));
        }

        [Test]
        public async Task WaitReady_SendetPingsBisErsteAntwort()
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            // Erst der dritte PING bekommt eine Antwort; ein früherer, spät beantworteter zählt ebenso.
            t.Beim = zeile =>
            {
                if (t.Zeilen.Count == 3) client.OnLine(Res(zeile.Split(' ')[1].TrimEnd('\n'), 0, ""));
            };
            await client.WaitReadyAsync(intervalMs: 10, attempts: 120);
            Assert.That(t.Zeilen, Is.EqualTo(new[] { "PING m1\n", "PING m2\n", "PING m3\n" }));
        }

        [Test]
        public async Task WaitReady_SpaeteAntwortAufErstenPingGenuegt()
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            var ready = client.WaitReadyAsync(intervalMs: 20, attempts: 50);
            await Task.Delay(70);
            Assert.That(t.Zeilen.Count, Is.GreaterThanOrEqualTo(2));
            client.OnLine(Res("m1", 0, ""));
            await ready;
        }

        [Test]
        public void WaitReady_OhneAntwortWirftNachAllenVersuchen()
        {
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            var ex = Assert.ThrowsAsync<AgentException>(async () => await client.WaitReadyAsync(intervalMs: 5, attempts: 4));
            Assert.That(ex.Message, Is.EqualTo("Linux antwortet nicht"));
            Assert.That(t.Zeilen, Has.Count.EqualTo(4));
        }

        [Test]
        public void WaitReady_Standardwerte500msMal120()
        {
            // Dokumentiert die Vorgaben, ohne 60 Sekunden zu warten: nur der Abbruch zählt.
            var t = new FakeTransport();
            var client = new AgentClient(t.Schreibe);
            using var cts = new CancellationTokenSource(80);
            Assert.CatchAsync<OperationCanceledException>(async () => await client.WaitReadyAsync(ct: cts.Token));
            Assert.That(t.Zeilen, Has.Count.EqualTo(1));
        }
    }

    public class ErmittlungsRunTests
    {
        private sealed class FakeRunner : IAgentRunner
        {
            public readonly List<string> Gesendet = new List<string>();
            public readonly List<TaskCompletionSource<AgentResult>> Offen = new List<TaskCompletionSource<AgentResult>>();

            public Task<AgentResult> RunAsync(string script, int timeoutMs = 30_000, CancellationToken ct = default)
            {
                lock (Gesendet) Gesendet.Add(script);
                var tcs = new TaskCompletionSource<AgentResult>();
                lock (Offen) Offen.Add(tcs);
                return tcs.Task;
            }

            public async Task WarteAufGesendet(int n)
            {
                for (var i = 0; i < 200; i++)
                {
                    lock (Gesendet) if (Gesendet.Count >= n) return;
                    await Task.Delay(5);
                }
                Assert.Fail($"{n} Skripte nicht gesendet");
            }
        }

        private static AgentResult Ok(string o = "") => new AgentResult { ExitCode = 0, Output = o };

        [Test]
        public async Task SendetSeriell_ErstNachDemVorgaenger()
        {
            var runner = new FakeRunner();
            var q = new ErmittlungsRun();
            var a = q.RunAsync(runner, "a", 1000, () => true);
            var b = q.RunAsync(runner, "b", 1000, () => true);
            await runner.WarteAufGesendet(1);
            await Task.Delay(30);
            Assert.That(runner.Gesendet, Is.EqualTo(new[] { "a" }));
            runner.Offen[0].SetResult(Ok("A"));
            Assert.That((await a).Output, Is.EqualTo("A"));
            await runner.WarteAufGesendet(2);
            runner.Offen[1].SetResult(Ok("B"));
            Assert.That((await b).Output, Is.EqualTo("B"));
        }

        [Test]
        public async Task VeralteterAuftragWirdVorDemSendenVerworfen()
        {
            var runner = new FakeRunner();
            var q = new ErmittlungsRun();
            var aktuell = true;
            var a = q.RunAsync(runner, "a", 1000, () => true);
            var b = q.RunAsync(runner, "b", 1000, () => aktuell);
            var c = q.RunAsync(runner, "c", 1000, () => true);
            await runner.WarteAufGesendet(1);
            aktuell = false;
            runner.Offen[0].SetResult(Ok());
            await a;
            Assert.ThrowsAsync<VeralteterAuftragException>(async () => await b);
            await runner.WarteAufGesendet(2);
            Assert.That(runner.Gesendet, Is.EqualTo(new[] { "a", "c" }));
            runner.Offen[1].SetResult(Ok());
            await c;
        }

        [Test]
        public async Task ErgebnisEinesInzwischenVeraltetenAuftragsWirdVerworfen()
        {
            var runner = new FakeRunner();
            var q = new ErmittlungsRun();
            var aktuell = true;
            var a = q.RunAsync(runner, "a", 1000, () => aktuell);
            await runner.WarteAufGesendet(1);
            aktuell = false;
            runner.Offen[0].SetResult(Ok());
            Assert.ThrowsAsync<VeralteterAuftragException>(async () => await a);
        }

        [Test]
        public async Task FehlerEinesAuftragsStopptDieSchlangeNicht()
        {
            var runner = new FakeRunner();
            var q = new ErmittlungsRun();
            var a = q.RunAsync(runner, "a", 1000, () => true);
            var b = q.RunAsync(runner, "b", 1000, () => true);
            await runner.WarteAufGesendet(1);
            runner.Offen[0].SetException(new TimeoutException("zu langsam"));
            Assert.ThrowsAsync<TimeoutException>(async () => await a);
            await runner.WarteAufGesendet(2);
            runner.Offen[1].SetResult(Ok("B"));
            Assert.That((await b).Output, Is.EqualTo("B"));
        }

        [Test]
        public void WerfenderIstAktuellGiltAlsVeraltet()
        {
            var runner = new FakeRunner();
            var q = new ErmittlungsRun();
            var a = q.RunAsync(runner, "a", 1000, () => throw new InvalidOperationException());
            Assert.ThrowsAsync<VeralteterAuftragException>(async () => await a);
            Assert.That(runner.Gesendet, Is.Empty);
        }
    }

    public class PruefLaufTests
    {
        [Test]
        public async Task PruefLauf_FuehrtBuendelAusUndSchreibtZustand_NachlaufGenauEinmal()
        {
            var e = TestDaten.Episode();
            var zustand = ErmittlungsEngine.StartZustand();
            var skripte = new List<string>();
            var gate = new TaskCompletionSource<bool>();
            var schreiben = 0;
            var lauf = new PruefLauf(new PruefLaufOptionen
            {
                Ermittlung = e,
                TimeoutMs = 1000,
                LeseZustand = () => zustand,
                Run = async (skript, _) =>
                {
                    skripte.Add(skript);
                    if (skripte.Count == 1) await gate.Task;
                    return new AgentResult { ExitCode = 0, Output = "pruefung\ts.rechte.s1\t0\nende\t1\n" };
                },
                SchreibeZustand = (z, ereignisse) => { zustand = z; schreiben++; },
            });
            lauf.Anstossen();
            lauf.Anstossen();
            lauf.Anstossen();
            Assert.That(lauf.Laeuft, Is.True);
            gate.SetResult(true);
            await lauf.Leerlauf();
            Assert.That(skripte, Has.Count.EqualTo(2));
            Assert.That(lauf.Laeuft, Is.False);
            Assert.That(schreiben, Is.EqualTo(2));
            Assert.That(zustand.Erledigt["rechte"], Is.EqualTo(1));
        }

        [Test]
        public async Task PruefLauf_FehlgeschlagenerRunWirdGemeldet_StoppeVerwirftErgebnis()
        {
            var e = TestDaten.Episode();
            var fehler = new List<PruefFehler>();
            var lauf = new PruefLauf(new PruefLaufOptionen
            {
                Ermittlung = e,
                LeseZustand = () => ErmittlungsEngine.StartZustand(),
                Run = (s, t) => Task.FromResult(new AgentResult { ExitCode = 3, Output = "" }),
                SchreibeZustand = (z, ev) => Assert.Fail("darf nicht schreiben"),
                MeldeFehler = fehler.Add,
            });
            lauf.Anstossen();
            await lauf.Leerlauf();
            Assert.That(fehler.Single().Art, Is.EqualTo(PruefFehlerArt.Agent));
            Assert.That(fehler.Single().Meldung, Is.EqualTo("Prüfung fehlgeschlagen (exit 3)"));

            var gate = new TaskCompletionSource<AgentResult>();
            var geschrieben = false;
            var zweiter = new PruefLauf(new PruefLaufOptionen
            {
                Ermittlung = e,
                LeseZustand = () => ErmittlungsEngine.StartZustand(),
                Run = (s, t) => gate.Task,
                SchreibeZustand = (z, ev) => geschrieben = true,
            });
            zweiter.Anstossen();
            zweiter.Stoppe();
            await zweiter.Leerlauf();
            gate.SetResult(new AgentResult { ExitCode = 0, Output = "ende\t0\n" });
            await Task.Delay(30);
            Assert.That(geschrieben, Is.False);
        }

        [Test]
        public async Task PruefLauf_VollstaendigeAusgabeFuehrtDenZustandWeiter()
        {
            var e = TestDaten.Episode();
            var zustand = ErmittlungsEngine.StartZustand();
            List<ErmittlungsEreignis> gesehen = null;
            var lauf = new PruefLauf(new PruefLaufOptionen
            {
                Ermittlung = e,
                LeseZustand = () => zustand,
                Run = (s, t) => Task.FromResult(new AgentResult
                {
                    ExitCode = 0,
                    Output = "pruefung\ts.rechte.s1\t0\npruefung\ts.log.s1\t1\npruefung\tb.b-sudoers\t0\npruefung\tb.b-stat\t1\n" +
                             "pruefung\tb.b-log\t1\npruefung\tb.b-nadia\t1\npruefung\te.verstoss\t1\nende\t7\n",
                }),
                SchreibeZustand = (z, ev) => { zustand = z; gesehen = ev; },
            });
            lauf.Anstossen();
            await lauf.Leerlauf();
            Assert.That(zustand.Erledigt["rechte"], Is.EqualTo(1));
            Assert.That(zustand.BelegeErkannt, Is.EqualTo(new[] { "b-sudoers" }));
            Assert.That(gesehen.Select(x => x.Art), Is.EqualTo(new[] { EreignisArt.SchrittErledigt, EreignisArt.BelegErkannt }));
        }
    }
}
