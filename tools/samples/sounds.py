#!/usr/bin/env python3
# The samples' sounds — every .wav under com.xpturn.feedback/Samples~/<bundle>/.../Art/Audio is written here from one row of
# SOUNDS: a tone (sines and their harmonics at 1/h, gliding to drop × the frequency over the sound), a noise (a 31-bit
# linear congruential generator from a fixed seed through a one-pole low pass) and an envelope (the attack, then the decay;
# the noise decays on its own). A loop has no envelope, its frequencies make whole cycles in its length and its noise filter
# runs around the end, so its end joins its start. A one-shot fades its last 5 ms. PCM16 mono 44.1 kHz, peak at the row's
# gain; the same rows give the same bytes. A bundle holds the sounds its scenes use (a bundle imports alone).
#   sounds.py [--out <Samples~>] [--check]   (--check writes nothing, lists the files that differ and exits 1)
import argparse, io, math, os, struct, sys, wave

RATE = 44100
FADE = 0.005
HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLES = os.path.normpath(os.path.join(HERE, "..", "..", "com.xpturn.feedback", "Samples~"))

#            name             seconds freqs        harmonics drop   noise  noise_lp attack  decay  noise_decay loop   gain  seed
SOUNDS = [
    ("Hit Light",      0.12, (190.0,),       2,   0.5,   0.6,  0.35,    0.001,  0.05,  0.02,       False, 0.7,  11),
    ("Hit Heavy",      0.25, (120.0,),       2,   0.42,  0.55, 0.2,     0.001,  0.1,   0.04,       False, 0.8,  12),
    ("Hit Critical",   0.35, (140.0,),       3,   0.35,  0.6,  0.5,     0.001,  0.16,  0.05,       False, 0.85, 13),
    ("Blip",           0.08, (1320.0,),      1,   1.0,   0.0,  1.0,     0.002,  0.03,  0.01,       False, 0.5,  14),
    ("Tone Loop",      2.0,  (220.0, 330.0), 6,   1.0,   0.0,  1.0,     0.0,    0.0,   0.0,        True,  0.5,  15),
    ("Ambience Loop",  4.0,  (55.0, 110.0),  3,   1.0,   0.35, 0.02,    0.0,    0.0,   0.0,        True,  0.5,  16),
    ("Break",          0.2,  (900.0,),       1,   0.6,   0.8,  0.9,     0.001,  0.08,  0.05,       False, 0.6,  17),
    ("Boom",           0.6,  (70.0,),        2,   0.4,   0.7,  0.06,    0.003,  0.3,   0.25,       False, 0.85, 18),
    ("Lose",           0.6,  (660.0,),       3,   1 / 3, 0.0,  1.0,     0.005,  0.5,   0.01,       False, 0.5,  19),
]

BUNDLES = {
    "Basics/Gallery/Art/Audio": ("Tone Loop", "Hit Heavy", "Blip"),
    "GameFeelLabs/Art/Audio": ("Hit Light", "Hit Heavy", "Hit Critical", "Ambience Loop"),
    "Advanced/Art/Audio": ("Hit Light", "Hit Heavy", "Hit Critical", "Ambience Loop"),
    "Showcase/Art/Audio": ("Blip", "Break", "Boom", "Lose", "Hit Heavy"),
}


def noise(n, seed, lp, loop):
    x = seed & 0x7FFFFFFF
    white = []
    for _ in range(n):
        x = (1103515245 * x + 12345) & 0x7FFFFFFF
        white.append(x / 0x3FFFFFFF - 1.0)
    y = 0.0
    out = [0.0] * n
    for _ in range(2 if loop else 1):
        for i in range(n):
            y += lp * (white[i] - y)
            out[i] = y
    return out


def render(row):
    name, seconds, freqs, harmonics, drop, amount, lp, attack, decay, noise_decay, loop, gain, seed = row
    n = int(round(seconds * RATE))
    hiss = noise(n, seed, lp, loop) if amount > 0 else [0.0] * n
    phases = [0.0] * len(freqs)
    raw = []
    for i in range(n):
        t = i / RATE
        tone = 0.0
        for k, f in enumerate(freqs):
            if loop:
                phase = 2.0 * math.pi * f * t
            else:
                phase = phases[k]
                phases[k] += 2.0 * math.pi * f * drop ** (t / seconds) / RATE
            for h in range(1, harmonics + 1):
                tone += math.sin(h * phase) / h
        if loop:
            v = (1.0 - amount) * tone / len(freqs) + amount * hiss[i]
        else:
            rise = min(1.0, t / attack) if attack > 0 else 1.0
            after = max(0.0, t - attack)
            v = rise * ((1.0 - amount) * tone * math.exp(-after / decay) + amount * hiss[i] * math.exp(-after / noise_decay))
            left = seconds - t
            if left < FADE:
                v *= max(0.0, left / FADE)
        raw.append(v)
    peak = max(abs(v) for v in raw) or 1.0
    frames = b"".join(struct.pack("<h", max(-32767, min(32767, int(round(v / peak * gain * 32767))))) for v in raw)
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(frames)
    return buffer.getvalue()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default=SAMPLES)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    rows = {row[0]: row for row in SOUNDS}
    made = {}
    differ = []
    for folder, names in BUNDLES.items():
        for name in names:
            if name not in made:
                made[name] = render(rows[name])
            path = os.path.join(args.out, folder, name + ".wav")
            same = os.path.isfile(path) and open(path, "rb").read() == made[name]
            if args.check:
                if not same:
                    differ.append(path)
            elif not same:
                os.makedirs(os.path.dirname(path), exist_ok=True)
                with open(path, "wb") as f:
                    f.write(made[name])
                print("wrote " + os.path.relpath(path, args.out))
    if args.check:
        for path in differ:
            print("differs: " + os.path.relpath(path, args.out))
        print("sounds: %d files, %d differ" % (sum(len(v) for v in BUNDLES.values()), len(differ)))
        return 1 if differ else 0
    return 0


if __name__ == "__main__":
    sys.exit(main())
