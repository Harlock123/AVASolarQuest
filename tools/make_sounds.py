#!/usr/bin/env python3
"""Synthesises AVASolarQuest's sound effects and tunes into ../src/AVASolarQuest/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASolarQuest", "Sounds")
RNG = random.Random(1981)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ship: a bright zap, the engine's rush, and the hyperspace jump sweeping up.
    write("Fire.wav", tone(0.13, 2200, 600, "square", release=0.6), 0.2)
    write("Thrust.wav", tone(0.26, 180, 130, "noise", attack=0.03, release=0.4), 0.22)
    write("Hyperspace.wav", cat(tone(0.25, 200, 2400, "tri", attack=0.01, release=0.3, vibrato=0.05, vib_rate=25), tone(0.15, 2400, 1200, "noise", release=0.6, gain=0.4)), 0.32)
    write("EnemyFire.wav", tone(0.16, 900, 400, "pulse", release=0.6), 0.22)
    write("Blast.wav", mix(tone(0.55, 1200, 60, "noise", release=0.45), tone(0.45, 320, 50, "square", release=0.45, gain=0.5)), 0.44)
    write("Clang.wav", mix(tone(0.35, 700, 690, "square", release=1.2, gain=0.5), tone(0.35, 1050, 1040, "tri", release=1.4, gain=0.5)), 0.32)
    # A survivor: picked up (a rising chime), or lost to the sun (a falling wail).
    write("Pickup.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["E5", "G5", "B5", "E6"]]), 0.34)
    write("Lost.wav", tone(0.6, 900, 180, "tri", attack=0.01, release=0.5, vibrato=0.06, vib_rate=9), 0.3)
    # Into the sun: a sizzle and a roar.
    write("Burned.wav", mix(tone(1.0, 3000, 400, "noise", attack=0.02, release=0.4), tone(0.9, 140, 60, "saw", release=0.4, gain=0.6)), 0.48)
    write("Death.wav", cat(mix(tone(0.9, 1200, 40, "noise", release=0.3), tone(0.8, 400, 30, "saw", release=0.3, gain=0.7)), *[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4"]]), 0.5)
    write("WaveStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["A4", "E5", "A5"]]), 0.3)
    write("WaveClear.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["A5", "C#6", "E6", "A6", "E6", "A6"]]), 0.36)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["E6", "B6", "E6", "B6", "E7"]]), 0.38)

    # The sun's hum: a low drone that swells and fades twice in four seconds, so it loops cleanly.
    n = int(RATE * 4)
    hum = []
    for i in range(n):
        t = i / RATE
        swell = 0.6 + 0.4 * math.sin(2 * math.pi * t / 2) ** 2
        s = math.sin(2 * math.pi * 55 * t) + 0.5 * math.sin(2 * math.pi * 82.5 * t) + 0.25 * math.sin(2 * math.pi * 110 * t + math.sin(2 * math.pi * 0.5 * t))
        hum.append(s * swell)
    write("Music.wav", hum, 0.16)

    # The title: a slow, wide theme in A minor, rising like a dawn (an original composition).
    lead = [("A4", 1), ("E5", 1), ("D5", 0.5), ("C5", 0.5), ("B4", 1),
            ("A4", 1), ("C5", 1), ("E5", 2),
            ("F5", 1), ("E5", 0.5), ("D5", 0.5), ("C5", 1), ("B4", 1),
            ("A4", 1), ("B4", 0.5), ("G#4", 0.5), ("A4", 2)]
    bass = [("A2", 2), ("E2", 2), ("F2", 2), ("C3", 2), ("D2", 2), ("A2", 2), ("E2", 2), ("A2", 2)]
    write("TitleTune.wav", cat(tune(96, lead, bass, kind="tri"), silence(1.2)), 0.44)
    write("StartTune.wav", tune(150, [("A4", 0.5), ("C5", 0.5), ("E5", 0.5), ("A5", 0.5), ("G#5", 0.5), ("A5", 1.5)], [("A2", 1), ("E2", 1), ("A2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(70, [("E5", 1), ("C5", 1), ("B4", 1), ("G#4", 1), ("A4", 4)], [("A2", 2), ("F2", 2), ("E2", 2), ("A1", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
