using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Staffel2.Game.UI
{
    /// <summary>
    /// Arbeitsplatz-Monitor als gut lesbares Bildschirm-Terminal (und in Episode 2 zusätzlich
    /// mit Reiter „Bildschirm“ für den grafischen 4:3-X11-Desktop): links das echte Linux,
    /// rechts Auftrag und Pager. F10 oder Strg+Q führt zurück in den Raum, F2 öffnet die Fallakte.
    /// </summary>
    public sealed class TerminalOverlay : Bildschirm
    {
        private readonly RectTransform _terminalFlaeche;
        private readonly RectTransform _bildschirmFlaeche;
        private readonly RectTransform _reiterLeiste;
        private readonly Image _reiterTerminalBg, _reiterBildschirmBg;
        private readonly TextMeshProUGUI _reiterTerminalText, _reiterBildschirmText;
        private readonly TerminalAnsicht _ansicht;
        private readonly BildschirmAnsicht _bildschirmAnsicht;
        private readonly TextMeshProUGUI _kopf, _auftrag, _status;
        private readonly PagerListe _pager;
        private readonly Button _pruefen;

        public bool ZeigtBildschirm { get; private set; }
        public BildschirmAnsicht GrafikAnsicht => _bildschirmAnsicht;

        public TerminalOverlay(GameRoot spiel, Transform leinwand) : base(spiel)
        {
            Wurzel = UiBaukasten.Flaeche(leinwand, "Terminal", UiBaukasten.Abdunkeln);
            UiBaukasten.Fuellen(Wurzel);

            // Monitor links.
            var rahmen = UiBaukasten.Flaeche(Wurzel, "Monitor", UiBaukasten.Hex("#0c0d0a"));
            UiBaukasten.Anker(rahmen, new Vector2(0, 0), new Vector2(0.68f, 1), new Vector2(28, 28), new Vector2(-10, -28));
            _kopf = UiBaukasten.Text(rahmen, "", 16, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.MidlineLeft, true);
            UiBaukasten.Anker(_kopf.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -42), new Vector2(-310, -6));

            // Reiter-Umschalter Terminal | Bildschirm (nur sichtbar, wenn Ermittlung.Bildschirm aktiv ist).
            _reiterLeiste = UiBaukasten.Leer(rahmen, "Reiter");
            UiBaukasten.Anker(_reiterLeiste, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-300, -40), new Vector2(-12, -6));
            var tBtnRt = UiBaukasten.Flaeche(_reiterLeiste, "Knopf Reiter Terminal", UiBaukasten.Signal);
            UiBaukasten.Anker(tBtnRt, new Vector2(0, 0), new Vector2(0.48f, 1), Vector2.zero, Vector2.zero);
            _reiterTerminalBg = tBtnRt.GetComponent<Image>();
            _reiterTerminalText = UiBaukasten.Text(tBtnRt, "Terminal", 16, UiBaukasten.Papier, true, TextAlignmentOptions.Center, true);
            UiBaukasten.Fuellen(_reiterTerminalText.rectTransform);
            var tBtn = tBtnRt.gameObject.AddComponent<Button>();
            tBtn.onClick.AddListener(() => WechsleReiter(false));

            var bBtnRt = UiBaukasten.Flaeche(_reiterLeiste, "Knopf Reiter Bildschirm", UiBaukasten.PanelHell);
            UiBaukasten.Anker(bBtnRt, new Vector2(0.52f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            _reiterBildschirmBg = bBtnRt.GetComponent<Image>();
            _reiterBildschirmText = UiBaukasten.Text(bBtnRt, "Bildschirm", 16, UiBaukasten.Tinte, false, TextAlignmentOptions.Center, true);
            UiBaukasten.Fuellen(_reiterBildschirmText.rectTransform);
            var bBtn = bBtnRt.gameObject.AddComponent<Button>();
            bBtn.onClick.AddListener(() => WechsleReiter(true));

            // 1. Terminal-Fläche
            _terminalFlaeche = UiBaukasten.Flaeche(rahmen, "Bildschirm", TerminalAnsicht.HintergrundFarbe);
            UiBaukasten.Fuellen(_terminalFlaeche, 10, 44, 10, 46);
            var text = UiBaukasten.Text(_terminalFlaeche, "", 22, UiBaukasten.Tinte, false, TextAlignmentOptions.TopLeft, true);
            UiBaukasten.Fuellen(text.rectTransform, 8, 4, 4, 6);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Masking;
            text.richText = true;
            text.lineSpacing = 0;

            _ansicht = _terminalFlaeche.gameObject.AddComponent<TerminalAnsicht>();
            _ansicht.Sitzung = spiel.Linux;
            _ansicht.Text = text;
            _ansicht.Flaeche = _terminalFlaeche;
            _ansicht.Schriftgroesse = spiel.Einstellungen.TerminalSchrift;

            // 2. Grafische Bildschirm-Fläche (4:3 SVGA für Episode 2)
            _bildschirmFlaeche = UiBaukasten.Flaeche(rahmen, "GrafikBildschirm", UiBaukasten.Hex("#090a08"));
            UiBaukasten.Fuellen(_bildschirmFlaeche, 10, 44, 10, 46);
            var hinweis = UiBaukasten.Text(_bildschirmFlaeche, "", 15, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.MidlineLeft, true);
            UiBaukasten.Anker(hinweis.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -34), new Vector2(-12, -4));

            var vgaContainer = UiBaukasten.Leer(_bildschirmFlaeche, "VgaRahmen");
            UiBaukasten.Fuellen(vgaContainer, 12, 10, 12, 38);
            var rawGo = new GameObject("VgaBild", typeof(RectTransform), typeof(RawImage), typeof(Outline), typeof(AspectRatioFitter));
            rawGo.transform.SetParent(vgaContainer, false);
            var rawImg = rawGo.GetComponent<RawImage>();
            rawImg.color = UiBaukasten.Hex("#090a08");
            rawImg.raycastTarget = true;
            var outline = rawGo.GetComponent<Outline>();
            outline.effectColor = UiBaukasten.Linie;
            outline.effectDistance = new Vector2(1f, -1f);
            var fitter = rawGo.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 4f / 3f;

            var leerText = UiBaukasten.Text(rawGo.transform, "Bildschirm wird geladen …", 18, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.Center, true);
            UiBaukasten.Fuellen(leerText.rectTransform, 24, 24, 24, 24);

            // Sichtbarer Gast-Mauszeiger auf dem 4:3-Bildschirm
            var zeigerRt = UiBaukasten.Flaeche(rawGo.transform, "Mauszeiger", UiBaukasten.Papier);
            zeigerRt.anchorMin = new Vector2(0.5f, 0.5f);
            zeigerRt.anchorMax = new Vector2(0.5f, 0.5f);
            zeigerRt.pivot = new Vector2(0f, 1f);
            zeigerRt.sizeDelta = new Vector2(10f, 14f);
            zeigerRt.GetComponent<Image>().raycastTarget = false;
            var zeigerRand = zeigerRt.gameObject.AddComponent<Outline>();
            zeigerRand.effectColor = UiBaukasten.Signal;
            zeigerRand.effectDistance = new Vector2(1.5f, -1.5f);

            _bildschirmAnsicht = _bildschirmFlaeche.gameObject.AddComponent<BildschirmAnsicht>();
            _bildschirmAnsicht.Sitzung = spiel.Linux;
            _bildschirmAnsicht.Bild = rawImg;
            _bildschirmAnsicht.Rahmen = outline;
            _bildschirmAnsicht.HinweisText = hinweis;
            _bildschirmAnsicht.LeerText = leerText;
            _bildschirmAnsicht.Zeiger = zeigerRt;
            _bildschirmFlaeche.gameObject.SetActive(false);

            _status = UiBaukasten.Text(rahmen, "", 16, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.MidlineLeft, true);
            UiBaukasten.Anker(_status.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(18, 6), new Vector2(-18, 40));

            // Rechte Spalte: Auftrag, Pager, Knöpfe.
            var spalte = UiBaukasten.Flaeche(Wurzel, "Seitenleiste", UiBaukasten.Panel);
            UiBaukasten.Anker(spalte, new Vector2(0.68f, 0), new Vector2(1, 1), new Vector2(10, 28), new Vector2(-28, -28));
            var portraet = UiBaukasten.Portraet(spalte, spiel.Alex?.Bild);
            float links = 20;
            if (portraet != null)
            {
                UiBaukasten.Anker(portraet.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -176), new Vector2(16 + 128, -16));
                links = 160;
            }
            var kicker = UiBaukasten.Kicker(spalte, "Auftrag · Alexander");
            UiBaukasten.Anker(kicker.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(links, -44), new Vector2(-20, -14));
            _auftrag = UiBaukasten.Text(spalte, "", 20, UiBaukasten.Tinte);
            UiBaukasten.Anker(_auftrag.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(links, -176), new Vector2(-20, -50));

            var pagerKicker = UiBaukasten.Kicker(spalte, "Pager");
            UiBaukasten.Anker(pagerKicker.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -210), new Vector2(-20, -180));
            _pager = new PagerListe(spalte, 18, out var pagerRt);
            UiBaukasten.Anker(pagerRt, new Vector2(0, 0), new Vector2(1, 1), new Vector2(16, 200), new Vector2(-12, -214));

            var knoepfe = UiBaukasten.Leer(spalte, "Knoepfe");
            UiBaukasten.Anker(knoepfe, new Vector2(0, 0), new Vector2(1, 0), new Vector2(16, 16), new Vector2(-16, 186));
            UiBaukasten.Spalte(knoepfe, 8);
            _pruefen = UiBaukasten.Knopf(knoepfe, "Stand prüfen", () => spiel.Sitzung?.StandPruefen());
            UiBaukasten.Knopf(knoepfe, "Fallakte  (F2)", () => spiel.Zeige(Ansicht.Fallakte));
            UiBaukasten.Knopf(knoepfe, "Zurück in den Raum  (F10)", () => spiel.Zeige(Ansicht.Raum), true);
            Wurzel.gameObject.SetActive(false);
        }

        public void SetzeSchrift(int groesse)
        {
            if (_ansicht != null) _ansicht.Schriftgroesse = Mathf.Clamp(groesse, 14, 34);
        }

        public void WechsleReiter(bool bildschirm)
        {
            bool erlaubt = Spiel.Sitzung?.Ermittlung.Bildschirm == true;
            if (bildschirm && !erlaubt) bildschirm = false;
            ZeigtBildschirm = bildschirm;

            _terminalFlaeche.gameObject.SetActive(!bildschirm);
            _bildschirmFlaeche.gameObject.SetActive(bildschirm);
            _ansicht.Fokus = !bildschirm;
            if (bildschirm)
            {
                _bildschirmAnsicht.AktualisiereBild();
                _bildschirmAnsicht.Fokus = true;
                Spiel.Sitzung?.BildschirmErstmalsGeoeffnet();
            }
            else
            {
                _bildschirmAnsicht.Fokus = false;
            }

            _reiterTerminalBg.color = !bildschirm ? UiBaukasten.Signal : UiBaukasten.PanelHell;
            _reiterTerminalText.color = !bildschirm ? UiBaukasten.Papier : UiBaukasten.Tinte;
            _reiterTerminalText.fontStyle = !bildschirm ? FontStyles.Bold : FontStyles.Normal;

            _reiterBildschirmBg.color = bildschirm ? UiBaukasten.Signal : UiBaukasten.PanelHell;
            _reiterBildschirmText.color = bildschirm ? UiBaukasten.Papier : UiBaukasten.Tinte;
            _reiterBildschirmText.fontStyle = bildschirm ? FontStyles.Bold : FontStyles.Normal;

            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            Aktualisiere();
        }

        protected override void Geoeffnet()
        {
            bool erlaubt = Spiel.Sitzung?.Ermittlung.Bildschirm == true;
            if (!erlaubt && ZeigtBildschirm)
            {
                WechsleReiter(false);
            }
            else if (ZeigtBildschirm)
            {
                _ansicht.Fokus = false;
                _bildschirmAnsicht.AktualisiereBild();
                _bildschirmAnsicht.Fokus = true;
            }
            else
            {
                _ansicht.Fokus = true;
            }
            // Ein Knopf darf Enter nicht abfangen: Fokus gehört dem aktiven Bereich.
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }

        public override void Zeigen(bool an)
        {
            base.Zeigen(an);
            if (!an)
            {
                if (_ansicht != null) _ansicht.Fokus = false;
                if (_bildschirmAnsicht != null) _bildschirmAnsicht.Fokus = false;
            }
        }

        public override void Aktualisiere()
        {
            var s = Spiel.Sitzung;
            var linux = Spiel.Linux;
            bool hatBildschirmReiter = s?.Ermittlung.Bildschirm == true;
            if (_reiterLeiste != null && _reiterLeiste.gameObject.activeSelf != hatBildschirmReiter)
                _reiterLeiste.gameObject.SetActive(hatBildschirmReiter);

            string host = s?.Ermittlung.Hostname ?? "rw-srv01";
            _kopf.text = $"azubi@{host}   ·   echtes Linux · Alpine · ohne Netzwerk";
            if (ZeigtBildschirm)
            {
                _status.text = (linux.Phase == LinuxPhase.Bereit ? "Bereit" : linux.Phase.ToString()) +
                               "   ·   Grafischer Bildschirm (1024×768 · Openbox)" +
                               (s?.PruefHinweisText != null ? "   ·   " + s.PruefHinweisText : "");
            }
            else
            {
                _status.text = (linux.Phase == LinuxPhase.Bereit ? "Bereit" : linux.Phase.ToString()) +
                               (_ansicht.ImRueckblick ? "   ·   Rückblick (Mausrad / Umschalt+Bild↓ zum Ende)" : "   ·   Umschalt+Bild↑ Rückblick   ·   Cmd/Strg+Umschalt+V einfügen") +
                               (s?.PruefHinweisText != null ? "   ·   " + s.PruefHinweisText : "");
            }
            if (s == null) { _auftrag.text = ""; return; }
            var naechste = s.Ermittlung.Spuren.Select(sp => s.AktuellerSchritt(sp)).Where(x => x != null).Select(x => "• " + x.Title).ToList();
            _auftrag.text = s.Zustand.Geloest ? "Fall gelöst." : naechste.Count > 0 ? string.Join("\n", naechste) : "Alle Spuren erledigt. Erhebe den Vorwurf in der Fallakte.";
            _pager.Aktualisiere(s);
            _pruefen.interactable = s.Bereit;
        }
    }
}
