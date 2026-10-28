using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Staffel2.Game.Editor
{
    /// <summary>
    /// Richtet die Alex-Büste für das Spiel ein (Editor-API, kein YAML von Hand):
    /// FBX-Import (Generic, Blendshapes, loopende Clips), Material, AnimatorController mit
    /// Ruhe/Erklaeren/Zuhoeren, Prefab unter Resources/Alex, Ebene „Dialog“. Entfernt den
    /// Kenney-Platzhalter aus dem Serverraum.
    /// </summary>
    public static class AlexBuilder
    {
        private const string Fbx = "Assets/Art/Alex/Alex_Bueste.fbx";
        private const string Textur = "Assets/Art/Alex/Alex_BaseColor.png";
        private const string Material = "Assets/Art/Alex/Alex_Comic.mat";
        private const string Ordner = "Assets/Resources/Alex";
        private const string Controller = Ordner + "/AlexDialog.controller";
        private const string Prefab = Ordner + "/AlexDialog.prefab";

        [MenuItem("Staffel 2/Alex einrichten")]
        public static string Einrichten()
        {
            Directory.CreateDirectory(Ordner);
            EbeneBenennen(AlexDialogStudio.Ebene, "Dialog");

            // Material: Lit mit wenig Glanz, damit die gemalte Schattierung trägt.
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Material);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, Material);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Textur));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.08f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);

            var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.importBlendShapes = true;
            imp.importAnimation = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Alex_MatteComic"), mat);
            // Nur die drei Haltungen; die vom Exporter erzeugten Shape-Key-Takes („Key|…“) weglassen.
            var clips = imp.defaultClipAnimations
                .Where(c => c.takeName.Contains("Alex_Idle") || c.takeName.Contains("Alex_Explain") || c.takeName.Contains("Alex_Listening"))
                .Select(c => { c.loopTime = true; c.name = Kurzname(c.takeName); return c; })
                .GroupBy(c => c.name).Select(g => g.First()).ToArray();
            imp.clipAnimations = clips;
            imp.SaveAndReimport();

            var animClips = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            AnimationClip Clip(string n) => animClips.FirstOrDefault(c => c.name == n);

            AssetDatabase.DeleteAsset(Controller);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(Controller);
            var sm = ctrl.layers[0].stateMachine;
            var ruhe = sm.AddState("Ruhe"); ruhe.motion = Clip("Idle");
            var erkl = sm.AddState("Erklaeren"); erkl.motion = Clip("Explain") ?? Clip("Idle");
            var zuh = sm.AddState("Zuhoeren"); zuh.motion = Clip("Listening") ?? Clip("Idle");
            sm.defaultState = ruhe;

            var modell = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(modell);
            inst.name = "AlexDialog";
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();  // kein ??: Unity-Objekte haben ein eigenes null
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (inst.GetComponent<AlexFigur>() == null) inst.AddComponent<AlexFigur>();
            PrefabUtility.SaveAsPrefabAsset(inst, Prefab);
            Object.DestroyImmediate(inst);

            // Kenney-Platzhalter aus dem Raum nehmen: Alex meldet sich über den Pager.
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Serverraum.unity");
            bool entfernt = false;
            foreach (var name in new[] { "AlexPlatzhalter", "Kollision_Alex" })
            {
                var go = GameObject.Find(name);
                if (go != null) { Object.DestroyImmediate(go); entfernt = true; }
            }
            if (entfernt) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }

            AssetDatabase.SaveAssets();
            var smr = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Mesh>().Select(m => $"{m.name}:{m.blendShapeCount}");
            return $"Clips: {string.Join(", ", animClips.Select(c => c.name))} | Meshes: {string.Join(", ", smr)} | Platzhalter entfernt: {entfernt}";
        }

        private static string Kurzname(string take) =>
            take.Contains("Idle") ? "Idle" : take.Contains("Explain") ? "Explain" : take.Contains("Listening") ? "Listening" : take;

        private static void EbeneBenennen(int index, string name)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var ebenen = tm.FindProperty("layers");
            var e = ebenen.GetArrayElementAtIndex(index);
            if (e.stringValue != name) { e.stringValue = name; tm.ApplyModifiedProperties(); }
        }
    }
}
