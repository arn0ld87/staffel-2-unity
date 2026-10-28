using UnityEngine;
using UnityEngine.Audio;

namespace Staffel2.World
{
    /// <summary>Spielt Schrittgeräusche, solange sich der CharacterController am Boden bewegt.</summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(AudioSource))]
    public class FootstepAudio : MonoBehaviour
    {
        public AudioClip[] Clips;
        public AudioMixerGroup MixerGroup;

        /// <summary>Strecke in Metern zwischen zwei Schritten.</summary>
        public float StrideLength = 1.9f;
        [Range(0f, 1f)] public float Volume = 0.6f;

        CharacterController _controller;
        AudioSource _source;
        float _distance;
        int _last = -1;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.outputAudioMixerGroup = MixerGroup;
        }

        void Update()
        {
            var velocity = _controller.velocity;
            velocity.y = 0f;
            var speed = velocity.magnitude;

            if (!_controller.isGrounded || speed < 0.5f)
            {
                _distance = StrideLength * 0.5f;
                return;
            }

            _distance += speed * Time.deltaTime;
            if (_distance < StrideLength) return;
            _distance = 0f;
            Step();
        }

        void Step()
        {
            if (Clips == null || Clips.Length == 0) return;
            var index = Random.Range(0, Clips.Length);
            if (Clips.Length > 1 && index == _last) index = (index + 1) % Clips.Length;
            _last = index;
            if (Clips[index] == null) return;

            _source.pitch = Random.Range(0.95f, 1.05f);
            _source.PlayOneShot(Clips[index], Volume);
        }
    }
}
