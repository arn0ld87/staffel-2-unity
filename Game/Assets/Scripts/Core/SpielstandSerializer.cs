// JSON-Serialisierung des Spielstands mit ausdrücklichem `version`-Feld und Migrationshaken.
// Kaputte Einträge fallen feldweise weg (wie parseStand in staffelStore.ts); eine neuere Version, als
// dieser Code kennt, wirft, damit ein Spielstand nie still beschädigt wird.
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Staffel2.Core
{
    public class SpielstandVersionException : Exception
    {
        public SpielstandVersionException(string message) : base(message) { }
    }

    public static class SpielstandSerializer
    {
        /// <summary>
        /// Migrationen: Schlüssel n hebt die Roh-JSON-Struktur von Version n auf n + 1. Eingetragen wird
        /// beim Anheben von AktuelleVersion; es gibt noch keine.
        /// </summary>
        public static readonly Dictionary<int, Func<JObject, JObject>> Migrationen = new Dictionary<int, Func<JObject, JObject>>();

        private static readonly JsonSerializerSettings Einstellungen = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };

        private static readonly JsonSerializer Leser = JsonSerializer.Create(Einstellungen);

        public static string Serialize(Spielstand stand)
        {
            stand.Version = Spielstand.AktuelleVersion;
            return JsonConvert.SerializeObject(stand, Einstellungen);
        }

        /// <summary>Liest einen Spielstand; leerer oder unlesbarer Text ergibt einen leeren Stand.</summary>
        public static Spielstand Deserialize(string json)
        {
            JObject wurzel;
            try { wurzel = JObject.Parse(json); }
            catch (Exception e) when (e is JsonException || e is ArgumentNullException) { return new Spielstand(); }

            var version = wurzel["version"] is JValue v && v.Type == JTokenType.Integer ? (int)v : 1;
            if (version > Spielstand.AktuelleVersion)
                throw new SpielstandVersionException($"Spielstand-Version {version} ist neuer als {Spielstand.AktuelleVersion}.");
            while (version < Spielstand.AktuelleVersion)
            {
                if (!Migrationen.TryGetValue(version, out var schritt))
                    throw new SpielstandVersionException($"Keine Migration von Spielstand-Version {version}.");
                wurzel = schritt(wurzel);
                version++;
            }

            var stand = new Spielstand { Version = version };
            if (wurzel["akte"] is JObject akte)
            {
                foreach (var eintrag in akte.Properties())
                    if (IstEintrag(eintrag.Value)) stand.Akte[eintrag.Name] = eintrag.Value.ToObject<AkteEintrag>(Leser);
            }
            if (wurzel["zurueckgesetztAm"] is JValue z && z.Type == JTokenType.Integer) stand.ZurueckgesetztAm = (long)z;
            stand.Laufend = LeseLaufend(wurzel["laufend"]);
            return stand;
        }

        private static bool IstEintrag(JToken t) =>
            t is JObject o
            && Ist(o["durationMs"], JTokenType.Integer, JTokenType.Float)
            && Ist(o["withSolution"], JTokenType.Boolean)
            && Ist(o["hintsUsed"], JTokenType.Integer, JTokenType.Float)
            && Ist(o["finishedAt"], JTokenType.Integer, JTokenType.Float)
            && (o["vorwurfVersuche"] == null || o["vorwurfVersuche"].Type == JTokenType.Null
                || Ist(o["vorwurfVersuche"], JTokenType.Integer, JTokenType.Float));

        private static bool Ist(JToken t, params JTokenType[] typen) => t != null && Array.IndexOf(typen, t.Type) >= 0;

        private static LaufendeErmittlung LeseLaufend(JToken token)
        {
            if (!(token is JObject o) || !(o["episodeId"] is JValue id) || id.Type != JTokenType.String) return null;
            try
            {
                var laufend = o.ToObject<LaufendeErmittlung>(Leser);
                var zustandToken = o["zustand"] as JObject;
                if (laufend == null || zustandToken == null) return null;
                // Ältere Stände kennen sperreRestPruefungen nicht (mitZustandsDefaults).
                int? rest = zustandToken["sperreRestPruefungen"] is JValue r && r.Type == JTokenType.Integer ? (int)r : (int?)null;
                laufend.Zustand = ErmittlungsEngine.MitZustandsDefaults(laufend.Zustand, rest);
                return laufend;
            }
            catch (JsonException) { return null; }
        }
    }
}
