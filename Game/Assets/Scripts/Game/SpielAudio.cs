using Staffel2.World;
using UnityEngine;

namespace Staffel2.Game
{
    /// <summary>
    /// Audio des Spiels: räumliches Lüfter-Ambiente an zwei Racks, leiser Klangteppich als
    /// Musik, Effekte für Interaktion, Pager, Erfolg und Fehler, UI-Klicks. Lautstärken
    /// getrennt nach Gesamt, Musik, Effekte, Sprache und Umgebung. Kein Ton pro Terminalzeichen;
    /// gleiche Effekte kurz hintereinander werden zusammengefasst.
    /// </summary>
    public sealed class SpielAudio : MonoBehaviour
    {
        private const int MaxEffektQuellen = 4;

        private AudioSource _musik;
        private readonly AudioSource[] _umgebung = new AudioSource[2];
        private readonly AudioSource[] _effekte = new AudioSource[MaxEffektQuellen];
        private int _naechsteQuelle;
        private float _gesamt = 0.8f, _musikPegel = 0.5f, _effektPegel = 0.8f, _umgebungPegel = 0.6f;
        private AudioClip _klick, _interaktion, _pager, _erfolg, _fehler, _tick;
        private float _letzterPager, _letzterKlick;

        private void Awake()
        {
            _klick = Resources.Load<AudioClip>("Audio/click_002");
            _interaktion = Resources.Load<AudioClip>("Audio/select_001");
            _pager = Resources.Load<AudioClip>("Audio/bong_001");
            _erfolg = Resources.Load<AudioClip>("Audio/confirmation_001");
            _fehler = Resources.Load<AudioClip>("Audio/error_004");
            _tick = Resources.Load<AudioClip>("Audio/tick_001");

            for (int i = 0; i < _effekte.Length; i++)
            {
                _effekte[i] = gameObject.AddComponent<AudioSource>();
                _effekte[i].playOnAwake = false;
                _effekte[i].spatialBlend = 0;
            }

            _musik = gameObject.AddComponent<AudioSource>();
            _musik.clip = Resources.Load<AudioClip>("Audio/ermittlung_teppich");
            _musik.loop = true;
            _musik.spatialBlend = 0;
            _musik.Play();

            // Lüfter an höchstens zwei Racks, räumlich mit sanftem Abfall.
            var racks = FindObjectsByType<RackStatusLights>(FindObjectsSortMode.None);
            var luefter = Resources.Load<AudioClip>("Audio/rack_luefter");
            for (int i = 0; i < _umgebung.Length; i++)
            {
                var go = new GameObject("Luefter " + i);
                go.transform.SetParent(transform, false);
                if (racks.Length > 0) go.transform.position = racks[(i * racks.Length / 2) % racks.Length].transform.position + Vector3.up * 1.2f;
                var src = go.AddComponent<AudioSource>();
                src.clip = luefter;
                src.loop = true;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 1.5f;
                src.maxDistance = 14f;
                src.dopplerLevel = 0;
                src.time = i * 3.1f;   // versetzt, damit sich die Loops nicht decken
                src.Play();
                _umgebung[i] = src;
            }
        }

        public void Lautstaerken(Einstellungen e)
        {
            _gesamt = e.Gesamt;
            _musikPegel = e.Musik;
            _effektPegel = e.Effekte;
            _umgebungPegel = e.Umgebung;
            AudioListener.volume = _gesamt;
            if (_musik != null) _musik.volume = 0.35f * _musikPegel;
            foreach (var u in _umgebung) if (u != null) u.volume = 0.55f * _umgebungPegel;
        }

        public void Klick()
        {
            if (Time.unscaledTime - _letzterKlick < 0.06f) return;
            _letzterKlick = Time.unscaledTime;
            Spiele(_klick, 0.5f);
        }

        public void Interaktion() => Spiele(_interaktion, 0.6f);

        public void Pager(string art)
        {
            // Mehrere Meldungen auf einmal: ein Signal genügt.
            if (Time.unscaledTime - _letzterPager < 1.2f) return;
            _letzterPager = Time.unscaledTime;
            Spiele(art == "erledigt" ? _tick : _pager, 0.55f);
        }

        public void Erfolg() => Spiele(_erfolg, 0.6f);
        public void Fehler() => Spiele(_fehler, 0.5f);

        private void Spiele(AudioClip clip, float pegel)
        {
            if (clip == null) return;
            var src = _effekte[_naechsteQuelle];
            _naechsteQuelle = (_naechsteQuelle + 1) % _effekte.Length;
            src.pitch = Random.Range(0.97f, 1.03f);
            src.PlayOneShot(clip, pegel * _effektPegel);
        }
    }
}
