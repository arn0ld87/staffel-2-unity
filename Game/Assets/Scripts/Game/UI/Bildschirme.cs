using UnityEngine;

namespace Staffel2.Game.UI
{
    /// <summary>Gemeinsame Basis: ein Wurzel-Panel, das ein- und ausgeblendet wird.</summary>
    public abstract class Bildschirm
    {
        protected readonly GameRoot Spiel;
        public RectTransform Wurzel { get; protected set; }

        protected Bildschirm(GameRoot spiel) { Spiel = spiel; }

        public bool Sichtbar => Wurzel != null && Wurzel.gameObject.activeSelf;

        public virtual void Zeigen(bool an)
        {
            if (Wurzel == null) return;
            bool war = Wurzel.gameObject.activeSelf;
            Wurzel.gameObject.SetActive(an);
            if (an && !war) Geoeffnet();
        }

        protected virtual void Geoeffnet() { }
        public virtual void Aktualisiere() { }
    }

    /// <summary>Hält alle Bildschirme und schaltet je Ansicht den passenden ein.</summary>
    public sealed class Bildschirme
    {
        public readonly Hud Hud;
        public readonly LadeBildschirm Lade;
        public readonly TerminalOverlay Terminal;
        public readonly FallakteOverlay Fallakte;
        public readonly PagerOverlay Pager;
        public readonly Hauptmenue Menue;
        public readonly PauseMenue Pause;
        public readonly AbschlussBildschirm Abschluss;
        private readonly Bildschirm[] _alle;

        public Bildschirme(GameRoot spiel)
        {
            var leinwand = UiBaukasten.Leinwand("UI", 10).transform;
            Hud = new Hud(spiel, leinwand);
            Terminal = new TerminalOverlay(spiel, leinwand);
            Fallakte = new FallakteOverlay(spiel, leinwand);
            Pager = new PagerOverlay(spiel, leinwand);
            Pause = new PauseMenue(spiel, leinwand);
            Abschluss = new AbschlussBildschirm(spiel, leinwand);
            Menue = new Hauptmenue(spiel, leinwand);
            Lade = new LadeBildschirm(spiel, leinwand);
            _alle = new Bildschirm[] { Terminal, Fallakte, Pager, Pause, Abschluss, Menue, Lade };
        }

        public void ZeigeNur(Ansicht ansicht)
        {
            Terminal.Zeigen(ansicht == Ansicht.Terminal);
            Fallakte.Zeigen(ansicht == Ansicht.Fallakte);
            Pager.Zeigen(ansicht == Ansicht.Pager);
            Pause.Zeigen(ansicht == Ansicht.Pause);
            Abschluss.Zeigen(ansicht == Ansicht.Abschluss);
            Menue.Zeigen(ansicht == Ansicht.Menue);
            Lade.Zeigen(ansicht == Ansicht.Laden);
            Hud.Zeigen(ansicht == Ansicht.Raum);
        }

        public void Aktualisiere()
        {
            Hud.Aktualisiere();
            foreach (var b in _alle) if (b.Sichtbar) b.Aktualisiere();
        }
    }
}
