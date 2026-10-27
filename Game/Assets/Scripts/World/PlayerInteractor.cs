using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Staffel2.World
{
    /// <summary>
    /// Sitzt an der Kamera: zielt per Raycast auf <see cref="Interactable"/>s, meldet den Hinweistext
    /// und löst bei E oder Linksklick <see cref="Interactable.Interacted"/> aus.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        public float Reach = 2.2f;
        public LayerMask Mask = ~0;

        PlayerController _player;
        Interactable _current;
        string _hint;

        public Interactable Current => _current;

        /// <summary>Hinweistext des anvisierten Objekts; null, wenn nichts anvisiert ist.</summary>
        public event Action<string> HintChanged;

        void Awake() => _player = GetComponentInParent<PlayerController>();

        void OnDisable() => Select(null);

        void Update()
        {
            if (_player != null && _player.InputBlocked)
            {
                Select(null);
                return;
            }

            Interactable found = null;
            if (Physics.Raycast(transform.position, transform.forward, out var hit, Reach, Mask,
                    QueryTriggerInteraction.Ignore))
                found = hit.collider.GetComponentInParent<Interactable>();
            Select(found);

            if (_current == null) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if ((keyboard != null && keyboard.eKey.wasPressedThisFrame)
                || (mouse != null && mouse.leftButton.wasPressedThisFrame))
                _current.Interact();
        }

        void Select(Interactable next)
        {
            if (next != _current)
            {
                if (_current != null) _current.SetHighlighted(false);
                _current = next;
                if (_current != null) _current.SetHighlighted(true);
            }

            var hint = _current != null ? _current.Prompt : null;
            if (hint == _hint) return;
            _hint = hint;
            HintChanged?.Invoke(_hint);
        }
    }
}
