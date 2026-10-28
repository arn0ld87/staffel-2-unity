using System.Collections.Generic;
using System.Linq;
using Staffel2.Core;
using TMPro;
using UnityEngine;

namespace Staffel2.Game.UI
{
    /// <summary>
    /// Durchsuchbare Befehlsübersicht aus dem Glossar der Webversion: Syntax, Erklärung,
    /// kurzes Beispiel und in welchen Episoden der Befehl im Werkzeugkasten steht. Keine Lösungen.
    /// </summary>
    public sealed class BefehlsUebersicht
    {
        private readonly RectTransform _wurzel, _liste;
        private readonly TMP_InputField _suche;
        private readonly GameRoot _spiel;
        private string _letzteSuche;

        public BefehlsUebersicht(GameRoot spiel, Transform parent)
        {
            _spiel = spiel;
            _wurzel = UiBaukasten.Flaeche(parent, "Befehle", UiBaukasten.Abdunkeln);
            UiBaukasten.Fuellen(_wurzel);
            var panel = UiBaukasten.Flaeche(_wurzel, "Panel", UiBaukasten.Panel);
            UiBaukasten.Anker(panel, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(-620, 50), new Vector2(620, -50));
            var t = UiBaukasten.Text(panel, "Befehlsübersicht", 30, UiBaukasten.Papier, true);
            UiBaukasten.Anker(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -70), new Vector2(-300, -20));
            var zu = UiBaukasten.Knopf(panel, "Schließen", () => Zeigen(false));
            UiBaukasten.Anker((RectTransform)zu.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-220, -72), new Vector2(-24, -20));
            var suchZeile = UiBaukasten.Leer(panel, "Suche");
            UiBaukasten.Anker(suchZeile, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -140), new Vector2(-28, -88));
            UiBaukasten.Spalte(suchZeile);
            _suche = UiBaukasten.Eingabe(suchZeile, "Befehl oder Stichwort suchen, z. B. grep, Rechte, Prozess");
            _liste = UiBaukasten.Scrollbereich(panel, "Liste", out var scroll);
            UiBaukasten.Anker((RectTransform)scroll.transform, Vector2.zero, Vector2.one, new Vector2(24, 24), new Vector2(-20, -156));
            UiBaukasten.Spalte(_liste, 10, 4);
            _wurzel.gameObject.SetActive(false);
        }

        public void Zeigen(bool an)
        {
            _wurzel.gameObject.SetActive(an);
            if (an) { _wurzel.SetAsLastSibling(); _letzteSuche = null; _suche.ActivateInputField(); }
        }

        public void Aktualisiere()
        {
            if (!_wurzel.gameObject.activeSelf) return;
            string q = (_suche.text ?? "").Trim().ToLowerInvariant();
            if (q == _letzteSuche) return;
            _letzteSuche = q;
            for (int i = _liste.childCount - 1; i >= 0; i--) Object.Destroy(_liste.GetChild(i).gameObject);

            // Episodenbezug: in welchen Werkzeugkästen steht der Befehl?
            var bezug = new Dictionary<string, List<int>>();
            foreach (var ep in _spiel.Daten.Episoden)
                foreach (var w in ep.Eintrag?.Werkzeuge ?? new List<Werkzeug>())
                {
                    if (!bezug.TryGetValue(w.Befehl, out var l)) bezug[w.Befehl] = l = new List<int>();
                    if (!l.Contains(ep.Ermittlung.Nr)) l.Add(ep.Ermittlung.Nr);
                }

            var eintraege = _spiel.Daten.Glossar.Values
                .Where(w => q.Length == 0 || (w.Befehl + " " + w.Erklaerung + " " + w.Beispiel).ToLowerInvariant().Contains(q))
                .OrderByDescending(w => bezug.ContainsKey(w.Befehl)).ThenBy(w => w.Befehl).Take(80).ToList();
            if (eintraege.Count == 0) UiBaukasten.Text(_liste, "Nichts gefunden.", 20, UiBaukasten.TinteGedimmt);
            foreach (var w in eintraege)
            {
                var karte = UiBaukasten.Flaeche(_liste, "Befehl", UiBaukasten.PanelHell);
                UiBaukasten.Spalte(karte, 4, 14);
                karte.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
                string episoden = bezug.TryGetValue(w.Befehl, out var nrs) ? "   <color=#a8a396>Episode " + string.Join(", ", nrs.OrderBy(n => n)) + "</color>" : "";
                UiBaukasten.Text(karte, $"<color=#db4a26>$</color> <noparse>{w.Befehl}</noparse>{episoden}", 22, UiBaukasten.Papier, true, TextAlignmentOptions.TopLeft, true);
                if (!string.IsNullOrEmpty(w.Erklaerung)) UiBaukasten.Text(karte, "<noparse>" + w.Erklaerung + "</noparse>", 18, UiBaukasten.Tinte);
                if (!string.IsNullOrEmpty(w.Beispiel)) UiBaukasten.Text(karte, "Beispiel: <noparse>" + w.Beispiel + "</noparse>", 17, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.TopLeft, true);
            }
        }
    }
}
