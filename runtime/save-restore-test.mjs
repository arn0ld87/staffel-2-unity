// Prüft Speichern und Fortsetzen der VM: Datei anlegen, Zustand sichern, Host beenden,
// aus dem Spielstand neu starten, Datei muss noch da sein.
//   node save-restore-test.mjs [--host "<befehl>"]
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import readline from 'node:readline';
import { parseArgs } from 'node:util';

const { values } = parseArgs({ options: { host: { type: 'string', default: 'node linux-host.mjs' } } });
const statePath = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 's2-')), 'vm.gz');
const b64 = (s) => Buffer.from(s).toString('base64');

function startHost(extra) {
  const [cmd, ...args] = values.host.split(' ');
  const child = spawn(cmd, [...args, ...extra], { stdio: ['pipe', 'pipe', 'inherit'] });
  const waiters = [];
  readline.createInterface({ input: child.stdout }).on('line', (raw) => {
    const m = JSON.parse(raw);
    for (const w of [...waiters]) if (w.test(m)) { waiters.splice(waiters.indexOf(w), 1); w.resolve(m); }
  });
  const waitFor = (test, ms = 60000) => new Promise((resolve, reject) => {
    const t = setTimeout(() => reject(new Error('Timeout')), ms);
    waiters.push({ test, resolve: (m) => { clearTimeout(t); resolve(m); } });
  });
  const send = (o) => child.stdin.write(JSON.stringify(o) + '\n');
  const run = async (id, script) => {
    send({ t: 'agent', line: `RUN ${id} ${b64(script)} 20` });
    const m = await waitFor((m) => m.t === 'agent' && m.line.startsWith(`RES ${id} `));
    return Number(m.line.split(' ')[2]);
  };
  const ready = async () => {
    for (let i = 0; i < 120; i++) {
      send({ t: 'agent', line: `PING p${i}` });
      if (await waitFor((m) => m.t === 'agent' && m.line.startsWith(`RES p${i} `), 500).then(() => true, () => false)) return;
    }
    throw new Error('Agent antwortet nicht');
  };
  return { child, send, run, ready, waitFor };
}

const t0 = Date.now();
const a = startHost([]);
await a.ready();
console.log('Exit Anlegen:', await a.run('m1', 'echo beweis > /home/azubi/belege-test.txt'));
a.send({ t: 'save', path: statePath });
const saved = await a.waitFor((m) => m.t === 'saved' || m.t === 'error', 120000);
console.log('Gespeichert:', saved.t, saved.bytes ? `${(saved.bytes / 1024 / 1024).toFixed(1)} MiB` : saved.message, `nach ${((Date.now() - t0) / 1000).toFixed(1)} s`);
a.send({ t: 'stop' });
await new Promise((r) => a.child.on('exit', r));

const t1 = Date.now();
const b = startHost(['--state', statePath]);
await b.ready();
console.log(`Fortgesetzt nach ${((Date.now() - t1) / 1000).toFixed(1)} s`);
console.log('Exit Prüfen (0 = Datei da):', await b.run('m2', 'grep -qx beweis /home/azubi/belege-test.txt'));
b.send({ t: 'stop' });
