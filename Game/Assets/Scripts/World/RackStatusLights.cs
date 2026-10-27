using UnityEngine;

namespace Staffel2.World
{
    /// <summary>
    /// Statuslämpchen eines Racks. Der Zustand steht nicht nur in der Farbe, sondern auch im Muster:
    /// Ruhe = ein schwaches blaues Lämpchen, Aktiv = cyanfarbenes Lauflicht, Warnung = alle blinken
    /// bernsteinfarben (1,5 Hz), Erledigt = alle grün und dauerhaft an.
    /// Die LED-Materialien brauchen aktivierte Emission (URP Lit); gefärbt wird per MaterialPropertyBlock.
    /// </summary>
    public class RackStatusLights : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        const float Helligkeit = 2.5f;
        const float BlinkHz = 1.5f;
        const float LaufHz = 4f;

        static readonly Color RuheFarbe = new Color(0.25f, 0.45f, 0.9f);
        static readonly Color AktivFarbe = new Color(0.25f, 0.85f, 1f);
        static readonly Color WarnFarbe = new Color(1f, 0.55f, 0.05f);
        static readonly Color ErledigtFarbe = new Color(0.25f, 1f, 0.4f);

        [SerializeField] Renderer[] leds;

        MaterialPropertyBlock _block;
        bool[] _an;
        RackState _state = RackState.Ruhe;

        public RackState State => _state;

        public void Configure(Renderer[] ledRenderers)
        {
            leds = ledRenderers;
            Apply(true);
        }

        public void SetState(RackState state)
        {
            _state = state;
            Apply(true);
        }

        void OnEnable() => Apply(true);

        void Update() => Apply(false);

        void Apply(bool force)
        {
            if (leds == null || leds.Length == 0) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            if (_an == null || _an.Length != leds.Length) { _an = new bool[leds.Length]; force = true; }

            var t = Time.time;
            var n = leds.Length;
            Color farbe;
            switch (_state)
            {
                case RackState.Aktiv: farbe = AktivFarbe; break;
                case RackState.Warnung: farbe = WarnFarbe; break;
                case RackState.Erledigt: farbe = ErledigtFarbe; break;
                default: farbe = RuheFarbe; break;
            }

            for (var i = 0; i < n; i++)
            {
                bool an;
                switch (_state)
                {
                    case RackState.Aktiv: an = (int)(t * LaufHz) % n == i; break;
                    case RackState.Warnung: an = (int)(t * BlinkHz * 2f) % 2 == 0; break;
                    case RackState.Erledigt: an = true; break;
                    default: an = i == 0; break;
                }

                if (!force && _an[i] == an) continue;
                _an[i] = an;
                if (leds[i] == null) continue;

                var staerke = an ? (_state == RackState.Ruhe ? 0.8f : Helligkeit) : 0f;
                _block.Clear();
                _block.SetColor(BaseColorId, an ? farbe : new Color(0.05f, 0.05f, 0.05f));
                _block.SetColor(EmissionColorId, farbe * staerke);
                leds[i].SetPropertyBlock(_block);
            }
        }
    }
}
