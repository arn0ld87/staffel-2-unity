using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Staffel2.Game.UI
{
    /// <summary>
    /// Arbeitsplatz-Monitor als gut lesbares Bildschirm-Terminal: links das echte Linux,
    /// rechts Auftrag und Pager. F10 oder Strg+Q führt zurück in den Raum, F2 öffnet die Fallakte.
    /// </summary>
    public sealed class TerminalOverlay : Bildschirm
    {
        private readonly TerminalAnsicht _ansicht;
        private readonly TextMeshProUGUI _kopf, _auftrag, _status;
        private readonly PagerListe _pager;
        private readonly Button _pruefen;

        public TerminalOverlay(GameRoot spiel, Transform leinwand) : base(spiel)
        {
            Wurzel = UiBaukasten.Flaeche(leinwand, "Terminal", UiBaukasten.Abdunkeln);
            UiBaukasten.Fuellen(Wurzel);

            // Terminal links.
            var rahmen = UiBaukasten.Flaeche(Wurzel, "Monitor", UiBaukasten.Hex("#0c0d0a"));
            UiBaukasten.Anker(rahmen, new Vector2(0, 0), new Vector2(0.68f, 1), new Vector2(28, 28), new Vector2(-10, -28));
            _kopf = UiBaukasten.Text(rahmen, "", 16, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.MidlineLeft, true);
            UiBaukasten.Anker(_kopf.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -40), new Vector2(-18, -8));
            var flaeche = UiBaukasten.Flaeche(rahmen, "Bildschirm", TerminalAnsicht.HintergrundFarbe);
            UiBaukasten.Fuellen(flaeche, 10, 44, 10, 46);
            var text = UiBaukasten.Text(flaeche, "", 22, UiBaukasten.Tinte, false, TextAlignmentOptions.TopLeft, true);
            UiBaukasten.Fuellen(text.rectTransform, 8, 4, 4, 6);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Masking;
            text.richText = true;
            text.lineSpacing = 0;
            _status = UiBaukasten.Text(rahmen, "", 16, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.MidlineLeft, true);
            UiBaukasten.Anker(_status.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(18, 6), new Vector2(-18, 40));

            _ansicht = flaeche.gameObject.AddComponent<TerminalAnsicht>();
            _ansicht.Sitzung = spiel.Linux;
            _ansicht.Text = text;
            _ansicht.Flaeche = flaeche;
            _ansicht.Schriftgroesse = spiel.Einstellungen.TerminalSchrift;

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

        protected override void Geoeffnet()
        {
            _ansicht.Fokus = true;
            // Ein Knopf darf Enter nicht abfangen: Fokus gehört dem Terminal.
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }

        public override void Zeigen(bool an)
        {
            base.Zeigen(an);
            if (!an && _ansicht != null) _ansicht.Fokus = false;
        }

        public override void Aktualisiere()
        {
            var s = Spiel.Sitzung;
            var linux = Spiel.Linux;
            string host = s?.Ermittlung.Hostname ?? "rw-srv01";
            _kopf.text = $"azubi@{host}   ·   echtes Linux · Alpine · ohne Netzwerk";
            _status.text = (linux.Phase == LinuxPhase.Bereit ? "Bereit" : linux.Phase.ToString()) +
                           (_ansicht.ImRueckblick ? "   ·   Rückblick (Mausrad / Umschalt+Bild↓ zum Ende)" : "   ·   Umschalt+Bild↑ Rückblick   ·   Cmd/Strg+Umschalt+V einfügen") +
                           (s?.PruefHinweisText != null ? "   ·   " + s.PruefHinweisText : "");
            if (s == null) { _auftrag.text = ""; return; }
            var naechste = s.Ermittlung.Spuren.Select(sp => s.AktuellerSchritt(sp)).Where(x => x != null).Select(x => "• " + x.Title).ToList();
            _auftrag.text = s.Zustand.Geloest ? "Fall gelöst." : naechste.Count > 0 ? string.Join("\n", naechste) : "Alle Spuren erledigt. Erhebe den Vorwurf in der Fallakte.";
            _pager.Aktualisiere(s);
            _pruefen.interactable = s.Bereit;
        }
    }
}
