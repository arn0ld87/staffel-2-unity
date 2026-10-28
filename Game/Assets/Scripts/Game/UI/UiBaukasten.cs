using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Staffel2.Game.UI
{
    /// <summary>
    /// Baut uGUI-Elemente im Code: dunkle, warme Flächen, helle Schrift, Orange nur als Akzent.
    /// Alle Bildschirme des Spiels entstehen hierüber, damit keine Szene von Hand gepflegt wird.
    /// </summary>
    public static class UiBaukasten
    {
        public static readonly Color Papier = Hex("#eeeee6");
        public static readonly Color Tinte = Hex("#e6e1d3");
        public static readonly Color TinteGedimmt = Hex("#a8a396");
        public static readonly Color Panel = Hex("#20231e", 0.96f);
        public static readonly Color PanelHell = Hex("#2c3029", 0.98f);
        public static readonly Color Linie = Hex("#3c4037");
        public static readonly Color Signal = Hex("#db4a26");
        public static readonly Color Erfolg = Hex("#8fb36b");
        public static readonly Color Abdunkeln = new Color(0.04f, 0.045f, 0.035f, 0.72f);

        public static TMP_FontAsset Schrift;      // UI-Schrift
        public static TMP_FontAsset Mono;         // Terminal und Kicker

        public static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c.a = alpha;
            return c;
        }

        public static Canvas Leinwand(string name, int sortOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                UnityEngine.Object.DontDestroyOnLoad(es);
            }
            return canvas;
        }

        public static RectTransform Flaeche(Transform parent, string name, Color farbe)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = farbe;
            img.raycastTarget = farbe.a > 0.01f;
            return (RectTransform)go.transform;
        }

        public static RectTransform Leer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Fuellen(RectTransform rt, float links = 0, float unten = 0, float rechts = 0, float oben = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(links, unten);
            rt.offsetMax = new Vector2(-rechts, -oben);
        }

        public static void Anker(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float groesse, Color farbe, bool fett = false, TextAlignmentOptions ausrichtung = TextAlignmentOptions.TopLeft, bool mono = false)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (mono && Mono != null) t.font = Mono;
            else if (Schrift != null) t.font = Schrift;
            t.text = text;
            t.fontSize = groesse;
            t.color = farbe;
            t.fontStyle = fett ? FontStyles.Bold : FontStyles.Normal;
            t.alignment = ausrichtung;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Kicker im Stil der Marke: Mono, Versalien, mit quadratischem Marker.</summary>
        public static TextMeshProUGUI Kicker(Transform parent, string text)
        {
            var t = Text(parent, "<color=#db4a26>■</color>  " + text.ToUpperInvariant(), 15, TinteGedimmt, false, TextAlignmentOptions.TopLeft, true);
            t.characterSpacing = 4;
            return t;
        }

        public static Button Knopf(Transform parent, string beschriftung, UnityAction klick, bool primaer = false, float hoehe = 52)
        {
            var rt = Flaeche(parent, "Knopf " + beschriftung, primaer ? Signal : PanelHell);
            var btn = rt.gameObject.AddComponent<Button>();
            var farben = btn.colors;
            farben.normalColor = Color.white;
            farben.highlightedColor = new Color(1.18f, 1.18f, 1.18f);
            farben.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            farben.selectedColor = new Color(1.12f, 1.12f, 1.12f);
            farben.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            btn.colors = farben;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = hoehe;
            le.preferredHeight = hoehe;
            var t = Text(rt, beschriftung, 22, primaer ? Papier : Tinte, primaer, TextAlignmentOptions.MidlineLeft);
            Fuellen(t.rectTransform, 20, 0, 16, 0);
            if (klick != null) btn.onClick.AddListener(klick);
            btn.onClick.AddListener(() => Klick?.Invoke());
            return btn;
        }

        /// <summary>Für UI-Klickgeräusche; die Audioschicht hängt sich hier an.</summary>
        public static event Action Klick;

        /// <summary>Bild der Alex-Büste (RenderTexture) mit dünnem Rahmen; null, wenn es kein Bild gibt.</summary>
        public static RawImage Portraet(Transform parent, Texture bild)
        {
            if (bild == null) return null;
            var go = new GameObject("Portraet", typeof(RectTransform), typeof(RawImage), typeof(Outline));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<RawImage>();
            img.texture = bild;
            img.raycastTarget = false;
            var rand = go.GetComponent<Outline>();
            rand.effectColor = Linie;
            rand.effectDistance = new Vector2(1, -1);
            return img;
        }

        public static VerticalLayoutGroup Spalte(RectTransform rt, float abstand = 10, int rand = 0)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = abstand;
            v.padding = new RectOffset(rand, rand, rand, rand);
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            return v;
        }

        /// <summary>Senkrecht scrollender Bereich; Inhalt wächst mit seinen Kindern.</summary>
        public static RectTransform Scrollbereich(Transform parent, string name, out ScrollRect scroll)
        {
            var root = Flaeche(parent, name, new Color(0, 0, 0, 0.001f));
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            var viewport = Flaeche(root, "Viewport", new Color(1, 1, 1, 0.003f));
            Fuellen(viewport, 0, 0, 14, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var inhalt = Leer(viewport, "Inhalt");
            inhalt.anchorMin = new Vector2(0, 1);
            inhalt.anchorMax = new Vector2(1, 1);
            inhalt.pivot = new Vector2(0.5f, 1);
            inhalt.offsetMin = Vector2.zero;
            inhalt.offsetMax = Vector2.zero;
            var fitter = inhalt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = inhalt;

            var leiste = Flaeche(root, "Leiste", Linie);
            Anker(leiste, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-10, 0), new Vector2(0, 0));
            var griff = Flaeche(leiste, "Griff", TinteGedimmt);
            Fuellen(griff);
            var bar = leiste.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = griff;
            bar.targetGraphic = griff.GetComponent<Image>();
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return inhalt;
        }

        public static Slider Regler(Transform parent, string beschriftung, float wert, UnityAction<float> geaendert)
        {
            var zeile = Leer(parent, "Regler " + beschriftung);
            var le = zeile.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 46;
            var t = Text(zeile, beschriftung, 20, Tinte, false, TextAlignmentOptions.MidlineLeft);
            Anker(t.rectTransform, new Vector2(0, 0), new Vector2(0.42f, 1), Vector2.zero, Vector2.zero);

            var bahn = Flaeche(zeile, "Bahn", Linie);
            Anker(bahn, new Vector2(0.45f, 0.42f), new Vector2(1, 0.58f), Vector2.zero, Vector2.zero);
            var fuell = Flaeche(bahn, "Fuellung", Signal);
            Fuellen(fuell);
            var griffBereich = Leer(bahn, "Griffbereich");
            Fuellen(griffBereich);
            var griff = Flaeche(griffBereich, "Griff", Papier);
            griff.sizeDelta = new Vector2(18, 26);

            var slider = zeile.gameObject.AddComponent<Slider>();
            slider.fillRect = fuell;
            slider.handleRect = griff;
            slider.targetGraphic = griff.GetComponent<Image>();
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = wert;
            // Wert auch als Zahl, damit die Einstellung nicht nur über Farbe/Länge lesbar ist.
            var zahl = Text(zeile, Mathf.RoundToInt(wert * 100) + " %", 18, TinteGedimmt, false, TextAlignmentOptions.MidlineRight, true);
            Anker(zahl.rectTransform, new Vector2(0.3f, 0), new Vector2(0.44f, 1), Vector2.zero, Vector2.zero);
            slider.onValueChanged.AddListener(v => zahl.text = Mathf.RoundToInt(v * 100) + " %");
            if (geaendert != null) slider.onValueChanged.AddListener(geaendert);
            return slider;
        }

        public static TMP_InputField Eingabe(Transform parent, string platzhalter, float hoehe = 50)
        {
            var rt = Flaeche(parent, "Eingabe", Hex("#141611"));
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = hoehe;
            var bereich = Leer(rt, "Textbereich");
            Fuellen(bereich, 14, 6, 14, 6);
            bereich.gameObject.AddComponent<RectMask2D>();
            var platz = Text(bereich, platzhalter, 20, TinteGedimmt, false, TextAlignmentOptions.MidlineLeft, true);
            Fuellen(platz.rectTransform);
            platz.fontStyle = FontStyles.Italic;
            var text = Text(bereich, "", 20, Tinte, false, TextAlignmentOptions.MidlineLeft, true);
            Fuellen(text.rectTransform);
            var feld = rt.gameObject.AddComponent<TMP_InputField>();
            feld.textViewport = bereich;
            feld.textComponent = text;
            feld.placeholder = platz;
            feld.caretColor = Signal;
            feld.customCaretColor = true;
            feld.lineType = TMP_InputField.LineType.SingleLine;
            return feld;
        }

        public static TMP_Dropdown Auswahl(Transform parent, string beschriftung, string[] optionen, int wert, UnityAction<int> geaendert)
        {
            var zeile = Leer(parent, "Auswahl " + beschriftung);
            zeile.gameObject.AddComponent<LayoutElement>().preferredHeight = 46;
            var t = Text(zeile, beschriftung, 20, Tinte, false, TextAlignmentOptions.MidlineLeft);
            Anker(t.rectTransform, new Vector2(0, 0), new Vector2(0.42f, 1), Vector2.zero, Vector2.zero);
            // Statt eines Dropdowns mit Template: Umschaltknopf, robust und tastaturbedienbar.
            var knopf = Flaeche(zeile, "Wert", PanelHell);
            Anker(knopf, new Vector2(0.45f, 0.05f), new Vector2(1, 0.95f), Vector2.zero, Vector2.zero);
            var label = Text(knopf, "", 19, Tinte, false, TextAlignmentOptions.MidlineLeft, true);
            Fuellen(label.rectTransform, 14, 0, 14, 0);
            int aktuell = Mathf.Clamp(wert, 0, Mathf.Max(0, optionen.Length - 1));
            label.text = optionen.Length > 0 ? "‹  " + optionen[aktuell] + "  ›" : "–";
            var btn = knopf.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                if (optionen.Length == 0) return;
                aktuell = (aktuell + 1) % optionen.Length;
                label.text = "‹  " + optionen[aktuell] + "  ›";
                geaendert?.Invoke(aktuell);
                Klick?.Invoke();
            });
            return null;
        }

        public static Toggle Schalter(Transform parent, string beschriftung, bool wert, UnityAction<bool> geaendert)
        {
            var zeile = Leer(parent, "Schalter " + beschriftung);
            zeile.gameObject.AddComponent<LayoutElement>().preferredHeight = 46;
            var t = Text(zeile, beschriftung, 20, Tinte, false, TextAlignmentOptions.MidlineLeft);
            Anker(t.rectTransform, new Vector2(0, 0), new Vector2(0.42f, 1), Vector2.zero, Vector2.zero);
            var box = Flaeche(zeile, "Box", PanelHell);
            Anker(box, new Vector2(0.45f, 0.1f), new Vector2(0.45f, 0.9f), Vector2.zero, new Vector2(110, 0));
            var haken = Flaeche(box, "An", Signal);
            Fuellen(haken, 4, 4, 4, 4);
            // Zustand zusätzlich als Text, nicht nur als Farbe.
            var label = Text(box, wert ? "AN" : "AUS", 17, Papier, true, TextAlignmentOptions.Center, true);
            Fuellen(label.rectTransform);
            var toggle = zeile.gameObject.AddComponent<Toggle>();
            toggle.graphic = haken.GetComponent<Image>();
            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.isOn = wert;
            toggle.onValueChanged.AddListener(v => { label.text = v ? "AN" : "AUS"; Klick?.Invoke(); });
            if (geaendert != null) toggle.onValueChanged.AddListener(geaendert);
            return toggle;
        }
    }
}
