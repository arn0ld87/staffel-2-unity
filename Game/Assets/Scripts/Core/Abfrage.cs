// Abfragen der Ansichten (Port von abfrage.ts): Der Agent führt das Skript als root aus, die Abfrage
// selbst läuft als Spielerkonto und liest nur. Hier entstehen Skript und Parser.
using System;
using System.Collections.Generic;
using System.Text;

namespace Staffel2.Core
{
    public static class Abfrage
    {
        public const int MaxZeilen = 400;
        /// <summary>Unter der Agent-Grenze von 65 536 Zeichen.</summary>
        public const int MaxBytes = 60_000;
        public const string Spielerkonto = "azubi";

        private static bool IstKontoName(string konto)
        {
            if (konto == null || konto.Length < 1 || konto.Length > 32) return false;
            for (var i = 0; i < konto.Length; i++)
            {
                var c = konto[i];
                var klein = c >= 'a' && c <= 'z';
                if (i == 0) { if (!(klein || c == '_')) return false; }
                else if (!(klein || (c >= '0' && c <= '9') || c == '_' || c == '-')) return false;
            }
            return true;
        }

        /// <summary>/^[a-z0-9_.-]{1,40}$/ — Schlüssel einer Ausgabezeile.</summary>
        public static bool IstSchluessel(string s)
        {
            if (s == null || s.Length < 1 || s.Length > 40) return false;
            foreach (var c in s)
            {
                var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Bash-Skript für eine Ansicht: Abfrage als Spielerkonto, gefiltert und gekürzt.</summary>
        public static string Skript(string abfrage, string konto = Spielerkonto)
        {
            if (!IstKontoName(konto)) throw new ArgumentException($"Ungültiges Konto: {konto}");
            return string.Join("\n", new[]
            {
                "__a=$(printf '%s' '" + AgentProtocol.EncodeBase64(abfrage) + "' | base64 -d)",
                "__f=$(mktemp)",
                "su -s /bin/bash " + konto + " -c \"$__a\" 2>/dev/null </dev/null \\",
                "  | grep -a -E $'^[a-z0-9_.-]{1,40}\\t' | grep -a -v -E $'^ende\\t' \\",
                "  | head -n " + MaxZeilen + " \\",
                "  | LC_ALL=C awk -v max=" + MaxBytes + " '{ n += length($0) + 1; if (n > max) exit; print }' > \"$__f\"",
                "__rc=${PIPESTATUS[0]}",
                "cat \"$__f\"",
                "printf 'ende\\t%s\\t%s\\n' \"$(grep -ac '' \"$__f\")\" \"$__rc\"",
                "rm -f \"$__f\"",
                "",
            });
        }

        private static bool NurZiffern(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private static long Zahl(string s) =>
            long.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
                ? n : long.MaxValue;

        /// <summary>Liest `schluessel TAB wert`-Zeilen; vollständig nur mit passender Schlusszahl.</summary>
        public static AbfrageAusgabe Lese(string text)
        {
            var ausgabe = new AbfrageAusgabe();
            var gelesen = 0;
            long? schluss = null;
            int? fehlerCode = null;
            foreach (var roh in text.Split('\n'))
            {
                var zeile = roh.EndsWith("\r", StringComparison.Ordinal) ? roh.Substring(0, roh.Length - 1) : roh;
                var tab = zeile.IndexOf('\t');
                if (tab == -1) continue;
                var schluessel = zeile.Substring(0, tab);
                var wert = zeile.Substring(tab + 1);
                // Nur das exakte `ende` ist reserviert: mit Ziffern die Schlusszeile, sonst ignoriert.
                if (schluessel == "ende")
                {
                    // `ende TAB zahl` (alt) oder `ende TAB zahl TAB exit` (neu); die letzte gültige gewinnt.
                    var teile = wert.Split('\t');
                    if (teile.Length <= 2 && NurZiffern(teile[0]) && (teile.Length == 1 || NurZiffern(teile[1])))
                    {
                        schluss = Zahl(teile[0]);
                        long? rc = teile.Length == 2 ? Zahl(teile[1]) : (long?)null;
                        fehlerCode = rc.HasValue && rc.Value != 0 && rc.Value != 141 ? (int)Math.Min(rc.Value, int.MaxValue) : (int?)null;
                    }
                    continue;
                }
                if (!IstSchluessel(schluessel)) continue;
                // [\x00-\x08\x0a-\x1f\x7f] entfallen; Tab bleibt.
                var sb = new StringBuilder(wert.Length);
                foreach (var c in wert)
                    if (!(c <= '\u0008' || (c >= '\u000a' && c <= '\u001f') || c == '\u007f')) sb.Append(c);
                if (!ausgabe.Werte.TryGetValue(schluessel, out var liste))
                {
                    liste = new List<string>();
                    ausgabe.Werte[schluessel] = liste;
                }
                liste.Add(sb.ToString());
                gelesen++;
            }
            ausgabe.Vollstaendig = schluss.HasValue && schluss.Value == gelesen;
            ausgabe.FehlerCode = fehlerCode;
            return ausgabe;
        }
    }
}
