using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Staffel2.World.Editor
{
    /// <summary>
    /// Baut die Szene "Serverraum" von Grund auf: Raum, Racks, Arbeitsplatz, Interaktionsobjekte,
    /// Licht und Spieler. Materialien landen unter Assets/Materials/World. Idempotent: die Szene wird überschrieben.
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Serverraum.unity";
        const string StartScenePath = "Assets/Scenes/Start.unity";
        const string MaterialDir = "Assets/Materials/World";
        const string FurniturePath = "Assets/ThirdParty/Kenney/Furniture/";
        const string AudioPath = "Assets/ThirdParty/Kenney/Audio/";

        // Raum: x -5..5, z -4..4, Höhe 3,2 m; Tür in der Südwand, Arbeitsplatz an der Ostwand.
        const float RoomW = 10f, RoomD = 8f, RoomH = 3.2f, Wall = 0.2f;

        // Kenney-Modelle schauen mit ihrer Vorderseite nach +Z (Annahme); bei falscher Ausrichtung hier drehen.
        const float ModellGierOffset = 0f;

        sealed class Mats
        {
            public Material Wand, WandPaneel, WandFuge, Boden, Fuge, Gitter, Decke, Rack, RackFront, EinschubA, EinschubB,
                EinschubC, Lueftung, Griff, Led, Kabel, KabelRot, KabelBlau, Tray, Akzent, AkzentBand, Leuchte, Tuer,
                Kork, Papier, PapierGelb, PapierBlau, FotoRahmen, FotoBild, Faden, Stift, Geraet, GeraetDisplay,
                Monitor, Lampe;
        }

        [MenuItem("Staffel 2/Serverraum bauen")]
        public static void BuildServerRoom()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var m = CreateMaterials();

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.46f, 0.43f, 0.39f);  // dunkel-warm, im Linear-Farbraum klar lesbar
            RenderSettings.skybox = null;

            var raum = new GameObject("Raum").transform;
            BuildRoom(raum, m);

            var racks = new GameObject("Racks").transform;
            var serverschrank = BuildRacks(racks, m);
            var kabelwege = new GameObject("Kabelwege").transform;
            BuildCableTrays(kabelwege, m);

            var arbeitsplatz = new GameObject("Arbeitsplatz").transform;
            var deskPos = BuildWorkstation(arbeitsplatz, m);

            BuildAktenwand(new GameObject("Aktenwand").transform, m);
            var licht = new GameObject("Licht").transform;
            BuildLights(licht, m);
            BuildVolume();
            BuildPlayer(deskPos);

            foreach (var statisch in new[] { raum, racks, kabelwege, licht }) MarkStatic(statisch);

            // Serverschrank-Interaktion an das erste Rack der Reihe B (nahe am Spawn).
            serverschrank.gameObject.name = "Serverschrank";

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Szene konnte nicht gespeichert werden: " + ScenePath);
            AssetDatabase.SaveAssets();
            UpdateBuildSettings();
            Debug.Log("Serverraum gebaut: " + ScenePath);
        }

        // ---------------------------------------------------------------- Materialien

        static Mats CreateMaterials()
        {
            return new Mats
            {
                Wand = Mat("Wand", Hex("383c34"), 0.18f),
                WandPaneel = Mat("WandPaneel", Hex("464a40"), 0.22f),
                WandFuge = Mat("WandFuge", Hex("1c1f19"), 0.1f),
                Boden = Mat("Boden", Hex("423d35"), 0.3f),
                Fuge = Mat("BodenFuge", Hex("181613"), 0.1f),
                Gitter = Mat("BodenGitter", Hex("22201d"), 0.4f),
                Decke = Mat("Decke", Hex("2b2e26"), 0.1f),
                Rack = Mat("Rack", Hex("2a2926"), 0.4f),
                RackFront = Mat("RackFront", Hex("3b3831"), 0.35f),
                EinschubA = Mat("EinschubA", Hex("262624"), 0.5f),
                EinschubB = Mat("EinschubB", Hex("33312d"), 0.5f),
                EinschubC = Mat("EinschubC", Hex("403d37"), 0.5f),
                Lueftung = Mat("Lueftung", Hex("080807"), 0.2f),
                Griff = Mat("Griff", Hex("57534c"), 0.6f),
                Led = Mat("Led", Hex("0d0d0d"), 0.6f),
                Kabel = Mat("Kabel", Hex("0c0c0b"), 0.3f),
                KabelRot = Mat("KabelRot", Hex("7a2016"), 0.3f),
                KabelBlau = Mat("KabelBlau", Hex("1f3a63"), 0.3f),
                Tray = Mat("Kabeltray", Hex("3a3834"), 0.45f),
                Akzent = Mat("Akzent", Hex("db4a26"), 0.3f, Hex("db4a26") * 0.6f),
                AkzentBand = Mat("AkzentBand", Hex("db4a26"), 0.3f, Hex("db4a26") * 1.4f),
                Leuchte = Mat("Leuchte", Hex("f2e2c4"), 0.2f, new Color(1f, 0.82f, 0.55f) * 2.2f),
                Tuer = Mat("Tuer", Hex("2e3028"), 0.2f),
                Kork = Mat("Aktenwand", Hex("4a3a28"), 0.05f),
                Papier = Mat("Papier", Hex("cfc9b6"), 0.05f),
                PapierGelb = Mat("PapierGelb", Hex("d9c36a"), 0.05f),
                PapierBlau = Mat("PapierBlau", Hex("7e98b2"), 0.05f),
                FotoRahmen = Mat("FotoRahmen", Hex("e6e1d3"), 0.05f),
                FotoBild = Mat("FotoBild", Hex("3b444b"), 0.1f),
                Faden = Mat("Faden", Hex("a8201a"), 0.1f),
                Stift = Mat("Stecknadel", Hex("d83a2a"), 0.5f),
                Geraet = Mat("Pager", Hex("1a1a1a"), 0.5f),
                GeraetDisplay = Mat("PagerDisplay", Hex("3a1208"), 0.2f, Hex("db4a26") * 1.2f),
                Monitor = Mat("Monitorflaeche", Hex("050505"), 0.6f),
                Lampe = Mat("Schreibtischlampe", Hex("2a2926"), 0.5f),
            };
        }

        static Material Mat(string name, Color color, float smoothness, Color? emission = null)
        {
            EnsureFolder(MaterialDir);
            var path = MaterialDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? throw new InvalidOperationException("URP-Lit-Shader nicht gefunden.");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            // Emission immer aktiv, damit Hervorhebung und Statuslichter per MaterialPropertyBlock wirken.
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", emission ?? Color.black);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        // ---------------------------------------------------------------- Primitive

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>Dünner Quader von a nach b (Kabel, Schnüre, Rahmen), ohne Collider.</summary>
        static GameObject Line(string name, Transform parent, Vector3 a, Vector3 b, float thickness, Material mat)
        {
            var go = Box(name, parent, (a + b) * 0.5f, new Vector3(thickness, thickness, (b - a).magnitude), mat, false);
            go.transform.localRotation = Quaternion.LookRotation(b - a);
            return go;
        }

        // ---------------------------------------------------------------- Raum

        /// <summary>Dünner Zylinder von a nach b (Kabel, Fäden), ohne Collider.</summary>
        static GameObject Zyl(string name, Transform parent, Vector3 a, Vector3 b, float durchmesser, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (a + b) * 0.5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            go.transform.localScale = new Vector3(durchmesser, (b - a).magnitude * 0.5f, durchmesser);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // ---------------------------------------------------------------- Raum

        static void BuildRoom(Transform root, Mats m)
        {
            var h = RoomH;
            Box("Boden", root, new Vector3(0, -0.05f, 0), new Vector3(RoomW + 2 * Wall, 0.1f, RoomD + 2 * Wall), m.Boden);
            Box("Decke", root, new Vector3(0, h + 0.05f, 0), new Vector3(RoomW + 2 * Wall, 0.1f, RoomD + 2 * Wall), m.Decke);
            Box("Wand_Sued", root, new Vector3(0, h / 2, -RoomD / 2 - Wall / 2), new Vector3(RoomW + 2 * Wall, h, Wall), m.Wand);
            Box("Wand_Nord", root, new Vector3(0, h / 2, RoomD / 2 + Wall / 2), new Vector3(RoomW + 2 * Wall, h, Wall), m.Wand);
            Box("Wand_West", root, new Vector3(-RoomW / 2 - Wall / 2, h / 2, 0), new Vector3(Wall, h, RoomD), m.Wand);
            Box("Wand_Ost", root, new Vector3(RoomW / 2 + Wall / 2, h / 2, 0), new Vector3(Wall, h, RoomD), m.Wand);

            // Tür in der Südwand mit schmalem Akzentstreifen über dem Rahmen.
            Box("Tuer", root, new Vector3(-4f, 1.05f, -RoomD / 2 + 0.03f), new Vector3(1f, 2.1f, 0.06f), m.Tuer, false);
            Box("Tuergriff", root, new Vector3(-3.6f, 1.0f, -RoomD / 2 + 0.08f), new Vector3(0.04f, 0.14f, 0.04f), m.Tray, false);
            Box("Tuerakzent", root, new Vector3(-4f, 2.16f, -RoomD / 2 + 0.02f), new Vector3(1.1f, 0.04f, 0.03f), m.Akzent, false);

            BuildFloorGrid(root, m);
            WallPanels(root, m, "Sued", new Vector3(0, 0, -RoomD / 2), Vector3.forward, RoomW);
            WallPanels(root, m, "Nord", new Vector3(0, 0, RoomD / 2), Vector3.back, RoomW);
            WallPanels(root, m, "West", new Vector3(-RoomW / 2, 0, 0), Vector3.right, RoomD);
            WallPanels(root, m, "Ost", new Vector3(RoomW / 2, 0, 0), Vector3.left, RoomD);

            // Dezentes Akzent-Lichtband an der Nordwand über den Racks.
            Box("Akzentband", root, new Vector3(0, 2.35f, RoomD / 2 - 0.015f), new Vector3(8f, 0.05f, 0.03f), m.AkzentBand, false);
            BuildBrandPosters(root, m);
        }

        /// <summary>Gerahmte Wandposter mit Referenzen auf alex-schneider.dev und die Blog-/Architektur-Themen.</summary>
        static void BuildBrandPosters(Transform root, Mats m)
        {
            var ordner = new GameObject("Poster_AlexSchneiderDev").transform;
            ordner.SetParent(root, false);

            EnsureDarkBrandBanner("Assets/Art/Brand/alex-schneider-dev-banner.png");

            Transform Poster(string name, string texPath, Vector3 pos, Vector3 lookDir, float breite, float hoehe, string untertitel)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null) return null;
                var mat = Mat("Poster_" + name, new Color(0.82f, 0.82f, 0.82f), 0.08f, Color.black);
                mat.SetTexture("_BaseMap", tex);
                EditorUtility.SetDirty(mat);

                var halter = new GameObject(name).transform;
                halter.SetParent(ordner, false);
                halter.localPosition = pos;
                halter.localRotation = Quaternion.LookRotation(lookDir);

                Box("Rahmen", halter, new Vector3(0, 0, -0.008f), new Vector3(breite + 0.06f, hoehe + 0.06f, 0.016f), m.EinschubA, false);
                Box("Akzentkante", halter, new Vector3(0, hoehe * 0.5f + 0.025f, -0.014f), new Vector3(breite + 0.06f, 0.014f, 0.02f), m.Akzent, false);

                var bild = GameObject.CreatePrimitive(PrimitiveType.Quad);
                bild.name = "Bild";
                bild.transform.SetParent(halter, false);
                bild.transform.localPosition = new Vector3(0, 0, -0.018f);
                bild.transform.localScale = new Vector3(breite, hoehe, 1f);
                bild.GetComponent<Renderer>().sharedMaterial = mat;
                UnityEngine.Object.DestroyImmediate(bild.GetComponent<Collider>());

                if (!string.IsNullOrEmpty(untertitel))
                {
                    var labelGo = new GameObject("Beschriftung");
                    labelGo.transform.SetParent(halter, false);
                    labelGo.transform.localPosition = new Vector3(0, -hoehe * 0.5f + 0.055f, -0.022f);
                    var tmp = labelGo.AddComponent<TextMeshPro>();
                    tmp.rectTransform.sizeDelta = new Vector2(breite * 0.92f, 0.10f);
                    tmp.text = untertitel;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.color = new Color(0.95f, 0.93f, 0.88f);
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMin = 0.08f;
                    tmp.fontSizeMax = 0.42f;
                }
                return halter;
            }

            // 1. Hauptbanner alex-schneider.dev an der Ostwand direkt über dem Arbeitsplatz (Blick nach +X, Normal -X)
            var haupt = Poster("Banner_AlexSchneiderDev", "Assets/Art/Brand/alex-schneider-dev-banner.png",
                new Vector3(RoomW / 2 - 0.025f, 2.05f, -1.8f), Vector3.right, 1.55f, 0.72f,
                "LINUX · CLOUD · KI-ARCHITEKTUR");
            if (haupt != null)
            {
                var titelGo = new GameObject("DomainTitel");
                titelGo.transform.SetParent(haupt, false);
                titelGo.transform.localPosition = new Vector3(0.10f, 0.05f, -0.023f);
                var tmp = titelGo.AddComponent<TextMeshPro>();
                tmp.rectTransform.sizeDelta = new Vector2(1.20f, 0.28f);
                tmp.text = "alex-schneider.dev";
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color = new Color(0.96f, 0.94f, 0.90f);
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 0.1f;
                tmp.fontSizeMax = 1.1f;
            }

            // 2. Agora Knowledge Graph an der Südwand neben dem Arbeitsplatz (Normal +Z, Quad schaut nach -Z)
            Poster("Poster_Agora", "Assets/Art/Brand/agora-knowledge-graph.png",
                new Vector3(1.4f, 1.78f, -RoomD / 2 + 0.025f), Vector3.back, 1.35f, 0.76f,
                "alex-schneider.dev · AGORA KNOWLEDGE GRAPH");

            // 3. KI-Sysadmin an der Südwand links daneben
            Poster("Poster_KiSysadmin", "Assets/Art/Brand/ki-sysadmin.png",
                new Vector3(-1.2f, 1.78f, -RoomD / 2 + 0.025f), Vector3.back, 1.35f, 0.76f,
                "alex-schneider.dev · KI-SYSADMIN & LINUX");

            // 4. Podman & Docker an der Ostwand nördlich vom Schreibtisch
            Poster("Poster_PodmanDocker", "Assets/Art/Brand/podman-docker.png",
                new Vector3(RoomW / 2 - 0.025f, 1.78f, 1.2f), Vector3.right, 1.35f, 0.76f,
                "alex-schneider.dev · PODMAN & DOCKER");
        }

        /// <summary>Ersetzt weißen SVG-Rasterhintergrund im Hauptbanner durch dunkles Anthrazit (#181a15).</summary>
        static void EnsureDarkBrandBanner(string assetPath)
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            if (!File.Exists(fullPath)) return;
            var raw = File.ReadAllBytes(fullPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(raw))
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return;
            }
            var corner = tex.GetPixel(4, 4);
            if (corner.r < 0.85f || corner.g < 0.85f || corner.b < 0.85f)
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return;
            }
            var pixels = tex.GetPixels32();
            var bg = new Color32(24, 26, 21, 255);
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (p.r > 235 && p.g > 235 && p.b > 235)
                    pixels[i] = bg;
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>Doppelboden: Fugen im 0,6-m-Raster, im kalten Gang gelochte Gitterkacheln.</summary>
        static void BuildFloorGrid(Transform root, Mats m)
        {
            const float kachel = 0.6f;
            for (var x = -RoomW / 2 + kachel; x < RoomW / 2 - 0.01f; x += kachel)
                Box("Fuge_X", root, new Vector3(x, 0.002f, 0), new Vector3(0.012f, 0.004f, RoomD), m.Fuge, false);
            for (var z = -RoomD / 2 + kachel; z < RoomD / 2 - 0.01f; z += kachel)
                Box("Fuge_Z", root, new Vector3(0, 0.002f, z), new Vector3(RoomW, 0.004f, 0.012f), m.Fuge, false);

            // Gitterkacheln zwischen den Rackreihen (Gang z = 1,5 .. 2,5).
            for (var x = -RoomW / 2 + kachel / 2; x < RoomW / 2; x += kachel)
            {
                if (x < -3.2f || x > 1.0f) continue;
                foreach (var z in new[] { -RoomD / 2 + kachel * 9.5f, -RoomD / 2 + kachel * 10.5f })
                {
                    Box("Gitterkachel", root, new Vector3(x, 0.003f, z), new Vector3(0.44f, 0.005f, 0.44f), m.Gitter, false);
                    for (var k = -2; k <= 2; k++)
                        Box("Gitterleiste", root, new Vector3(x, 0.0065f, z + k * 0.08f), new Vector3(0.4f, 0.003f, 0.02f), m.Lueftung, false);
                }
            }
        }

        /// <summary>Wandgliederung: Sockel, Brüstungspaneel mit Leiste und senkrechte Fugen im 1,25-m-Raster.</summary>
        static void WallPanels(Transform root, Mats m, string name, Vector3 faceCenter, Vector3 normal, float length)
        {
            var rot = Quaternion.LookRotation(normal);
            void Teil(string n, float y, float tiefe, float breite, float hoehe, float along, Material mat)
            {
                var along3 = rot * Vector3.right * along;
                var go = Box(n + "_" + name, root, faceCenter + normal * (tiefe / 2) + along3 + Vector3.up * y,
                    new Vector3(breite, hoehe, tiefe), mat, false);
                go.transform.localRotation = rot;
            }

            Teil("Paneel", 0.55f, 0.02f, length, 1.0f, 0f, m.WandPaneel);
            Teil("Sockel", 0.05f, 0.04f, length, 0.1f, 0f, m.WandFuge);
            Teil("Leiste", 1.05f, 0.035f, length, 0.04f, 0f, m.Tray);
            for (var along = -length / 2 + 1.25f; along < length / 2 - 0.1f; along += 1.25f)
                Teil("Fuge", RoomH / 2, 0.03f, 0.025f, RoomH, along, m.WandFuge);
        }

        // ---------------------------------------------------------------- Racks

        static Transform BuildRacks(Transform root, Mats m)
        {
            Transform serverschrank = null;
            for (var i = 0; i < 4; i++)
            {
                var x = -2.4f + 0.9f * i;
                BuildRack("Rack_A" + (i + 1), root, new Vector3(x, 0, 3.0f), 0f, m, false);
                var istSchrank = i == 0;
                var rackB = BuildRack("Rack_B" + (i + 1), root, new Vector3(x, 0, 1.0f), 180f, m, istSchrank);
                if (istSchrank) serverschrank = rackB;
            }
            return serverschrank;
        }

        // Einschubhöhen in Höheneinheiten (1U/2U); eine HE ist stilisiert 8 cm hoch.
        static readonly int[] Einschuebe = { 2, 1, 1, 1, 2, 1, 1, 2, 1, 2, 1, 1 };

        /// <summary>
        /// Ein Rack (0,6 × 2,0 × 1,0 m); die Front liegt lokal bei -Z, die Kabelbündel an der Rückseite (+Z).
        /// Die LEDs (Kopfleiste zuerst, dann je Einschub zwei) steuert RackStatusLights.
        /// </summary>
        static Transform BuildRack(string name, Transform parent, Vector3 pos, float yaw, Mats m, bool interaktiv)
        {
            const float he = 0.08f, spalt = 0.012f;
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(0, yaw, 0);

            var body = Box("Gehaeuse", root, new Vector3(0, 1f, 0), new Vector3(0.6f, 2.0f, 1.0f), m.Rack);
            var front = Box("Front", root, new Vector3(0, 1f, -0.51f), new Vector3(0.54f, 1.9f, 0.03f), m.RackFront, false);
            Box("Schiene_L", root, new Vector3(-0.285f, 1f, -0.525f), new Vector3(0.03f, 1.9f, 0.03f), m.EinschubA, false);
            Box("Schiene_R", root, new Vector3(0.285f, 1f, -0.525f), new Vector3(0.03f, 1.9f, 0.03f), m.EinschubA, false);

            var leds = new List<Renderer>();
            void Led(float lx, float ly)
            {
                var led = Box("LED" + (leds.Count + 1), root, new Vector3(lx, ly, -0.553f), new Vector3(0.014f, 0.014f, 0.012f), m.Led, false);
                leds.Add(led.GetComponent<Renderer>());
            }

            for (var i = 0; i < 4; i++)
            {
                var kopf = Box("LED" + (leds.Count + 1), root, new Vector3(-0.18f + i * 0.07f, 1.82f, -0.54f), new Vector3(0.035f, 0.035f, 0.03f), m.Led, false);
                leds.Add(kopf.GetComponent<Renderer>());
            }

            var y = 0.12f;
            for (var u = 0; u < Einschuebe.Length; u++)
            {
                var hoehe = Einschuebe[u] * he;
                var cy = y + hoehe / 2;
                var mat = u % 3 == 0 ? m.EinschubA : u % 3 == 1 ? m.EinschubB : m.EinschubC;
                Box("Einschub" + (u + 1), root, new Vector3(0, cy, -0.54f), new Vector3(0.5f, hoehe, 0.02f), mat, false);

                var leisten = Einschuebe[u] == 2 ? 5 : 3;
                for (var s = 0; s < leisten; s++)
                {
                    var sy = cy - hoehe * 0.35f + s * (hoehe * 0.7f / (leisten - 1));
                    Box("Lueftung", root, new Vector3(-0.1f, sy, -0.5515f), new Vector3(0.24f, 0.006f, 0.003f), m.Lueftung, false);
                }
                Box("Griff_L", root, new Vector3(-0.225f, cy, -0.557f), new Vector3(0.012f, hoehe * 0.6f, 0.014f), m.Griff, false);
                Box("Griff_R", root, new Vector3(0.225f, cy, -0.557f), new Vector3(0.012f, hoehe * 0.6f, 0.014f), m.Griff, false);
                Led(0.08f, cy);
                Led(0.115f, cy);
                y += hoehe + spalt;
            }
            root.gameObject.AddComponent<RackStatusLights>().Configure(leds.ToArray());

            // Rückseite: Kabelbündel in Rot, Blau und Schwarz.
            Zyl("Kabel_Rot", root, new Vector3(-0.15f, 0.2f, 0.56f), new Vector3(-0.15f, 1.95f, 0.56f), 0.045f, m.KabelRot);
            Zyl("Kabel_Blau", root, new Vector3(-0.07f, 0.2f, 0.58f), new Vector3(-0.07f, 1.95f, 0.58f), 0.035f, m.KabelBlau);
            Zyl("Kabel_Schwarz", root, new Vector3(0.02f, 0.2f, 0.56f), new Vector3(0.02f, 1.95f, 0.56f), 0.05f, m.Kabel);
            Zyl("Kabel_Rot2", root, new Vector3(0.12f, 0.2f, 0.58f), new Vector3(0.12f, 1.95f, 0.58f), 0.03f, m.KabelRot);
            Zyl("Kabel_Blau2", root, new Vector3(0.2f, 0.2f, 0.56f), new Vector3(0.2f, 1.95f, 0.56f), 0.04f, m.KabelBlau);

            if (interaktiv)
            {
                Box("Beschriftung", root, new Vector3(0, 1.67f, -0.535f), new Vector3(0.3f, 0.025f, 0.02f), m.Akzent, false);
                root.gameObject.AddComponent<Interactable>().Configure("serverschrank", "E – Serverschrank untersuchen",
                    new[] { body.GetComponent<Renderer>(), front.GetComponent<Renderer>() });
            }
            return root;
        }

        static void BuildCableTrays(Transform root, Mats m)
        {
            var kabel = new[] { m.Kabel, m.KabelRot, m.KabelBlau };
            foreach (var z in new[] { 3.0f, 1.0f })
            {
                var name = z > 2f ? "A" : "B";
                const float y = 2.75f;
                Box("Tray_" + name, root, new Vector3(-0.9f, y, z), new Vector3(3.8f, 0.05f, 0.45f), m.Tray, false);
                Line("TrayRand_" + name + "1", root, new Vector3(-2.8f, y + 0.04f, z - 0.22f), new Vector3(1.0f, y + 0.04f, z - 0.22f), 0.03f, m.Tray);
                Line("TrayRand_" + name + "2", root, new Vector3(-2.8f, y + 0.04f, z + 0.22f), new Vector3(1.0f, y + 0.04f, z + 0.22f), 0.03f, m.Tray);
                for (var x = -2.7f; x < 1.0f; x += 0.4f)
                    Box("Sprosse_" + name, root, new Vector3(x, y + 0.03f, z), new Vector3(0.03f, 0.015f, 0.42f), m.Tray, false);
                for (var k = 0; k < 3; k++)
                    Zyl("Kabel_" + name + k, root, new Vector3(-2.7f, y + 0.06f, z - 0.12f + k * 0.12f), new Vector3(0.9f, y + 0.06f, z - 0.12f + k * 0.12f), 0.04f, kabel[k]);
                for (var i = 0; i < 4; i++)
                {
                    var x = -2.4f + 0.9f * i;
                    Line("Abgang_" + name + i, root, new Vector3(x + 0.1f, y, z + 0.1f), new Vector3(x + 0.1f, 2.0f, z + 0.1f), 0.03f, m.Kabel);
                }
            }
        }

        // ---------------------------------------------------------------- Arbeitsplatz

        /// <summary>Baut Schreibtisch, Stuhl, Monitor, Lampe, Pager und zweitem Monitor; liefert die Tischmitte.</summary>
        static Vector3 BuildWorkstation(Transform root, Mats m)
        {
            // Front (Sitzseite) des Schreibtischs und des Monitors zeigt nach -X, zur Raummitte.
            var front = new Vector3(-1, 0, 0);
            var yaw = -90f + ModellGierOffset;
            const float deskZ = -1.8f;

            var desk = Model(FurniturePath + "desk.fbx", "Schreibtisch", root, yaw, 0.75f, 1f);
            var ds = desk.transform.localScale.x;
            var db = WorldBounds(desk);
            PlaceOnFloor(desk, new Vector3(RoomW / 2 - db.size.x / 2 - 0.05f, 0f, deskZ));
            db = WorldBounds(desk);
            var top = db.max.y;
            AddCollider("Kollision_Schreibtisch", root, db);

            var chair = Model(FurniturePath + "chairDesk.fbx", "Buerostuhl", root, 90f + ModellGierOffset, 0.95f, 1f);
            var cb = WorldBounds(chair);
            PlaceOnFloor(chair, new Vector3(db.min.x - cb.size.x / 2 + 0.2f, 0f, deskZ));
            AddCollider("Kollision_Stuhl", root, WorldBounds(chair));

            var screen = Model(FurniturePath + "computerScreen.fbx", "Bildschirm", root, yaw, 0.42f, 1f);
            var sb = WorldBounds(screen);
            PlaceOnFloor(screen, new Vector3(db.max.x - sb.size.x / 2 - 0.1f, top, deskZ));
            sb = WorldBounds(screen);

            var keyboard = Model(FurniturePath + "computerKeyboard.fbx", "Tastatur", root, yaw, 0f, ds);
            PlaceOnFloor(keyboard, new Vector3(db.center.x - db.size.x * 0.12f, top, deskZ));
            var mouse = Model(FurniturePath + "computerMouse.fbx", "Maus", root, yaw, 0f, ds);
            PlaceOnFloor(mouse, new Vector3(db.center.x - db.size.x * 0.12f, top, deskZ - 0.32f));

            var lamp = Model(FurniturePath + "lampSquareFloor.fbx", "Stehlampe", root, yaw, 1.6f, 1f);
            PlaceOnFloor(lamp, new Vector3(RoomW / 2 - 0.4f, 0f, db.min.z - 0.4f));
            AddCollider("Kollision_Lampe", root, WorldBounds(lamp));

            // Monitorfläche vor dem Bildschirm; die Beschriftung liest man von der Sitzseite.
            BuildMonitorFace(root, m, sb, front, "Monitorflaeche", "TERMINAL", MonitorState.Bereit);

            // Terminal: Interaktionskörper um den Bildschirm.
            var terminal = AddCollider("Terminal", root, sb);
            terminal.AddComponent<Interactable>().Configure("terminal", "E – Terminal benutzen", screen.GetComponentsInChildren<Renderer>());

            // Pager: kleines Gerät links der Tastatur (aus Sicht des Sitzenden).
            var pager = Box("Pager", root, new Vector3(db.center.x - db.size.x * 0.1f, top + 0.015f, deskZ + 0.4f),
                new Vector3(0.07f, 0.03f, 0.11f), m.Geraet);
            pager.transform.localRotation = Quaternion.Euler(0, yaw + 20f, 0);
            Box("PagerDisplay", pager.transform, new Vector3(0, 0.52f, 0.1f), new Vector3(0.7f, 0.05f, 0.4f), m.GeraetDisplay, false);
            pager.AddComponent<Interactable>().Configure("pager", "E – Pager ansehen", new[] { pager.GetComponent<Renderer>() });

            // Zweiter Monitor links neben dem Terminal (aus Sicht des Sitzenden), parallel ausgerichtet.
            var screen2 = Model(FurniturePath + "computerScreen.fbx", "Bildschirm2", root, yaw, 0.42f, 1f);
            var s2z = Mathf.Min(sb.center.z + sb.size.z * 1.08f, db.max.z - sb.size.z / 2 - 0.02f);
            PlaceOnFloor(screen2, new Vector3(sb.center.x, top, s2z));
            BuildMonitorFace(root, m, WorldBounds(screen2), front, "Monitorflaeche2", "ÜBERSICHT", MonitorState.Bereit);

            // Schreibtischlampe rechts vom Sitzenden: Fuß, Arm, leuchtender Kopf und warmer Spot.
            var lz = Mathf.Max(deskZ - sb.size.z * 1.1f, db.min.z + 0.15f);
            BuildDeskLamp(root, m, new Vector3(db.max.x - 0.22f, top, lz));

            return db.center;
        }

        static void BuildMonitorFace(Transform root, Mats m, Bounds sb, Vector3 front, string name, string text, MonitorState state)
        {
            var faceW = Mathf.Abs(front.x) > 0.5f ? sb.size.z : sb.size.x;
            var w = faceW * 0.78f;
            var h = sb.size.y * 0.55f;
            var halfDepth = Mathf.Abs(front.x) * sb.extents.x + Mathf.Abs(front.z) * sb.extents.z;
            var pos = sb.center + front * (halfDepth + 0.004f) + Vector3.up * (sb.size.y * 0.12f);

            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(-front);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Flaeche";
            quad.transform.SetParent(go.transform, false);
            quad.transform.localScale = new Vector3(w, h, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = m.Monitor;
            UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());

            var labelGo = new GameObject("Beschriftung");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0, 0, -0.003f);
            var label = labelGo.AddComponent<TextMeshPro>();
            label.rectTransform.sizeDelta = new Vector2(w * 0.9f, h * 0.9f);
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = 0.1f;
            label.fontSizeMax = 3f;

            var monitor = go.AddComponent<MonitorScreen>();
            monitor.Configure(quad.GetComponent<Renderer>(), label);
            monitor.SetState(state);
        }

        // ---------------------------------------------------------------- Aktenwand

        /// <summary>Farbiges Blatt als Quad, zur Raummitte (+X) gewandt und um <paramref name="roll"/> Grad gedreht.</summary>
        static GameObject Blatt(Transform root, string name, Vector3 pos, float w, float h, float roll, Material mat)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            q.transform.SetParent(root, false);
            q.transform.localPosition = pos;
            q.transform.localRotation = Quaternion.LookRotation(Vector3.left) * Quaternion.Euler(0, 0, roll);
            q.transform.localScale = new Vector3(w, h, 1f);
            q.GetComponent<Renderer>().sharedMaterial = mat;
            UnityEngine.Object.DestroyImmediate(q.GetComponent<Collider>());
            return q;
        }

        static void BuildAktenwand(Transform root, Mats m)
        {
            var x = -RoomW / 2 + 0.025f;
            Box("Pinnwand", root, new Vector3(x, 1.6f, -0.5f), new Vector3(0.05f, 1.4f, 3.0f), m.Kork);

            // Zettel (z, y, Breite, Höhe, Drehung) und Fotos ohne Inhalt; rein dekorativ.
            var zettel = new (float z, float y, float w, float h, float roll, Material mat)[]
            {
                (-1.75f, 1.95f, 0.22f, 0.30f, 5f, m.Papier), (-1.30f, 1.70f, 0.28f, 0.20f, -7f, m.PapierGelb),
                (-0.85f, 2.00f, 0.20f, 0.28f, 3f, m.PapierBlau), (-0.35f, 1.72f, 0.30f, 0.22f, -4f, m.Papier),
                (0.15f, 1.98f, 0.22f, 0.30f, 6f, m.PapierGelb), (0.65f, 1.68f, 0.26f, 0.20f, -5f, m.Papier),
                (-1.50f, 1.30f, 0.20f, 0.28f, -3f, m.PapierBlau), (-0.70f, 1.28f, 0.30f, 0.20f, 4f, m.Papier),
                (-0.05f, 1.30f, 0.24f, 0.30f, -6f, m.PapierGelb),
                (0.45f, 1.20f, 0.26f, 0.32f, 7f, null), (-1.15f, 1.05f, 0.24f, 0.30f, -5f, null),
            };

            var pins = new List<Vector3>();
            for (var i = 0; i < zettel.Length; i++)
            {
                var (z, y, w, h, roll, mat) = zettel[i];
                var px = x - 0.001f + 0.0008f * (i + 1);
                if (mat != null)
                    Blatt(root, "Zettel", new Vector3(px, y, z), w, h, roll, mat);
                else
                {
                    Blatt(root, "FotoRahmen", new Vector3(px, y, z), w, h, roll, m.FotoRahmen);
                    Blatt(root, "FotoBild", new Vector3(px + 0.0005f, y + h * 0.05f, z), w * 0.82f, h * 0.78f, roll, m.FotoBild);
                }
                var pin = new Vector3(x + 0.015f, y + h * 0.5f - 0.035f, z);
                Box("Stecknadel", root, pin, new Vector3(0.02f, 0.02f, 0.02f), m.Stift, false);
                pins.Add(pin + Vector3.right * 0.005f);
            }

            // Rote Fadenlinien zwischen den Nadeln.
            foreach (var (a, b) in new[] { (0, 2), (2, 4), (4, 9), (9, 7), (7, 3), (3, 1), (1, 6), (6, 10), (10, 8) })
                Zyl("Faden", root, pins[a], pins[b], 0.006f, m.Faden);

            root.gameObject.AddComponent<Interactable>().Configure("fallakte", "E – Fallakte ansehen");
        }

        // ---------------------------------------------------------------- Licht

        static void BuildLights(Transform root, Mats m)
        {
            var warm = new Color(1f, 0.82f, 0.62f);
            AddLight(root, "Deckenlicht_Gang", LightType.Point, new Vector3(-1.1f, 2.9f, 2.0f), warm, 4.8f, 9f);
            AddLight(root, "Deckenlicht_Gang_Ost", LightType.Point, new Vector3(2.4f, 2.9f, 2.0f), warm, 4.0f, 8.5f);
            AddLight(root, "Deckenlicht_Mitte", LightType.Point, new Vector3(-1.0f, 3.0f, -1.5f), warm, 4.2f, 9.5f);
            AddLight(root, "Aktenwand_Licht", LightType.Point, new Vector3(-3.8f, 2.7f, -0.5f), warm, 3.8f, 6.5f);

            var desk = AddLight(root, "Arbeitsplatz_Spot", LightType.Spot, new Vector3(3.0f, 2.9f, -1.8f), warm, 7.5f, 8f);
            desk.transform.rotation = Quaternion.LookRotation(new Vector3(1.3f, -2.1f, 0f));
            desk.spotAngle = 88f;

            AddLight(root, "Akzent_Orange", LightType.Point, new Vector3(-4.0f, 2.4f, -3.4f), Hex("db4a26"), 2.2f, 4.5f);

            // Sichtbare Leuchtkörper an der Decke über den Punktlichtern.
            Deckenleuchte(root, m, -1.1f, 2.0f, 0.9f, 0.35f);
            Deckenleuchte(root, m, 2.4f, 2.0f, 0.9f, 0.35f);
            Deckenleuchte(root, m, -1.0f, -1.5f, 1.2f, 0.4f);
            Deckenleuchte(root, m, -3.8f, -0.5f, 0.4f, 0.9f);
            Deckenleuchte(root, m, 3.0f, -1.8f, 0.5f, 0.25f);
        }

        static void Deckenleuchte(Transform root, Mats m, float x, float z, float breite, float tiefe)
        {
            Box("Leuchte_Rahmen", root, new Vector3(x, 3.18f, z), new Vector3(breite + 0.08f, 0.04f, tiefe + 0.08f), m.Tray, false);
            Box("Leuchte", root, new Vector3(x, 3.155f, z), new Vector3(breite, 0.015f, tiefe), m.Leuchte, false);
        }

        /// <summary>Schreibtischlampe: Fuß, Arm, Schirm mit leuchtender Unterseite und warmem Spot auf die Tastatur.</summary>
        static void BuildDeskLamp(Transform root, Mats m, Vector3 fuss)
        {
            var lamp = new GameObject("Schreibtischlampe").transform;
            lamp.SetParent(root, false);
            lamp.position = fuss;

            var knie = new Vector3(0f, 0.32f, 0f);
            var kopf = new Vector3(-0.16f, 0.36f, 0f);
            Zyl("Fuss", lamp, Vector3.zero, new Vector3(0, 0.02f, 0), 0.14f, m.Lampe);
            Zyl("Arm_Unten", lamp, new Vector3(0, 0.02f, 0), knie, 0.012f, m.Lampe);
            Zyl("Arm_Oben", lamp, knie, kopf, 0.012f, m.Lampe);
            Box("Schirm", lamp, kopf + new Vector3(0, -0.012f, 0), new Vector3(0.12f, 0.03f, 0.09f), m.Lampe, false);
            Box("Leuchtflaeche", lamp, kopf + new Vector3(0, -0.029f, 0), new Vector3(0.10f, 0.005f, 0.075f), m.Leuchte, false);

            var spot = AddLight(lamp, "Schreibtischlampe_Spot", LightType.Spot, fuss + kopf + new Vector3(0, -0.04f, 0),
                new Color(1f, 0.78f, 0.52f), 2.2f, 2.2f);
            spot.transform.rotation = Quaternion.LookRotation(new Vector3(-0.35f, -1f, 0f));
            spot.spotAngle = 80f;
        }

        // ---------------------------------------------------------------- Nachbearbeitung und Performance

        /// <summary>Globales Volume mit ACES-Tonemapping, leichtem Bloom (LEDs, Leuchten) und leichter Vignette.</summary>
        static void BuildVolume()
        {
            EnsureFolder(MaterialDir);
            var path = MaterialDir + "/ServerraumProfil.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.3f);
            bloom.scatter.Override(0.6f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.14f);
            vignette.smoothness.Override(0.4f);
            // ACES dunkelt stark ab; Belichtung und etwas Kontrast heben, damit der Raum lesbar bleibt.
            var farbe = profile.Add<ColorAdjustments>(true);
            farbe.postExposure.Override(1.75f);
            farbe.contrast.Override(8f);
            farbe.saturation.Override(6f);

            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("GlobalVolume");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0;
            volume.sharedProfile = profile;
        }

        /// <summary>
        /// Statische Batches für die unbewegte Geometrie. Ausgenommen: LEDs und alles unter einem
        /// Interactable, weil sie per MaterialPropertyBlock gefärbt werden.
        /// </summary>
        static void MarkStatic(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>())
            {
                if (t.name.StartsWith("LED") || t.GetComponentInParent<Interactable>() != null) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }

        static Light AddLight(Transform parent, string name, LightType type, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        // ---------------------------------------------------------------- Spieler

        static void BuildPlayer(Vector3 deskCenter)
        {
            var spawn = new Vector3(-3.3f, 0.05f, -3.0f);
            var player = new GameObject("Spieler");
            player.transform.position = spawn;
            var look = deskCenter - spawn;
            look.y = 0;
            player.transform.rotation = Quaternion.LookRotation(look);

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.03f;
            player.AddComponent<PlayerController>();

            var camGo = new GameObject("Kamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0, PlayerController.AugenHoehe, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.02f);
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 70f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<PlayerInteractor>();

            var steps = player.AddComponent<FootstepAudio>();
            var clips = new List<AudioClip>();
            for (var i = 0; i < 5; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioPath + "footstep_concrete_00" + i + ".ogg");
                if (clip != null) clips.Add(clip);
                else Debug.LogWarning("Schrittgeräusch fehlt: footstep_concrete_00" + i + ".ogg");
            }
            steps.Clips = clips.ToArray();
        }

        // ---------------------------------------------------------------- Modelle und Maße

        /// <summary>
        /// Instanziiert ein FBX als Prefab-Instanz und skaliert gleichmäßig auf die Zielhöhe
        /// (height &gt; 0) oder mit festem Faktor. Fehlt die Datei, steht ein Platzhalterwürfel da.
        /// </summary>
        static GameObject Model(string path, string name, Transform parent, float yaw, float height, float scale)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject go;
            if (asset != null)
                go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            else
            {
                Debug.LogError("Modell fehlt, Platzhalter wird gebaut: " + path);
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            }
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;

            if (height > 0f)
            {
                var h = WorldBounds(go).size.y;
                if (h > 0.0001f) go.transform.localScale *= height / h;
            }
            return go;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        /// <summary>Verschiebt das Objekt, bis Boden-Mitte seiner Bounds auf (x, minY, z) liegt.</summary>
        static void PlaceOnFloor(GameObject go, Vector3 xMinYz)
        {
            var b = WorldBounds(go);
            go.transform.position += new Vector3(xMinYz.x - b.center.x, xMinYz.y - b.min.y, xMinYz.z - b.center.z);
        }

        /// <summary>Unskalierter Kollisionskörper in Weltbounds (für Modelle ohne Collider).</summary>
        static GameObject AddCollider(string name, Transform parent, Bounds b)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = b.center;
            go.AddComponent<BoxCollider>().size = b.size;
            return go;
        }

        // ---------------------------------------------------------------- Build-Einstellungen

        static void UpdateBuildSettings()
        {
            PlayerSettings.runInBackground = true;
            foreach (var rpPath in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
            {
                var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
                if (rp != null && rp.maxAdditionalLightsCount < 8)
                {
                    rp.maxAdditionalLightsCount = 8;
                    EditorUtility.SetDirty(rp);
                }
            }
            var list = new List<EditorBuildSettingsScene>();
            if (File.Exists(StartScenePath)) list.Add(new EditorBuildSettingsScene(StartScenePath, true));
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.path == StartScenePath || s.path == ScenePath || s.path.EndsWith("/SampleScene.unity")) continue;
                list.Add(s);
            }
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
