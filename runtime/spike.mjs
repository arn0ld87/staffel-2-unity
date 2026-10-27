// Technik-Spike: startet linux-host.mjs als Kindprozess, so wie das Spiel es tut, und
// misst Startzeit, Agent-Antwort, eine echte Prüfung und eine Terminal-Eingabe.
//   node spike.mjs [--host "<befehl>"] [--image terminal|bildschirm]
import { spawn } from 'node:child_process';
import readline from 'node:readline';
import { parseArgs } from 'node:util';

const { values } = parseArgs({
  options: {
    host: { type: 'string', default: 'node linux-host.mjs' },
    image: { type: 'string', default: 'terminal' },
  },
});

const [cmd, ...args] = values.host.split(' ');
const t0 = Date.now();
const child = spawn(cmd, [...args, '--image', values.image], { stdio: ['pipe', 'pipe', 'inherit'] });
const lines = readline.createInterface({ input: child.stdout });
const log = (msg) => console.log(`[${((Date.now() - t0) / 1000).toFixed(2)} s] ${msg}`);
let terminal = '';
const waiters = [];

lines.on('line', (raw) => {
  const m = JSON.parse(raw);
  if (m.t === 'out') terminal += Buffer.from(m.d, 'base64').toString('utf8');
  if (m.t === 'started') log(`Emulator gestartet (${m.ms} ms im Host)`);
  if (m.t === 'error') log(`FEHLER ${m.message}`);
  for (const w of [...waiters]) if (w.test(m)) { waiters.splice(waiters.indexOf(w), 1); w.resolve(m); }
});

const waitFor = (test, ms = 60000) =>
  new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Timeout')), ms);
    waiters.push({ test, resolve: (m) => { clearTimeout(timer); resolve(m); } });
  });
const toAgent = (line) => child.stdin.write(JSON.stringify({ t: 'agent', line }) + '\n');
const type = (text) => child.stdin.write(JSON.stringify({ t: 'in', d: Buffer.from(text).toString('base64') }) + '\n');
const b64 = (s) => Buffer.from(s).toString('base64');
const run = async (id, script) => {
  toAgent(`RUN ${id} ${b64(script)} 20`);
  const m = await waitFor((m) => m.t === 'agent' && m.line.startsWith(`RES ${id} `));
  const [, , code, payload] = m.line.split(' ');
  return { code: Number(code), out: payload === '-' ? '' : Buffer.from(payload, 'base64').toString() };
};

try {
  // Agent bereit? Wie AgentClient.waitReady: PING bis RES.
  let ready = false;
  for (let i = 0; i < 120 && !ready; i++) {
    toAgent(`PING p${i}`);
    ready = await waitFor((m) => m.t === 'agent' && m.line.startsWith(`RES p${i} `), 500).then(() => true, () => false);
  }
  if (!ready) throw new Error('Agent antwortet nicht');
  log('Agent bereit (PING beantwortet)');

  const info = await run('m1', 'uname -sr; cat /etc/alpine-release; id azubi; ip -o link 2>/dev/null | cut -d: -f2 | tr -d " " | tr "\\n" " "; echo; ls /mnt /media 2>/dev/null | wc -l');
  log(`RUN m1 exit=${info.code}\n${info.out.trim()}`);

  // Episode 0, Schritt "kasse-zurueck": Setup-Ausschnitt anlegen, falsche und richtige Lösung prüfen.
  const setup = await run('m2', 'mkdir -p /srv/rechenwerk/uebung && printf "a\\n" > /srv/rechenwerk/uebung/kaffeekasse.bak && printf "b\\n" > /srv/rechenwerk/uebung/kaffeekasse.csv && chown -R azubi /srv/rechenwerk');
  log(`Setup exit=${setup.code}`);
  const verify = 'cmp -s /srv/rechenwerk/uebung/kaffeekasse.bak /srv/rechenwerk/uebung/kaffeekasse.csv';
  log(`Prüfung vor Lösung exit=${(await run('m3', verify)).code} (erwartet 1)`);

  terminal = '';
  type('cp /srv/rechenwerk/uebung/kaffeekasse.bak /srv/rechenwerk/uebung/kaffeekasse.csv\r');
  type('ls -l /srv/rechenwerk/uebung\r');
  await waitFor((m) => m.t === 'out' && /kaffeekasse\.csv/.test(terminal) && /\$ $/.test(terminal.replace(/\x1b\[[0-9;?]*[A-Za-z]/g, '')), 20000);
  log('Terminal-Ausgabe:\n' + terminal.replace(/\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x07]*\x07/g, '').trim());
  log(`Prüfung nach Lösung exit=${(await run('m4', verify)).code} (erwartet 0)`);

  // Isolation: Host-Pfade dürfen in der VM nicht existieren.
  const iso = await run('m5', `ls ${process.env.HOME} /Volumes 2>&1 | head -2; ping -c1 -W1 1.1.1.1 >/dev/null 2>&1 && echo NETZ || echo KEIN_NETZ`);
  log(`Isolation:\n${iso.out.trim()}`);
  log(`RSS Host-Prozess: siehe ps`);
  console.log('PID', child.pid);
  await new Promise((r) => setTimeout(r, 300));
  child.stdin.write(JSON.stringify({ t: 'stop' }) + '\n');
} catch (e) {
  log(`ABBRUCH ${e.message}\nTerminal:\n${terminal}`);
  child.kill();
  process.exit(1);
}
