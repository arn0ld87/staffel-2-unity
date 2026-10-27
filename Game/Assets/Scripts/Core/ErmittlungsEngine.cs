// Ermittlungslogik (Port von ermittlung.ts): rein, ohne Uhr, Zufall oder VM. Sie sagt, welche
// Prüfungen nach einem Prompt fällig sind, baut daraus ein Bündel für einen RUN, liest dessen
// Ausgabe und führt Zustand und Vorwurf weiter. Übergebene Objekte bleiben unverändert.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Staffel2.Core
{
    public static class ErmittlungsEngine
    {
        /// <summary>So viele vollständige Prüfungen nach einer Sperre, bis sie spätestens endet.</summary>
        public const int SperrePruefungen = 3;
        public const int MindestSchrittarten = 3;
        public const int DiagnoseMaxZeichen = 200;
        public const int AntwortTimeoutMs = 10_000;
        public const string PruefIdVerstoss = "e.verstoss";

        public static string PruefIdSchritt(string spurId, string schrittId) => $"s.{spurId}.{schrittId}";
        public static string PruefIdDiagnose(string spurId, string schrittId) => $"d.{spurId}.{schrittId}";
        public static string PruefIdBeleg(string belegId) => $"b.{belegId}";

        // ---- Zeichenmuster (ASCII, ohne die Regex-Eigenheit, dass `$` vor einem End-\n passt) ----

        private static bool IstIdZeichen(char c, bool punkt) =>
            (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || (punkt && c == '.');

        private static bool PasstId(string s, bool punkt, int max)
        {
            if (s == null || s.Length < 1 || s.Length > max) return false;
            foreach (var c in s) if (!IstIdZeichen(c, punkt)) return false;
            return true;
        }

        /// <summary>/^[a-z0-9-]{1,40}$/</summary>
        public static bool IstGueltigeId(string id) => PasstId(id, false, 40);

        /// <summary>/^[A-Za-z0-9 .:_/-]+$/ — erlaubte Zeichen einer getrimmten Antwort.</summary>
        public static bool IstGueltigeAntwort(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var c in text)
            {
                var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                    || c == ' ' || c == '.' || c == ':' || c == '_' || c == '/' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        private static bool NurZiffern(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private static long ZahlOderMax(string ziffern) =>
            long.TryParse(ziffern, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
                ? n : long.MaxValue;

        // ---- Datenprüfung ----

        /// <summary>Prüft die Daten einer Episode und sammelt deutsche Fehlermeldungen; leer heißt gültig.</summary>
        public static List<string> PruefeDaten(Ermittlung e)
        {
            var fehler = new List<string>();
            var spuren = e.Spuren;
            var ansichten = e.Ansichten;
            var vorwurf = e.Vorwurf;
            var rahmen = e.Einsatzrahmen;

            foreach (var spur in spuren)
            {
                if (!IstGueltigeId(spur.Id)) fehler.Add($"Ungültige ID: Spur \"{spur.Id}\"");
                foreach (var schritt in spur.Schritte)
                    if (!IstGueltigeId(schritt.Id)) fehler.Add($"Ungültige ID: Schritt \"{schritt.Id}\"");
            }
            foreach (var beleg in vorwurf.Belege)
                if (!IstGueltigeId(beleg.Id)) fehler.Add($"Ungültige ID: Belegregel \"{beleg.Id}\"");
            foreach (var person in vorwurf.Verdaechtige)
                if (!IstGueltigeId(person.Id)) fehler.Add($"Ungültige ID: Verdächtige \"{person.Id}\"");
            foreach (var ansicht in ansichten)
            {
                if (!IstGueltigeId(ansicht.Id)) fehler.Add($"Ungültige ID: Ansicht \"{ansicht.Id}\"");
                foreach (var hotspot in ansicht.Hotspots)
                    if (!IstGueltigeId(hotspot.Id)) fehler.Add($"Ungültige ID: Hotspot \"{hotspot.Id}\"");
            }

            var spurIds = new HashSet<string>();
            foreach (var spur in spuren)
            {
                if (!spurIds.Add(spur.Id)) fehler.Add($"Doppelte ID: Spur \"{spur.Id}\"");
                var schrittIds = new HashSet<string>();
                foreach (var schritt in spur.Schritte)
                    if (!schrittIds.Add(schritt.Id)) fehler.Add($"Doppelte ID: Schritt \"{schritt.Id}\"");
            }
            var belegIds = new HashSet<string>();
            foreach (var beleg in vorwurf.Belege)
                if (!belegIds.Add(beleg.Id)) fehler.Add($"Doppelte ID: Belegregel \"{beleg.Id}\"");
            var personIds = new HashSet<string>();
            foreach (var person in vorwurf.Verdaechtige)
                if (!personIds.Add(person.Id)) fehler.Add($"Doppelte ID: Verdächtige \"{person.Id}\"");
            var ansichtIds = new HashSet<string>();
            foreach (var ansicht in ansichten)
            {
                if (!ansichtIds.Add(ansicht.Id)) fehler.Add($"Doppelte ID: Ansicht \"{ansicht.Id}\"");
                var hotspotIds = new HashSet<string>();
                foreach (var hotspot in ansicht.Hotspots)
                    if (!hotspotIds.Add(hotspot.Id)) fehler.Add($"Doppelte ID: Hotspot \"{hotspot.Id}\"");
            }

            if (!personIds.Contains(vorwurf.TaeterId)) fehler.Add($"Unbekannte Täter-ID: \"{vorwurf.TaeterId}\"");

            foreach (var beleg in vorwurf.Belege)
            {
                if (!personIds.Contains(beleg.Stuetzt))
                    fehler.Add($"Beleg \"{beleg.Id}\" stützt keine Verdächtige: \"{beleg.Stuetzt}\"");
                if (!spurIds.Contains(beleg.SpurId))
                    fehler.Add($"Beleg \"{beleg.Id}\" gehört zu keiner Spur: \"{beleg.SpurId}\"");
            }

            var gegenTaeter = vorwurf.Belege.Where(b => b.Stuetzt == vorwurf.TaeterId).ToList();
            if (vorwurf.MindestBelege < 1 || vorwurf.MindestBelege > gegenTaeter.Count)
                fehler.Add($"mindestBelege muss zwischen 1 und {gegenTaeter.Count} liegen");

            if (vorwurf.JedeSpur)
            {
                var spurenMitBeleg = new HashSet<string>(gegenTaeter.Select(b => b.SpurId));
                var fehlend = spuren.Where(s => !spurenMitBeleg.Contains(s.Id)).Select(s => s.Id).ToList();
                if (fehlend.Count > 0)
                    fehler.Add($"jedeSpur verlangt einen Beleg gegen den Täter aus jeder Spur; es fehlt: {string.Join(", ", fehlend)}");
            }

            if (vorwurf.Verdaechtige.Count < 3) fehler.Add("Mindestens 3 Verdächtige nötig");

            foreach (var ansicht in ansichten)
                if (string.IsNullOrWhiteSpace(ansicht.Abfrage)) fehler.Add($"Ansicht \"{ansicht.Id}\": leere abfrage");
            foreach (var spur in spuren)
                foreach (var schritt in spur.Schritte)
                    if (string.IsNullOrWhiteSpace(schritt.Verify)) fehler.Add($"Schritt \"{spur.Id}/{schritt.Id}\": leeres verify");
            foreach (var beleg in vorwurf.Belege)
                if (string.IsNullOrWhiteSpace(beleg.Verify)) fehler.Add($"Belegregel \"{beleg.Id}\": leeres verify");
            if (string.IsNullOrWhiteSpace(rahmen.Verstoss)) fehler.Add("Einsatzrahmen: leeres verstoss");

            var arten = new HashSet<Schrittart>();
            foreach (var spur in spuren)
            {
                Schrittart? vorige = null;
                foreach (var schritt in spur.Schritte)
                {
                    var name = $"Schritt \"{spur.Id}/{schritt.Id}\"";
                    if (!Enum.IsDefined(typeof(Schrittart), schritt.Art))
                    {
                        fehler.Add($"{name}: Schrittart fehlt oder ist unbekannt");
                        vorige = null;
                        continue;
                    }
                    arten.Add(schritt.Art);
                    if (schritt.Art == Schrittart.Ablesen && schritt.Antwort == null)
                        fehler.Add($"{name}: ablesen braucht eine antwort");
                    else if (schritt.Art != Schrittart.Ablesen && schritt.Antwort != null)
                        fehler.Add($"{name}: antwort gibt es nur bei ablesen");
                    if (vorige.HasValue && schritt.Art == vorige.Value)
                        fehler.Add($"Spur \"{spur.Id}\": Schrittart \"{ArtName(schritt.Art)}\" steht zweimal direkt hintereinander (bei {schritt.Id})");
                    vorige = schritt.Art;
                }
            }
            if (arten.Count < MindestSchrittarten)
                fehler.Add($"Mindestens {MindestSchrittarten} verschiedene Schrittarten nötig");

            return fehler;
        }

        private static string ArtName(Schrittart art) => art.ToString().ToLowerInvariant();

        // ---- Zustand ----

        public static ErmittlungsZustand StartZustand() => new ErmittlungsZustand();

        /// <summary>
        /// Ergänzt SperreRestPruefungen für einen älter gespeicherten Zustand, der es nicht kennt
        /// (rest == null oder negativ): bei aktiver Sperre SperrePruefungen, sonst 0. Gibt eine Kopie zurück.
        /// </summary>
        public static ErmittlungsZustand MitZustandsDefaults(ErmittlungsZustand z, int? rest)
        {
            var kopie = z.Kopie();
            kopie.SperreRestPruefungen = rest.HasValue && rest.Value >= 0
                ? rest.Value
                : (z.SperreBis.HasValue ? SperrePruefungen : 0);
            return kopie;
        }

        private static int Erledigt(ErmittlungsZustand z, string spurId) =>
            z.Erledigt.TryGetValue(spurId, out var n) ? n : 0;

        /// <summary>Der Schritt an der Stelle Erledigt[spur.Id]; alle erledigt ergibt null.</summary>
        public static ErmittlungsSchritt AktuellerSchritt(Spur spur, ErmittlungsZustand z)
        {
            var index = Erledigt(z, spur.Id);
            return index >= 0 && index < spur.Schritte.Count ? spur.Schritte[index] : null;
        }

        /// <summary>Prüfungen für den nächsten Prompt: je Spur der aktuelle Schritt, alle Belege, der Rahmen.</summary>
        public static List<Pruefung> OffenePruefungen(Ermittlung e, ErmittlungsZustand z)
        {
            var pruefungen = new List<Pruefung>();
            if (z.Geloest) return pruefungen;
            foreach (var spur in e.Spuren)
            {
                var schritt = AktuellerSchritt(spur, z);
                if (schritt == null) continue;
                pruefungen.Add(new Pruefung { Id = PruefIdSchritt(spur.Id, schritt.Id), Skript = schritt.Verify });
                if (!string.IsNullOrEmpty(schritt.Diagnose))
                    pruefungen.Add(new Pruefung { Id = PruefIdDiagnose(spur.Id, schritt.Id), Skript = schritt.Diagnose, Diagnose = true });
            }
            foreach (var beleg in e.Vorwurf.Belege)
                pruefungen.Add(new Pruefung { Id = PruefIdBeleg(beleg.Id), Skript = beleg.Verify });
            if (!z.EinsatzrahmenVerletzt)
                pruefungen.Add(new Pruefung { Id = PruefIdVerstoss, Skript = e.Einsatzrahmen.Verstoss });
            return pruefungen;
        }

        // ---- Bündel und Antwort-Skript ----

        /// <summary>
        /// Ein Bash-Skript für einen einzigen RUN; jede Prüfung liefert eine Zeile mit ihrem Exit-Code.
        /// Ein identisches Skript steht nur einmal im Bündel und läuft einmal.
        /// </summary>
        public static string BuendelSkript(IReadOnlyList<Pruefung> pruefungen)
        {
            var anzahl = new Dictionary<string, int>();
            var ergebnisse = pruefungen.Where(p => !p.Diagnose).ToList();
            foreach (var p in ergebnisse) anzahl[p.Skript] = (anzahl.TryGetValue(p.Skript, out var n) ? n : 0) + 1;
            var ergebnisVar = new Dictionary<string, string>();
            var teile = new List<string>();
            foreach (var p in pruefungen)
            {
                if (!PasstId(p.Id, true, 90)) throw new ArgumentException($"Ungültige Prüf-ID: {p.Id}");
                if (p.Diagnose)
                {
                    // Nur die erste Zeile, ohne Steuerzeichen und gekürzt, mit Bash-Builtins; Fehler bleiben stumm.
                    teile.Add(
                        "dz=$( (\n" + p.Skript + "\n) 2>/dev/null </dev/null | { IFS= read -r dz; printf %s \"$dz\"; } )\n" +
                        "dz=${dz//[[:cntrl:]]/}\n" +
                        "printf 'diagnose\\t%s\\t%s\\n' '" + p.Id + "' \"${dz:0:" + DiagnoseMaxZeichen + "}\"");
                    continue;
                }
                var ergebnis = "\"$?\"";
                var lauf = "(\n" + p.Skript + "\n) >/dev/null 2>&1 </dev/null\n";
                if (anzahl[p.Skript] > 1)
                {
                    var bekannt = ergebnisVar.TryGetValue(p.Skript, out var b) ? b : null;
                    var name = bekannt ?? "r" + ergebnisVar.Count;
                    ergebnisVar[p.Skript] = name;
                    ergebnis = "\"$" + name + "\"";
                    lauf = bekannt == null ? lauf + name + "=$?\n" : "";
                }
                teile.Add(lauf + "printf 'pruefung\\t%s\\t%s\\n' '" + p.Id + "' " + ergebnis);
            }
            teile.Add("printf 'ende\\t%s\\n' '" + ergebnisse.Count + "'");
            return string.Join("\n", teile) + "\n";
        }

        /// <summary>
        /// Skript für die Prüfung einer Antwort: Die Antwort geht nur als Base64 in den Text und steht der
        /// Prüfung (Subshell, stumm, Exit-Code wird durchgereicht) in $ANTWORT zur Verfügung.
        /// </summary>
        public static string AntwortSkript(string pruefung, string antwort)
        {
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(antwort));
            return "ANTWORT=$(printf %s '" + base64 + "' | base64 -d)\nexport ANTWORT\n(\n" + pruefung + "\n) >/dev/null 2>&1 </dev/null\n";
        }

        /// <summary>Meldung der Gast-Diagnose: ohne Steuerzeichen, auf höchstens DiagnoseMaxZeichen gekürzt.</summary>
        private static string BereinigeDiagnose(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
                if (!(c <= '\u001f' || (c >= '\u007f' && c <= '\u009f'))) sb.Append(c);
            var s = sb.ToString();
            return s.Length > DiagnoseMaxZeichen ? s.Substring(0, DiagnoseMaxZeichen) : s;
        }

        /// <summary>Liest die Ausgabe eines Bündels; vollständig nur mit passender Schlusszahl.</summary>
        public static PruefungsAusgabe LeseBuendelAusgabe(string text)
        {
            var ausgabe = new PruefungsAusgabe();
            var gelesen = 0;
            long? schluss = null;
            foreach (var roh in text.Split('\n'))
            {
                var zeile = roh.EndsWith("\r", StringComparison.Ordinal) ? roh.Substring(0, roh.Length - 1) : roh;
                var teile = zeile.Split('\t');
                if (teile.Length == 3 && teile[0] == "pruefung" && NurZiffern(teile[2]))
                {
                    ausgabe.Ergebnisse[teile[1]] = teile[2] == "0";
                    gelesen++;
                }
                else if (teile.Length >= 3 && teile[0] == "diagnose")
                {
                    ausgabe.Diagnosen[teile[1]] = BereinigeDiagnose(string.Join("\t", teile.Skip(2)));
                }
                else if (teile.Length == 2 && teile[0] == "ende" && NurZiffern(teile[1]))
                {
                    schluss = ZahlOderMax(teile[1]);
                }
            }
            ausgabe.Vollstaendig = schluss.HasValue && schluss.Value == gelesen;
            return ausgabe;
        }

        // ---- Fortschritt ----

        /// <summary>Alle Schritte aller Spuren sind erledigt.</summary>
        public static bool AlleSpurenErledigt(Ermittlung e, ErmittlungsZustand z) =>
            e.Spuren.All(spur => AktuellerSchritt(spur, z) == null);

        /// <summary>Pager-Text für einen richtigen Vorwurf, solange noch Schritte offen sind.</summary>
        public static IReadOnlyList<string> OffeneSchritteText(Ermittlung e, ErmittlungsZustand z)
        {
            var offen = new List<string>();
            foreach (var spur in e.Spuren)
            {
                var schritt = AktuellerSchritt(spur, z);
                if (schritt != null) offen.Add($"{schritt.Title} (Spur {spur.Titel})");
            }
            return new[]
            {
                $"Der Vorwurf sitzt. Bevor der Fall abgeschlossen ist, fehlt noch: {string.Join("; ", offen)}.",
                "Die Schritte prüft das System weiter; der Abschluss folgt, sobald alles erledigt ist.",
            };
        }

        /// <summary>Schließt ab, sobald ein richtiger Vorwurf und alle Spuren zusammenkommen.</summary>
        private static ErmittlungsZustand SchliesseAb(Ermittlung e, ErmittlungsZustand z, List<ErmittlungsEreignis> ereignisse)
        {
            if (!z.VorwurfRichtig || z.Geloest || !AlleSpurenErledigt(e, z)) return z;
            ereignisse.Add(ErmittlungsEreignis.Einfach(EreignisArt.FallGeloest));
            var neu = z.Kopie();
            neu.Geloest = true;
            neu.SperreBis = null;
            neu.SperreRestPruefungen = 0;
            return neu;
        }

        /// <summary>Führt eine vollständige Ausgabe in den Zustand; unvollständige Ausgaben ändern nichts.</summary>
        public static PruefAnwendung WendePruefungAn(Ermittlung e, ErmittlungsZustand z, PruefungsAusgabe ausgabe)
        {
            if (z.Geloest) return new PruefAnwendung { Zustand = z };
            if (!ausgabe.Vollstaendig)
            {
                return new PruefAnwendung
                {
                    Zustand = z,
                    Ereignisse = { ErmittlungsEreignis.Einfach(EreignisArt.PruefungUnvollstaendig) },
                };
            }

            var ereignisse = new List<ErmittlungsEreignis>();
            var erledigt = new Dictionary<string, int>(z.Erledigt);

            foreach (var spur in e.Spuren)
            {
                var schritt = AktuellerSchritt(spur, z);
                if (schritt == null) continue;
                if (!(ausgabe.Ergebnisse.TryGetValue(PruefIdSchritt(spur.Id, schritt.Id), out var bestanden) && bestanden)) continue;
                var neu = Erledigt(z, spur.Id) + 1;
                erledigt[spur.Id] = neu;
                ereignisse.Add(ErmittlungsEreignis.SchrittErledigt(spur.Id, schritt.Id));
                if (neu >= spur.Schritte.Count) ereignisse.Add(ErmittlungsEreignis.SpurErledigt(spur.Id));
            }

            var letzteDiagnose = z.LetzteDiagnose == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(z.LetzteDiagnose);
            foreach (var spur in e.Spuren)
            {
                var schritt = AktuellerSchritt(spur, z);
                if (schritt == null || string.IsNullOrEmpty(schritt.Diagnose)) continue;
                var key = $"{spur.Id}/{schritt.Id}";
                var verifyBekannt = ausgabe.Ergebnisse.TryGetValue(PruefIdSchritt(spur.Id, schritt.Id), out var verify);
                var text = ausgabe.Diagnosen.TryGetValue(PruefIdDiagnose(spur.Id, schritt.Id), out var t) ? t : "";
                if (!(verifyBekannt && !verify) || text.Length == 0)
                {
                    letzteDiagnose.Remove(key);
                }
                else if (!(letzteDiagnose.TryGetValue(key, out var alt) && alt == text))
                {
                    letzteDiagnose[key] = text;
                    ereignisse.Add(ErmittlungsEreignis.Diagnose(spur.Id, schritt.Id, text));
                }
            }

            var vorher = new HashSet<string>(z.BelegeErkannt);
            var erkannt = new List<string>();
            var neuErkannt = new List<string>();
            var verloren = new List<string>();
            foreach (var beleg in e.Vorwurf.Belege)
            {
                var key = PruefIdBeleg(beleg.Id);
                var bisher = vorher.Contains(beleg.Id);
                var jetzt = ausgabe.Ergebnisse.TryGetValue(key, out var wert) ? wert : bisher;
                if (jetzt)
                {
                    erkannt.Add(beleg.Id);
                    if (!bisher) neuErkannt.Add(beleg.Id);
                }
                else if (bisher)
                {
                    verloren.Add(beleg.Id);
                }
            }
            foreach (var belegId in neuErkannt) ereignisse.Add(ErmittlungsEreignis.BelegErkannt(belegId));
            foreach (var belegId in verloren) ereignisse.Add(ErmittlungsEreignis.BelegVerloren(belegId));
            erkannt.Sort(string.CompareOrdinal);

            var sperreBis = z.SperreBis;
            var sperreRest = z.SperreRestPruefungen;
            if (z.SperreBis.HasValue)
            {
                if (erkannt.Count >= z.SperreBis.Value)
                {
                    sperreBis = null;
                    sperreRest = 0;
                    ereignisse.Add(ErmittlungsEreignis.Einfach(EreignisArt.SperreAufgehoben));
                }
                else
                {
                    sperreRest = z.SperreRestPruefungen - 1;
                    if (sperreRest <= 0)
                    {
                        sperreBis = null;
                        sperreRest = 0;
                        ereignisse.Add(ErmittlungsEreignis.Einfach(EreignisArt.SperreAufgehoben));
                    }
                }
            }

            var rahmenVerletzt = z.EinsatzrahmenVerletzt;
            if (!z.EinsatzrahmenVerletzt && ausgabe.Ergebnisse.TryGetValue(PruefIdVerstoss, out var verstoss) && verstoss)
            {
                rahmenVerletzt = true;
                ereignisse.Add(ErmittlungsEreignis.Einfach(EreignisArt.EinsatzrahmenVerletzt));
                var zwischen = z.Kopie();
                zwischen.Erledigt = erledigt;
                zwischen.BelegeErkannt = erkannt;
                zwischen.SperreBis = sperreBis;
                var naechste = NaechsteSperre(e, zwischen);
                if (naechste == null)
                {
                    sperreBis = null;
                    sperreRest = 0;
                }
                else
                {
                    sperreBis = naechste;
                    sperreRest = SperrePruefungen;
                }
            }

            var neuZ = new ErmittlungsZustand
            {
                Erledigt = erledigt,
                BelegeErkannt = erkannt,
                EinsatzrahmenVerletzt = rahmenVerletzt,
                SperreBis = sperreBis,
                SperreRestPruefungen = sperreRest,
                VorwurfVersuche = z.VorwurfVersuche,
                VorwurfRichtig = z.VorwurfRichtig,
                Geloest = z.Geloest,
                LetzteDiagnose = letzteDiagnose.Count > 0 ? letzteDiagnose : null,
            };
            return new PruefAnwendung { Zustand = SchliesseAb(e, neuZ, ereignisse), Ereignisse = ereignisse };
        }

        /// <summary>Eine richtige Antwort erledigt den aktuellen Schritt wie ein bestandenes verify; sonst nichts.</summary>
        public static PruefAnwendung WendeAntwortAn(Ermittlung e, ErmittlungsZustand z, string spurId, string schrittId, bool richtig)
        {
            var unveraendert = new PruefAnwendung { Zustand = z };
            if (!richtig || z.Geloest) return unveraendert;
            var spur = e.Spuren.FirstOrDefault(s => s.Id == spurId);
            var schritt = spur == null ? null : AktuellerSchritt(spur, z);
            if (spur == null || schritt == null || schritt.Id != schrittId) return unveraendert;
            var neu = Erledigt(z, spur.Id) + 1;
            var ereignisse = new List<ErmittlungsEreignis> { ErmittlungsEreignis.SchrittErledigt(spurId, schrittId) };
            if (neu >= spur.Schritte.Count) ereignisse.Add(ErmittlungsEreignis.SpurErledigt(spurId));
            var kopie = z.Kopie();
            kopie.Erledigt[spur.Id] = neu;
            return new PruefAnwendung { Zustand = SchliesseAb(e, kopie, ereignisse), Ereignisse = ereignisse };
        }

        // ---- Sperre und Vorwurf ----

        /// <summary>Sperre einen Beleg hinter den Stand; ohne weiteren Beleg bleibt sie aus (Sackgassen-Regel).</summary>
        public static int? NaechsteSperre(Ermittlung e, ErmittlungsZustand z)
        {
            if (e.Vorwurf.OhneSperre) return null;
            var ziel = z.BelegeErkannt.Count + 1;
            if (ziel > e.Vorwurf.Belege.Count) return null;
            return Math.Max(ziel, z.SperreBis ?? 0);
        }

        public static bool IstGesperrt(ErmittlungsZustand z) =>
            z.SperreBis.HasValue && z.BelegeErkannt.Count < z.SperreBis.Value;

        /// <summary>Bewertet einen Vorwurf; die Fälle der Spec-Tabelle in fester Reihenfolge.</summary>
        public static VorwurfAuswertung PruefeVorwurf(Ermittlung e, ErmittlungsZustand z, VorwurfEingabe vorwurf)
        {
            var v = e.Vorwurf;

            if (z.Geloest)
                return new VorwurfAuswertung { Zustand = z, Ergebnis = VorwurfErgebnis.BereitsGeloest, Antwort = new string[0] };

            // Ein richtiger Vorwurf steht fest; der Fall schließt erst, wenn auch alle Spuren erledigt sind.
            if (z.VorwurfRichtig)
                return new VorwurfAuswertung { Zustand = z, Ergebnis = VorwurfErgebnis.RichtigOffen, Antwort = OffeneSchritteText(e, z) };

            if (IstGesperrt(z))
                return new VorwurfAuswertung { Zustand = z, Ergebnis = VorwurfErgebnis.Gesperrt, Antwort = v.Antworten.Gesperrt };

            var erkannt = new HashSet<string>(z.BelegeErkannt);
            if (vorwurf.BelegIds.Any(id => !erkannt.Contains(id)))
                return new VorwurfAuswertung { Zustand = z, Ergebnis = VorwurfErgebnis.BelegUnbekannt, Antwort = new string[0] };

            if (vorwurf.VerdaechtigeId != v.TaeterId)
                return Gesperrt(e, z, VorwurfErgebnis.FalschePerson, v.Antworten.FalschePerson);

            var genannt = new HashSet<string>(vorwurf.BelegIds);
            var gegenTaeter = v.Belege.Where(b => genannt.Contains(b.Id) && b.Stuetzt == v.TaeterId).ToList();
            var spuren = new HashSet<string>(gegenTaeter.Select(b => b.SpurId));
            var spurenVollstaendig = e.Spuren.All(s => spuren.Contains(s.Id));
            if (gegenTaeter.Count < v.MindestBelege || (v.JedeSpur && !spurenVollstaendig))
                return Gesperrt(e, z, VorwurfErgebnis.ZuWenigBelege, v.Antworten.ZuWenigBelege);

            var richtig = z.Kopie();
            richtig.VorwurfVersuche = z.VorwurfVersuche + 1;
            richtig.SperreBis = null;
            richtig.SperreRestPruefungen = 0;
            if (!AlleSpurenErledigt(e, z))
            {
                richtig.VorwurfRichtig = true;
                return new VorwurfAuswertung { Zustand = richtig, Ergebnis = VorwurfErgebnis.RichtigOffen, Antwort = OffeneSchritteText(e, richtig) };
            }
            richtig.Geloest = true;
            return new VorwurfAuswertung { Zustand = richtig, Ergebnis = VorwurfErgebnis.Richtig, Antwort = v.Antworten.Richtig };
        }

        private static VorwurfAuswertung Gesperrt(Ermittlung e, ErmittlungsZustand z, VorwurfErgebnis ergebnis, IReadOnlyList<string> antwort)
        {
            var sperre = NaechsteSperre(e, z);
            var neu = z.Kopie();
            neu.VorwurfVersuche = z.VorwurfVersuche + 1;
            neu.SperreBis = sperre;
            neu.SperreRestPruefungen = sperre == null ? 0 : SperrePruefungen;
            return new VorwurfAuswertung { Zustand = neu, Ergebnis = ergebnis, Antwort = antwort };
        }
    }
}
