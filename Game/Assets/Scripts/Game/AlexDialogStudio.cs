using UnityEngine;

namespace Staffel2.Game
{
    /// <summary>
    /// Kleines Studio abseits des Raums: Alex-Büste, eigenes Licht, eigene Kamera, die in eine
    /// RenderTexture zeichnet. Pager und Terminal zeigen das Bild; die Hauptkamera sieht die
    /// Ebene „Dialog“ nicht. Ohne Prefab (z. B. in Tests) bleibt das Studio leer.
    /// </summary>
    public sealed class AlexDialogStudio : MonoBehaviour
    {
        public const int Ebene = 31;
        public RenderTexture Bild { get; private set; }
        public AlexFigur Figur { get; private set; }

        private void Awake()
        {
            transform.position = new Vector3(0, -200, 0);
            var prefab = Resources.Load<GameObject>("Alex/AlexDialog");
            if (prefab == null) { Debug.LogWarning("AlexDialog-Prefab fehlt (Resources/Alex/AlexDialog)."); return; }

            var alex = Instantiate(prefab, transform);
            alex.transform.localPosition = Vector3.zero;
            alex.transform.localRotation = Quaternion.identity;
            SetzeEbene(alex.transform);
            Figur = alex.GetComponent<AlexFigur>();
            if (Figur == null) Figur = alex.AddComponent<AlexFigur>();

            // Kamera auf Kopf und Schultern (Gesicht-Mesh statt Ganzkörper, damit Pager/Terminal ein Porträt zeigen).
            Bounds grenzen;
            if (Figur != null && Figur.Gesicht != null)
            {
                grenzen = Figur.Gesicht.bounds;
            }
            else
            {
                grenzen = new Bounds(alex.transform.position + Vector3.up * 1.5f, new Vector3(0.4f, 0.55f, 0.4f));
                foreach (var r in alex.GetComponentsInChildren<Renderer>())
                    if (r.name.Contains("Bust")) grenzen = r.bounds;
            }
            float h = Mathf.Clamp(grenzen.size.y, 0.35f, 0.65f);
            Vector3 ziel = grenzen.center + Vector3.up * h * 0.10f;

            Bild = new RenderTexture(640, 800, 24) { name = "AlexDialogBild", antiAliasing = 4 };
            var camGo = new GameObject("AlexKamera");
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.targetTexture = Bild;
            cam.cullingMask = 1 << Ebene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x26, 0x2a, 0x24, 0xff);
            cam.fieldOfView = 24f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            // Vorderseite des Modells zeigt nach +Z (Unity-Export aus Blender mit -Z vorwärts).
            camGo.transform.position = ziel + alex.transform.forward * h * 2.6f;
            camGo.transform.LookAt(ziel);

            // Warmes Führungslicht und orange Kante, nur für die Dialog-Ebene.
            // Die Textur ist gemalt und bringt ihre Schattierung mit: helles, fast neutrales Licht,
            // Orange nur als schmale Kante.
            Licht("Fuehrung", ziel + new Vector3(0.7f, 0.6f, 1.6f) * h, new Color(1f, 0.97f, 0.92f), 4.2f, h * 5f);
            Licht("Fuell", ziel + new Vector3(-0.9f, 0.1f, 1.4f) * h, new Color(0.9f, 0.93f, 1f), 2.2f, h * 5f);
            Licht("Kante", ziel + new Vector3(-1.2f, 0.7f, -1f) * h, new Color(0.86f, 0.29f, 0.15f), 2.0f, h * 4f);
        }

        private void Licht(string name, Vector3 pos, Color farbe, float staerke, float reichweite)
        {
            var go = new GameObject("Licht " + name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = farbe;
            l.intensity = staerke;
            l.range = reichweite;
            l.cullingMask = 1 << Ebene;
            l.shadows = LightShadows.None;
        }

        private static void SetzeEbene(Transform t)
        {
            t.gameObject.layer = Ebene;
            foreach (Transform kind in t) SetzeEbene(kind);
        }

        private void OnDestroy()
        {
            if (Bild != null) Bild.Release();
        }
    }
}
