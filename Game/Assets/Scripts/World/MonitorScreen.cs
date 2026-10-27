using TMPro;
using UnityEngine;

namespace Staffel2.World
{
    /// <summary>
    /// Leuchtende Monitorfläche mit einem kurzen, großen Schriftzug. Der Zustand steht auch im Text
    /// bzw. im Pulsieren (Fehler), nicht nur in der Farbe.
    /// </summary>
    public class MonitorScreen : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] Renderer face;
        [SerializeField] TMP_Text label;

        MaterialPropertyBlock _block;
        MonitorState _state = MonitorState.Aus;

        public MonitorState State => _state;

        public void Configure(Renderer faceRenderer, TMP_Text labelText)
        {
            face = faceRenderer;
            label = labelText;
            Apply(1f);
        }

        public void SetLabel(string text)
        {
            if (label != null) label.text = text ?? string.Empty;
        }

        public void SetState(MonitorState state)
        {
            _state = state;
            Apply(1f);
        }

        void OnEnable() => Apply(1f);

        void Update()
        {
            if (_state == MonitorState.Fehler)
                Apply(0.7f + 0.3f * Mathf.Sin(Time.time * Mathf.PI * 2f));
        }

        void Apply(float pulse)
        {
            Color color;
            switch (_state)
            {
                case MonitorState.Bereit: color = new Color(0.9f, 0.55f, 0.2f); break;
                case MonitorState.Aktiv: color = new Color(0.25f, 0.85f, 0.55f); break;
                case MonitorState.Fehler: color = new Color(0.9f, 0.18f, 0.12f); break;
                default: color = Color.black; break;
            }

            if (face != null)
            {
                if (_block == null) _block = new MaterialPropertyBlock();
                _block.Clear();
                _block.SetColor(BaseColorId, color * 0.15f);
                _block.SetColor(EmissionColorId, color * (0.55f * pulse));
                face.SetPropertyBlock(_block);
            }

            if (label != null)
            {
                label.enabled = _state != MonitorState.Aus;
                label.color = Color.Lerp(color, Color.white, 0.75f);
            }
        }
    }
}
