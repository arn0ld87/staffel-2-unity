using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Staffel2.Game.UI
{
    /// <summary>
    /// Grafischer 4:3-Bildschirm der Linux-VM (Episode 2, 1024×768 SVGA) nach V86Bildschirm.tsx.
    /// Zeigt den VGA-Framebuffer als RawImage, hebt den aktiven Eingabefokus hervor und reicht
    /// Maus (VMMouse absolut + Tasten + Mausrad) sowie PS/2-Tastatur-Scancodes an v86 weiter.
    /// </summary>
    public sealed class BildschirmAnsicht : MonoBehaviour
    {
        public LinuxSitzung Sitzung;
        public RawImage Bild;
        public Outline Rahmen;
        public TextMeshProUGUI HinweisText;
        public TextMeshProUGUI LeerText;
        public RectTransform Zeiger;

        private bool _fokus;
        private int _letzteMausX = 512;
        private int _letzteMausY = 384;
        private int _letzteButtons;

        public bool Fokus
        {
            get => _fokus;
            set
            {
                if (_fokus == value)
                {
                    AktualisiereHinweis();
                    return;
                }
                _fokus = value;
                if (!_fokus && Sitzung != null)
                {
                    // Umschalt- und Maustasten loslassen, damit in X11 nichts hängenbleibt.
                    Sitzung.SendeScancodes(0xaa, 0xb6);
                    if (_letzteButtons != 0)
                    {
                        _letzteButtons = 0;
                        Sitzung.SendeMaus(_letzteMausX, _letzteMausY, 1024, 768, 0, 0, 0, 0);
                    }
                }
                AktualisiereHinweis();
            }
        }

        private void OnEnable()
        {
            if (Sitzung != null)
            {
                Sitzung.VmBildschirmAktualisiert += AufBildschirmAktualisiert;
                Sitzung.SetzeBildschirmAktiv(true);
            }
            AktualisiereBild();
            AktualisiereHinweis();
        }

        private void OnDisable()
        {
            if (Sitzung != null)
            {
                Sitzung.VmBildschirmAktualisiert -= AufBildschirmAktualisiert;
                Sitzung.SetzeBildschirmAktiv(false);
            }
            Fokus = false;
        }

        private void AufBildschirmAktualisiert()
        {
            AktualisiereBild();
        }

        public void AktualisiereBild()
        {
            if (Bild == null || Sitzung == null) return;
            bool hatBild = Sitzung.VmBildschirm != null && Sitzung.VmBildschirmGrafisch;
            if (hatBild)
            {
                if (Bild.texture != Sitzung.VmBildschirm)
                {
                    Bild.texture = Sitzung.VmBildschirm;
                    // v86 liefert Zeile 0 oben, Unitys Texture2D hat Zeile 0 unten.
                    Bild.uvRect = new Rect(0f, 1f, 1f, -1f);
                }
                Bild.color = Color.white;
            }
            else
            {
                Bild.texture = null;
                Bild.color = UiBaukasten.Hex("#090a08");
            }
            if (LeerText != null)
            {
                LeerText.gameObject.SetActive(!hatBild);
                if (!hatBild)
                {
                    LeerText.text = Sitzung.Phase == LinuxPhase.Bereit
                        ? "Bildschirm wird geladen …\n(Falls ausgeschaltet: im Reiter „Terminal“ den Befehl  bildschirm  eingeben.)"
                        : "Linux-Bildschirm startet …";
                }
            }
            PositioniereZeiger(_letzteMausX, _letzteMausY);
        }

        public void AktualisiereHinweis()
        {
            if (HinweisText != null)
            {
                HinweisText.text = _fokus
                    ? "<color=#db4a26>●</color>  Maus und Tastatur gehen an Linux. Zurück: Umschalt+Esc oder außerhalb klicken."
                    : "Klick auf den Bildschirm: Maus und Tastatur gehen an Linux.   ·   Rechtsklick auf den Hintergrund öffnet das Menü.";
            }
            if (Rahmen != null)
            {
                Rahmen.effectColor = _fokus ? UiBaukasten.Signal : UiBaukasten.Linie;
                Rahmen.effectDistance = _fokus ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
            }
        }

        /// <summary>Bewegt den Zeiger im Gast auf (x, y) im 1024×768-Raster (für Spieler und Demo).</summary>
        public void SetzeMausPosition(int x, int y, int buttons = 0, int wheel = 0)
        {
            int w = Sitzung?.VmBildschirm != null ? Sitzung.VmBildschirm.width : 1024;
            int h = Sitzung?.VmBildschirm != null ? Sitzung.VmBildschirm.height : 768;
            int nx = Mathf.Clamp(x, 0, w - 1);
            int ny = Mathf.Clamp(y, 0, h - 1);
            int dx = nx - _letzteMausX;
            int dy = ny - _letzteMausY;
            _letzteMausX = nx;
            _letzteMausY = ny;
            _letzteButtons = buttons;
            PositioniereZeiger(nx, ny);
            Sitzung?.SendeMaus(nx, ny, w, h, dx, dy, buttons, wheel);
        }

        private void PositioniereZeiger(int vx, int vy)
        {
            if (Zeiger == null || Bild == null) return;
            int w = Sitzung?.VmBildschirm != null ? Sitzung.VmBildschirm.width : 1024;
            int h = Sitzung?.VmBildschirm != null ? Sitzung.VmBildschirm.height : 768;
            var rect = Bild.rectTransform.rect;
            if (rect.width <= 1f || rect.height <= 1f) return;
            float normX = Mathf.Clamp01(vx / (float)Mathf.Max(1, w - 1));
            float normY = Mathf.Clamp01(vy / (float)Mathf.Max(1, h - 1));
            float lx = rect.xMin + normX * rect.width;
            float ly = rect.yMax - normY * rect.height;
            Zeiger.anchoredPosition = new Vector2(lx, ly);
        }

        private void Update()
        {
            if (Sitzung == null || Sitzung.Phase != LinuxPhase.Bereit || Bild == null) return;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 screenPos = mouse.position.ReadValue();
                bool inBild = RectTransformUtility.ScreenPointToLocalPointInRectangle(Bild.rectTransform, screenPos, null, out var local) &&
                              Bild.rectTransform.rect.Contains(local);

                bool klickStart = mouse.leftButton.wasPressedThisFrame ||
                                  mouse.rightButton.wasPressedThisFrame ||
                                  mouse.middleButton.wasPressedThisFrame;

                if (klickStart)
                {
                    if (inBild && !_fokus)
                    {
                        Fokus = true;
                        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
                    }
                    else if (!inBild && _fokus)
                    {
                        Fokus = false;
                    }
                }

                if (_fokus && inBild)
                {
                    var rect = Bild.rectTransform.rect;
                    int w = Sitzung.VmBildschirm != null ? Sitzung.VmBildschirm.width : 1024;
                    int h = Sitzung.VmBildschirm != null ? Sitzung.VmBildschirm.height : 768;
                    float normX = Mathf.Clamp01((local.x - rect.xMin) / Mathf.Max(1f, rect.width));
                    float normY = Mathf.Clamp01(1f - (local.y - rect.yMin) / Mathf.Max(1f, rect.height));
                    int vx = Mathf.Clamp(Mathf.RoundToInt(normX * (w - 1)), 0, w - 1);
                    int vy = Mathf.Clamp(Mathf.RoundToInt(normY * (h - 1)), 0, h - 1);

                    int buttons = (mouse.leftButton.isPressed ? 1 : 0) |
                                  (mouse.rightButton.isPressed ? 2 : 0) |
                                  (mouse.middleButton.isPressed ? 4 : 0);
                    float scrollY = mouse.scroll.ReadValue().y;
                    int wheel = scrollY > 0.05f ? 1 : scrollY < -0.05f ? -1 : 0;

                    if (vx != _letzteMausX || vy != _letzteMausY || buttons != _letzteButtons || wheel != 0)
                    {
                        SetzeMausPosition(vx, vy, buttons, wheel);
                    }
                }
            }

            var kb = Keyboard.current;
            if (!_fokus || kb == null) return;

            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (shift && kb.escapeKey.wasPressedThisFrame)
            {
                Fokus = false;
                return;
            }

            foreach (var entry in KeyScancodes)
            {
                var ctrl = kb[entry.Key];
                if (ctrl == null) continue;
                if (ctrl.wasPressedThisFrame)
                {
                    if (entry.Extended) Sitzung.SendeScancodes(0xE0, entry.Code);
                    else Sitzung.SendeScancodes(entry.Code);
                }
                if (ctrl.wasReleasedThisFrame)
                {
                    if (entry.Extended) Sitzung.SendeScancodes(0xE0, entry.Code | 0x80);
                    else Sitzung.SendeScancodes(entry.Code | 0x80);
                }
            }
        }

        private readonly struct ScancodeMap
        {
            public readonly Key Key;
            public readonly int Code;
            public readonly bool Extended;
            public ScancodeMap(Key key, int code, bool extended = false)
            {
                Key = key;
                Code = code;
                Extended = extended;
            }
        }

        // PS/2 Set-1 Scancodes (F2 und F10 bleiben für Fallakte/Raum reserviert).
        private static readonly ScancodeMap[] KeyScancodes =
        {
            new ScancodeMap(Key.Escape, 0x01),
            new ScancodeMap(Key.Digit1, 0x02),
            new ScancodeMap(Key.Digit2, 0x03),
            new ScancodeMap(Key.Digit3, 0x04),
            new ScancodeMap(Key.Digit4, 0x05),
            new ScancodeMap(Key.Digit5, 0x06),
            new ScancodeMap(Key.Digit6, 0x07),
            new ScancodeMap(Key.Digit7, 0x08),
            new ScancodeMap(Key.Digit8, 0x09),
            new ScancodeMap(Key.Digit9, 0x0A),
            new ScancodeMap(Key.Digit0, 0x0B),
            new ScancodeMap(Key.Minus, 0x0C),
            new ScancodeMap(Key.Equals, 0x0D),
            new ScancodeMap(Key.Backspace, 0x0E),
            new ScancodeMap(Key.Tab, 0x0F),
            new ScancodeMap(Key.Q, 0x10),
            new ScancodeMap(Key.W, 0x11),
            new ScancodeMap(Key.E, 0x12),
            new ScancodeMap(Key.R, 0x13),
            new ScancodeMap(Key.T, 0x14),
            new ScancodeMap(Key.Y, 0x15),
            new ScancodeMap(Key.U, 0x16),
            new ScancodeMap(Key.I, 0x17),
            new ScancodeMap(Key.O, 0x18),
            new ScancodeMap(Key.P, 0x19),
            new ScancodeMap(Key.LeftBracket, 0x1A),
            new ScancodeMap(Key.RightBracket, 0x1B),
            new ScancodeMap(Key.Enter, 0x1C),
            new ScancodeMap(Key.NumpadEnter, 0x1C, true),
            new ScancodeMap(Key.LeftCtrl, 0x1D),
            new ScancodeMap(Key.RightCtrl, 0x1D, true),
            new ScancodeMap(Key.A, 0x1E),
            new ScancodeMap(Key.S, 0x1F),
            new ScancodeMap(Key.D, 0x20),
            new ScancodeMap(Key.F, 0x21),
            new ScancodeMap(Key.G, 0x22),
            new ScancodeMap(Key.H, 0x23),
            new ScancodeMap(Key.J, 0x24),
            new ScancodeMap(Key.K, 0x25),
            new ScancodeMap(Key.L, 0x26),
            new ScancodeMap(Key.Semicolon, 0x27),
            new ScancodeMap(Key.Quote, 0x28),
            new ScancodeMap(Key.Backquote, 0x29),
            new ScancodeMap(Key.LeftShift, 0x2A),
            new ScancodeMap(Key.Backslash, 0x2B),
            new ScancodeMap(Key.Z, 0x2C),
            new ScancodeMap(Key.X, 0x2D),
            new ScancodeMap(Key.C, 0x2E),
            new ScancodeMap(Key.V, 0x2F),
            new ScancodeMap(Key.B, 0x30),
            new ScancodeMap(Key.N, 0x31),
            new ScancodeMap(Key.M, 0x32),
            new ScancodeMap(Key.Comma, 0x33),
            new ScancodeMap(Key.Period, 0x34),
            new ScancodeMap(Key.Slash, 0x35),
            new ScancodeMap(Key.NumpadDivide, 0x35, true),
            new ScancodeMap(Key.RightShift, 0x36),
            new ScancodeMap(Key.LeftAlt, 0x38),
            new ScancodeMap(Key.RightAlt, 0x38, true),
            new ScancodeMap(Key.Space, 0x39),
            new ScancodeMap(Key.F1, 0x3B),
            new ScancodeMap(Key.F3, 0x3D),
            new ScancodeMap(Key.F4, 0x3E),
            new ScancodeMap(Key.F5, 0x3F),
            new ScancodeMap(Key.F6, 0x40),
            new ScancodeMap(Key.F7, 0x41),
            new ScancodeMap(Key.F8, 0x42),
            new ScancodeMap(Key.F9, 0x43),
            new ScancodeMap(Key.F11, 0x57),
            new ScancodeMap(Key.F12, 0x58),
            new ScancodeMap(Key.CapsLock, 0x3A),
            // <>| zwischen linker Umschalttaste und Z (deutsche Tastatur); das Input System liefert
            // diese Zusatztaste je nach Plattform meist als OEM1, garantiert ist das nicht.
            new ScancodeMap(Key.OEM1, 0x56),
            new ScancodeMap(Key.NumLock, 0x45),
            new ScancodeMap(Key.NumpadMultiply, 0x37),
            new ScancodeMap(Key.NumpadMinus, 0x4A),
            new ScancodeMap(Key.NumpadPlus, 0x4E),
            new ScancodeMap(Key.NumpadPeriod, 0x53),
            new ScancodeMap(Key.Numpad7, 0x47),
            new ScancodeMap(Key.Numpad8, 0x48),
            new ScancodeMap(Key.Numpad9, 0x49),
            new ScancodeMap(Key.Numpad4, 0x4B),
            new ScancodeMap(Key.Numpad5, 0x4C),
            new ScancodeMap(Key.Numpad6, 0x4D),
            new ScancodeMap(Key.Numpad1, 0x4F),
            new ScancodeMap(Key.Numpad2, 0x50),
            new ScancodeMap(Key.Numpad3, 0x51),
            new ScancodeMap(Key.Numpad0, 0x52),
            new ScancodeMap(Key.LeftMeta, 0x5B, true),
            new ScancodeMap(Key.RightMeta, 0x5C, true),
            new ScancodeMap(Key.Home, 0x47, true),
            new ScancodeMap(Key.UpArrow, 0x48, true),
            new ScancodeMap(Key.PageUp, 0x49, true),
            new ScancodeMap(Key.LeftArrow, 0x4B, true),
            new ScancodeMap(Key.RightArrow, 0x4D, true),
            new ScancodeMap(Key.End, 0x4F, true),
            new ScancodeMap(Key.DownArrow, 0x50, true),
            new ScancodeMap(Key.PageDown, 0x51, true),
            new ScancodeMap(Key.Insert, 0x52, true),
            new ScancodeMap(Key.Delete, 0x53, true),
        };
    }
}
