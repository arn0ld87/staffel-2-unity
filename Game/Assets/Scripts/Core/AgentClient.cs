// Request/Response-Schicht über AgentProtocol (Port von agentClient.ts): ordnet Antworten per ID den
// wartenden Aufrufen zu. Transportunabhängig: `writeLine` schreibt eine Zeile (mit abschließendem \n)
// in den Kanal, OnLine/Feed bekommen dessen Ausgabe. Der Client startet keine eigenen Threads; seine
// Zeitgeber laufen über Task.Delay-ähnliche Timer, der Zustand ist durch ein Lock geschützt.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Staffel2.Core
{
    public sealed class AgentResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
    }

    public class AgentException : Exception
    {
        public AgentException(string message) : base(message) { }
    }

    public interface IAgentRunner
    {
        Task<AgentResult> RunAsync(string script, int timeoutMs = 30_000, CancellationToken ct = default);
    }

    public sealed class AgentClient : IAgentRunner
    {
        /// <summary>Obergrenze der Laufzeit im Gast in Sekunden.</summary>
        public const int MaxGuestSeconds = 55;  // wie TIMEOUT_SECONDS im mission-agent des Images

        private sealed class Pending
        {
            public TaskCompletionSource<AgentResult> Source;
            public CancellationTokenSource Timer;
            public CancellationTokenRegistration Registration;
        }

        private readonly Action<string> _writeLine;
        private readonly AgentParser _parser = new AgentParser();
        private readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>();
        private readonly object _lock = new object();
        private int _counter;

        public AgentClient(Action<string> writeLine)
        {
            _writeLine = writeLine ?? throw new ArgumentNullException(nameof(writeLine));
        }

        /// <summary>Beliebig zerstückelte Ausgabe des Kanals; Zeilen werden gepuffert.</summary>
        public void Feed(string chunk)
        {
            foreach (var message in _parser.Push(chunk)) Dispatch(message);
        }

        /// <summary>Eine vollständige Zeile des Kanals (ohne \n).</summary>
        public void OnLine(string line)
        {
            if (line == null) return;
            if (line.EndsWith("\r", StringComparison.Ordinal)) line = line.Substring(0, line.Length - 1);
            var message = AgentProtocol.ParseLine(line);
            if (message != null) Dispatch(message);
        }

        /// <summary>
        /// Führt ein Skript im Gast aus. Der Gast bricht eine Sekunde früher ab, als wir warten (höchstens
        /// MaxGuestSeconds): Ein hängender Lauf blockiert den Agenten nie länger, als die Antwort interessiert.
        /// </summary>
        public Task<AgentResult> RunAsync(string script, int timeoutMs = 30_000, CancellationToken ct = default)
        {
            var id = NextId();
            var guestSeconds = Math.Min(MaxGuestSeconds, Math.Max(1, timeoutMs / 1000 - 1));
            return Request(id, AgentProtocol.EncodeRun(id, script, guestSeconds), timeoutMs, ct);
        }

        /// <summary>Antwort an `mission` in der VM (FIFO), ohne Prozessstart im Agenten.</summary>
        public Task<AgentResult> ReplyAsync(string requestId, int exitCode, string text, int timeoutMs = 5_000, CancellationToken ct = default)
        {
            var id = NextId();
            return Request(id, AgentProtocol.EncodeReply(id, requestId, exitCode, text), timeoutMs, ct);
        }

        /// <summary>
        /// PING wiederholen, bis die erste Antwort da ist. Jeder PING bleibt bis zum Ende offen: Unter Last
        /// antwortet der Gast, braucht aber länger als intervalMs.
        /// </summary>
        public async Task WaitReadyAsync(int intervalMs = 500, int attempts = 120, CancellationToken ct = default)
        {
            var offen = new List<string>();
            var erstes = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                for (var i = 0; i < attempts; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var id = NextId();
                    offen.Add(id);
                    // Eine Zeitüberschreitung oder ein ERR zu einem einzelnen PING entscheidet nichts.
                    var ping = Request(id, AgentProtocol.EncodePing(id), intervalMs * attempts, ct);
                    _ = ping.ContinueWith(
                        t => { if (t.Status == TaskStatus.RanToCompletion) erstes.TrySetResult(true); },
                        TaskScheduler.Default);
                    using (var takt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        var wartet = Task.Delay(intervalMs, takt.Token);
                        var fertig = await Task.WhenAny(erstes.Task, wartet).ConfigureAwait(false);
                        takt.Cancel();
                        if (fertig == erstes.Task) return;
                    }
                    ct.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                foreach (var id in offen) Forget(id);
            }
            throw new AgentException("Linux antwortet nicht");
        }

        private void Forget(string id)
        {
            Pending entry;
            lock (_lock)
            {
                if (!_pending.TryGetValue(id, out entry)) return;
                _pending.Remove(id);
            }
            Aufraeumen(entry);
            entry.Source.TrySetCanceled();
        }

        private static void Aufraeumen(Pending entry)
        {
            entry.Registration.Dispose();
            entry.Timer.Dispose();
        }

        private string NextId()
        {
            lock (_lock) { _counter += 1; return "m" + _counter; }
        }

        private Task<AgentResult> Request(string id, string line, int timeoutMs, CancellationToken ct)
        {
            var source = new TaskCompletionSource<AgentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = new Pending { Source = source, Timer = new CancellationTokenSource() };
            lock (_lock) _pending[id] = entry;
            entry.Timer.Token.Register(() =>
            {
                if (!Entferne(id, entry)) return;
                source.TrySetException(new TimeoutException($"Agent-Timeout ({id})"));
            });
            if (ct.CanBeCanceled)
            {
                entry.Registration = ct.Register(() =>
                {
                    if (!Entferne(id, entry)) return;
                    source.TrySetCanceled(ct);
                });
            }
            entry.Timer.CancelAfter(timeoutMs);
            try
            {
                _writeLine(line);
            }
            catch (Exception error)
            {
                if (Entferne(id, entry)) source.TrySetException(error);
            }
            return source.Task;
        }

        /// <summary>Löst den Eintrag aus der Tabelle; true nur für den, der ihn tatsächlich entfernt hat.</summary>
        private bool Entferne(string id, Pending entry)
        {
            lock (_lock)
            {
                if (!_pending.TryGetValue(id, out var aktuell) || !ReferenceEquals(aktuell, entry)) return false;
                _pending.Remove(id);
            }
            // Dispose aus dem eigenen Timer-Callback ist erlaubt.
            Aufraeumen(entry);
            return true;
        }

        private void Dispatch(AgentMessage message)
        {
            if (message.Type == AgentMessageType.Hello || message.Id == null) return;
            Pending entry;
            lock (_lock)
            {
                if (!_pending.TryGetValue(message.Id, out entry)) return;
                _pending.Remove(message.Id);
            }
            Aufraeumen(entry);
            if (message.Type == AgentMessageType.Result)
                entry.Source.TrySetResult(new AgentResult { ExitCode = message.ExitCode, Output = message.Output });
            else
                entry.Source.TrySetException(new AgentException($"Agent-Fehler ({message.Id}): {message.Reason}"));
        }
    }
}
