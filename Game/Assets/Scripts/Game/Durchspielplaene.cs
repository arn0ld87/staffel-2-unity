using System;
using System.Collections.Generic;
using System.Linq;
using Staffel2.Core;

namespace Staffel2.Game
{
    public enum PlanArt { Tippe, Antwort, Vorwurf, Speichern, Fortsetzen, Fallakte, Terminal, Raum }

    /// <summary>Ein Schritt eines Durchspielplans mit der Bedingung, die danach gelten muss.</summary>
    public sealed class PlanSchritt
    {
        public PlanArt Art;
        public string Text;           // Befehl, Antwort oder Verdächtigen-ID
        public string SpurId;         // für Antworten
        public string Beschreibung;
        public Func<Ermittlungssitzung, bool> Erwartung;
        public float MaxSekunden = 60;

        public static PlanSchritt Tippe(string befehl, Func<Ermittlungssitzung, bool> erwartung = null, string beschreibung = null) =>
            new PlanSchritt { Art = PlanArt.Tippe, Text = befehl, Erwartung = erwartung, Beschreibung = beschreibung ?? befehl };

        public static PlanSchritt Antwort(string spurId, string text, Func<Ermittlungssitzung, bool> erwartung, string beschreibung = null) =>
            new PlanSchritt { Art = PlanArt.Antwort, SpurId = spurId, Text = text, Erwartung = erwartung, Beschreibung = beschreibung ?? $"Antwort {text}" };

        public static PlanSchritt Vorwurf(string verdaechtigeId, Func<Ermittlungssitzung, bool> erwartung, string beschreibung = null) =>
            new PlanSchritt { Art = PlanArt.Vorwurf, Text = verdaechtigeId, Erwartung = erwartung, Beschreibung = beschreibung ?? $"Vorwurf gegen {verdaechtigeId}" };

        public static PlanSchritt Ansicht(PlanArt art) => new PlanSchritt { Art = art, Beschreibung = art.ToString() };
        public static PlanSchritt Speichern() => new PlanSchritt { Art = PlanArt.Speichern, Beschreibung = "Speichern" };
        /// <summary>Hauptmenü und Fortsetzen: Zustand und Belegdateien müssen in der fortgesetzten VM stimmen.</summary>
        public static PlanSchritt Fortsetzen() => new PlanSchritt { Art = PlanArt.Fortsetzen, Beschreibung = "Fortsetzen aus dem Spielstand" };
    }

    /// <summary>
    /// Vorgesehene Lösungswege je Episode, wörtlich aus den E2E-Specs der Webversion
    /// (e2e/helpers/staffel2.ts, e2e/12, 07, 16, 15), samt den dort geprüften Gegenproben.
    /// </summary>
    public static class Durchspielplaene
    {
        public static bool Erledigt(Ermittlungssitzung s, string spurId, string schrittId)
        {
            var spur = s.Ermittlung.Spuren.First(x => x.Id == spurId);
            int index = spur.Schritte.FindIndex(x => x.Id == schrittId);
            return s.Zustand.Erledigt.TryGetValue(spurId, out var n) && n > index;
        }

        private static Func<Ermittlungssitzung, bool> Schritt(string spur, string schritt) => s => Erledigt(s, spur, schritt);
        private static Func<Ermittlungssitzung, bool> Beleg(string id) => s => s.Zustand.BelegeErkannt.Contains(id);
        private static Func<Ermittlungssitzung, bool> NichtErledigt(string spur, string schritt) => s => !Erledigt(s, spur, schritt);
        private static Func<Ermittlungssitzung, bool> PagerEnthaelt(string text) => s => s.Pager.Any(p => p.Zeilen.Any(z => z.Contains(text)));
        private static readonly Func<Ermittlungssitzung, bool> Geloest = s => s.Zustand.Geloest;
        private static readonly Func<Ermittlungssitzung, bool> NichtGeloest = s => !s.Zustand.Geloest && s.Zustand.VorwurfVersuche > 0;

        public static List<PlanSchritt> Fuer(int nr)
        {
            switch (nr)
            {
                case 0: return Episode0();
                case 1: return Episode1();
                case 2: return Episode2();
                case 3: return Episode3();
                default: return null;
            }
        }

        private static List<PlanSchritt> Episode0()
        {
            const string ordner = "/srv/rechenwerk/uebung";
            return new List<PlanSchritt>
            {
                PlanSchritt.Ansicht(PlanArt.Terminal),
                PlanSchritt.Tippe($"ls -l {ordner}/kaffeekasse.csv {ordner}/kaffeekasse.bak > ~/belege/kasse-meta.txt", Beleg("kasse-meta")),
                PlanSchritt.Tippe("cat ~/belege/kasse-meta.txt"),
                PlanSchritt.Ansicht(PlanArt.Fallakte),
                PlanSchritt.Antwort("akte", "timo", NichtErledigt("akte", "besitzer"), "Gegenprobe: falsche Antwort"),
                PlanSchritt.Antwort("akte", "kaffee", Schritt("akte", "besitzer")),
                PlanSchritt.Ansicht(PlanArt.Terminal),
                PlanSchritt.Tippe($"grep 2026-04-15 {ordner}/kaffeekasse.csv > ~/belege/kasse-zeilen.txt && grep 2026-04-15 {ordner}/kaffeekasse.bak >> ~/belege/kasse-zeilen.txt", Beleg("kasse-zeilen")),
                PlanSchritt.Tippe($"cp {ordner}/kaffeekasse.bak {ordner}/kaffeekasse.csv", Schritt("inhalt", "kasse-zurueck")),
                PlanSchritt.Speichern(),
                PlanSchritt.Fortsetzen(),
                PlanSchritt.Ansicht(PlanArt.Fallakte),
                PlanSchritt.Vorwurf("kaffee", Geloest),
            };
        }

        private static List<PlanSchritt> Episode1()
        {
            return new List<PlanSchritt>
            {
                PlanSchritt.Ansicht(PlanArt.Terminal),
                PlanSchritt.Tippe("grep sudoers.d /var/log/rechenwerk-audit.log > ~/belege/log-zeile.txt", Beleg("log-zeile")),
                PlanSchritt.Tippe("grep -B1 nadia /etc/sudoers.d/rw-fernwartung > ~/belege/rechte-zeile.txt", Beleg("rechte-zeile")),
                PlanSchritt.Ansicht(PlanArt.Fallakte),
                PlanSchritt.Antwort("rechte", "RW-1042", Schritt("rechte", "kennung")),
                PlanSchritt.Ansicht(PlanArt.Terminal),
                PlanSchritt.Tippe("ls -l /etc/sudoers.d/rw-fernwartung $(find /usr/local -perm -4000 -type f) > ~/belege/rechte-metadaten.txt", Beleg("rechte-metadaten")),
                PlanSchritt.Tippe("sudo mv /etc/sudoers.d/rw-fernwartung ~/quarantaene/", Schritt("rechte", "regel-weg")),
                PlanSchritt.Tippe("sudo mv /usr/local/bin/rw-status ~/quarantaene/", Schritt("rechte", "status-weg")),
                PlanSchritt.Speichern(),
                PlanSchritt.Ansicht(PlanArt.Fallakte),
                PlanSchritt.Vorwurf("nadia", NichtGeloest, "Gegenprobe: falsche Person"),
                PlanSchritt.Vorwurf("timo", Geloest),
            };
        }

        private static List<PlanSchritt> Episode2()
        {
            return new List<PlanSchritt>
            {
                PlanSchritt.Ansicht(PlanArt.Terminal),
                PlanSchritt.Tippe("ps | grep -E 'rw-abgleich|crond' > ~/belege/prozess-zeile.txt", Beleg("prozess-zeile")),
                PlanSchritt.Tippe("sudo pkill -x rw-abgleich", PagerEnthaelt("Cron-Eintrag startet ihn gleich wieder"), "Gegenprobe: nur beenden, Cron startet neu"),
                PlanSchritt.Tippe("sudo crontab -u abgleich -r; sudo pkill -x rw-abgleich", Schritt("prozess", "wiedergaenger")),
                PlanSchritt.Ansicht(PlanArt.Fallakte),
                // Der Zettel wird am Bildschirm gelesen (Bildschirm-Image); die Antwort selbst ist Text.
                PlanSchritt.Antwort("zettel", "RW-2117", NichtErledigt("zettel", "kennung"), "Gegenprobe: falsche Kennung"),
                PlanSchritt.Antwort("zettel", "RW-2122", Schritt("zettel", "kennung")),
                PlanSchritt.Ansicht(PlanArt.Terminal),
                PlanSchritt.Tippe("grep 'change=RW-2122 ' /var/log/rechenwerk-wartung.log > ~/belege/wartung-zeile.txt", Beleg("wartung-zeile")),
                PlanSchritt.Speichern(),
                PlanSchritt.Ansicht(PlanArt.Fallakte),
                PlanSchritt.Vorwurf("timo", NichtGeloest, "Gegenprobe: falsche Person"),
                PlanSchritt.Vorwurf("nadia", Geloest),
            };
        }

        private static List<PlanSchritt> Episode3()
        {
            const string log = "/var/log/rechenwerk-auth.log";
            const string kopie = "/srv/rechenwerk/log-spiegel/rechenwerk-auth.log";
            const string skript = "/srv/rechenwerk/wartung/bin/logkuerzen.sh";
            return new List<PlanSchritt>
            {
                PlanSchritt.Ansicht(PlanArt.Terminal),
                PlanSchritt.Tippe($"diff {log} {kopie} | head -20"),
                PlanSchritt.Ansicht(PlanArt.Fallakte),
                PlanSchritt.Antwort("luecke", "02:07-04:05", NichtErledigt("luecke", "zeitraum"), "Gegenprobe: falscher Zeitraum"),
                PlanSchritt.Antwort("luecke", "02:14-03:09", Schritt("luecke", "zeitraum")),
                PlanSchritt.Ansicht(PlanArt.Terminal),
                PlanSchritt.Tippe($"grep -vxFf {log} {kopie} > ~/belege/luecke-zeilen.txt", Beleg("luecke-zeilen")),
                PlanSchritt.Tippe($"grep -vxFf {log} {kopie} | cut -d' ' -f2 | sort | uniq -c > ~/belege/konten-zaehlung.txt", Beleg("konten-zaehlung")),
                PlanSchritt.Tippe($"mkdir -p ~/quarantaene && cp {skript} ~/quarantaene/", PagerEnthaelt("Das Original liegt noch im Wartungsordner"), "Gegenprobe: cp statt mv"),
                PlanSchritt.Tippe($"rm ~/quarantaene/logkuerzen.sh && mv {skript} ~/quarantaene/", Schritt("zaehlung", "skript-quarantaene")),
                PlanSchritt.Speichern(),
                PlanSchritt.Ansicht(PlanArt.Fallakte),
                PlanSchritt.Vorwurf("timo", NichtGeloest, "Gegenprobe: falsche Person"),
                PlanSchritt.Vorwurf("nadia", Geloest),
            };
        }
    }
}
