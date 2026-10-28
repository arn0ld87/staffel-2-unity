using System.Collections;
using System.IO;
using UnityEngine;

namespace Staffel2.Game
{
    /// <summary>
    /// Schreibt jedes gerenderte Bild (mit UI) als JPG in einen Ordner. Mit festem
    /// Time.captureFramerate entsteht ein gleichmäßiges Video, egal wie lange ein Bild dauert.
    /// Nur für Demo und Abnahme; im normalen Spiel inaktiv.
    /// </summary>
    public sealed class BildAufnahme : MonoBehaviour
    {
        public string Ordner;
        public int Bildrate = 30;
        public int Qualitaet = 88;
        public int Bilder { get; private set; }
        public bool Laeuft { get; private set; }

        public void Starten(string ordner)
        {
            Ordner = ordner;
            Directory.CreateDirectory(Ordner);
            Time.captureFramerate = Bildrate;
            Laeuft = true;
            StartCoroutine(Aufnehmen());
        }

        public void Stoppen()
        {
            Laeuft = false;
            Time.captureFramerate = 0;
        }

        private IEnumerator Aufnehmen()
        {
            var ende = new WaitForEndOfFrame();
            while (Laeuft)
            {
                yield return ende;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(Ordner, $"bild_{Bilder:00000}.jpg"), tex.EncodeToJPG(Qualitaet));
                Destroy(tex);
                Bilder++;
            }
        }
    }
}
