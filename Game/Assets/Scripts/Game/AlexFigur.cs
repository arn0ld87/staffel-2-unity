using UnityEngine;

namespace Staffel2.Game
{
    public enum AlexHaltung { Ruhe, Erklaeren, Zuhoeren, Zeigen }

    /// <summary>
    /// Mimik und Haltung der Alex-Büste: zufälliges Blinzeln, Mundbewegung beim Sprechen,
    /// Ausdruck zwischen Lächeln und neutral, weiche Übergänge zwischen Idle, Erklären und
    /// Zuhören. Shape Keys werden in LateUpdate gesetzt, weil die Clips des FBX konstante
    /// Nullkurven für sie enthalten und der Animator sie sonst überschreibt.
    /// Bewegung reduzieren dämpft Kopfbewegung und Mundflattern.
    /// </summary>
    public sealed class AlexFigur : MonoBehaviour
    {
        public SkinnedMeshRenderer Gesicht;
        public SkinnedMeshRenderer Mundraum;
        public Animator Animator;
        public bool BewegungReduziert;

        private int _blinzelnL = -1, _blinzelnR = -1, _mundAuf = -1, _mundSchmal = -1, _neutral = -1, _mundraumAuf = -1;
        private float _naechstesBlinzeln, _blinzelZeit = -1f;
        private float _sprichtBis, _mund, _mundZiel, _schmal;
        private float _neutralWert, _neutralZiel = 0.35f;
        private AlexHaltung _haltung = AlexHaltung.Ruhe;
        private float _haltungBis;
        private Quaternion _grundDrehung;
        private Vector3? _zeigeZiel;

        private void Awake()
        {
            if (Animator == null) Animator = GetComponentInChildren<Animator>();
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                if (smr.sharedMesh.GetBlendShapeIndex("Blinzeln_L") >= 0) Gesicht = smr;
                else if (smr.sharedMesh.blendShapeCount > 0) Mundraum = smr;
            }
            if (Gesicht != null)
            {
                var m = Gesicht.sharedMesh;
                _blinzelnL = m.GetBlendShapeIndex("Blinzeln_L");
                _blinzelnR = m.GetBlendShapeIndex("Blinzeln_R");
                _mundAuf = m.GetBlendShapeIndex("Mund_Auf");
                _mundSchmal = m.GetBlendShapeIndex("Mund_Schmal");
                _neutral = m.GetBlendShapeIndex("Neutral");
            }
            if (Mundraum != null) _mundraumAuf = Mundraum.sharedMesh.GetBlendShapeIndex("Mund_Auf");
            _naechstesBlinzeln = Time.time + Random.Range(1.5f, 4f);
            _grundDrehung = transform.rotation;
            Haltung(AlexHaltung.Ruhe, 0f);
        }

        /// <summary>Alex spricht für die angegebene Zeit (Untertitel stehen im Pager).</summary>
        public void Spreche(float sekunden)
        {
            _sprichtBis = Mathf.Max(_sprichtBis, Time.time + Mathf.Clamp(sekunden, 0.4f, 12f));
            Haltung(AlexHaltung.Erklaeren, sekunden);
        }

        /// <summary>0 = Lächeln (Modell), 1 = ernst-neutral.</summary>
        public void Ausdruck(float neutral) => _neutralZiel = Mathf.Clamp01(neutral);

        /// <summary>
        /// Zeigt mit dem rechten Arm auf einen Punkt im Raum: dreht sich dafür um die Hochachse, bis die
        /// rechte Seite zum Ziel weist, und kehrt danach in die Ausgangsdrehung zurück. Ohne Zeigen-Clip
        /// (z. B. die Büste) passiert nichts.
        /// </summary>
        public void ZeigeAuf(Vector3 ziel, float dauer)
        {
            if (!HatZustand("Zeigen")) return;
            _zeigeZiel = ziel;
            _haltung = AlexHaltung.Ruhe;  // erzwingt den Wechsel, auch wenn er gerade erklärt
            Haltung(AlexHaltung.Zeigen, dauer);
        }

        private bool HatZustand(string name) =>
            Animator != null && Animator.runtimeAnimatorController != null && Animator.HasState(0, Animator.StringToHash(name));

        public void Haltung(AlexHaltung haltung, float dauer)
        {
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            if (_haltung == AlexHaltung.Zeigen && haltung != AlexHaltung.Ruhe && Time.time < _haltungBis) return; // erst fertig zeigen
            if (haltung == AlexHaltung.Zuhoeren && Time.time < _sprichtBis) return; // erst ausreden
            if (haltung == _haltung && dauer > 0) { _haltungBis = Mathf.Max(_haltungBis, Time.time + dauer); return; }
            _haltung = haltung;
            _haltungBis = dauer > 0 ? Time.time + dauer : float.MaxValue;
            if (haltung != AlexHaltung.Zeigen) _zeigeZiel = null;
            string zustand = haltung == AlexHaltung.Erklaeren ? "Erklaeren" : haltung == AlexHaltung.Zuhoeren ? "Zuhoeren"
                : haltung == AlexHaltung.Zeigen ? "Zeigen" : "Ruhe";
            Animator.CrossFadeInFixedTime(zustand, 0.35f);
        }

        private void Update()
        {
            if (_haltung != AlexHaltung.Ruhe && Time.time > _haltungBis) Haltung(AlexHaltung.Ruhe, 0f);
            if (Animator != null) Animator.speed = BewegungReduziert ? 0.5f : 1f;

            // Zum Zeigen so drehen, dass die rechte Seite (Zeigearm) zum Ziel weist; sonst zurück.
            Quaternion soll = _grundDrehung;
            if (_zeigeZiel.HasValue)
            {
                var richtung = _zeigeZiel.Value - transform.position;
                richtung.y = 0;
                if (richtung.sqrMagnitude > 0.01f) soll = Quaternion.LookRotation(richtung) * Quaternion.Euler(0, -90f, 0);
            }
            transform.rotation = Quaternion.Slerp(transform.rotation, soll, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        private void LateUpdate()
        {
            if (Gesicht == null) return;
            float t = Time.time;

            // Blinzeln: alle 2–6 s, ca. 160 ms, gelegentlich doppelt.
            if (_blinzelZeit < 0 && t >= _naechstesBlinzeln) _blinzelZeit = 0;
            float lid = 0;
            if (_blinzelZeit >= 0)
            {
                _blinzelZeit += Time.deltaTime;
                const float dauer = 0.16f;
                lid = Mathf.Sin(Mathf.Clamp01(_blinzelZeit / dauer) * Mathf.PI);
                if (_blinzelZeit >= dauer)
                {
                    _blinzelZeit = -1;
                    _naechstesBlinzeln = t + (Random.value < 0.15f ? 0.25f : Random.Range(2f, 6f));
                }
            }

            // Sprechen: unregelmäßige Silben, kein Lippensync mit Audio (es gibt keine Sprachausgabe).
            bool spricht = t < _sprichtBis;
            if (spricht)
            {
                float silbe = Mathf.PerlinNoise(t * 7.5f, 0.3f);
                _mundZiel = Mathf.Lerp(0.15f, BewegungReduziert ? 0.5f : 1f, silbe);
                _schmal = Mathf.PerlinNoise(t * 3.1f, 4.2f) > 0.62f ? 0.6f : 0f;
            }
            else { _mundZiel = 0; _schmal = 0; }
            _mund = Mathf.MoveTowards(_mund, _mundZiel, Time.deltaTime * 9f);
            _neutralWert = Mathf.MoveTowards(_neutralWert, spricht ? Mathf.Min(_neutralZiel, 0.6f) : _neutralZiel, Time.deltaTime * 1.5f);

            Setze(Gesicht, _blinzelnL, lid);
            Setze(Gesicht, _blinzelnR, lid);
            Setze(Gesicht, _mundAuf, _mund);
            Setze(Gesicht, _mundSchmal, _schmal * _mund);
            Setze(Gesicht, _neutral, _neutralWert);
            if (Mundraum != null) Setze(Mundraum, _mundraumAuf, _mund);
        }

        private static void Setze(SkinnedMeshRenderer smr, int index, float wert01)
        {
            if (index >= 0) smr.SetBlendShapeWeight(index, wert01 * 100f);
        }
    }
}
