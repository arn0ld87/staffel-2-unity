# Erzeugt nahtlos loopbare Ambiente-Klänge für den Serverraum (eigene Erzeugung, kein
# Fremdmaterial): Lüfterrauschen mit leichtem Netzbrummen und einen leisen Klangteppich.
#   python3 -I erzeuge_ambiente.py <zielordner>
import math, random, struct, sys, wave

RATE = 44100
ziel = sys.argv[1]

def schreibe(name, samples):
    with wave.open(f"{ziel}/{name}", "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", max(-32767, min(32767, int(s * 32767)))) for s in samples))

def nahtlos(samples, blende):
    # Ende in den Anfang überblenden, damit der Loop keine hörbare Naht hat.
    n = len(samples) - blende
    out = samples[:n]
    for i in range(blende):
        t = i / blende
        out[i] = samples[n + i] * (1 - t) + out[i] * t
    return out

random.seed(135)
dauer = 8.0
n = int(RATE * dauer) + RATE
# Braunes Rauschen (tiefes Lüfterrauschen) + Tiefpass + 50/100 Hz Brummen + langsame Schwebung.
lp, braun, s = 0.0, 0.0, []
for i in range(n):
    braun = (braun + random.uniform(-1, 1) * 0.02) * 0.995
    lp += (random.uniform(-1, 1) - lp) * 0.06
    t = i / RATE
    hum = 0.05 * math.sin(2 * math.pi * 50 * t) + 0.025 * math.sin(2 * math.pi * 100 * t)
    lfo = 0.85 + 0.15 * math.sin(2 * math.pi * 0.25 * t)
    s.append((braun * 2.2 + lp * 0.18 + hum) * lfo * 0.6)
schreibe("rack_luefter.wav", nahtlos(s, RATE))

# Klangteppich: zwei leise Sinus-Akkorde mit langsamer Hüllkurve, ganzzahlige Perioden im Loop.
dauer = 16.0
n = int(RATE * dauer)
akkord = [55.0, 82.5, 110.0, 164.81]
s = []
for i in range(n):
    t = i / RATE
    v = sum(math.sin(2 * math.pi * f * t + k) for k, f in enumerate(akkord)) / len(akkord)
    huelle = 0.6 + 0.4 * math.sin(2 * math.pi * t / dauer)
    s.append(v * huelle * 0.18)
schreibe("ermittlung_teppich.wav", s)
print("ok")
