using UnityEngine;
using UnityEngine.InputSystem;

namespace Staffel2.World
{
    /// <summary>
    /// Ego-Perspektive: WASD, Maus, Sprint (Shift), Schwerkraft. Der Cursor ist gesperrt, solange
    /// gespielt wird; bei <see cref="InputBlocked"/> ruhen Bewegung und Blick und der Cursor ist frei.
    /// Die Kamera ist ein Kind dieses Objekts (Augenhöhe ca. 1,65 m).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public const float AugenHoehe = 1.65f;

        /// <summary>Grad pro Maus-Zählschritt.</summary>
        public float MouseSensitivity = 0.12f;

        /// <summary>Schaltet Kopfwippen und Kamerapendeln ab.</summary>
        public bool ReducedMotion;

        public float WalkSpeed = 3.2f;
        public float SprintSpeed = 5.2f;
        public float Gravity = 18f;

        bool _inputBlocked;
        CharacterController _controller;
        Transform _camera;
        float _yaw;
        float _pitch;
        float _verticalSpeed;
        Vector3 _horizontalVelocity;
        float _bobPhase;
        float _bobWeight;

        /// <summary>Friert Bewegung und Blick ein und gibt den Cursor frei.</summary>
        public bool InputBlocked
        {
            get => _inputBlocked;
            set
            {
                _inputBlocked = value;
                ApplyCursor();
            }
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) _camera = cam.transform;
            _yaw = transform.eulerAngles.y;
        }

        void OnEnable() => ApplyCursor();

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void ApplyCursor()
        {
            if (!isActiveAndEnabled) return;
            Cursor.lockState = _inputBlocked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _inputBlocked;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            // Nach Esc oder Fokusverlust per Klick wieder einfangen.
            if (!_inputBlocked && Cursor.lockState != CursorLockMode.Locked
                && mouse != null && mouse.leftButton.wasPressedThisFrame)
                ApplyCursor();

            if (!_inputBlocked && mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var delta = mouse.delta.ReadValue() * MouseSensitivity;
                _yaw += delta.x;
                _pitch = Mathf.Clamp(_pitch - delta.y, -85f, 85f);
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }

            var wish = Vector3.zero;
            var sprint = false;
            if (!_inputBlocked && keyboard != null)
            {
                var x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                var z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                wish = transform.right * x + transform.forward * z;
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                sprint = keyboard.leftShiftKey.isPressed;
            }

            var target = wish * (sprint ? SprintSpeed : WalkSpeed);
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, target, 24f * Time.deltaTime);

            if (_controller.isGrounded && _verticalSpeed < 0f) _verticalSpeed = -2f;
            else _verticalSpeed -= Gravity * Time.deltaTime;

            _controller.Move((_horizontalVelocity + Vector3.up * _verticalSpeed) * Time.deltaTime);

            UpdateCamera();
        }

        void UpdateCamera()
        {
            if (_camera == null) return;

            var speed = _horizontalVelocity.magnitude;
            var moving = !ReducedMotion && _controller.isGrounded && speed > 0.2f;
            _bobWeight = Mathf.MoveTowards(_bobWeight, moving ? 1f : 0f, 6f * Time.deltaTime);
            if (moving) _bobPhase += speed * 2.2f * Time.deltaTime;

            var bob = ReducedMotion ? 0f : Mathf.Sin(_bobPhase * 2f) * 0.022f * _bobWeight;
            var roll = ReducedMotion ? 0f : Mathf.Sin(_bobPhase) * 0.45f * _bobWeight;

            _camera.localPosition = new Vector3(0f, AugenHoehe + bob, 0f);
            _camera.localRotation = Quaternion.Euler(_pitch, 0f, roll);
        }
    }
}
