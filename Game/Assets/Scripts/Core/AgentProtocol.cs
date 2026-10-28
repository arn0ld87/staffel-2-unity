// Zeilenprotokoll des versteckten Befehlskanals zum Agenten in der VM (Port von agentProtocol.ts).
//
//   Host -> Gast   PING <id>
//                  RUN <id> <base64(skript)> [sekunden]
//                  REPLY <id> <anfrage-id>:<exitcode> <text>
//   Gast -> Host   HELLO 1
//                  RES <id> <exitcode> <base64(stdout+stderr)|->
//                  ERR <id|-> <grund>
//
// Eine Nachricht je Zeile (\n). Zeilen, die nicht zum Protokoll passen (Echo, Rauschen), ignoriert
// der Parser.
using System;
using System.Collections.Generic;
using System.Text;

namespace Staffel2.Core
{
    public enum AgentMessageType
    {
        Hello,
        Result,
        Error,
    }

    public class AgentMessage
    {
        public AgentMessageType Type { get; set; }
        public int Version { get; set; }
        /// <summary>null bei `ERR - grund`.</summary>
        public string Id { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Reason { get; set; }
    }

    public static class AgentProtocol
    {
        public const int ProtocolVersion = 1;
        /// <summary>Obergrenze für eine einzelne Zeile (Gast kürzt auf 64 KiB, Base64 +33 %).</summary>
        public const int MaxLineLength = 128 * 1024;

        public static bool IsValidId(string id)
        {
            if (id == null || id.Length < 1 || id.Length > 32) return false;
            foreach (var c in id)
            {
                var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        public static string EncodeBase64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        public static string DecodeBase64(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));

        private static void AssertValidId(string id)
        {
            if (!IsValidId(id)) throw new ArgumentException($"Ungültige Agent-ID: {id}");
        }

        public static string EncodePing(string id)
        {
            AssertValidId(id);
            return $"PING {id}\n";
        }

        public static string EncodeReply(string id, string requestId, int exitCode, string text)
        {
            AssertValidId(id);
            AssertValidId(requestId);
            var code = Math.Min(255, Math.Max(0, exitCode));
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') continue; // \r?\n → \x1e
                if (c == '\n') { sb.Append('\u001e'); continue; }
                // [\x00-\x08\x0a-\x1d\x1f\x7f] entfallen; \t und \x1e bleiben.
                if ((c <= '\u0008') || (c >= '\u000a' && c <= '\u001d') || c == '\u001f' || c == '\u007f') continue;
                sb.Append(c);
            }
            return $"REPLY {id} {requestId}:{code} {sb}\n";
        }

        public static string EncodeRun(string id, string script, int? limitSeconds = null)
        {
            AssertValidId(id);
            var limit = limitSeconds.HasValue ? " " + Math.Min(999, Math.Max(1, limitSeconds.Value)) : "";
            return $"RUN {id} {EncodeBase64(script)}{limit}\n";
        }

        private static bool NurZiffern(string s, int minLen, int maxLen)
        {
            if (s.Length < minLen || s.Length > maxLen) return false;
            foreach (var c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private static bool IstBase64(string payload)
        {
            if (payload.Length % 4 != 0) return false;
            var ende = payload.Length;
            var pad = 0;
            while (ende > 0 && payload[ende - 1] == '=' && pad < 2) { ende--; pad++; }
            for (var i = 0; i < ende; i++)
            {
                var c = payload[i];
                var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Liest eine vollständige Zeile (ohne \n); null, wenn sie nicht zum Protokoll passt.</summary>
        public static AgentMessage ParseLine(string line)
        {
            var parts = line.Split(' ');
            var kind = parts[0];

            if (kind == "HELLO" && parts.Length == 2)
            {
                if (parts[1].Length == 0) return new AgentMessage { Type = AgentMessageType.Hello, Version = 0 };
                return int.TryParse(parts[1], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var version)
                    ? new AgentMessage { Type = AgentMessageType.Hello, Version = version }
                    : null;
            }

            if (kind == "ERR" && parts.Length == 3)
            {
                var id = parts[1];
                if (id != "-" && !IsValidId(id)) return null;
                return new AgentMessage { Type = AgentMessageType.Error, Id = id == "-" ? null : id, Reason = parts[2] };
            }

            if (kind == "RES" && parts.Length == 4)
            {
                var id = parts[1];
                var code = parts[2];
                var payload = parts[3];
                if (!IsValidId(id) || !NurZiffern(code, 1, 3)) return null;
                var exitCode = int.Parse(code, System.Globalization.CultureInfo.InvariantCulture);
                if (payload == "-") return new AgentMessage { Type = AgentMessageType.Result, Id = id, ExitCode = exitCode, Output = "" };
                if (!IstBase64(payload)) return new AgentMessage { Type = AgentMessageType.Error, Id = id, Reason = "bad-output" };
                string output;
                try { output = DecodeBase64(payload); }
                catch (FormatException) { return new AgentMessage { Type = AgentMessageType.Error, Id = id, Reason = "bad-output" }; }
                return new AgentMessage { Type = AgentMessageType.Result, Id = id, ExitCode = exitCode, Output = output };
            }

            return null;
        }
    }

    /// <summary>
    /// Zustandsbehafteter Zeilenpuffer: Push nimmt beliebig zerstückelte Ausgabe des Kanals entgegen und
    /// liefert die dabei vollständig gewordenen Nachrichten.
    /// </summary>
    public sealed class AgentParser
    {
        private readonly StringBuilder _buffer = new StringBuilder();

        public List<AgentMessage> Push(string chunk)
        {
            var messages = new List<AgentMessage>();
            var start = 0;
            for (var end = chunk.IndexOf('\n', start); end != -1; end = chunk.IndexOf('\n', start))
            {
                _buffer.Append(chunk, start, end - start);
                var raw = _buffer.ToString();
                _buffer.Clear();
                start = end + 1;
                var message = AgentProtocol.ParseLine(raw.EndsWith("\r", StringComparison.Ordinal) ? raw.Substring(0, raw.Length - 1) : raw);
                if (message != null) messages.Add(message);
            }
            _buffer.Append(chunk, start, chunk.Length - start);
            if (_buffer.Length > AgentProtocol.MaxLineLength) _buffer.Clear();
            return messages;
        }
    }
}
