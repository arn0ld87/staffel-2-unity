// Headless v86-Host der nativen Staffel 2.
//
// Startet die Alpine-VM aus dem Savestate der Webversion und reicht beide seriellen
// Kanäle als JSON-Zeilen über stdin/stdout durch. Der Host kennt weder Episoden noch
// Prüflogik; das Agent-Protokoll (PING/RUN/REPLY) spricht das Spiel selbst.
//
//   stdin  {"t":"in","d":"<base64>"}      Tastatur -> ttyS0 (Spielerterminal)
//          {"t":"agent","line":"PING m1"} eine Zeile -> ttyS1 (mission-agent)
//          {"t":"screen","on":true}       Grafischen Bildschirm (SVGA) abonnieren/abbestellen
//          {"t":"mouse","x":N,"y":N,"w":N,"h":N,"buttons":N,"wheel":N} Maus -> v86-Bus
//          {"t":"key","codes":[N,...]}    PS/2-Scancodes -> v86-Bus
//          {"t":"save","path":"/..."}     VM-Zustand gzip-komprimiert in eine Datei schreiben
//          {"t":"stop"}                   VM beenden, Prozess endet
//   stdout {"t":"booting"} {"t":"started","ms":N}
//          {"t":"saved","path":"...","bytes":N}
//          {"t":"out","d":"<base64>"}     Ausgabe von ttyS0
//          {"t":"agent","line":"RES ..."} eine Zeile von ttyS1
//          {"t":"frame","w":N,"h":N,"d":"<base64-deflate-rgba>"} SVGA-Vollbild
//          {"t":"error","message":"..."}
//
// Isolation: kein Netzwerkadapter (keine network_relay_url), das Dateisystem kommt aus
// den mitgelieferten, inhaltsadressierten Image-Dateien; Schreibzugriffe der VM bleiben
// im Speicher. Es gibt keinen Pfad, über den die VM Host-Dateien oder die Host-Shell
// erreicht.
import fs from 'node:fs';
import path from 'node:path';
import readline from 'node:readline';
import zlib from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { V86 } from 'v86';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const { values } = parseArgs({
  options: {
    assets: { type: 'string', default: path.join(HERE, 'assets', 'linux') },
    image: { type: 'string', default: 'terminal' },
    wasm: { type: 'string' },
    // Gespeicherter Spielstand der VM (gzip), statt des Savestates aus dem Image.
    state: { type: 'string' },
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

// v86 legt den SVGA-Zielpuffer in WASM nur an, wenn globalThis.ImageData existiert.
if (typeof globalThis.ImageData === 'undefined') {
  globalThis.ImageData = class ImageData {
    constructor(data, width, height) {
      this.data = data;
      this.width = width;
      this.height = height;
    }
  };
}

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
  initial_state: values.state
    ? { buffer: toArrayBuffer(zlib.gunzipSync(fs.readFileSync(values.state))) }
    : { url: path.join(imageDir, 'alpine-state.bin.zst') },
});

function toArrayBuffer(buf) {
  return buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength);
}

// Schreibt erst eine temporäre Datei und benennt dann um: ein Abbruch hinterlässt nie
// einen halben Spielstand.
async function saveState(target) {
  const state = await emulator.save_state();
  const packed = zlib.gzipSync(Buffer.from(state), { level: 6 });
  const tmp = `${target}.tmp`;
  fs.writeFileSync(tmp, packed);
  fs.renameSync(tmp, target);
  send({ t: 'saved', path: target, bytes: packed.length });
}

let screenEnabled = false;
let screenInterval = null;
let forceFullFrame = false;
let frameDirty = false;
let lastButtonsMask = 0;

function attachScreenHook() {
  const vga = emulator.v86?.cpu?.devices?.vga;
  if (!vga) return null;
  if (!vga._hookAttached) {
    vga._hookAttached = true;
    const origUpdate = vga.screen.update_buffer?.bind(vga.screen);
    vga.screen.update_buffer = (layers) => {
      if (origUpdate) origUpdate(layers);
      if (layers && layers.length > 0 && layers[0].buffer_height > 0) {
        frameDirty = true;
      }
    };
  }
  return vga;
}

function flushScreenFrame() {
  if (!screenEnabled) return;
  const vga = attachScreenHook();
  if (!vga) return;
  if (!vga.graphical_mode || !vga.svga_enabled) {
    if (forceFullFrame) {
      forceFullFrame = false;
      send({ t: 'frame', w: 0, h: 0, d: '' });
    }
    return;
  }
  if (!vga.image_data || vga.image_data.data.byteLength === 0) {
    vga.set_size_graphical(
      vga.svga_width,
      vga.svga_height,
      vga.svga_width,
      vga.svga_height,
      vga.svga_bpp
    );
  }
  if (forceFullFrame) {
    vga.cpu.svga_mark_dirty();
  }
  frameDirty = false;
  vga.screen_fill_buffer();
  if (!frameDirty && !forceFullFrame) return;
  forceFullFrame = false;
  const img = vga.image_data;
  if (!img || !img.data || img.data.byteLength === 0) return;
  const raw = Buffer.from(img.data.buffer, img.data.byteOffset, img.data.byteLength);
  const packed = zlib.deflateRawSync(raw, { level: 1 });
  send({ t: 'frame', w: vga.svga_width, h: vga.svga_height, d: packed.toString('base64') });
}

function setScreenEnabled(on) {
  screenEnabled = Boolean(on);
  if (screenEnabled) {
    forceFullFrame = true;
    if (!screenInterval) {
      screenInterval = setInterval(flushScreenFrame, 80);
    }
    flushScreenFrame();
  } else if (screenInterval) {
    clearInterval(screenInterval);
    screenInterval = null;
  }
}

emulator.add_listener('emulator-started', () => {
  send({ t: 'started', ms: Date.now() - startedAt });
  if (screenEnabled) {
    forceFullFrame = true;
    flushScreenFrame();
  }
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
    if (screenInterval) {
      clearInterval(screenInterval);
      screenInterval = null;
    }
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
  } else if (message.t === 'screen') {
    setScreenEnabled(Boolean(message.on));
  } else if (message.t === 'mouse') {
    if (
      typeof message.x === 'number' &&
      typeof message.y === 'number' &&
      typeof message.w === 'number' &&
      typeof message.h === 'number' &&
      message.w > 0 &&
      message.h > 0
    ) {
      emulator.bus.send('mouse-absolute', [message.x, message.y, message.w, message.h]);
    }
    if (typeof message.dx === 'number' && typeof message.dy === 'number' && (message.dx !== 0 || message.dy !== 0)) {
      emulator.bus.send('mouse-delta', [message.dx, message.dy]);
    }
    if (typeof message.buttons === 'number') {
      const b = message.buttons | 0;
      if (b !== lastButtonsMask) {
        lastButtonsMask = b;
        // Bit 0: Links, Bit 1: Rechts, Bit 2: Mitte -> v86 erwartet [links, mitte, rechts].
        emulator.bus.send('mouse-click', [Boolean(b & 1), Boolean(b & 4), Boolean(b & 2)]);
      }
    }
    if (typeof message.wheel === 'number' && message.wheel !== 0) {
      emulator.bus.send('mouse-wheel', [Math.sign(message.wheel), 0]);
    }
  } else if (message.t === 'key' && Array.isArray(message.codes)) {
    for (const code of message.codes) {
      if (typeof code === 'number') {
        emulator.bus.send('keyboard-code', code & 0xff);
      }
    }
  } else if (message.t === 'save' && typeof message.path === 'string') {
    saveState(message.path).catch((e) => send({ t: 'error', message: `Speichern fehlgeschlagen: ${e.message}` }));
  } else if (message.t === 'stop') {
    stop(0);
  }
});
// Spiel beendet oder abgestürzt: VM nicht verwaist weiterlaufen lassen.
input.on('close', () => stop(0));
process.on('SIGTERM', () => stop(0));
