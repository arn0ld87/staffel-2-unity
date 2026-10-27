using System;
using System.Linq;
using Staffel2.Core;
using TMPro;
using UnityEngine;

namespace Staffel2.Game.UI
{
    /// <summary>Fall gelöst: Szene, Beweisstück, Laufzeit, Hilfen und weiter zur nächsten Episode.</summary>
    public sealed class AbschlussBildschirm : Bildschirm
    {
        private readonly RectTransform _inhalt;

        public AbschlussBildschirm(GameRoot spiel, Transform leinwand) : base(spiel)
        {
            Wurzel = UiBaukasten.Flaeche(leinwand, "Abschluss", UiBaukasten.Hex("#141611", 0.94f));
            UiBaukasten.Fuellen(Wurzel);
            var panel = UiBaukasten.Flaeche(Wurzel, "Panel", UiBaukasten.Panel);
            UiBaukasten.Anker(panel, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(-560, 60), new Vector2(560, -60));
            _inhalt = UiBaukasten.Scrollbereich(panel, "Inhalt", out var scroll);
            UiBaukasten.Fuellen((RectTransform)scroll.transform, 32, 32, 24, 32);
            UiBaukasten.Spalte(_inhalt, 12, 4);
            Wurzel.gameObject.SetActive(false);
        }

        public void Setze(Ermittlungssitzung s, AkteEintrag eintrag)
        {
            for (int i = _inhalt.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_inhalt.GetChild(i).gameObject);
            var e = s.Ermittlung;
            UiBaukasten.Kicker(_inhalt, $"Episode {e.Nr} · gelöst");
            UiBaukasten.Text(_inhalt, e.Titel, 40, UiBaukasten.Papier, true);
            foreach (var z in e.Szene ?? new System.Collections.Generic.List<string>())
                UiBaukasten.Text(_inhalt, "<noparse>" + z + "</noparse>", 20, UiBaukasten.Tinte);
            if (e.Beweisstueck != null)
            {
                UiBaukasten.Kicker(_inhalt, "Beweisstück");
                UiBaukasten.Text(_inhalt, $"{e.Beweisstueck.Titel}\n<color=#a8a396>{e.Beweisstueck.Beschreibung}</color>\n<color=#db4a26>FLAG{{{e.Beweisstueck.Code}}}</color>", 21, UiBaukasten.Tinte, false, TextAlignmentOptions.TopLeft);
            }
            var dauer = TimeSpan.FromMilliseconds(eintrag.DurationMs);
            UiBaukasten.Kicker(_inhalt, "Akte");
            UiBaukasten.Text(_inhalt, $"Dauer {(int)dauer.TotalMinutes} min {dauer.Seconds:00} s · {eintrag.HintsUsed} Tipps{(eintrag.WithSolution ? " · mit Lösung" : " · ohne Lösung")} · {eintrag.VorwurfVersuche ?? 0} Vorwurf-Versuche", 19, UiBaukasten.TinteGedimmt, false, TextAlignmentOptions.TopLeft, true);

            var naechste = Spiel.Daten.Episoden.FirstOrDefault(ep => ep.Ermittlung.Nr > e.Nr);
            if (naechste != null)
                UiBaukasten.Knopf(_inhalt, $"Weiter mit Episode {naechste.Ermittlung.Nr}: {naechste.Ermittlung.Titel}", () => Spiel.NeueEpisode(naechste), true, 60);
            else UiBaukasten.Text(_inhalt, "Das war die letzte vorhandene Episode der Staffel.", 20, UiBaukasten.Tinte);
            UiBaukasten.Knopf(_inhalt, "Zum Hauptmenü", () => Spiel.ZumHauptmenue());
        }
    }
}
