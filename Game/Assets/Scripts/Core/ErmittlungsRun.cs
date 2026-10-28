// Warteschlange der RUNs einer Ermittlung (Port von ermittlungsRun.ts): serialisiert Setup-, Prüf- und
// Abfrage-RUNs. Der Agent-Timeout beginnt erst beim tatsächlichen Senden. Veraltete Aufträge
// (istAktuell == false) werden verworfen, bevor sie gesendet werden, und ihr Ergebnis wird verworfen.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Staffel2.Core
{
    public class VeralteterAuftragException : Exception
    {
        public VeralteterAuftragException() : base("Der Ermittlungsauftrag ist nicht mehr aktuell.") { }
    }

    public sealed class ErmittlungsRun
    {
        private sealed class Auftrag
        {
            public IAgentRunner Client;
            public string Skript;
            public int TimeoutMs;
            public Func<bool> IstAktuell;
            public TaskCompletionSource<AgentResult> Source;
        }

        private readonly Queue<Auftrag> _warteschlange = new Queue<Auftrag>();
        private readonly object _lock = new object();
        private bool _sendet;

        private static bool IstGueltig(Auftrag auftrag)
        {
            try { return auftrag.IstAktuell(); }
            catch { return false; }
        }

        public Task<AgentResult> RunAsync(IAgentRunner client, string skript, int timeoutMs, Func<bool> istAktuell)
        {
            var source = new TaskCompletionSource<AgentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _warteschlange.Enqueue(new Auftrag
                {
                    Client = client, Skript = skript, TimeoutMs = timeoutMs, IstAktuell = istAktuell, Source = source,
                });
            }
            _ = Pumpe();
            return source.Task;
        }

        private async Task Pumpe()
        {
            lock (_lock)
            {
                if (_sendet) return;
                _sendet = true;
            }
            try
            {
                for (;;)
                {
                    Auftrag auftrag;
                    lock (_lock)
                    {
                        if (_warteschlange.Count == 0)
                        {
                            _sendet = false;
                            return;
                        }
                        auftrag = _warteschlange.Dequeue();
                    }
                    if (!IstGueltig(auftrag))
                    {
                        auftrag.Source.TrySetException(new VeralteterAuftragException());
                        continue;
                    }
                    AgentResult ergebnis;
                    try
                    {
                        ergebnis = await auftrag.Client.RunAsync(auftrag.Skript, auftrag.TimeoutMs).ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        auftrag.Source.TrySetException(error);
                        continue;
                    }
                    if (IstGueltig(auftrag)) auftrag.Source.TrySetResult(ergebnis);
                    else auftrag.Source.TrySetException(new VeralteterAuftragException());
                }
            }
            catch
            {
                lock (_lock) _sendet = false;
                throw;
            }
        }
    }
}
