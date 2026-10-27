using System;
using UnityEngine;

namespace Staffel2.World
{
    /// <summary>
    /// Anvisierbares Objekt (braucht einen Collider an sich oder einem Kind). Beim Anvisieren wird
    /// es per MaterialPropertyBlock getönt und leicht zum Leuchten gebracht, ohne eigene Shader.
    /// </summary>
    public class Interactable : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly Color HighlightColor = new Color(1f, 0.70f, 0.42f, 1f);

        [SerializeField] string id;
        [SerializeField] string prompt;
        [SerializeField] Renderer[] highlightRenderers;

        MaterialPropertyBlock _block;
        Color[] _baseColors;
        bool _highlighted;

        public string Id => id;
        public string Prompt => prompt;

        public event Action<Interactable> Interacted;

        /// <summary>Setzt Kennung, Hinweistext und optional die zu hervorhebenden Renderer (Standard: alle Kinder).</summary>
        public void Configure(string newId, string newPrompt, Renderer[] renderers = null)
        {
            id = newId;
            prompt = newPrompt;
            if (renderers != null) highlightRenderers = renderers;
        }

        public void Interact() => Interacted?.Invoke(this);

        public void SetHighlighted(bool on)
        {
            if (on == _highlighted) return;
            _highlighted = on;

            var renderers = Renderers();
            if (_block == null) _block = new MaterialPropertyBlock();

            if (on && _baseColors == null)
            {
                _baseColors = new Color[renderers.Length];
                for (var i = 0; i < renderers.Length; i++)
                {
                    var mat = renderers[i] != null ? renderers[i].sharedMaterial : null;
                    _baseColors[i] = mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white;
                }
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (!on)
                {
                    renderers[i].SetPropertyBlock(null);
                    continue;
                }
                _block.Clear();
                _block.SetColor(BaseColorId, Color.Lerp(_baseColors[i], HighlightColor, 0.45f));
                _block.SetColor(EmissionColorId, HighlightColor * 0.25f);
                renderers[i].SetPropertyBlock(_block);
            }
        }

        Renderer[] Renderers()
        {
            if (highlightRenderers == null || highlightRenderers.Length == 0)
                highlightRenderers = GetComponentsInChildren<Renderer>();
            return highlightRenderers;
        }

        void OnDisable() => SetHighlighted(false);
    }
}
