using System.Text;
using Staffel2.Terminal;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Staffel2.Game.UI
{
    /// <summary>
    /// Zeichnet den VT-Bildschirm der Linux-Sitzung als TMP-Text in fester Zeichenbreite und
    /// leitet Tastatur, Einfügen und Mausrad an das Terminal. Gut lesbar als Bildschirm-UI,
    /// nicht auf einer schrägen 3D-Fläche.
    /// </summary>
    public sealed class TerminalAnsicht : MonoBehaviour
    {
        // Warme, gedämpfte Palette passend zum Serverraum; Index wie ANSI 0–15.
        private static readonly string[] Palette =
        {
            "#1b1d19", "#d0573a", "#8fb36b", "#d9a54a", "#6b93b8", "#b07aa8", "#6fb3ad", "#d8d4c6",
            "#5c5f55", "#ee7650", "#a9cf84", "#f0c46a", "#8ab2d6", "#cf98c6", "#8ed1cb", "#f3f0e6",
        };
        private const string Vordergrund = "#e6e1d3";
        private const string Hintergrund = "#141611";
        private const string CursorFarbe = "#db4a26";

        public LinuxSitzung Sitzung;
        public TMP_Text Text;
        public RectTransform Flaeche;
        public bool Fokus;
        public float Schriftgroesse = 22f;

        private int _gezeichneteVersion = -1;
        private int _scrollOffset;
        private float _zeichenBreite, _zeilenHoehe;
        private Key _wiederholTaste = Key.None;
        private float _wiederholAb;
        private bool _blinkAn = true;
        private float _blinkZeit;
        private readonly StringBuilder _sb = new StringBuilder(16000);

        public static readonly Color HintergrundFarbe = new Color32(0x14, 0x16, 0x11, 0xf6);

        private void OnEnable()
        {
            if (Keyboard.current != null) Keyboard.current.onTextInput += TextEingabe;
            _gezeichneteVersion = -1;
        }

        private void OnDisable()
        {
            if (Keyboard.current != null) Keyboard.current.onTextInput -= TextEingabe;
        }

        private void Update()
        {
            if (Sitzung == null || Text == null) return;
            MesseUndPasseAn();
            if (Fokus) Tastatur();
            Mausrad();

            _blinkZeit += Time.unscaledDeltaTime;
            if (_blinkZeit > 0.55f) { _blinkZeit = 0; _blinkAn = !_blinkAn; _gezeichneteVersion = -1; }
            var bild = Sitzung.Bildschirm;
            if (bild.Version != _gezeichneteVersion)
            {
                Zeichne(bild);
                _gezeichneteVersion = bild.Version;
            }
        }

        /// <summary>Spalten und Zeilen aus der Fläche und der Schriftgröße ableiten.</summary>
        private void MesseUndPasseAn()
        {
            if (Text.fontSize != Schriftgroesse) { Text.fontSize = Schriftgroesse; _zeichenBreite = 0; }
            if (_zeichenBreite <= 0)
            {
                var g = Text.GetPreferredValues("MMMMMMMMMM");
                _zeichenBreite = Mathf.Max(1f, g.x / 10f);
                _zeilenHoehe = Mathf.Max(1f, Text.GetPreferredValues("M\nM").y / 2f);
            }
            var r = Flaeche.rect;
            int cols = Mathf.Clamp(Mathf.FloorToInt((r.width - 8) / _zeichenBreite), 40, 200);
            int rows = Mathf.Clamp(Mathf.FloorToInt((r.height - 8) / _zeilenHoehe), 12, 80);
            Sitzung.GroesseSetzen(cols, rows);
        }

        private void Zeichne(VtScreen bild)
        {
            _sb.Clear();
            int rueck = bild.AlternateScreen ? 0 : Mathf.Min(_scrollOffset, bild.Scrollback.Count);
            for (int y = 0; y < bild.Rows; y++)
            {
                int quelle = y - rueck;
                VtCell[] zeile = quelle < 0 ? bild.Scrollback[bild.Scrollback.Count + quelle] : bild.Line(quelle);
                bool cursorZeile = rueck == 0 && quelle == bild.CursorY && Fokus && bild.CursorVisible && _blinkAn;
                ZeichneZeile(zeile, cursorZeile ? bild.CursorX : -1);
                if (y < bild.Rows - 1) _sb.Append('\n');
            }
            Text.text = _sb.ToString();
        }

        private void ZeichneZeile(VtCell[] zeile, int cursorX)
        {
            int ende = zeile.Length;
            // Leere Zellen rechts nicht ausgeben, außer der Cursor steht dort.
            while (ende > 0 && zeile[ende - 1].Ch == ' ' && zeile[ende - 1].Bg == 255 && !zeile[ende - 1].Inverse && ende - 1 != cursorX) ende--;
            int i = 0;
            while (i < ende)
            {
                var stil = Stil(zeile[i], i == cursorX);
                int j = i + 1;
                while (j < ende && j != cursorX && i != cursorX && Stil(zeile[j], false) == stil) j++;
                _sb.Append(stil.Item1);
                _sb.Append("<noparse>");
                for (int k = i; k < j; k++)
                {
                    char c = zeile[k].Ch;
                    _sb.Append(c == '<' ? '‹' : c);
                }
                _sb.Append("</noparse>");
                _sb.Append(stil.Item2);
                i = j;
            }
        }

        private static (string, string) Stil(VtCell c, bool cursor)
        {
            string fg = c.Fg == 255 ? Vordergrund : Palette[c.Fg & 15];
            if (c.Bold && c.Fg < 8) fg = Palette[(c.Fg & 7) + 8];
            string bg = c.Bg == 255 ? null : Palette[c.Bg & 15];
            if (c.Inverse) { var t = bg ?? Hintergrund; bg = fg; fg = t; }
            if (cursor) { bg = CursorFarbe; fg = Hintergrund; }
            var auf = new StringBuilder();
            var zu = new StringBuilder();
            auf.Append("<color=").Append(fg).Append('>');
            if (bg != null) { auf.Append("<mark=").Append(bg).Append("ff padding=0,0,0,0>"); zu.Insert(0, "</mark>"); }
            if (c.Underline) { auf.Append("<u>"); zu.Insert(0, "</u>"); }
            if (c.Bold) { auf.Append("<b>"); zu.Insert(0, "</b>"); }
            zu.Append("</color>");
            return (auf.ToString(), zu.ToString());
        }

        private void TextEingabe(char ch)
        {
            if (!Fokus || Sitzung == null) return;
            var kb = Keyboard.current;
            if (kb != null && (kb.ctrlKey.isPressed || kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed)) return;
            if (ch < ' ' || ch == '\x7f') return; // Steuerzeichen kommen über Tastatur()
            _scrollOffset = 0;
            Sitzung.Tippe(TerminalKeys.Text(ch.ToString()));
        }

        private void Tastatur()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool strg = kb.ctrlKey.isPressed;
            bool cmd = kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed;

            // Einfügen: Cmd+V (macOS) oder Strg+Umschalt+V.
            if ((cmd && kb.vKey.wasPressedThisFrame) || (strg && kb.shiftKey.isPressed && kb.vKey.wasPressedThisFrame))
            {
                Sitzung.Tippe(TerminalKeys.Text(GUIUtility.systemCopyBuffer ?? ""));
                return;
            }
            if (strg && !kb.shiftKey.isPressed)
            {
                for (Key k = Key.A; k <= Key.Z; k++)
                {
                    if (kb[k].wasPressedThisFrame)
                    {
                        Sitzung.Tippe(TerminalKeys.Control((char)('a' + (k - Key.A))));
                        return;
                    }
                }
            }

            Sondertaste(kb, Key.Enter, TerminalKey.Enter);
            Sondertaste(kb, Key.NumpadEnter, TerminalKey.Enter);
            Sondertaste(kb, Key.Backspace, TerminalKey.Backspace);
            Sondertaste(kb, Key.Tab, TerminalKey.Tab);
            Sondertaste(kb, Key.Escape, TerminalKey.Escape);
            Sondertaste(kb, Key.UpArrow, TerminalKey.Up);
            Sondertaste(kb, Key.DownArrow, TerminalKey.Down);
            Sondertaste(kb, Key.LeftArrow, TerminalKey.Left);
            Sondertaste(kb, Key.RightArrow, TerminalKey.Right);
            Sondertaste(kb, Key.Home, TerminalKey.Home);
            Sondertaste(kb, Key.End, TerminalKey.End);
            Sondertaste(kb, Key.Delete, TerminalKey.Delete);
            if (kb.shiftKey.isPressed && kb.pageUpKey.wasPressedThisFrame) { Scrolle(Sitzung.Bildschirm.Rows / 2); return; }
            if (kb.shiftKey.isPressed && kb.pageDownKey.wasPressedThisFrame) { Scrolle(-Sitzung.Bildschirm.Rows / 2); return; }
            Sondertaste(kb, Key.PageUp, TerminalKey.PageUp);
            Sondertaste(kb, Key.PageDown, TerminalKey.PageDown);

            // Tastenwiederholung für gehaltene Sondertasten.
            if (_wiederholTaste != Key.None)
            {
                if (!kb[_wiederholTaste].isPressed) _wiederholTaste = Key.None;
                else if (Time.unscaledTime >= _wiederholAb)
                {
                    _wiederholAb = Time.unscaledTime + 0.035f;
                    Sende(ZuTerminalTaste(_wiederholTaste));
                }
            }
        }

        private void Sondertaste(Keyboard kb, Key key, TerminalKey t)
        {
            if (!kb[key].wasPressedThisFrame) return;
            Sende(t);
            if (t != TerminalKey.Enter && t != TerminalKey.Escape && t != TerminalKey.Tab)
            {
                _wiederholTaste = key;
                _wiederholAb = Time.unscaledTime + 0.38f;
            }
        }

        private void Sende(TerminalKey t)
        {
            if (t == TerminalKey.None) return;
            _scrollOffset = 0;
            Sitzung.Tippe(TerminalKeys.Encode(t, Sitzung.Bildschirm.ApplicationCursorKeys));
        }

        private static TerminalKey ZuTerminalTaste(Key k)
        {
            switch (k)
            {
                case Key.Backspace: return TerminalKey.Backspace;
                case Key.UpArrow: return TerminalKey.Up;
                case Key.DownArrow: return TerminalKey.Down;
                case Key.LeftArrow: return TerminalKey.Left;
                case Key.RightArrow: return TerminalKey.Right;
                case Key.Delete: return TerminalKey.Delete;
                case Key.PageUp: return TerminalKey.PageUp;
                case Key.PageDown: return TerminalKey.PageDown;
                case Key.Home: return TerminalKey.Home;
                case Key.End: return TerminalKey.End;
                default: return TerminalKey.None;
            }
        }

        private void Mausrad()
        {
            var maus = Mouse.current;
            if (maus == null || Flaeche == null) return;
            float rad = maus.scroll.ReadValue().y;
            if (Mathf.Abs(rad) < 0.01f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(Flaeche, maus.position.ReadValue(), null)) return;
            Scrolle(rad > 0 ? 3 : -3);
        }

        private void Scrolle(int zeilen)
        {
            var bild = Sitzung.Bildschirm;
            if (bild.AlternateScreen) return;
            _scrollOffset = Mathf.Clamp(_scrollOffset + zeilen, 0, bild.Scrollback.Count);
            _gezeichneteVersion = -1;
        }

        public bool ImRueckblick => _scrollOffset > 0;
    }
}
