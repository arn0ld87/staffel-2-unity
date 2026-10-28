using UnityEngine;

namespace Staffel2.Game.UI
{
    /// <summary>Pause: Weiter, Speichern, Befehlsübersicht, Einstellungen, Hauptmenü, Beenden.</summary>
    public sealed class PauseMenue : Bildschirm
    {
        private readonly EinstellungenPanel _einstellungen;
        private readonly BefehlsUebersicht _befehle;

        public PauseMenue(GameRoot spiel, Transform leinwand) : base(spiel)
        {
            Wurzel = UiBaukasten.Flaeche(leinwand, "Pause", UiBaukasten.Abdunkeln);
            UiBaukasten.Fuellen(Wurzel);
            var panel = UiBaukasten.Flaeche(Wurzel, "Panel", UiBaukasten.Panel);
            UiBaukasten.Anker(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-260, -280), new Vector2(260, 280));
            UiBaukasten.Spalte(panel, 10, 28);
            UiBaukasten.Text(panel, "Pause", 34, UiBaukasten.Papier, true);
            UiBaukasten.Knopf(panel, "Weiter  (Esc)", () => spiel.Zeige(Ansicht.Raum), true);
            UiBaukasten.Knopf(panel, "Speichern", () => _ = spiel.SpeichernAsync(true));
            UiBaukasten.Knopf(panel, "Befehlsübersicht", () => _befehle.Zeigen(true));
            UiBaukasten.Knopf(panel, "Einstellungen", () => _einstellungen.Zeigen(true));
            UiBaukasten.Knopf(panel, "Speichern und Hauptmenü", () => spiel.ZumHauptmenue());
            UiBaukasten.Knopf(panel, "Speichern und beenden", () => spiel.Beenden());
            _einstellungen = new EinstellungenPanel(spiel, Wurzel);
            _befehle = new BefehlsUebersicht(spiel, Wurzel);
            Wurzel.gameObject.SetActive(false);
        }

        public override void Aktualisiere() => _befehle.Aktualisiere();
    }
}
