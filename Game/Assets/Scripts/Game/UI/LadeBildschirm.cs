using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Staffel2.Game.UI
{
    /// <summary>Während Linux startet und das Setup läuft; bei Fehler mit Ausweg.</summary>
    public sealed class LadeBildschirm : Bildschirm
    {
        private readonly TextMeshProUGUI _text, _punkte;
        private readonly RectTransform _fehlerKnoepfe;

        public LadeBildschirm(GameRoot spiel, Transform leinwand) : base(spiel)
        {
            Wurzel = UiBaukasten.Flaeche(leinwand, "Laden", UiBaukasten.Hex("#141611", 0.97f));
            UiBaukasten.Fuellen(Wurzel);
            var k = UiBaukasten.Kicker(Wurzel, "Ein Linux-Krimi · Staffel 2");
            UiBaukasten.Anker(k.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, 90), new Vector2(400, 120));
            _text = UiBaukasten.Text(Wurzel, "", 30, UiBaukasten.Papier, false, TextAlignmentOptions.Top);
            UiBaukasten.Anker(_text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-600, -60), new Vector2(600, 80));
            _text.alignment = TextAlignmentOptions.TopLeft;
            UiBaukasten.Anker(_text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, -80), new Vector2(600, 80));
            _punkte = UiBaukasten.Text(Wurzel, "", 22, UiBaukasten.Signal, false, TextAlignmentOptions.TopLeft, true);
            UiBaukasten.Anker(_punkte.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, -130), new Vector2(400, -90));

            _fehlerKnoepfe = UiBaukasten.Leer(Wurzel, "Fehler");
            UiBaukasten.Anker(_fehlerKnoepfe, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, -260), new Vector2(0, -140));
            UiBaukasten.Spalte(_fehlerKnoepfe, 10);
            UiBaukasten.Knopf(_fehlerKnoepfe, "Zum Hauptmenü", () => spiel.ZumHauptmenue(), true);
            _fehlerKnoepfe.gameObject.SetActive(false);
            Wurzel.gameObject.SetActive(false);
        }

        public void FehlerAnzeigen(bool an) => _fehlerKnoepfe.gameObject.SetActive(an);

        public override void Aktualisiere()
        {
            _text.text = Spiel.LadeText ?? "";
            int n = Mathf.FloorToInt(Time.unscaledTime * 3) % 4;
            int sek = Mathf.Max(0, Mathf.FloorToInt(Time.realtimeSinceStartup - Spiel.LadeStartZeit));
            _punkte.text = _fehlerKnoepfe.gameObject.activeSelf ? "" : $"■ {new string('■', n)}{new string('□', 3 - n)}   {sek} s";
        }
    }
}
