// Exportiert die Staffel-2-Episoden der Webversion nach JSON für das native Spiel.
// Die Inhalte werden nicht abgeschrieben: das Skript importiert die TypeScript-Quellen
// der Webversion direkt und serialisiert sie.
//
//   bun tools/export-episoden.ts --web /pfad/zu/alexle135.de --out Game/Assets/StreamingAssets/staffel2
//
// Bildimporte (Porträts) liefert Bun als Pfad; exportiert wird nur der Dateiname, die
// Bilder kopiert das Skript daneben.
import { copyFileSync, mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { execFileSync } from 'node:child_process';

const { values } = parseArgs({
  options: { web: { type: 'string' }, out: { type: 'string' } },
});
if (!values.web || !values.out) throw new Error('--web und --out angeben');
const web = path.resolve(values.web);
const out = path.resolve(values.out);
mkdirSync(path.join(out, 'bilder'), { recursive: true });

const mod = (p: string) => import(path.join(web, p));
const { staffel2Episoden } = await mod('src/data/staffeln/staffel-2/index.ts');
const rahmen = await mod('src/lib/missions/staffel2Rahmen.ts');
const einsatz = await mod('src/lib/missions/einsatzrahmenSetup.ts');
const pager = await mod('src/lib/missions/pager.ts');
const glossar = await mod('src/lib/missions/glossar.ts').catch(() => ({}));

const bilder = new Set<string>();
// Porträts und andere Bildimporte: Bun liefert einen absoluten Pfad oder ein Objekt mit src.
function bild(value: unknown): string | unknown {
  const p = typeof value === 'string' ? value : (value as { src?: string })?.src;
  if (typeof p === 'string' && /\.(webp|png|jpe?g|svg)$/i.test(p)) {
    bilder.add(p);
    return path.basename(p);
  }
  return value;
}

function plain(value: unknown, key = ''): unknown {
  if (typeof value === 'function') return undefined;
  if (Array.isArray(value)) return value.map((v) => plain(v));
  if (value && typeof value === 'object') {
    if (key === 'portraet' || 'src' in (value as object)) return bild(value);
    const o: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(value)) {
      const p = plain(v, k);
      if (p !== undefined) o[k] = p;
    }
    return o;
  }
  if (key === 'portraet') return bild(value);
  return value;
}

// Werte, die in der Webversion Funktionen sind, werden hier pro Episode ausgewertet.
function skript(fn: unknown, ...args: unknown[]): string | null {
  return typeof fn === 'function' ? String(fn(...args)) : typeof fn === 'string' ? fn : null;
}

const episoden = staffel2Episoden.map((eintrag: Record<string, unknown>) => {
  const e = (eintrag.ermittlung ?? eintrag) as Record<string, unknown>;
  return {
    eintrag: plain({ ...eintrag, ermittlung: undefined }),
    ermittlung: plain(e),
    // Wie ErmittlungApp.tsx: Rahmen mit Standard-Hostname, Einsatzrahmen mit den sudo-Befehlen.
    rahmenSetup: skript(rahmen.rahmenSetupSkript, e.hostname ?? rahmen.STAFFEL2_HOSTNAME),
    einsatzrahmenSetup: skript(
      einsatz.einsatzrahmenSetupSkript,
      (e.einsatzrahmen as { sudoBefehle?: string[] })?.sudoBefehle ?? []
    ),
  };
});

const quelle = execFileSync('git', ['-C', web, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const daten = {
  format: 1,
  quelle: { repo: 'alexle135.de', commit: quelle },
  episoden,
  pagerPosen: plain(pager.POSEN ?? null),
  pagerTakt: { msJeZeichen: pager.MS_JE_ZEICHEN, msJeZeile: pager.MS_JE_ZEILE, msPause: pager.MS_PAUSE },
  glossar: glossar.glossar instanceof Map ? Object.fromEntries(glossar.glossar) : null,
  exporte: {
    rahmen: Object.keys(rahmen),
    einsatz: Object.keys(einsatz),
    pager: Object.keys(pager),
    glossar: Object.keys(glossar),
  },
};

writeFileSync(path.join(out, 'staffel2.json'), JSON.stringify(daten, null, 2) + '\n');
for (const b of bilder) copyFileSync(b, path.join(out, 'bilder', path.basename(b)));
console.log(`Episoden: ${episoden.length}, Bilder: ${bilder.size}, Quelle: ${quelle.slice(0, 7)}`);
