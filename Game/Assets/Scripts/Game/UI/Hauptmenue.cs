using System;
using Staffel2.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Staffel2.Game.UI
{
    /// <summary>Hauptmenü: Fortsetzen, Episodenwahl mit Fortschritt, Einstellungen, Zurücksetzen (bestätigt), Beenden.</summary>
    public sealed class Hauptmenue : Bildschirm
    {
        private readonly RectTransform _knoepfe, _episoden, _bestaetigen;
        private readonly EinstellungenPanel _einstellungen;
        private readonly BefehlsUebersicht _befehle;
        private string _stand;

        public Hauptmenue(GameRoot spiel, Transform leinwand) : base(spiel)
        {
            Wurzel = UiBaukasten.Flaeche(leinwand, "Hauptmenue", UiBaukasten.Hex("#141611", 0.86f));
            UiBaukasten.Fuellen(Wurzel);
            var kicker = UiBaukasten.Kicker(Wurzel, "alex-schneider.dev · Terminal-Missionen");
            UiBaukasten.Anker(kicker.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(120, -170), new Vector2(900, -140));
            var titel = UiBaukasten.Text(Wurzel, "Ein Linux-Krimi\n<size=60%><color=#db4a26>Staffel 2</color></size>", 78, UiBaukasten.Papier, true);
            UiBaukasten.Anker(titel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(116, -390), new Vector2(1000, -175));

            _knoepfe = UiBaukasten.Leer(Wurzel, "Knoepfe");
            UiBaukasten.Anker(_knoepfe, new Vector2(0, 1), new Vector2(0, 1), new Vector2(120, -900), new Vector2(560, -430));
            UiBaukasten.Spalte(_knoepfe, 10);

            _episoden = UiBaukasten.Flaeche(Wurzel, "Episoden", UiBaukasten.Panel);
            UiBaukasten.Anker(_episoden, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-860, -330), new Vector2(-120, 330));
            UiBaukasten.Spalte(_episoden, 10, 24);

            _bestaetigen = UiBaukasten.Flaeche(Wurzel, "Bestaetigen", UiBaukasten.PanelHell);
            UiBaukasten.Anker(_bestaetigen, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380, -150), new Vector2(380, 150));
            UiBaukasten.Spalte(_bestaetigen, 14, 28);
            UiBaukasten.Text(_bestaetigen, "Fortschritt wirklich löschen?", 28, UiBaukasten.Papier, true);
            UiBaukasten.Text(_bestaetigen, "Gelöste Episoden und die laufende Ermittlung werden entfernt. Einstellungen bleiben.", 19, UiBaukasten.Tinte);
            UiBaukasten.Knopf(_bestaetigen, "Endgültig löschen", () => { spiel.FortschrittZuruecksetzen(); _bestaetigen.gameObject.SetActive(false); _stand = null; }, true);
            UiBaukasten.Knopf(_bestaetigen, "Abbrechen", () => _bestaetigen.gameObject.SetActive(false));
            _bestaetigen.gameObject.SetActive(false);

            _einstellungen = new EinstellungenPanel(spiel, Wurzel);
            _befehle = new BefehlsUebersicht(spiel, Wurzel);
            Wurzel.gameObject.SetActive(false);
        }

        protected override void Geoeffnet() => _stand = null;

        public override void Aktualisiere()
        {
            _befehle.Aktualisiere();
            // Nur neu bauen, wenn sich der Spielstand geändert hat.
            string stand = Spiel.KannFortsetzen + "|" + string.Join(",", Spiel.Stand.Akte.Keys);
            if (stand == _stand) return;
            _stand = stand;
            Leeren(_knoepfe);
            Leeren(_episoden);

            if (Spiel.KannFortsetzen)
            {
                var ep = Spiel.Daten.EpisodeMitId(Spiel.Stand.Laufend.EpisodeId);
                UiBaukasten.Knopf(_knoepfe, $"Fortsetzen · Episode {ep.Ermittlung.Nr}", () => Spiel.Fortsetzen(), true, 60);
            }
            UiBaukasten.Knopf(_knoepfe, "Befehlsübersicht", () => _befehle.Zeigen(true));
            UiBaukasten.Knopf(_knoepfe, "Einstellungen", () => _einstellungen.Zeigen(true));
            UiBaukasten.Knopf(_knoepfe, "Fortschritt löschen …", () => _bestaetigen.gameObject.SetActive(true));
            UiBaukasten.Knopf(_knoepfe, "Beenden", () => Spiel.Beenden());

            UiBaukasten.Kicker(_episoden, "Episoden · Neue Ermittlung starten");
            foreach (var ep in Spiel.Daten.Episoden)
            {
                var e = ep.Ermittlung;
                bool geloest = Spiel.Stand.Akte.TryGetValue(e.Id, out var akte);
                string info = geloest ? $"  <color=#8fb36b>✓ gelöst</color> <color=#a8a396>· {Dauer(akte.DurationMs)}{(akte.WithSolution ? " · mit Lösung" : "")}</color>" : "";
                string bild = e.Bildschirm ? "  <color=#a8a396>· mit Bildschirm</color>" : "";
                var episode = ep;
                UiBaukasten.Knopf(_episoden, $"{e.Nr}  {e.Titel}{info}{bild}", () => Spiel.NeueEpisode(episode), false, 62);
            }
            UiBaukasten.Text(_episoden, "Eine neue Ermittlung ersetzt eine laufende. Speichern: Pause-Menü (Esc) oder automatisch nach jedem Fortschritt.", 16, UiBaukasten.TinteGedimmt);
        }

        private static string Dauer(long ms)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{t.Minutes} min {t.Seconds:00} s";
        }

        private static void Leeren(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(rt.GetChild(i).gameObject);
        }
    }
}
