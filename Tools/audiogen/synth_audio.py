#!/usr/bin/env python3
"""Synthesises the FearMe breath and ambient-layer clips.

Pure standard library on purpose: no numpy, no install step, runs anywhere
python3 does. Writes 16-bit mono WAV at 44.1 kHz.

The ambient layers are built to loop seamlessly. Tonal parts use only
frequencies completing a whole number of cycles per loop; noise beds are
wrapped with a circular crossfade, so the last sample leads back into the
first with no seam.

    python3 synth_audio.py <output-root>
"""

import array
import math
import os
import random
import sys
import wave

SR = 44100


# ---------------------------------------------------------------- primitives

def noise(n, rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(n)]


def lowpass(x, cutoff):
    # One-pole. Gentle by design; stacking two reads as "distant" rather than
    # "muffled", which a steeper filter tends to give.
    a = 1.0 - math.exp(-2.0 * math.pi * cutoff / SR)
    y, out = 0.0, [0.0] * len(x)
    for i, s in enumerate(x):
        y += a * (s - y)
        out[i] = y
    return out


def highpass(x, cutoff):
    return [s - l for s, l in zip(x, lowpass(x, cutoff))]


def bandpass_sweep(x, f_start, f_end, q):
    """Chamberlin state-variable bandpass whose centre glides f_start -> f_end.

    The glide is what makes filtered noise read as breath rather than hiss:
    air moving through a throat that is opening or closing.
    """
    n = len(x)
    q1 = 1.0 / q
    low = band = 0.0
    out = [0.0] * n
    for i, s in enumerate(x):
        t = i / max(1, n - 1)
        fc = f_start + (f_end - f_start) * t
        f = 2.0 * math.sin(math.pi * min(fc, SR / 6.0) / SR)
        high = s - low - q1 * band
        band += f * high
        low += f * band
        out[i] = band
    return out


def circular_crossfade(x, fade_secs=0.75):
    """Fold the tail back over the head so the buffer loops without a seam.

    Anything filtered must be filtered BEFORE this runs. A filter carries
    state, so filtering afterwards leaves the start and end of the buffer in
    different states and the seam ticks once per loop.
    """
    f = int(fade_secs * SR)
    n = len(x) - f
    out = x[:n]
    for i in range(f):
        w = i / f
        out[i] = out[i] * w + x[n + i] * (1.0 - w)
    return out


WARMUP = int(0.25 * SR)


def bed(n, rng, shape):
    """Noise bed: generate long, let `shape` filter it, discard the filter
    warm-up, then wrap. Returns exactly n samples that loop cleanly."""
    pad = int(0.75 * SR)
    return circular_crossfade(shape(noise(n + pad + WARMUP, rng))[WARMUP:])


def loop_sine(n, cycles, phase=0.0, amp=1.0):
    """Sine completing exactly `cycles` whole cycles across the buffer."""
    return [amp * math.sin(2.0 * math.pi * cycles * i / n + phase) for i in range(n)]


def loop_lfo(n, cycles, lo, hi, phase=0.0):
    span = (hi - lo) * 0.5
    mid = lo + span
    return [mid + span * math.sin(2.0 * math.pi * cycles * i / n + phase) for i in range(n)]


def env_breath(n, attack, hold, curve=2.0):
    """Raised-cosine attack into a power-curve decay. No clicks at either end."""
    a = max(1, int(attack * n))
    h = int(hold * n)
    out = [0.0] * n
    for i in range(n):
        if i < a:
            out[i] = 0.5 - 0.5 * math.cos(math.pi * i / a)
        elif i < a + h:
            out[i] = 1.0
        else:
            t = (i - a - h) / max(1, n - a - h)
            out[i] = (1.0 - t) ** curve
    return out


def mix(*layers):
    n = max(len(l) for l in layers)
    out = [0.0] * n
    for layer in layers:
        for i, s in enumerate(layer):
            out[i] += s
    return out


def gain(x, g):
    return [s * g for s in x]


def normalise(x, peak=0.89):
    p = max(abs(s) for s in x) or 1.0
    return [s * (peak / p) for s in x]


def edge_fade(x, secs=0.008):
    f = min(int(secs * SR), len(x) // 2)
    for i in range(f):
        w = i / f
        x[i] *= w
        x[-1 - i] *= w
    return x


def write_wav(path, samples):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = array.array("h", (int(max(-1.0, min(1.0, s)) * 32767) for s in samples))
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    return path


# ------------------------------------------------------------------- breaths

def breath(rng, dur, f_start, f_end, q, attack, hold, curve, rasp=0.0):
    """One inhale or exhale. rasp adds the voiced edge of a strained breath."""
    n = int(dur * SR)
    body = bandpass_sweep(noise(n, rng), f_start, f_end, q)
    air = gain(lowpass(noise(n, rng), 900.0), 0.35)
    parts = [body, air]

    if rasp > 0.0:
        # A weak buzz near the bottom of the voice, jittered so it stays a
        # rasp and never settles into a hum.
        buzz = [0.0] * n
        phase = 0.0
        for i in range(n):
            phase += 2.0 * math.pi * (95.0 + rng.uniform(-9.0, 9.0)) / SR
            buzz[i] = math.sin(phase) * (0.6 + 0.4 * rng.random())
        parts.append(gain(lowpass(buzz, 700.0), rasp))

    env = env_breath(n, attack, hold, curve)
    return [s * e for s, e in zip(mix(*parts), env)]


def silence(dur):
    return [0.0] * int(dur * SR)


def breath_clip(segments):
    return edge_fade(normalise(mix(*[seq for seq in [sum(segments, [])]]), 0.82))


# ------------------------------------------------------------ ambient layers

def layer_sub(rng, dur=20.0):
    """Sub-bass drone. Felt more than heard; nothing above ~120 Hz."""
    n = int(dur * SR)
    # 36 Hz and 54 Hz — a fifth apart, both whole cycles across the loop.
    base = loop_sine(n, 720, amp=0.62)          # 36 Hz
    fifth = loop_sine(n, 1080, amp=0.20)        # 54 Hz
    # Two cycles of drift over the loop keeps it from sitting perfectly still.
    breathe = loop_lfo(n, 2, 0.72, 1.0)
    rumble = gain(bed(n, rng, lambda v: lowpass(v, 55.0)), 2.2)
    body = mix(base, fifth, rumble)
    return normalise([s * m for s, m in zip(body, breathe)], 0.72)


def layer_room(rng, dur=20.0):
    """Room tone of an empty house: air, a little rumble, wind well outside."""
    n = int(dur * SR)
    air = bed(n, rng, lambda v: highpass(lowpass(v, 5200.0), 300.0))
    rumble = gain(bed(n, rng, lambda v: lowpass(v, 90.0)), 2.6)
    # Wind: a noise band swelling three times across the loop, never peaking
    # hard enough to become an event.
    wind = bed(n, rng, lambda v: bandpass_sweep(v, 420.0, 420.0, 1.4))
    swell = loop_lfo(n, 3, 0.10, 1.0)
    wind = [s * m for s, m in zip(wind, swell)]
    return normalise(mix(gain(air, 0.5), rumble, gain(wind, 0.9)), 0.60)


def layer_shimmer(rng, dur=20.0):
    """High dissonant ringing. Quiet, unstable, hard to locate."""
    n = int(dur * SR)
    # Deliberately close partials so they beat against each other.
    tones = []
    for cycles, amp, lfo_cycles in ((47000, 0.30, 5), (49780, 0.26, 7), (62600, 0.18, 11)):
        tone = loop_sine(n, cycles, phase=rng.uniform(0, 6.28), amp=amp)
        wobble = loop_lfo(n, lfo_cycles, 0.15, 1.0, phase=rng.uniform(0, 6.28))
        tones.append([s * m for s, m in zip(tone, wobble)])
    hiss = gain(bed(n, rng, lambda v: highpass(v, 4200.0)), 0.22)
    return normalise(mix(*tones, hiss), 0.45)


def layer_dread(rng, dur=20.0):
    """A slow swell that rises and falls away. Starts and ends at silence, so
    it loops without needing a crossfade at all."""
    n = int(dur * SR)
    swell = [math.sin(math.pi * i / n) ** 2.4 for i in range(n)]
    low = mix(loop_sine(n, 1100, amp=0.55),    # 55 Hz
              loop_sine(n, 1650, amp=0.22),    # 82.5 Hz, a fifth up
              loop_sine(n, 2620, amp=0.10))    # 131 Hz, dissonant against both
    breathy = gain(bandpass_sweep(noise(n, rng), 180.0, 520.0, 1.1), 0.8)
    body = mix(low, breathy)
    return normalise([s * e for s, e in zip(body, swell)], 0.66)


# ----------------------------------------------------------------------- main

def main(root):
    rng = random.Random(20260911)
    written = []

    heavy = os.path.join(root, "Breathing", "Heavy")
    calm = os.path.join(root, "Breathing", "Calm")
    layers = os.path.join(root, "Ambient", "Layers")

    # Heavy 01 - fast panicked panting, five shallow mouth breaths.
    seg = []
    for _ in range(5):
        seg += breath(rng, 0.24, 900, 1500, 1.5, 0.16, 0.08, 1.6)
        seg += silence(0.03)
        seg += breath(rng, 0.20, 800, 480, 1.4, 0.10, 0.05, 1.8)
        seg += silence(rng.uniform(0.05, 0.11))
    written.append(write_wav(os.path.join(heavy, "Heavy_Breath_01.wav"), breath_clip([seg])))

    # Heavy 02 - ragged and exhausted, slower, voiced edge on the exhales.
    seg = []
    for _ in range(3):
        seg += breath(rng, 0.52, 700, 1350, 1.2, 0.22, 0.12, 1.5, rasp=0.10)
        seg += silence(0.06)
        seg += breath(rng, 0.62, 760, 380, 1.1, 0.10, 0.18, 1.4, rasp=0.20)
        seg += silence(rng.uniform(0.14, 0.24))
    written.append(write_wav(os.path.join(heavy, "Heavy_Breath_02.wav"), breath_clip([seg])))

    # Heavy 03 - one sharp gasp, then breathing held down small and tight.
    seg = breath(rng, 0.34, 1100, 2100, 2.1, 0.05, 0.04, 2.2)
    seg += silence(0.22)
    for _ in range(4):
        seg += breath(rng, 0.26, 950, 1250, 2.6, 0.24, 0.05, 1.9, rasp=0.05)
        seg += silence(0.04)
        seg += gain(breath(rng, 0.22, 700, 520, 2.4, 0.14, 0.04, 2.0), 0.7)
        seg += silence(rng.uniform(0.10, 0.18))
    written.append(write_wav(os.path.join(heavy, "Heavy_Breath_03.wav"), breath_clip([seg])))

    # Calm 02 - a soft exhale through parted lips.
    seg = breath(rng, 0.9, 520, 360, 0.9, 0.28, 0.10, 1.5)
    seg += silence(0.25)
    written.append(write_wav(os.path.join(calm, "Calm_Breath_02.wav"),
                             edge_fade(normalise(seg, 0.55))))

    # Calm 03 - a slow deep breath in, a pause, a long breath out.
    seg = breath(rng, 1.35, 420, 780, 1.0, 0.40, 0.14, 1.6)
    seg += silence(0.30)
    seg += breath(rng, 1.90, 620, 300, 0.9, 0.20, 0.16, 1.3)
    written.append(write_wav(os.path.join(calm, "Calm_Breath_03.wav"),
                             edge_fade(normalise(seg, 0.60))))

    # Ambient layers - seamless, 20 s each.
    written.append(write_wav(os.path.join(layers, "Layer_1_Sub.wav"), layer_sub(rng)))
    written.append(write_wav(os.path.join(layers, "Layer_2_Room.wav"), layer_room(rng)))
    written.append(write_wav(os.path.join(layers, "Layer_3_Shimmer.wav"), layer_shimmer(rng)))
    written.append(write_wav(os.path.join(layers, "Layer_4_Dread.wav"), layer_dread(rng)))

    for path in written:
        print("wrote", path)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "Audio")
