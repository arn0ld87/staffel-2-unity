using System;
using System.IO;
using System.Threading.Tasks;
using Staffel2.Core;
using Staffel2.Linux;
using Staffel2.Terminal;
using UnityEngine;

namespace Staffel2.Game
{
    public enum LinuxPhase { Aus, Laden, Start, Bereit, Fehler }

    /// <summary>
    /// Verbindet Linux-Host, Terminalbildschirm und Agent. Pumpt die Host-Nachrichten im
    /// Hauptthread, damit Tasks des AgentClient auf dem Hauptthread weiterlaufen.
    /// </summary>
    public sealed class LinuxSitzung : MonoBehaviour
    {
        public LinuxPhase Phase { get; private set; } = LinuxPhase.Aus;
        public string FehlerText { get; private set; }
        public VtScreen Bildschirm { get; private set; } = new VtScreen(100, 30);
        public AgentClient Agent { get; private set; }
        public LinuxImage Image { get; private set; }
        public float StartDauer { get; private set; }
        public Texture2D VmBildschirm { get; private set; }
        public int VmBildschirmFrames { get; private set; }
        public bool VmBildschirmGrafisch { get; private set; }

        public event Action PhaseGeaendert;
        public event Action<string> PromptSignal;
        public event Action<string> Gespeichert;
        public event Action VmBildschirmAktualisiert;

        private LinuxHost _host;
        private float _startZeit;
        private int _generation;
        private TaskCompletionSource<string> _speichern;

        public static string Laufzeitordner => Path.Combine(Application.streamingAssetsPath, "linux");

        public static string BinaryName
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.OSXEditor:
                    case RuntimePlatform.OSXPlayer: return "linux-host-darwin-arm64";
                    case RuntimePlatform.LinuxEditor:
                    case RuntimePlatform.LinuxPlayer: return "linux-host-linux-x64";
                    case RuntimePlatform.WindowsEditor:
                    case RuntimePlatform.WindowsPlayer: return "linux-host-windows-x64.exe";
                    default: return null;
                }
            }
        }

        /// <summary>Startet die VM; <paramref name="vmZustand"/> setzt einen gespeicherten Stand fort.</summary>
        public async Task<bool> StartenAsync(LinuxImage image, string vmZustand = null)
        {
            Stoppen();
            int generation = ++_generation;
            Image = image;
            Bildschirm = new VtScreen(Bildschirm.Cols, Bildschirm.Rows);
            Bildschirm.PromptSignal += p => PromptSignal?.Invoke(p);
            SetzePhase(LinuxPhase.Laden, null);
            _startZeit = Time.realtimeSinceStartup;
            try
            {
                if (BinaryName == null) throw new PlatformNotSupportedException("Diese Plattform hat noch keinen Linux-Host.");
                _host = new LinuxHost();
                _host.Start(Laufzeitordner, BinaryName, image, vmZustand);
                Agent = new AgentClient(line => _host?.SendAgent(line));
                SetzePhase(LinuxPhase.Start, null);
                await Agent.WaitReadyAsync();
                if (generation != _generation) return false;
                StartDauer = Time.realtimeSinceStartup - _startZeit;
                SetzePhase(LinuxPhase.Bereit, null);
                return true;
            }
            catch (Exception e)
            {
                if (generation == _generation)
                    SetzePhase(LinuxPhase.Fehler, e is AgentException ? "Linux antwortet nicht." : e.Message);
                return false;
            }
        }

        /// <summary>Begrüßung wie in der Webversion und frischer Prompt.</summary>
        public async Task BegruessenAsync()
        {
            if (Agent == null) return;
            // Die Begrüßung zeigt schon die frische Login-Shell des Rahmen-Setups; nur der Prompt fehlt.
            await Task.Yield();
            Tippe(new byte[] { (byte)'\r' });
        }

        public void Tippe(byte[] bytes) => _host?.SendTerminal(bytes);

        public void SetzeBildschirmAktiv(bool aktiv) => _host?.SetScreenEnabled(aktiv);

        public void SendeMaus(int x, int y, int w, int h, int dx, int dy, int buttons, int wheel = 0) =>
            _host?.SendMouse(x, y, w, h, dx, dy, buttons, wheel);

        public void SendeScancodes(params int[] codes) => _host?.SendKeyScancodes(codes);

        /// <summary>Spaltenzahl der Shell anpassen (stty über den Agenten, nichts im Terminal sichtbar).</summary>
        public void GroesseSetzen(int cols, int rows)
        {
            if (cols == Bildschirm.Cols && rows == Bildschirm.Rows) return;
            Bildschirm.Resize(cols, rows);
            if (Phase == LinuxPhase.Bereit && Agent != null)
                _ = Agent.RunAsync($"stty -F /dev/ttyS0 cols {Bildschirm.Cols} rows {Bildschirm.Rows}", 10_000).ContinueWith(_ => { });
        }

        public Task ReplyAsync(string requestId, int exitCode, string text) =>
            Agent != null ? Agent.ReplyAsync(requestId, exitCode, text) : Task.CompletedTask;

        /// <summary>VM-Zustand in eine Datei sichern; liefert den Pfad oder wirft bei Fehler.</summary>
        public Task<string> SpeichernAsync(string pfad)
        {
            if (_host == null || !_host.Running) return Task.FromException<string>(new InvalidOperationException("Linux läuft nicht."));
            _speichern?.TrySetCanceled();
            var tcs = new TaskCompletionSource<string>();
            _speichern = tcs;
            _host.SaveState(pfad);
            // Antwortet der Host nicht, darf das Speichern nicht ewig hängen.
            _ = Task.Delay(30_000).ContinueWith(_ => tcs.TrySetException(new TimeoutException("Linux hat den Spielstand nicht rechtzeitig gesichert.")));
            return tcs.Task;
        }

        public void Stoppen()
        {
            _generation++;
            _host?.Dispose();
            _host = null;
            Agent = null;
            VmBildschirmFrames = 0;
            VmBildschirmGrafisch = false;
            if (Phase != LinuxPhase.Aus) SetzePhase(LinuxPhase.Aus, null);
        }

        private void Update()
        {
            if (_host == null) return;
            int budget = 400;
            while (budget-- > 0 && _host != null && _host.TryDequeue(out var m))
            {
                switch (m.Type)
                {
                    case "out": Bildschirm.Feed(m.Data); break;
                    case "agent": Agent?.OnLine(m.Line); break;
                    case "saved": _speichern?.TrySetResult(m.Text); break;
                    case "frame":
                        if (m.Width > 0 && m.Height > 0 && m.Data != null && m.Data.Length == m.Width * m.Height * 4)
                        {
                            if (VmBildschirm == null || VmBildschirm.width != m.Width || VmBildschirm.height != m.Height)
                            {
                                if (VmBildschirm != null) Destroy(VmBildschirm);
                                VmBildschirm = new Texture2D(m.Width, m.Height, TextureFormat.RGBA32, false)
                                {
                                    filterMode = FilterMode.Bilinear,
                                    wrapMode = TextureWrapMode.Clamp,
                                };
                            }
                            VmBildschirm.LoadRawTextureData(m.Data);
                            VmBildschirm.Apply(false);
                            VmBildschirmGrafisch = true;
                            VmBildschirmFrames++;
                            VmBildschirmAktualisiert?.Invoke();
                        }
                        else
                        {
                            VmBildschirmGrafisch = false;
                            VmBildschirmAktualisiert?.Invoke();
                        }
                        break;
                    case "error":
                        if (_speichern != null && !_speichern.Task.IsCompleted && m.Text != null && m.Text.StartsWith("Speichern"))
                            _speichern.TrySetException(new IOException(m.Text));
                        else Debug.LogWarning("Linux-Host: " + m.Text);
                        break;
                    case "stderr": Debug.Log("Linux-Host: " + m.Text); break;
                    case "exit":
                        if (Phase != LinuxPhase.Aus) SetzePhase(LinuxPhase.Fehler, "Linux wurde beendet (Code " + m.Text + ").");
                        break;
                }
            }
        }

        private void SetzePhase(LinuxPhase phase, string fehler)
        {
            Phase = phase;
            FehlerText = fehler;
            PhaseGeaendert?.Invoke();
        }

        private void OnDestroy() => Stoppen();
        private void OnApplicationQuit() => Stoppen();
    }
}
