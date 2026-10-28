using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Staffel2.Linux
{
    /// <summary>Welches VM-Image läuft: Terminal (128 MB) oder Bildschirm mit X11 (512 MB, Episode 2).</summary>
    public enum LinuxImage { Terminal, Bildschirm }

    /// <summary>Nachricht vom Host-Prozess, auf dem Hauptthread abgeholt.</summary>
    public struct HostMessage
    {
        public string Type;      // booting | started | out | agent | frame | error | exit
        public byte[] Data;      // bei out oder frame (RGBA32)
        public string Line;      // bei agent
        public string Text;      // bei error/exit
        public int Ms;           // bei started
        public int Width;        // bei frame
        public int Height;       // bei frame
    }

    /// <summary>
    /// Startet `linux-host` (headless v86) als Kindprozess und spricht JSON-Zeilen über
    /// stdin/stdout. Keine Shell, feste Argumentliste, eigenes Arbeitsverzeichnis.
    /// Die VM hat keinen Netz-Relay und keinen Zugriff auf Host-Dateien: ihr Dateisystem
    /// kommt aus den Image-Dateien, Schreibzugriffe bleiben im Speicher des Hosts.
    /// Lesen läuft auf einem Hintergrundthread, Abholen über <see cref="TryDequeue"/>
    /// auf dem Hauptthread.
    /// </summary>
    public sealed class LinuxHost : IDisposable
    {
        private readonly ConcurrentQueue<HostMessage> _inbox = new ConcurrentQueue<HostMessage>();
        private readonly object _writeLock = new object();
        private Process _process;
        private StreamWriter _stdin;

        private static readonly Newtonsoft.Json.JsonSerializerSettings AsciiJson = new Newtonsoft.Json.JsonSerializerSettings
        {
            StringEscapeHandling = Newtonsoft.Json.StringEscapeHandling.EscapeNonAscii,
            Formatting = Newtonsoft.Json.Formatting.None,
        };

        /// <summary>Eine Protokollzeile an den Host: kompaktes JSON, nur ASCII.</summary>
        public static string Kodiere(JObject message) => Newtonsoft.Json.JsonConvert.SerializeObject(message, AsciiJson);

        public bool Running => _process != null && !_process.HasExited;
        public int? ProcessId => _process?.Id;

        /// <summary>
        /// Startet den Host. <paramref name="runtimeDir"/> enthält das Binary
        /// (linux-host-&lt;plattform&gt;), v86.wasm und assets/ mit dem Image.
        /// </summary>
        public void Start(string runtimeDir, string binaryName, LinuxImage image, string savedState = null)
        {
            if (_process != null) throw new InvalidOperationException("Host läuft bereits");
            string binary = Path.Combine(runtimeDir, binaryName);
            if (!File.Exists(binary)) throw new FileNotFoundException("Linux-Host fehlt", binary);
            EnsureExecutable(binary);

            var psi = new ProcessStartInfo
            {
                FileName = binary,
                WorkingDirectory = runtimeDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            var args = new System.Collections.Generic.List<string>
            {
                "--assets", Path.Combine(runtimeDir, "assets"),
                "--wasm", Path.Combine(runtimeDir, "v86.wasm"),
                "--image", image == LinuxImage.Bildschirm ? "bildschirm" : "terminal",
            };
            if (!string.IsNullOrEmpty(savedState)) { args.Add("--state"); args.Add(savedState); }
            psi.Arguments = JoinArguments(args.ToArray());
            // Keine Zugangsdaten oder Pfade des Entwicklungsrechners an die Laufzeit durchreichen.
            foreach (var key in new[] { "SSH_AUTH_SOCK", "GITHUB_TOKEN", "GH_TOKEN", "NPM_TOKEN", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "ANTHROPIC_API_KEY", "OPENAI_API_KEY" })
                psi.Environment.Remove(key);

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) Parse(e.Data); };
            _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _inbox.Enqueue(new HostMessage { Type = "stderr", Text = e.Data }); };
            _process.Exited += (_, __) => _inbox.Enqueue(new HostMessage { Type = "exit", Text = SafeExitCode() });
            _process.Start();
            _stdin = _process.StandardInput;
            _stdin.AutoFlush = true;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public bool TryDequeue(out HostMessage message) => _inbox.TryDequeue(out message);

        /// <summary>Tastatur des Spielers → ttyS0.</summary>
        public void SendTerminal(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            Write(new JObject { ["t"] = "in", ["d"] = Convert.ToBase64String(bytes) });
        }

        /// <summary>Eine Protokollzeile an den mission-agent auf ttyS1.</summary>
        public void SendAgent(string line)
        {
            Write(new JObject { ["t"] = "agent", ["line"] = line.TrimEnd('\n') });
        }

        /// <summary>VM-Zustand gzip-komprimiert nach <paramref name="path"/>; Antwort als "saved".</summary>
        public void SaveState(string path)
        {
            Write(new JObject { ["t"] = "save", ["path"] = path });
        }

        /// <summary>Grafischen Bildschirm (SVGA-Vollbilder) abonnieren oder abbestellen.</summary>
        public void SetScreenEnabled(bool on)
        {
            Write(new JObject { ["t"] = "screen", ["on"] = on });
        }

        /// <summary>Mausposition (0..w-1, 0..h-1), Bewegung, Tastenmaske (Bit 0=Links, 1=Rechts, 2=Mitte) und Mausrad an v86 senden.</summary>
        public void SendMouse(int x, int y, int w, int h, int dx, int dy, int buttons, int wheel = 0)
        {
            Write(new JObject
            {
                ["t"] = "mouse",
                ["x"] = x,
                ["y"] = y,
                ["w"] = w,
                ["h"] = h,
                ["dx"] = dx,
                ["dy"] = dy,
                ["buttons"] = buttons,
                ["wheel"] = wheel,
            });
        }

        /// <summary>PS/2-Scancodes (Set 1, inkl. 0xE0-Präfix und Break-Codes mit Bit 7) an v86 senden.</summary>
        public void SendKeyScancodes(params int[] codes)
        {
            if (codes == null || codes.Length == 0) return;
            Write(new JObject { ["t"] = "key", ["codes"] = new JArray(codes) });
        }

        private void Write(JObject message)
        {
            if (!Running) return;
            lock (_writeLock)
            {
                // Nicht-ASCII (z. B. „–“ im Spielstandpfad) als \uXXXX: unabhängig von der Kodierung von stdin.
                try { _stdin.WriteLine(Kodiere(message)); }
                catch (IOException) { /* Prozess beendet; Exit-Ereignis folgt */ }
                catch (ObjectDisposedException) { }
            }
        }

        private void Parse(string line)
        {
            try
            {
                var o = JObject.Parse(line);
                string t = (string)o["t"];
                switch (t)
                {
                    case "out": _inbox.Enqueue(new HostMessage { Type = "out", Data = Convert.FromBase64String((string)o["d"] ?? "") }); break;
                    case "agent": _inbox.Enqueue(new HostMessage { Type = "agent", Line = (string)o["line"] ?? "" }); break;
                    case "started": _inbox.Enqueue(new HostMessage { Type = "started", Ms = (int?)o["ms"] ?? 0 }); break;
                    case "booting": _inbox.Enqueue(new HostMessage { Type = "booting" }); break;
                    case "saved": _inbox.Enqueue(new HostMessage { Type = "saved", Text = (string)o["path"] }); break;
                    case "frame":
                    {
                        int w = (int?)o["w"] ?? 0;
                        int h = (int?)o["h"] ?? 0;
                        string b64 = (string)o["d"] ?? "";
                        byte[] raw = null;
                        if (w > 0 && h > 0 && b64.Length > 0)
                        {
                            byte[] compressed = Convert.FromBase64String(b64);
                            raw = new byte[w * h * 4];
                            using (var ms = new MemoryStream(compressed))
                            using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
                            {
                                int offset = 0;
                                while (offset < raw.Length)
                                {
                                    int read = ds.Read(raw, offset, raw.Length - offset);
                                    if (read <= 0) break;
                                    offset += read;
                                }
                            }
                        }
                        _inbox.Enqueue(new HostMessage { Type = "frame", Width = w, Height = h, Data = raw });
                        break;
                    }
                    case "error": _inbox.Enqueue(new HostMessage { Type = "error", Text = (string)o["message"] }); break;
                }
            }
            catch (Exception e)
            {
                _inbox.Enqueue(new HostMessage { Type = "error", Text = "Unlesbare Host-Zeile: " + e.Message });
            }
        }

        private string SafeExitCode()
        {
            try { return _process.ExitCode.ToString(); } catch { return "?"; }
        }

        /// <summary>StreamingAssets verliert beim Kopieren in den Player teils das Ausführungsrecht.</summary>
        private static void EnsureExecutable(string path)
        {
            if (Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.MacOSX) return;
            try
            {
                var chmod = new ProcessStartInfo("/bin/chmod", JoinArguments("u+x", path)) { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(chmod)) p?.WaitForExit(5000);
            }
            catch (Exception) { /* Start meldet den Fehler verständlicher */ }
        }

        /// <summary>
        /// Argumente nach der MSVC-Regel (CommandLineToArgvW), die Windows und der Unix-Parser
        /// von .NET gleich anwenden: jedes Argument in Anführungszeichen; Backslashes werden nur
        /// vor einem " oder am Ende verdoppelt, damit C:\Pfade unverändert ankommen. Es läuft keine Shell.
        /// </summary>
        public static string JoinArguments(params string[] args)
        {
            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('"');
                int backslashes = 0;
                foreach (char c in a)
                {
                    if (c == '\\') { backslashes++; continue; }
                    if (c == '"') { sb.Append('\\', backslashes * 2 + 1).Append('"'); }
                    else { sb.Append('\\', backslashes).Append(c); }
                    backslashes = 0;
                }
                sb.Append('\\', backslashes * 2).Append('"');
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_process == null) return;
            try
            {
                if (!_process.HasExited)
                {
                    Write(new JObject { ["t"] = "stop" });
                    _stdin?.Close();
                    if (!_process.WaitForExit(1500)) _process.Kill();
                }
            }
            catch (Exception) { }
            _process.Dispose();
            _process = null;
        }
    }
}
