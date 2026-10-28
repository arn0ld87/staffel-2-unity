// Erzeugt Fixtures/expected.json aus den TypeScript-Originalen (Referenz fuer byteidentische Skripte).
//   bun core/tests/gen-expected.ts [pfad/zum/main-ref]
import { writeFileSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ref = process.argv[2] ?? '/Volumes/T7/Worktrees/alexle135.de/main-ref';
const m = (f: string) => join(ref, 'src/lib/missions', f);
const eng = await import(m('ermittlung.ts'));
const proto = await import(m('agentProtocol.ts'));
const abf = await import(m('abfrage.ts'));

const here = dirname(fileURLToPath(import.meta.url));
const daten = JSON.parse(readFileSync(join(here, '../staffel2.fixture.json'), 'utf8'));
const eps = daten.episoden.map((e: any) => e.ermittlung);

const out: Record<string, string> = {};
eps.forEach((e: any, i: number) => {
  out[`buendel-e${i}-start`] = eng.buendelSkript(eng.offenePruefungen(e, eng.startZustand()));
});
out['buendel-e1-fortschritt'] = eng.buendelSkript(
  eng.offenePruefungen(eps[1], { ...eng.startZustand(), erledigt: { rechte: 2, log: 0 }, einsatzrahmenVerletzt: true })
);
out['buendel-e2-halb'] = eng.buendelSkript(
  eng.offenePruefungen(eps[2], { ...eng.startZustand(), erledigt: { prozess: 1, zettel: 1 } })
);
out['buendel-duplikate'] = eng.buendelSkript([
  { id: 'a', skript: 'true' },
  { id: 'b', skript: 'echo "x"; exit 2' },
  { id: 'c', skript: 'true' },
  { id: 'd.diag', skript: 'echo "Hallo\tWelt"', diagnose: true },
  { id: 'e', skript: 'echo "x"; exit 2' },
  { id: 'f', skript: 'true' },
  { id: 'g', skript: 'false' },
]);
out['buendel-leer'] = eng.buendelSkript([]);
out['antwort'] = eng.antwortSkript('test "$ANTWORT" = "12:34"', 'Müller 12:34/x');
out['abfrage-e1'] = abf.abfrageSkript(eps[1].ansichten[0].abfrage);
out['abfrage-konto'] = abf.abfrageSkript("printf 'a\\tb\\n'", 'spieler_1');
out['run'] = proto.encodeRun('m1', 'echo "hi" # äöü\n', 19);
out['run-ohne-limit'] = proto.encodeRun('m2', 'true');
out['reply'] = proto.encodeReply('m3', 'r12x3', { exitCode: 300, text: 'a\r\nb\u0001c\td\u001ee\u007f' });
out['ping'] = proto.encodePing('m4');

writeFileSync(join(here, 'Fixtures/expected.json'), JSON.stringify(out, null, 2) + '\n');
console.log(Object.keys(out).length, 'Faelle');
