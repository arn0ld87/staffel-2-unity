// Headless v86-Host der nativen Staffel 2.
//
// Startet die Alpine-VM aus dem Savestate der Webversion und reicht beide seriellen
// Kanäle als JSON-Zeilen über stdin/stdout durch. Der Host kennt weder Episoden noch
// Prüflogik; das Agent-Protokoll (PING/RUN/REPLY) spricht das Spiel selbst.
//
//   stdin  {"t":"in","d":"<base64>"}      Tastatur -> ttyS0 (Spielerterminal)
//          {"t":"agent","line":"PING m1"} eine Zeile -> ttyS1 (mission-agent)
//          {"t":"stop"}                   VM beenden, Prozess endet
//   stdout {"t":"booting"} {"t":"started","ms":N}
//          {"t":"out","d":"<base64>"}     Ausgabe von ttyS0
//          {"t":"agent","line":"RES ..."} eine Zeile von ttyS1
//          {"t":"error","message":"..."}
//
// Isolation: kein Netzwerkadapter (keine network_relay_url), das Dateisystem kommt aus
// den mitgelieferten, inhaltsadressierten Image-Dateien; Schreibzugriffe der VM bleiben
// im Speicher. Es gibt keinen Pfad, über den die VM Host-Dateien oder die Host-Shell
// erreicht.
import path from 'node:path';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { V86 } from 'v86';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const { values } = parseArgs({
  options: {
    assets: { type: 'string', default: path.join(HERE, 'assets', 'linux') },
    image: { type: 'string', default: 'terminal' },
    wasm: { type: 'string' },
  },
});

// Muss zum Savestate passen (build-state.mjs der Webversion).
const CMDLINE =
  'rw root=host9p rootfstype=9p rootflags=trans=virtio,cache=loose modules=virtio_pci tsc=reliable init_on_free=on';
const IMAGES = {
  terminal: { memoryMb: 128, vgaMb: 2, dir: '.' },
  bildschirm: { memoryMb: 512, vgaMb: 8, dir: 'bildschirm' },
};

const image = IMAGES[values.image];
if (!image) {
  send({ t: 'error', message: `Unbekanntes Image: ${values.image}` });
  process.exit(2);
}

function send(message) {
  process.stdout.write(JSON.stringify(message) + '\n');
}

function resolveWasm() {
  if (values.wasm) return values.wasm;
  return fileURLToPath(import.meta.resolve('v86/build/v86.wasm'));
}

const assets = values.assets;
const imageDir = path.join(assets, image.dir);
const startedAt = Date.now();
send({ t: 'booting' });

const emulator = new V86({
  bios: { url: path.join(assets, 'seabios.bin') },
  vga_bios: { url: path.join(assets, 'vgabios.bin') },
  wasm_path: resolveWasm(),
  autostart: true,
  memory_size: image.memoryMb * 1024 * 1024,
  vga_memory_size: image.vgaMb * 1024 * 1024,
  uart1: true,
  disable_keyboard: true,
  disable_mouse: true,
  disable_speaker: true,
  cmdline: CMDLINE,
  bzimage_initrd_from_filesystem: true,
  filesystem: {
    baseurl: path.join(assets, 'alpine-rootfs-flat'),
    basefs: path.join(imageDir, 'alpine-fs.json'),
  },
  initial_state: { url: path.join(imageDir, 'alpine-state.bin.zst') },
});

emulator.add_listener('emulator-started', () => {
  send({ t: 'started', ms: Date.now() - startedAt });
});

// ttyS0: Bytes je Microtask bündeln, damit nicht jedes Zeichen eine JSON-Zeile wird.
let pending = [];
let flushQueued = false;
emulator.add_listener('serial0-output-byte', (byte) => {
  pending.push(byte);
  if (flushQueued) return;
  flushQueued = true;
  queueMicrotask(() => {
    flushQueued = false;
    const chunk = Buffer.from(pending);
    pending = [];
    send({ t: 'out', d: chunk.toString('base64') });
  });
});

// ttyS1: zeilenweise weiterreichen, Protokollzeilen sind ASCII.
let agentLine = '';
emulator.add_listener('serial1-output-byte', (byte) => {
  if (byte === 10) {
    send({ t: 'agent', line: agentLine.replace(/\r$/, '') });
    agentLine = '';
  } else if (agentLine.length < 256 * 1024) {
    agentLine += String.fromCharCode(byte);
  }
});

function stop(code = 0) {
  try {
    emulator.destroy();
  } finally {
    process.exit(code);
  }
}

const input = readline.createInterface({ input: process.stdin });
input.on('line', (line) => {
  let message;
  try {
    message = JSON.parse(line);
  } catch {
    send({ t: 'error', message: 'Ungültige Eingabezeile' });
    return;
  }
  if (message.t === 'in' && typeof message.d === 'string') {
    emulator.serial_send_bytes(0, Buffer.from(message.d, 'base64'));
  } else if (message.t === 'agent' && typeof message.line === 'string') {
    emulator.serial_send_bytes(1, new TextEncoder().encode(message.line.replace(/\n/g, '') + '\n'));
  } else if (message.t === 'stop') {
    stop(0);
  }
});
// Spiel beendet oder abgestürzt: VM nicht verwaist weiterlaufen lassen.
input.on('close', () => stop(0));
process.on('SIGTERM', () => stop(0));
