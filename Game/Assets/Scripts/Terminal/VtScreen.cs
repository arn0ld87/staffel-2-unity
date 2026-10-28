using System;
using System.Collections.Generic;
using System.Text;

namespace Staffel2.Terminal
{
    /// <summary>Eine Zelle des Terminalrasters: Zeichen plus Darstellung.</summary>
    public struct VtCell
    {
        public char Ch;
        public byte Fg;      // 0–15 Palette, 255 = Standard
        public byte Bg;      // 0–15 Palette, 255 = Standard
        public bool Bold;
        public bool Inverse;
        public bool Underline;

        public static VtCell Blank => new VtCell { Ch = ' ', Fg = 255, Bg = 255 };
    }

    /// <summary>
    /// Kleiner VT100/xterm-Emulator für die serielle Konsole der Alpine-VM. Deckt ab, was
    /// bash, nano, less, top und die Missionswerkzeuge senden: Cursorbewegung, Löschen,
    /// Scrollbereiche, Einfügen/Löschen von Zeilen und Zeichen, SGR-Farben, alternativer
    /// Bildschirm, Anwendungs-Cursortasten. OSC-Sequenzen werden nicht dargestellt; OSC 7771
    /// (Prompt-Signal der Missionsshell) wird als Ereignis gemeldet.
    /// Rein, ohne Unity: wird in EditMode-Tests direkt geprüft.
    /// </summary>
    public sealed class VtScreen
    {
        public const int MaxScrollback = 2000;

        public int Cols { get; private set; }
        public int Rows { get; private set; }
        public int CursorX { get; private set; }
        public int CursorY { get; private set; }
        public bool CursorVisible { get; private set; } = true;
        public bool ApplicationCursorKeys { get; private set; }
        public bool AlternateScreen => _alt != null;
        /// <summary>Wird bei jeder sichtbaren Änderung erhöht; die Darstellung zeichnet nur bei Änderung neu.</summary>
        public int Version { get; private set; }

        /// <summary>OSC 7771 ; payload BEL – payload ist "anfrage-id;befehl".</summary>
        public event Action<string> PromptSignal;
        public event Action Bell;

        private VtCell[][] _lines;
        private VtCell[][] _alt;          // gesicherter Hauptbildschirm, solange der alternative aktiv ist
        private readonly List<VtCell[]> _scrollback = new List<VtCell[]>();
        private int _top, _bottom;        // Scrollbereich, inklusive
        private VtCell _pen = VtCell.Blank;
        private int _savedX, _savedY;
        private bool _wrapPending;

        private enum State { Ground, Escape, Csi, Osc, OscEscape, Charset }
        private State _state;
        private readonly StringBuilder _params = new StringBuilder();
        private readonly StringBuilder _osc = new StringBuilder();
        private readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();
        private readonly char[] _chars = new char[4096];

        public VtScreen(int cols = 100, int rows = 30)
        {
            Cols = Math.Max(10, cols);
            Rows = Math.Max(2, rows);
            _lines = NewBuffer(Rows, Cols);
            _top = 0;
            _bottom = Rows - 1;
        }

        public IReadOnlyList<VtCell[]> Scrollback => _scrollback;
        public VtCell[] Line(int y) => _lines[y];

        /// <summary>Text einer Zeile ohne Darstellung, rechts ohne Leerzeichen.</summary>
        public string LineText(int y)
        {
            var sb = new StringBuilder(Cols);
            foreach (var c in _lines[y]) sb.Append(c.Ch);
            return sb.ToString().TrimEnd();
        }

        /// <summary>Ganzer sichtbarer Inhalt, Zeilen durch \n getrennt (für Tests und Untertitel).</summary>
        public string Text()
        {
            var sb = new StringBuilder();
            for (int y = 0; y < Rows; y++) sb.Append(LineText(y)).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }

        public void Resize(int cols, int rows)
        {
            cols = Math.Max(10, cols);
            rows = Math.Max(2, rows);
            if (cols == Cols && rows == Rows) return;
            var next = NewBuffer(rows, cols);
            // Unten ausrichten, damit der Prompt sichtbar bleibt.
            int shift = Math.Max(0, CursorY - (rows - 1));
            for (int y = 0; y < shift; y++) PushScrollback(_lines[y]);
            for (int y = 0; y < rows && y + shift < Rows; y++)
                Array.Copy(_lines[y + shift], next[y], Math.Min(cols, Cols));
            _lines = next;
            if (_alt != null) _alt = NewBuffer(rows, cols);
            Cols = cols;
            Rows = rows;
            CursorY = Math.Min(rows - 1, CursorY - shift);
            CursorX = Math.Min(cols - 1, CursorX);
            _top = 0;
            _bottom = rows - 1;
            _wrapPending = false;
            Version++;
        }

        public void Feed(byte[] data) => Feed(data, 0, data.Length);

        public void Feed(byte[] data, int offset, int count)
        {
            while (count > 0)
            {
                int take = Math.Min(count, 1024);
                int n = _utf8.GetChars(data, offset, take, _chars, 0, false);
                for (int i = 0; i < n; i++) Put(_chars[i]);
                offset += take;
                count -= take;
            }
            Version++;
        }

        public void Feed(string text)
        {
            foreach (var ch in text) Put(ch);
            Version++;
        }

        private void Put(char ch)
        {
            switch (_state)
            {
                case State.Ground: Ground(ch); break;
                case State.Escape: Escape(ch); break;
                case State.Csi: Csi(ch); break;
                case State.Osc:
                    if (ch == '\x07') EndOsc();
                    else if (ch == '\x1b') _state = State.OscEscape;
                    else if (_osc.Length < 8192) _osc.Append(ch);
                    break;
                case State.OscEscape:
                    // ST = ESC \
                    if (ch == '\\') EndOsc();
                    else { _state = State.Escape; Escape(ch); }
                    break;
                case State.Charset: _state = State.Ground; break;
            }
        }

        private void Ground(char ch)
        {
            switch (ch)
            {
                case '\x1b': _state = State.Escape; return;
                case '\r': CursorX = 0; _wrapPending = false; return;
                case '\n': case '\x0b': case '\x0c': LineFeed(); return;
                case '\b': if (CursorX > 0) CursorX--; _wrapPending = false; return;
                case '\t': CursorX = Math.Min(Cols - 1, (CursorX / 8 + 1) * 8); return;
                case '\x07': Bell?.Invoke(); return;
                case '\x0e': case '\x0f': return; // Zeichensatzumschaltung ignorieren
            }
            if (ch < ' ' || ch == '\x7f') return;
            if (_wrapPending)
            {
                CursorX = 0;
                LineFeed();
                _wrapPending = false;
            }
            var cell = _pen;
            cell.Ch = ch;
            _lines[CursorY][CursorX] = cell;
            if (CursorX == Cols - 1) _wrapPending = true;
            else CursorX++;
        }

        private void Escape(char ch)
        {
            _state = State.Ground;
            switch (ch)
            {
                case '[': _params.Clear(); _state = State.Csi; break;
                case ']': _osc.Clear(); _state = State.Osc; break;
                case '(': case ')': case '*': case '+': _state = State.Charset; break;
                case '7': _savedX = CursorX; _savedY = CursorY; break;
                case '8': CursorX = _savedX; CursorY = _savedY; _wrapPending = false; break;
                case 'M': ReverseIndex(); break;
                case 'D': LineFeed(); break;
                case 'E': CursorX = 0; LineFeed(); break;
                case 'c': Reset(); break;
                case '=': case '>': break; // Ziffernblock-Modus
            }
        }

        private void Csi(char ch)
        {
            if ((ch >= '0' && ch <= '9') || ch == ';' || ch == '?' || ch == '>' || ch == '!' || ch == ' ')
            {
                if (_params.Length < 64) _params.Append(ch);
                return;
            }
            _state = State.Ground;
            string raw = _params.ToString();
            bool priv = raw.StartsWith("?");
            var p = ParseParams(priv ? raw.Substring(1) : raw.TrimStart('>', '!'));
            int P(int i, int def) => i < p.Count && p[i] > 0 ? p[i] : def;

            switch (ch)
            {
                case 'A': CursorY = Math.Max(CursorY < _top ? 0 : _top, CursorY - P(0, 1)); break;
                case 'B': case 'e': CursorY = Math.Min(CursorY > _bottom ? Rows - 1 : _bottom, CursorY + P(0, 1)); break;
                case 'C': case 'a': CursorX = Math.Min(Cols - 1, CursorX + P(0, 1)); break;
                case 'D': CursorX = Math.Max(0, CursorX - P(0, 1)); break;
                case 'E': CursorX = 0; CursorY = Math.Min(Rows - 1, CursorY + P(0, 1)); break;
                case 'F': CursorX = 0; CursorY = Math.Max(0, CursorY - P(0, 1)); break;
                case 'G': case '`': CursorX = Clamp(P(0, 1) - 1, 0, Cols - 1); break;
                case 'd': CursorY = Clamp(P(0, 1) - 1, 0, Rows - 1); break;
                case 'H': case 'f':
                    CursorY = Clamp(P(0, 1) - 1, 0, Rows - 1);
                    CursorX = Clamp(P(1, 1) - 1, 0, Cols - 1);
                    break;
                case 'J': EraseDisplay(p.Count > 0 ? p[0] : 0); break;
                case 'K': EraseLine(p.Count > 0 ? p[0] : 0); break;
                case 'X': for (int i = 0; i < P(0, 1) && CursorX + i < Cols; i++) _lines[CursorY][CursorX + i] = BlankPen(); break;
                case 'P': DeleteChars(P(0, 1)); break;
                case '@': InsertChars(P(0, 1)); break;
                case 'L': if (CursorY >= _top && CursorY <= _bottom) ScrollDown(CursorY, _bottom, P(0, 1)); break;
                case 'M': if (CursorY >= _top && CursorY <= _bottom) ScrollUp(CursorY, _bottom, P(0, 1), false); break;
                case 'S': ScrollUp(_top, _bottom, P(0, 1), _top == 0 && _alt == null); break;
                case 'T': ScrollDown(_top, _bottom, P(0, 1)); break;
                case 'r':
                    _top = Clamp(P(0, 1) - 1, 0, Rows - 1);
                    _bottom = Clamp(P(1, Rows) - 1, 0, Rows - 1);
                    if (_bottom <= _top) { _top = 0; _bottom = Rows - 1; }
                    CursorX = 0; CursorY = 0;
                    break;
                case 's': _savedX = CursorX; _savedY = CursorY; break;
                case 'u': CursorX = _savedX; CursorY = _savedY; break;
                case 'm': Sgr(p); break;
                case 'h': case 'l':
                    if (priv) foreach (var mode in p) PrivateMode(mode, ch == 'h');
                    break;
                // n (Statusabfragen), c (Geräteattribute), t (Fenster): keine Antwort nötig.
            }
            _wrapPending = false;
        }

        private void PrivateMode(int mode, bool on)
        {
            switch (mode)
            {
                case 1: ApplicationCursorKeys = on; break;
                case 25: CursorVisible = on; break;
                case 47: case 1047: case 1049:
                    if (on && _alt == null)
                    {
                        if (mode == 1049) { _savedX = CursorX; _savedY = CursorY; }
                        _alt = _lines;
                        _lines = NewBuffer(Rows, Cols);
                    }
                    else if (!on && _alt != null)
                    {
                        _lines = _alt;
                        _alt = null;
                        if (mode == 1049) { CursorX = _savedX; CursorY = _savedY; }
                    }
                    break;
            }
        }

        private void Sgr(List<int> p)
        {
            if (p.Count == 0) p.Add(0);
            for (int i = 0; i < p.Count; i++)
            {
                int c = p[i];
                if (c == 0) { _pen = VtCell.Blank; }
                else if (c == 1) _pen.Bold = true;
                else if (c == 22) _pen.Bold = false;
                else if (c == 4) _pen.Underline = true;
                else if (c == 24) _pen.Underline = false;
                else if (c == 7) _pen.Inverse = true;
                else if (c == 27) _pen.Inverse = false;
                else if (c >= 30 && c <= 37) _pen.Fg = (byte)(c - 30);
                else if (c >= 90 && c <= 97) _pen.Fg = (byte)(c - 90 + 8);
                else if (c == 39) _pen.Fg = 255;
                else if (c >= 40 && c <= 47) _pen.Bg = (byte)(c - 40);
                else if (c >= 100 && c <= 107) _pen.Bg = (byte)(c - 100 + 8);
                else if (c == 49) _pen.Bg = 255;
                else if ((c == 38 || c == 48) && i + 2 < p.Count && p[i + 1] == 5)
                {
                    // 256-Farben auf die 16er-Palette abbilden: genügt für bash und nano.
                    int idx = p[i + 2];
                    byte mapped = (byte)(idx < 16 ? idx : 7);
                    if (c == 38) _pen.Fg = mapped; else _pen.Bg = mapped;
                    i += 2;
                }
                else if ((c == 38 || c == 48) && i + 4 < p.Count && p[i + 1] == 2)
                {
                    i += 4; // Truecolor ignorieren
                }
            }
        }

        private void EndOsc()
        {
            _state = State.Ground;
            string s = _osc.ToString();
            int semi = s.IndexOf(';');
            if (semi > 0 && s.Substring(0, semi) == "7771")
                PromptSignal?.Invoke(s.Substring(semi + 1));
        }

        private void LineFeed()
        {
            if (CursorY == _bottom) ScrollUp(_top, _bottom, 1, _top == 0 && _alt == null);
            else if (CursorY < Rows - 1) CursorY++;
        }

        private void ReverseIndex()
        {
            if (CursorY == _top) ScrollDown(_top, _bottom, 1);
            else if (CursorY > 0) CursorY--;
        }

        private void ScrollUp(int top, int bottom, int n, bool toScrollback)
        {
            n = Math.Min(n, bottom - top + 1);
            for (int k = 0; k < n; k++)
            {
                if (toScrollback) PushScrollback(_lines[top]);
                for (int y = top; y < bottom; y++) _lines[y] = _lines[y + 1];
                _lines[bottom] = BlankLine();
            }
        }

        private void ScrollDown(int top, int bottom, int n)
        {
            n = Math.Min(n, bottom - top + 1);
            for (int k = 0; k < n; k++)
            {
                for (int y = bottom; y > top; y--) _lines[y] = _lines[y - 1];
                _lines[top] = BlankLine();
            }
        }

        private void PushScrollback(VtCell[] line)
        {
            _scrollback.Add((VtCell[])line.Clone());
            if (_scrollback.Count > MaxScrollback) _scrollback.RemoveAt(0);
        }

        private void EraseDisplay(int mode)
        {
            if (mode == 0)
            {
                EraseLine(0);
                for (int y = CursorY + 1; y < Rows; y++) _lines[y] = BlankLine();
            }
            else if (mode == 1)
            {
                EraseLine(1);
                for (int y = 0; y < CursorY; y++) _lines[y] = BlankLine();
            }
            else
            {
                for (int y = 0; y < Rows; y++) _lines[y] = BlankLine();
                if (mode == 3) _scrollback.Clear();
            }
        }

        private void EraseLine(int mode)
        {
            int from = mode == 0 ? CursorX : 0;
            int to = mode == 1 ? CursorX : Cols - 1;
            for (int x = from; x <= to && x < Cols; x++) _lines[CursorY][x] = BlankPen();
        }

        private void DeleteChars(int n)
        {
            var line = _lines[CursorY];
            n = Math.Min(n, Cols - CursorX);
            Array.Copy(line, CursorX + n, line, CursorX, Cols - CursorX - n);
            for (int x = Cols - n; x < Cols; x++) line[x] = BlankPen();
        }

        private void InsertChars(int n)
        {
            var line = _lines[CursorY];
            n = Math.Min(n, Cols - CursorX);
            Array.Copy(line, CursorX, line, CursorX + n, Cols - CursorX - n);
            for (int x = CursorX; x < CursorX + n; x++) line[x] = BlankPen();
        }

        private void Reset()
        {
            _lines = NewBuffer(Rows, Cols);
            _alt = null;
            _pen = VtCell.Blank;
            CursorX = CursorY = 0;
            _top = 0;
            _bottom = Rows - 1;
            CursorVisible = true;
            ApplicationCursorKeys = false;
        }

        private VtCell BlankPen()
        {
            var c = VtCell.Blank;
            c.Bg = _pen.Bg;
            return c;
        }

        private VtCell[] BlankLine()
        {
            var line = new VtCell[Cols];
            var b = BlankPen();
            for (int x = 0; x < Cols; x++) line[x] = b;
            return line;
        }

        private static VtCell[][] NewBuffer(int rows, int cols)
        {
            var buf = new VtCell[rows][];
            for (int y = 0; y < rows; y++)
            {
                buf[y] = new VtCell[cols];
                for (int x = 0; x < cols; x++) buf[y][x] = VtCell.Blank;
            }
            return buf;
        }

        private static List<int> ParseParams(string s)
        {
            var list = new List<int>();
            if (s.Length == 0) return list;
            foreach (var part in s.Split(';'))
                list.Add(int.TryParse(part, out var v) ? Math.Min(v, 9999) : 0);
            return list;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    }
}
