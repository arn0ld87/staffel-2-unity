using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Staffel2.Game.Editor
{
    /// <summary>
    /// Backt die Zeichen der Spielschrift fest ein (statischer Atlas). Dynamische Atlanten wachsen
    /// bei jedem Play-Mode-Lauf und erzeugen Git-Rauschen. Eingebacken werden Latin-1, Satz-,
    /// Pfeil-, Rahmen- und Blockzeichen sowie jedes Zeichen aus Episodendaten und Spielcode.
    /// Was JetBrains Mono nicht enthält, kommt statisch in die TMP-Fallback-Schrift.
    /// Grenze: Ein Zeichen, das in keiner der beiden Schriften eingebacken ist, erscheint als Kästchen.
    /// </summary>
    public static class SchriftAtlas
    {
        const string Haupt = "Assets/Resources/Fonts/JetBrainsMono SDF.asset";
        const string Fallback = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";

        [MenuItem("Staffel 2/Schrift-Atlas backen")]
        public static string Backen()
        {
            var haupt = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Haupt);
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fallback);
            if (haupt == null || fallback == null) return "Schrift-Asset fehlt";

            var zeichen = new SortedSet<uint>();
            void Bereich(int von, int bis) { for (int c = von; c <= bis; c++) zeichen.Add((uint)c); }
            Bereich(0x20, 0x7E);
            Bereich(0xA0, 0xFF);
            Bereich(0x2010, 0x2027);
            Bereich(0x2030, 0x203A);
            Bereich(0x20AC, 0x20AC);
            Bereich(0x2190, 0x2199);
            Bereich(0x2500, 0x25FF);
            Bereich(0x2713, 0x2718);
            foreach (var datei in Quelltexte())
                foreach (var cp in Codepunkte(File.ReadAllText(datei)))
                    zeichen.Add(cp);

            var fehlend = Einbacken(haupt, zeichen.ToArray());
            var ganzFehlend = Einbacken(fallback, fehlend);
            AssetDatabase.SaveAssets();
            string rest = ganzFehlend.Length == 0 ? "" : ": " + string.Join(" ", ganzFehlend.Take(20).Select(c => $"U+{c:X4}"));
            return $"JetBrains Mono {haupt.characterTable.Count} Zeichen, Fallback {fallback.characterTable.Count}, in keiner Schrift {ganzFehlend.Length}{rest}";
        }

        static uint[] Einbacken(TMP_FontAsset font, uint[] zeichen)
        {
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.ClearFontAssetData(true);
            uint[] fehlend = new uint[0];
            if (zeichen.Length > 0) font.TryAddCharacters(zeichen, out fehlend);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(font);
            return fehlend ?? new uint[0];
        }

        static IEnumerable<string> Quelltexte()
        {
            yield return Path.Combine(Application.streamingAssetsPath, "staffel2", "staffel2.json");
            foreach (var cs in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
                yield return cs;
        }

        static IEnumerable<uint> Codepunkte(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.ConvertToUtf32(text, i);
                if (char.IsHighSurrogate(text[i])) i++;
                if (cp >= 0x20 && cp != 0x7F && !(cp >= 0x80 && cp < 0xA0)) yield return (uint)cp;
            }
        }
    }
}
