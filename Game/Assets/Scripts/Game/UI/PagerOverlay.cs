using TMPro;
using UnityEngine;

namespace Staffel2.Game.UI
{
    /// <summary>Vollständiger Pager-Verlauf zum Nachlesen, zuverlässig scrollbar.</summary>
    public sealed class PagerOverlay : Bildschirm
    {
        private readonly PagerListe _liste;

        public PagerOverlay(GameRoot spiel, Transform leinwand) : base(spiel)
        {
            Wurzel = UiBaukasten.Flaeche(leinwand, "Pager", UiBaukasten.Abdunkeln);
            UiBaukasten.Fuellen(Wurzel);
            var panel = UiBaukasten.Flaeche(Wurzel, "Verlauf", UiBaukasten.Panel);
            UiBaukasten.Anker(panel, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(-520, 50), new Vector2(520, -50));
            var t = UiBaukasten.Text(panel, "Pager · Verlauf", 28, UiBaukasten.Papier, true);
            UiBaukasten.Anker(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -64), new Vector2(-280, -18));
            var zu = UiBaukasten.Knopf(panel, "Schließen  (Esc)", () => spiel.Zeige(spiel.Sitzung != null ? Ansicht.Raum : Ansicht.Menue));
            UiBaukasten.Anker((RectTransform)zu.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-260, -68), new Vector2(-20, -18));
            _liste = new PagerListe(panel, 21, out var rt);
            UiBaukasten.Anker(rt, Vector2.zero, Vector2.one, new Vector2(20, 20), new Vector2(-14, -84));
            Wurzel.gameObject.SetActive(false);
        }

        public override void Aktualisiere() => _liste.Aktualisiere(Spiel.Sitzung);
    }
}
