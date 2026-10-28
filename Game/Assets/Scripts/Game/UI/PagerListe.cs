using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Staffel2.Game.UI
{
    /// <summary>
    /// Scrollbarer Pager-Verlauf. Baut nur neue Nachrichten an und springt ans Ende, solange
    /// man nicht selbst nach oben gescrollt hat. Lange Verläufe bleiben so zuverlässig scrollbar.
    /// </summary>
    public sealed class PagerListe
    {
        private readonly RectTransform _inhalt;
        private readonly ScrollRect _scroll;
        private readonly float _schrift;
        private readonly List<int> _ids = new List<int>();
        private Ermittlungssitzung _sitzung;

        public PagerListe(Transform parent, float schrift, out RectTransform wurzel)
        {
            _schrift = schrift;
            _inhalt = UiBaukasten.Scrollbereich(parent, "Pager-Verlauf", out _scroll);
            UiBaukasten.Spalte(_inhalt, 12, 4);
            wurzel = (RectTransform)_scroll.transform;
        }

        public void Aktualisiere(Ermittlungssitzung sitzung)
        {
            if (sitzung != _sitzung)
            {
                _sitzung = sitzung;
                _ids.Clear();
                for (int i = _inhalt.childCount - 1; i >= 0; i--) Object.Destroy(_inhalt.GetChild(i).gameObject);
            }
            if (sitzung == null) return;
            if (sitzung.Pager.Count < _ids.Count)
            {
                _sitzung = null;
                Aktualisiere(sitzung);
                return;
            }
            bool amEnde = _scroll.verticalNormalizedPosition < 0.02f || _ids.Count == 0;
            bool neu = false;
            for (int i = _ids.Count; i < sitzung.Pager.Count; i++)
            {
                Baue(sitzung.Pager[i]);
                _ids.Add(sitzung.Pager[i].Id);
                neu = true;
            }
            if (neu && amEnde)
            {
                Canvas.ForceUpdateCanvases();
                _scroll.verticalNormalizedPosition = 0;
            }
        }

        private void Baue(PagerNachricht n)
        {
            var farbe = n.Art == "erledigt" ? UiBaukasten.Hex("#26301f", 0.95f) : n.Art == "szene" ? UiBaukasten.Hex("#302820", 0.95f) : UiBaukasten.PanelHell;
            var karte = UiBaukasten.Flaeche(_inhalt, "Nachricht " + n.Id, farbe);
            var spalte = UiBaukasten.Spalte(karte, 4, 12);
            spalte.childControlHeight = true;
            var fitter = karte.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            string art = n.Art == "erledigt" ? "Erledigt" : n.Art == "szene" ? "Alexander" : "Auftrag";
            string marker = n.Art == "erledigt" ? "<color=#8fb36b>✓</color>" : "<color=#db4a26>■</color>";
            UiBaukasten.Text(karte, marker + "  " + art.ToUpperInvariant(), _schrift - 5, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.TopLeft, true);
            foreach (var z in n.Zeilen)
                UiBaukasten.Text(karte, Escape(z), _schrift, UiBaukasten.Tinte);
        }

        private static string Escape(string s) => "<noparse>" + s.Replace("</noparse>", "</no parse>") + "</noparse>";
    }
}
