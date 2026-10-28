using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Staffel2.Game.Editor
{
    /// <summary>
    /// Baut die spielbare Szene vollständig und reproduzierbar: Serverraum (World-Builder),
    /// Alex-Prefab, GameRoot, Startszene für den Play Mode.
    /// </summary>
    public static class SpielSzeneBuilder
    {
        private const string Szene = "Assets/Scenes/Serverraum.unity";

        [MenuItem("Staffel 2/Spielszene komplett bauen")]
        public static string Alles()
        {
            Staffel2.World.Editor.SceneBuilder.BuildServerRoom();
            string alex = AlexBuilder.Einrichten() + " | " + AlexTrellisBuilder.Einrichten();
            var scene = EditorSceneManager.OpenScene(Szene);
            if (Object.FindFirstObjectByType<GameRoot>() == null) new GameObject("GameRoot").AddComponent<GameRoot>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Szene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Szene, true) };
            return "Szene gebaut. " + alex;
        }
    }
}
