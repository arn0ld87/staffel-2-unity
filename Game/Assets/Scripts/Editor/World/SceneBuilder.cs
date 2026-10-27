using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        const string AlexPath = "Assets/ThirdParty/Kenney/Characters/character-a.fbx";

        // Raum: x -5..5, z -4..4, Höhe 3,2 m; Tür in der Südwand, Arbeitsplatz an der Ostwand.
        const float RoomW = 10f, RoomD = 8f, RoomH = 3.2f, Wall = 0.2f;

        // Kenney-Modelle schauen mit ihrer Vorderseite nach +Z (Annahme); bei falscher Ausrichtung hier drehen.
        const float ModellGierOffset = 0f;

        sealed class Mats
        {
            public Material Wand, Boden, Decke, Rack, RackFront, RackEinschub, Led, Kabel, Tray, Akzent,
                Tuer, Kork, Papier, Geraet, GeraetDisplay, Monitor;
        }

        [MenuItem("Staffel 2/Serverraum bauen")]
        public static void BuildServerRoom()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var m = CreateMaterials();

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.15f, 0.14f);
            RenderSettings.skybox = null;

            var raum = new GameObject("Raum").transform;
            BuildRoom(raum, m);

            var racks = new GameObject("Racks").transform;
            var serverschrank = BuildRacks(racks, m);
            BuildCableTrays(new GameObject("Kabelwege").transform, m);

            var arbeitsplatz = new GameObject("Arbeitsplatz").transform;
            var deskPos = BuildWorkstation(arbeitsplatz, m);

            BuildAktenwand(new GameObject("Aktenwand").transform, m);
            BuildLights(new GameObject("Licht").transform);
            BuildPlayer(deskPos);

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
                Wand = Mat("Wand", Hex("20231e"), 0.15f),
                Boden = Mat("Boden", Hex("2a2620"), 0.3f),
                Decke = Mat("Decke", Hex("171914"), 0.1f),
                Rack = Mat("Rack", Hex("1b1a18"), 0.4f),
                RackFront = Mat("RackFront", Hex("272520"), 0.35f),
                RackEinschub = Mat("RackEinschub", Hex("121211"), 0.5f),
                Led = Mat("Led", Hex("0d0d0d"), 0.6f),
                Kabel = Mat("Kabel", Hex("0c0c0b"), 0.3f),
                Tray = Mat("Kabeltray", Hex("3a3834"), 0.45f),
                Akzent = Mat("Akzent", Hex("db4a26"), 0.3f, Hex("db4a26") * 0.6f),
                Tuer = Mat("Tuer", Hex("2e3028"), 0.2f),
                Kork = Mat("Aktenwand", Hex("4a3a28"), 0.05f),
                Papier = Mat("Papier", Hex("c9c3b0"), 0.05f),
                Geraet = Mat("Pager", Hex("1a1a1a"), 0.5f),
                GeraetDisplay = Mat("PagerDisplay", Hex("3a1208"), 0.2f, Hex("db4a26") * 1.2f),
                Monitor = Mat("Monitorflaeche", Hex("050505"), 0.6f),
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

        /// <summary>Ein Rack (0,6 × 2,0 × 1,0 m); die Front liegt lokal bei -Z.</summary>
        static Transform BuildRack(string name, Transform parent, Vector3 pos, float yaw, Mats m, bool interaktiv)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(0, yaw, 0);

            var body = Box("Gehaeuse", root, new Vector3(0, 1f, 0), new Vector3(0.6f, 2.0f, 1.0f), m.Rack);
            var front = Box("Front", root, new Vector3(0, 1f, -0.51f), new Vector3(0.54f, 1.9f, 0.03f), m.RackFront, false);
            for (var i = 0; i < 7; i++)
                Box("Einschub" + (i + 1), root, new Vector3(0, 0.25f + i * 0.17f, -0.535f), new Vector3(0.5f, 0.12f, 0.02f), m.RackEinschub, false);

            var leds = new List<Renderer>();
            for (var i = 0; i < 4; i++)
            {
                var led = Box("LED" + (i + 1), root, new Vector3(-0.18f + i * 0.07f, 1.82f, -0.54f), new Vector3(0.035f, 0.035f, 0.03f), m.Led, false);
                leds.Add(led.GetComponent<Renderer>());
            }
            root.gameObject.AddComponent<RackStatusLights>().Configure(leds.ToArray());

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
            foreach (var z in new[] { 3.0f, 1.0f })
            {
                var name = z > 2f ? "A" : "B";
                const float y = 2.75f;
                Box("Tray_" + name, root, new Vector3(-0.9f, y, z), new Vector3(3.8f, 0.05f, 0.45f), m.Tray, false);
                Line("TrayRand_" + name + "1", root, new Vector3(-2.8f, y + 0.04f, z - 0.22f), new Vector3(1.0f, y + 0.04f, z - 0.22f), 0.03f, m.Tray);
                Line("TrayRand_" + name + "2", root, new Vector3(-2.8f, y + 0.04f, z + 0.22f), new Vector3(1.0f, y + 0.04f, z + 0.22f), 0.03f, m.Tray);
                for (var k = 0; k < 3; k++)
                    Line("Kabel_" + name + k, root, new Vector3(-2.7f, y + 0.05f, z - 0.12f + k * 0.12f), new Vector3(0.9f, y + 0.05f, z - 0.12f + k * 0.12f), 0.04f, m.Kabel);
                for (var i = 0; i < 4; i++)
                {
                    var x = -2.4f + 0.9f * i;
                    Line("Abgang_" + name + i, root, new Vector3(x + 0.1f, y, z + 0.1f), new Vector3(x + 0.1f, 2.0f, z + 0.1f), 0.03f, m.Kabel);
                }
            }
        }

        // ---------------------------------------------------------------- Arbeitsplatz

        /// <summary>Baut Schreibtisch, Stuhl, Monitor, Lampe, Pager und Alex-Platzhalter; liefert die Tischmitte.</summary>
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
            BuildMonitorFace(root, m, sb, front);

            // Terminal: Interaktionskörper um den Bildschirm.
            var terminal = AddCollider("Terminal", root, sb);
            terminal.AddComponent<Interactable>().Configure("terminal", "E – Terminal benutzen", screen.GetComponentsInChildren<Renderer>());

            // Pager: kleines Gerät links der Tastatur (aus Sicht des Sitzenden).
            var pager = Box("Pager", root, new Vector3(db.center.x - db.size.x * 0.1f, top + 0.015f, deskZ + 0.4f),
                new Vector3(0.07f, 0.03f, 0.11f), m.Geraet);
            pager.transform.localRotation = Quaternion.Euler(0, yaw + 20f, 0);
            Box("PagerDisplay", pager.transform, new Vector3(0, 0.52f, 0.1f), new Vector3(0.7f, 0.05f, 0.4f), m.GeraetDisplay, false);
            pager.AddComponent<Interactable>().Configure("pager", "E – Pager ansehen", new[] { pager.GetComponent<Renderer>() });

            // Alex: Platzhalter neben dem Schreibtisch, ohne Logik.
            var alex = Model(AlexPath, "AlexPlatzhalter", null, 0f, 1.75f, 1f);
            alex.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, 0f, -0.7f)) * Quaternion.Euler(0, ModellGierOffset, 0);
            PlaceOnFloor(alex, new Vector3(db.center.x - 0.2f, 0f, db.max.z + 0.7f));
            AddCollider("Kollision_Alex", alex.transform.parent, WorldBounds(alex));

            return db.center;
        }

        static void BuildMonitorFace(Transform root, Mats m, Bounds sb, Vector3 front)
        {
            var faceW = Mathf.Abs(front.x) > 0.5f ? sb.size.z : sb.size.x;
            var w = faceW * 0.78f;
            var h = sb.size.y * 0.55f;
            var halfDepth = Mathf.Abs(front.x) * sb.extents.x + Mathf.Abs(front.z) * sb.extents.z;
            var pos = sb.center + front * (halfDepth + 0.004f) + Vector3.up * (sb.size.y * 0.12f);

            var go = new GameObject("Monitorflaeche");
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
            label.text = "TERMINAL";
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = 0.1f;
            label.fontSizeMax = 3f;

            var monitor = go.AddComponent<MonitorScreen>();
            monitor.Configure(quad.GetComponent<Renderer>(), label);
            monitor.SetState(MonitorState.Bereit);
        }

        // ---------------------------------------------------------------- Aktenwand

        static void BuildAktenwand(Transform root, Mats m)
        {
            var x = -RoomW / 2 + 0.025f;
            var board = Box("Pinnwand", root, new Vector3(x, 1.6f, -0.5f), new Vector3(0.05f, 1.4f, 3.0f), m.Kork);

            // Zettel ohne Inhalt; Lage nur zur Optik.
            var papers = new (float z, float y, float rot)[]
            {
                (-1.7f, 1.95f, 4f), (-1.2f, 1.75f, -6f), (-0.65f, 2.0f, 3f), (-0.1f, 1.7f, -3f),
                (0.45f, 1.95f, 5f), (0.95f, 1.65f, -4f), (-1.45f, 1.35f, -2f), (0.15f, 1.3f, 6f),
            };
            foreach (var (z, y, rot) in papers)
            {
                var p = Box("Zettel", root, new Vector3(x + 0.03f, y, z), new Vector3(0.012f, 0.28f, 0.2f), m.Papier, false);
                p.transform.localRotation = Quaternion.Euler(rot, 0, 0);
            }

            // Eine einzige Akzentschnur zwischen zwei Zetteln.
            Line("Schnur", root, new Vector3(x + 0.045f, 1.95f, -1.7f), new Vector3(x + 0.045f, 1.7f, -0.65f), 0.006f, m.Akzent);

            root.gameObject.AddComponent<Interactable>().Configure("fallakte", "E – Fallakte ansehen");
        }

        // ---------------------------------------------------------------- Licht

        static void BuildLights(Transform root)
        {
            var warm = new Color(1f, 0.80f, 0.58f);
            AddLight(root, "Deckenlicht_Gang", LightType.Point, new Vector3(-1.1f, 2.9f, 2.0f), warm, 1.4f, 6f);
            AddLight(root, "Deckenlicht_Mitte", LightType.Point, new Vector3(-1.0f, 3.0f, -1.5f), warm, 1.1f, 7f);
            AddLight(root, "Aktenwand_Licht", LightType.Point, new Vector3(-3.8f, 2.7f, -0.5f), warm, 1.0f, 4.5f);

            var desk = AddLight(root, "Arbeitsplatz_Spot", LightType.Spot, new Vector3(3.0f, 2.9f, -1.8f), warm, 3.0f, 6f);
            desk.transform.rotation = Quaternion.LookRotation(new Vector3(1.3f, -2.1f, 0f));
            desk.spotAngle = 85f;

            AddLight(root, "Akzent_Orange", LightType.Point, new Vector3(-4.0f, 2.4f, -3.4f), Hex("db4a26"), 1.6f, 3.5f);
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
