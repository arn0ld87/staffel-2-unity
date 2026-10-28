using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Staffel2.Game.Editor
{
    /// <summary>
    /// Richtet die zweite Alex-Figur (aus TRELLIS, tools/blender/alex_trellis_rig.py) ein und stellt sie
    /// zusätzlich zur bestehenden Figur in den Serverraum: FBX-Import (Generic, Blendshapes, vier Clips),
    /// Materialien, AnimatorController, Prefab unter Resources/Alex, Objekt „AlexRaumFigurNeu“.
    /// </summary>
    public static class AlexTrellisBuilder
    {
        private const string Fbx = "Assets/Art/Alex/Alex_Trellis.fbx";
        private const string Textur = "Assets/Art/Alex/Alex_Trellis_BaseColor.png";
        private const string Material = "Assets/Art/Alex/Alex_Trellis.mat";
        private const string MundMaterial = "Assets/Art/Alex/Alex_Trellis_Mundraum.mat";
        private const string Ordner = "Assets/Resources/Alex";
        private const string Controller = Ordner + "/AlexTrellis.controller";
        private const string Prefab = Ordner + "/AlexTrellis.prefab";
        public const string RaumName = "AlexRaumFigurNeu";

        private static readonly (string take, string name, string zustand)[] Clips =
        {
            ("Alex_Idle", "Idle", "Ruhe"), ("Alex_Explain", "Explain", "Erklaeren"),
            ("Alex_Listening", "Listening", "Zuhoeren"), ("Alex_Zeigen", "Zeigen", "Zeigen"),
        };

        [MenuItem("Staffel 2/Alex (TRELLIS) einrichten")]
        public static string Einrichten()
        {
            Directory.CreateDirectory(Ordner);

            AssetDatabase.ImportAsset(Textur, ImportAssetOptions.ForceUpdate);
            var mat = MaterialHolen(Material, "Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Textur));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.08f);
            mat.SetFloat("_Metallic", 0f);
            var mund = MaterialHolen(MundMaterial, "Universal Render Pipeline/Unlit");
            mund.SetColor("_BaseColor", new Color(0.05f, 0.015f, 0.015f));
            EditorUtility.SetDirty(mat);
            EditorUtility.SetDirty(mund);

            AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate);
            var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.importBlendShapes = true;
            imp.importAnimation = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Alex_Trellis_Mat"), mat);
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Alex_Mundraum"), mund);
            // Nur die vier Haltungen; die vom Exporter erzeugten Shape-Key-Takes („Key|…“) weglassen.
            imp.clipAnimations = imp.defaultClipAnimations
                .Select(c => (c, def: Clips.FirstOrDefault(d => c.takeName == d.take)))
                .Where(x => x.def.take != null)
                .Select(x => { x.c.name = x.def.name; x.c.loopTime = x.def.name != "Zeigen"; return x.c; })
                .ToArray();
            imp.SaveAndReimport();

            var animClips = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToList();
            AssetDatabase.DeleteAsset(Controller);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(Controller);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var d in Clips)
            {
                var zustand = sm.AddState(d.zustand);
                zustand.motion = animClips.FirstOrDefault(c => c.name == d.name) ?? animClips.FirstOrDefault(c => c.name == "Idle");
                if (d.zustand == "Ruhe") sm.defaultState = zustand;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx));
            inst.name = "AlexTrellis";
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();  // kein ??: Unity-Objekte haben ein eigenes null
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (inst.GetComponent<AlexFigur>() == null) inst.AddComponent<AlexFigur>();
            PrefabUtility.SaveAsPrefabAsset(inst, Prefab);
            Object.DestroyImmediate(inst);

            // In den Raum: freie Fläche zwischen Eingang und Arbeitsplatz, Blick zum Startpunkt des Spielers.
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Serverraum.unity");
            var alt = GameObject.Find(RaumName);
            if (alt != null) Object.DestroyImmediate(alt);
            var raum = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            raum.name = RaumName;
            var pos = new Vector3(-1.2f, 0f, -1.4f);
            var blick = new Vector3(-3.3f, 0f, -3.0f) - pos;
            raum.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(blick));
            var col = raum.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.90f, 0f);
            col.size = new Vector3(0.55f, 1.80f, 0.40f);
            raum.AddComponent<Staffel2.World.Interactable>()
                .Configure("alex", "E – Mit Alex sprechen", raum.GetComponentsInChildren<Renderer>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            var smr = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Mesh>().Select(m => $"{m.name}:{m.blendShapeCount}");
            return $"Clips: {string.Join(", ", animClips.Select(c => c.name))} | Meshes: {string.Join(", ", smr)} | {RaumName} platziert";
        }

        private static Material MaterialHolen(string pfad, string shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            if (m != null) return m;
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, pfad);
            return m;
        }
    }
}
