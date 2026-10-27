using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Staffel2.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Staffel2.Game.UI
{
    /// <summary>
    /// Fallakte: Spuren mit aktuellem Schritt, Auftrag, gestuften Tipps und Antwortfeld;
    /// Belege, Verdächtige, Einsatzrahmen und Vorwurf. Lösungen erscheinen nur auf Wunsch
    /// (dritte Tippstufe). Lange Inhalte scrollen.
    /// </summary>
    public sealed class FallakteOverlay : Bildschirm
    {
        private readonly RectTransform _links, _rechts;
        private readonly TextMeshProUGUI _titel;
        private readonly Dictionary<string, string> _eingaben = new Dictionary<string, string>();
        private readonly HashSet<string> _auswahlBelege = new HashSet<string>();
        private readonly Dictionary<string, List<string>> _gezeigteTipps = new Dictionary<string, List<string>>();
        private string _verdaechtig;
        private string _signatur;
        private string _vorwurfMeldung;

        public FallakteOverlay(GameRoot spiel, Transform leinwand) : base(spiel)
        {
            Wurzel = UiBaukasten.Flaeche(leinwand, "Fallakte", UiBaukasten.Abdunkeln);
            UiBaukasten.Fuellen(Wurzel);
            var panel = UiBaukasten.Flaeche(Wurzel, "Akte", UiBaukasten.Panel);
            UiBaukasten.Fuellen(panel, 60, 40, 60, 40);
            _titel = UiBaukasten.Text(panel, "", 30, UiBaukasten.Papier, true);
            UiBaukasten.Anker(_titel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -70), new Vector2(-300, -20));
            var zu = UiBaukasten.Knopf(panel, "Schließen  (Esc)", () => spiel.Zeige(spiel.Sitzung != null ? Ansicht.Raum : Ansicht.Menue));
            UiBaukasten.Anker((RectTransform)zu.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-270, -72), new Vector2(-24, -20));

            _links = UiBaukasten.Scrollbereich(panel, "Spuren", out var sl);
            UiBaukasten.Anker((RectTransform)sl.transform, new Vector2(0, 0), new Vector2(0.58f, 1), new Vector2(24, 24), new Vector2(-10, -90));
            UiBaukasten.Spalte(_links, 16, 4);
            _rechts = UiBaukasten.Scrollbereich(panel, "Belege", out var sr);
            UiBaukasten.Anker((RectTransform)sr.transform, new Vector2(0.58f, 0), new Vector2(1, 1), new Vector2(10, 24), new Vector2(-24, -90));
            UiBaukasten.Spalte(_rechts, 12, 4);
            Wurzel.gameObject.SetActive(false);
        }

        protected override void Geoeffnet() => _signatur = null;

        public override void Aktualisiere()
        {
            var s = Spiel.Sitzung;
            if (s == null) { _titel.text = "Keine laufende Ermittlung"; return; }
            string sig = JsonConvert.SerializeObject(s.Zustand) + s.Laufend?.HintsUsed + s.PrueftAntwort + s.PrueftVorwurf + string.Join(",", _auswahlBelege) + _verdaechtig + _vorwurfMeldung;
            if (sig == _signatur) return;
            _signatur = sig;
            _titel.text = $"Fallakte · Episode {s.Ermittlung.Nr}: {s.Ermittlung.Titel}";
            Leeren(_links);
            Leeren(_rechts);
            foreach (var spur in s.Ermittlung.Spuren) BaueSpur(s, spur);
            BaueRechts(s);
        }

        private void BaueSpur(Ermittlungssitzung s, Spur spur)
        {
            var karte = Karte(_links, UiBaukasten.PanelHell);
            var schritt = s.AktuellerSchritt(spur);
            int erledigt = s.Zustand.Erledigt.TryGetValue(spur.Id, out var n) ? n : 0;
            UiBaukasten.Kicker(karte, $"Spur · {spur.Titel} · {Mathf.Min(erledigt, spur.Schritte.Count)}/{spur.Schritte.Count}");
            if (schritt == null)
            {
                UiBaukasten.Text(karte, "<color=#8fb36b>✓</color> Spur abgeschlossen.", 21, UiBaukasten.Tinte);
                return;
            }
            UiBaukasten.Text(karte, Esc(schritt.Title), 23, UiBaukasten.Papier, true);
            foreach (var z in schritt.Pager) UiBaukasten.Text(karte, Esc(z), 19, UiBaukasten.Tinte);

            if (schritt.Art == Schrittart.Ablesen && schritt.Antwort != null)
            {
                UiBaukasten.Text(karte, Esc(schritt.Antwort.Frage) + $"  <color=#a8a396>({Esc(schritt.Antwort.Format)})</color>", 20, UiBaukasten.Papier);
                var feld = UiBaukasten.Eingabe(karte, "Antwort eintragen");
                feld.characterLimit = 64;
                if (_eingaben.TryGetValue(spur.Id, out var alt)) feld.text = alt;
                feld.onValueChanged.AddListener(v => _eingaben[spur.Id] = v);
                feld.onSubmit.AddListener(v => _ = s.AntwortenAsync(spur.Id, v));
                var knopf = UiBaukasten.Knopf(karte, s.PrueftAntwort ? "Wird geprüft …" : "Antwort prüfen", () => _ = s.AntwortenAsync(spur.Id, feld.text), true, 46);
                knopf.interactable = !s.PrueftAntwort;
            }

            // Tipps: Richtung, Befehl, Lösung – nur auf ausdrücklichen Wunsch.
            string key = spur.Id + "/" + schritt.Id;
            if (!_gezeigteTipps.TryGetValue(key, out var tipps)) _gezeigteTipps[key] = tipps = new List<string>();
            int gezeigt = s.GezeigteTipps(spur);
            while (tipps.Count < gezeigt && tipps.Count < schritt.Tipps.Count) tipps.Add(schritt.Tipps[tipps.Count]);
            string[] stufen = { "Richtung", "Befehl", "Lösung" };
            for (int i = 0; i < tipps.Count; i++)
                UiBaukasten.Text(karte, $"<color=#d9a54a>Tipp {i + 1} · {stufen[Mathf.Min(i, 2)]}</color>\n{Esc(tipps[i])}", 18, UiBaukasten.Tinte, false, TextAlignmentOptions.TopLeft, i > 0);
            if (gezeigt < Mathf.Min(3, schritt.Tipps.Count))
            {
                string label = gezeigt == 2 ? "Lösung zeigen (zählt als Hilfe mit Lösung)" : $"Tipp {gezeigt + 1} von 3 zeigen";
                UiBaukasten.Knopf(karte, label, () => { var t = s.TippAufdecken(spur); if (t != null) tipps.Add(t); _signatur = null; }, false, 44);
            }
        }

        private void BaueRechts(Ermittlungssitzung s)
        {
            var e = s.Ermittlung;
            var belegKarte = Karte(_rechts, UiBaukasten.PanelHell);
            UiBaukasten.Kicker(belegKarte, $"Belege · {s.Zustand.BelegeErkannt.Count} erkannt · mindestens {e.Vorwurf.MindestBelege} für den Vorwurf");
            UiBaukasten.Text(belegKarte, "Ein Beleg ist eine Datei in ~/belege/ mit echter Befehlsausgabe.", 17, UiBaukasten.TinteGedimmt);
            _auswahlBelege.RemoveWhere(id => !s.Zustand.BelegeErkannt.Contains(id));
            foreach (var b in e.Vorwurf.Belege)
            {
                bool erkannt = s.Zustand.BelegeErkannt.Contains(b.Id);
                if (!erkannt)
                {
                    UiBaukasten.Text(belegKarte, $"<color=#5c5f55>○</color>  <color=#a8a396>offen</color> · {Esc(b.Beschreibung)}", 18, UiBaukasten.TinteGedimmt);
                    continue;
                }
                bool gewaehlt = _auswahlBelege.Contains(b.Id);
                UiBaukasten.Knopf(belegKarte, (gewaehlt ? "[x]  " : "[ ]  ") + $"~/belege/{b.Id}.txt · {b.Beschreibung}", () =>
                {
                    if (!_auswahlBelege.Remove(b.Id)) _auswahlBelege.Add(b.Id);
                    _signatur = null;
                }, false, 48);
            }

            var verdKarte = Karte(_rechts, UiBaukasten.PanelHell);
            UiBaukasten.Kicker(verdKarte, "Verdächtige");
            foreach (var v in e.Vorwurf.Verdaechtige)
            {
                bool gewaehlt = _verdaechtig == v.Id;
                UiBaukasten.Knopf(verdKarte, (gewaehlt ? "◉  " : "○  ") + $"{v.Name} · {v.Rolle}", () => { _verdaechtig = v.Id; _signatur = null; }, false, 48);
            }

            var vorwurf = Karte(_rechts, UiBaukasten.PanelHell);
            UiBaukasten.Kicker(vorwurf, "Vorwurf");
            if (s.Zustand.Geloest) UiBaukasten.Text(vorwurf, "<color=#8fb36b>✓</color> Fall gelöst.", 20, UiBaukasten.Tinte);
            else
            {
                if (ErmittlungsEngine.IstGesperrt(s.Zustand)) UiBaukasten.Text(vorwurf, "Gesperrt: " + string.Join(" ", e.Vorwurf.Antworten.Gesperrt), 18, UiBaukasten.Signal);
                if (_vorwurfMeldung != null) UiBaukasten.Text(vorwurf, Esc(_vorwurfMeldung), 18, UiBaukasten.Tinte);
                var knopf = UiBaukasten.Knopf(vorwurf, s.PrueftVorwurf ? "Belege werden geprüft …" : "Vorwurf erheben", async () =>
                {
                    if (_verdaechtig == null) { _vorwurfMeldung = "Wähle zuerst eine verdächtige Person."; _signatur = null; return; }
                    var ergebnis = await s.VorwurfAsync(_verdaechtig, _auswahlBelege.ToList());
                    var letzte = s.Pager.LastOrDefault();
                    _vorwurfMeldung = ergebnis == null ? null : letzte != null ? string.Join(" ", letzte.Zeilen) : null;
                    _signatur = null;
                }, true, 54);
                knopf.interactable = !s.PrueftVorwurf;
            }

            var rahmen = Karte(_rechts, UiBaukasten.PanelHell);
            UiBaukasten.Kicker(rahmen, "Einsatzrahmen");
            foreach (var z in e.Einsatzrahmen.Text) UiBaukasten.Text(rahmen, Esc(z), 17, UiBaukasten.TinteGedimmt);
            if (s.Zustand.EinsatzrahmenVerletzt) UiBaukasten.Text(rahmen, "<color=#db4a26>■</color> Rahmen verletzt.", 18, UiBaukasten.Tinte);
        }

        private static RectTransform Karte(RectTransform parent, Color farbe)
        {
            var karte = UiBaukasten.Flaeche(parent, "Karte", farbe);
            UiBaukasten.Spalte(karte, 8, 16);
            karte.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return karte;
        }

        private static void Leeren(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) Object.Destroy(rt.GetChild(i).gameObject);
        }

        private static string Esc(string s) => "<noparse>" + (s ?? "").Replace("</noparse>", "</no parse>") + "</noparse>";
    }
}
