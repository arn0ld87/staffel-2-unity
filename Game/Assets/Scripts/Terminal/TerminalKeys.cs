using System.Text;

namespace Staffel2.Terminal
{
    public enum TerminalKey
    {
        None, Enter, Backspace, Tab, Escape, Up, Down, Left, Right, Home, End, PageUp, PageDown, Delete, Insert,
    }

    /// <summary>Übersetzt Tasten in die Bytes, die ein xterm an die serielle Konsole sendet.</summary>
    public static class TerminalKeys
    {
        public static byte[] Encode(TerminalKey key, bool applicationCursorKeys)
        {
            string s = key switch
            {
                TerminalKey.Enter => "\r",
                TerminalKey.Backspace => "\x7f",
                TerminalKey.Tab => "\t",
                TerminalKey.Escape => "\x1b",
                TerminalKey.Up => applicationCursorKeys ? "\x1bOA" : "\x1b[A",
                TerminalKey.Down => applicationCursorKeys ? "\x1bOB" : "\x1b[B",
                TerminalKey.Right => applicationCursorKeys ? "\x1bOC" : "\x1b[C",
                TerminalKey.Left => applicationCursorKeys ? "\x1bOD" : "\x1b[D",
                TerminalKey.Home => "\x1b[H",
                TerminalKey.End => "\x1b[F",
                TerminalKey.PageUp => "\x1b[5~",
                TerminalKey.PageDown => "\x1b[6~",
                TerminalKey.Delete => "\x1b[3~",
                TerminalKey.Insert => "\x1b[2~",
                _ => "",
            };
            return Encoding.ASCII.GetBytes(s);
        }

        /// <summary>Strg+Buchstabe als Steuerzeichen (Strg+C = 0x03), sonst leer.</summary>
        public static byte[] Control(char letter)
        {
            char c = char.ToUpperInvariant(letter);
            if (c >= 'A' && c <= 'Z') return new[] { (byte)(c - 'A' + 1) };
            if (c == '[') return new byte[] { 0x1b };
            if (c == '\\') return new byte[] { 0x1c };
            if (c == ']') return new byte[] { 0x1d };
            return new byte[0];
        }

        /// <summary>Getippter Text als UTF-8; Zeilenumbrüche werden zu \r wie bei einer echten Tastatur.</summary>
        public static byte[] Text(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (ch == '\n') sb.Append('\r');
                else if (ch == '\r') continue;
                else if (ch >= ' ' || ch == '\t') sb.Append(ch);
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
