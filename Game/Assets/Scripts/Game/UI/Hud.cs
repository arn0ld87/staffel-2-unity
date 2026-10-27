using System.Linq;
using Staffel2.Core;
using TMPro;
using UnityEngine;

namespace Staffel2.Game.UI
{
    /// <summary>
    /// Anzeige im Raum: Fadenkreuz, Interaktionshinweis, letzte Pager-Meldung, Statuszeile
    /// (Linux, Spuren, Belege) und kurze Meldungen. Alles auch als Text, nicht nur als Farbe.
    /// </summary>
    public sealed class Hud : Bildschirm
    {
        private readonly TextMeshProUGUI _hinweis, _status, _meldung, _pagerText, _speicher, _ziel;
        private readonly RectTransform _pagerKarte;
        private float _meldungBis, _pagerBis, _speicherBis;

        public Hud(GameRoot spiel, Transform leinwand) : base(spiel)
        {
            Wurzel = UiBaukasten.Leer(leinwand, "HUD");
            UiBaukasten.Fuellen(Wurzel);

            var kreuz = UiBaukasten.Text(Wurzel, "·", 34, UiBaukasten.Tinte, true, TextAlignmentOptions.Center);
            UiBaukasten.Anker(kreuz.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-20, -20), new Vector2(20, 20));

            var hinweisFlaeche = UiBaukasten.Flaeche(Wurzel, "Hinweis", UiBaukasten.Panel);
            UiBaukasten.Anker(hinweisFlaeche, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-260, 150), new Vector2(260, 200));
            _hinweis = UiBaukasten.Text(hinweisFlaeche, "", 22, UiBaukasten.Tinte, false, TextAlignmentOptions.Center);
            UiBaukasten.Fuellen(_hinweis.rectTransform, 12, 0, 12, 0);

            var statusFlaeche = UiBaukasten.Flaeche(Wurzel, "Status", UiBaukasten.Panel);
            UiBaukasten.Anker(statusFlaeche, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), new Vector2(700, 64));
            _status = UiBaukasten.Text(statusFlaeche, "", 18, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.MidlineLeft, true);
            UiBaukasten.Fuellen(_status.rectTransform, 14, 0, 14, 0);

            var zielFlaeche = UiBaukasten.Flaeche(Wurzel, "Ziel", UiBaukasten.Panel);
            UiBaukasten.Anker(zielFlaeche, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -132), new Vector2(620, -24));
            _ziel = UiBaukasten.Text(zielFlaeche, "", 19, UiBaukasten.Tinte);
            UiBaukasten.Fuellen(_ziel.rectTransform, 16, 10, 16, 12);

            _pagerKarte = UiBaukasten.Flaeche(Wurzel, "Pager", UiBaukasten.Panel);
            UiBaukasten.Anker(_pagerKarte, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-620, -240), new Vector2(-24, -24));
            var kicker = UiBaukasten.Kicker(_pagerKarte, "Pager · P für Verlauf");
            UiBaukasten.Anker(kicker.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -40), new Vector2(-16, -10));
            _pagerText = UiBaukasten.Text(_pagerKarte, "", 19, UiBaukasten.Tinte);
            UiBaukasten.Fuellen(_pagerText.rectTransform, 16, 12, 16, 46);
            _pagerText.overflowMode = TextOverflowModes.Ellipsis;
            _pagerKarte.gameObject.SetActive(false);

            var meldungFlaeche = UiBaukasten.Flaeche(Wurzel, "Meldung", UiBaukasten.PanelHell);
            UiBaukasten.Anker(meldungFlaeche, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-380, -110), new Vector2(380, -50));
            _meldung = UiBaukasten.Text(meldungFlaeche, "", 21, UiBaukasten.Papier, false, TextAlignmentOptions.Center);
            UiBaukasten.Fuellen(_meldung.rectTransform, 12, 0, 12, 0);

            _speicher = UiBaukasten.Text(Wurzel, "Gespeichert", 16, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.BottomRight, true);
            UiBaukasten.Anker(_speicher.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-300, 20), new Vector2(-24, 50));
        }

        public void SetzeHinweis(string text) => _hinweis.text = text ?? "";

        public void Meldung(string text)
        {
            _meldung.text = text;
            _meldungBis = Time.unscaledTime + 4.5f;
        }

        public void Pager(PagerNachricht n)
        {
            string praefix = n.Art == "erledigt" ? "<color=#8fb36b>✓</color> " : n.Art == "szene" ? "" : "<color=#db4a26>■</color> ";
            _pagerText.text = praefix + string.Join(" ", n.Zeilen);
            _pagerBis = Time.unscaledTime + 9f;
        }

        public void Speicherpunkt() => _speicherBis = Time.unscaledTime + 2f;

        public override void Aktualisiere()
        {
            float t = Time.unscaledTime;
            _hinweis.transform.parent.gameObject.SetActive(Sichtbar && _hinweis.text.Length > 0);
            _meldung.transform.parent.gameObject.SetActive(t < _meldungBis);
            _pagerKarte.gameObject.SetActive(t < _pagerBis);
            _speicher.gameObject.SetActive(t < _speicherBis);

            var s = Spiel.Sitzung;
            var linux = Spiel.Linux;
            string linuxText = linux.Phase == LinuxPhase.Bereit ? "echtes Linux · Alpine · ohne Netzwerk" : linux.Phase == LinuxPhase.Fehler ? "Linux: Fehler" : "Linux startet …";
            if (s == null) { _status.text = linuxText; _ziel.transform.parent.gameObject.SetActive(false); return; }
            int fertig = s.Ermittlung.Spuren.Count(sp => s.AktuellerSchritt(sp) == null);
            _status.text = $"{linuxText}   ·   Spuren {fertig}/{s.Ermittlung.Spuren.Count}   ·   Belege {s.Zustand.BelegeErkannt.Count}";
            var naechste = s.Ermittlung.Spuren.Select(sp => s.AktuellerSchritt(sp)).FirstOrDefault(x => x != null);
            _ziel.transform.parent.gameObject.SetActive(true);
            _ziel.text = s.Zustand.Geloest ? "Fall gelöst." :
                naechste != null ? $"<color=#a8a396>EPISODE {s.Ermittlung.Nr} · {s.Ermittlung.Titel.ToUpperInvariant()}</color>\n{naechste.Title}" :
                "<color=#a8a396>Alle Spuren erledigt.</color>\nErhebe den Vorwurf an der Aktenwand (F).";
            if (s.PruefHinweisText != null) _status.text += "   ·   " + s.PruefHinweisText;
        }
    }
}
