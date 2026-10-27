using System.Linq;
using UnityEngine;

namespace Staffel2.Game.UI
{
    /// <summary>Lautstärken getrennt, Grafik, Auflösung, Maus, Bewegung reduzieren, Terminalschrift.</summary>
    public sealed class EinstellungenPanel
    {
        private readonly RectTransform _wurzel;

        public EinstellungenPanel(GameRoot spiel, Transform parent)
        {
            _wurzel = UiBaukasten.Flaeche(parent, "Einstellungen", UiBaukasten.Abdunkeln);
            UiBaukasten.Fuellen(_wurzel);
            var panel = UiBaukasten.Flaeche(_wurzel, "Panel", UiBaukasten.Panel);
            UiBaukasten.Anker(panel, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(-480, 50), new Vector2(480, -50));
            var inhalt = UiBaukasten.Scrollbereich(panel, "Inhalt", out var scroll);
            UiBaukasten.Fuellen((RectTransform)scroll.transform, 28, 28, 20, 28);
            UiBaukasten.Spalte(inhalt, 8, 4);

            var e = spiel.Einstellungen;
            void Speichern() => spiel.EinstellungenSpeichern();
            UiBaukasten.Text(inhalt, "Einstellungen", 30, UiBaukasten.Papier, true);
            UiBaukasten.Kicker(inhalt, "Ton");
            UiBaukasten.Regler(inhalt, "Gesamt", e.Gesamt, v => { e.Gesamt = v; Speichern(); });
            UiBaukasten.Regler(inhalt, "Musik", e.Musik, v => { e.Musik = v; Speichern(); });
            UiBaukasten.Regler(inhalt, "Effekte", e.Effekte, v => { e.Effekte = v; Speichern(); });
            UiBaukasten.Regler(inhalt, "Umgebung", e.Umgebung, v => { e.Umgebung = v; Speichern(); });
            UiBaukasten.Regler(inhalt, "Sprache", e.Sprache, v => { e.Sprache = v; Speichern(); });
            UiBaukasten.Text(inhalt, "Alle Hinweise erscheinen auch als Text. Sprachausgabe gibt es derzeit nicht.", 16, UiBaukasten.TinteGedimmt);

            UiBaukasten.Kicker(inhalt, "Bild");
            var stufen = QualitySettings.names;
            int q = e.Qualitaet >= 0 ? e.Qualitaet : QualitySettings.GetQualityLevel();
            UiBaukasten.Auswahl(inhalt, "Grafikqualität", stufen, q, i => { e.Qualitaet = i; Speichern(); });
            var aufl = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct().Where(r => r.x >= 1024).ToList();
            if (aufl.Count == 0) aufl.Add(new Vector2Int(Screen.width, Screen.height));
            int ai = Mathf.Max(0, aufl.FindIndex(r => r.x == (e.AufloesungBreite > 0 ? e.AufloesungBreite : Screen.width) && r.y == (e.AufloesungHoehe > 0 ? e.AufloesungHoehe : Screen.height)));
            UiBaukasten.Auswahl(inhalt, "Auflösung", aufl.Select(r => $"{r.x} × {r.y}").ToArray(), ai, i => { e.AufloesungBreite = aufl[i].x; e.AufloesungHoehe = aufl[i].y; Speichern(); });
            UiBaukasten.Schalter(inhalt, "Vollbild", e.Vollbild, v => { e.Vollbild = v; Speichern(); });
            UiBaukasten.Schalter(inhalt, "VSync", e.VSync, v => { e.VSync = v; Speichern(); });

            UiBaukasten.Kicker(inhalt, "Steuerung und Lesbarkeit");
            UiBaukasten.Regler(inhalt, "Mausempfindlichkeit", Mathf.InverseLerp(0.2f, 3f, e.Mausempfindlichkeit), v => { e.Mausempfindlichkeit = Mathf.Lerp(0.2f, 3f, v); Speichern(); });
            UiBaukasten.Schalter(inhalt, "Bewegung reduzieren", e.BewegungReduziert, v => { e.BewegungReduziert = v; Speichern(); });
            var groessen = new[] { 16, 18, 20, 22, 24, 28, 32 };
            UiBaukasten.Auswahl(inhalt, "Terminalschrift", groessen.Select(g => g + " pt").ToArray(), System.Array.IndexOf(groessen, e.TerminalSchrift) is int gi && gi >= 0 ? gi : 3, i => { e.TerminalSchrift = groessen[i]; Speichern(); });

            UiBaukasten.Knopf(inhalt, "Fertig", () => Zeigen(false), true);
            _wurzel.gameObject.SetActive(false);
        }

        public void Zeigen(bool an)
        {
            _wurzel.gameObject.SetActive(an);
            if (an) _wurzel.SetAsLastSibling();
        }
    }
}
