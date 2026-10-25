"""Egg hatching sound effects for the story-mode opening.

tap  : a light knock on the shell from inside (~0.15 s)
crack: the shell bursting open - crunchy crackle + small pop (~0.6 s)
Usage: python make_egg_sfx.py tap|crack <output.wav>
"""
import sys
import wave
import numpy as np

SR = 44100
rng = np.random.default_rng(9)


def write(path, sig, peak):
    sig = sig / (np.max(np.abs(sig)) + 1e-9) * peak
    fade = int(0.003 * SR)
    sig[-fade:] *= np.linspace(1, 0, fade)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((sig * 32767).astype(np.int16).tobytes())
    print(f"wrote {path}: {len(sig) / SR:.2f}s")


def knock(n, pitch, decay):
    """껍질 두드리는 소리: 짧은 딸깍 + 속이 빈 울림"""
    t = np.arange(n) / SR
    click = rng.uniform(-1, 1, n) * np.exp(-t * 900)
    body = (np.sin(2 * np.pi * pitch * t) + 0.5 * np.sin(2 * np.pi * pitch * 2.7 * t)) * np.exp(-t * decay)
    return click * 0.8 + body * 0.6


def tap(path):
    n = int(0.15 * SR)
    write(path, knock(n, 1400, 60), 0.7)


def crack(path):
    n = int(0.6 * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    # 바삭바삭: 짧은 딸깍 수십 개가 처음에 몰렸다가 잦아듦
    times = np.sort(rng.exponential(0.07, 38))
    for s in times:
        i = int(s * SR)
        if i >= n:
            continue
        m = min(n - i, int(0.03 * SR))
        tt = np.arange(m) / SR
        out[i:i + m] += rng.uniform(-1, 1, m) * np.exp(-tt * rng.uniform(250, 600)) * rng.uniform(0.3, 1.0)
    # 껍데기가 톡 갈라지는 큰 소리 하나
    out[:int(0.12 * SR)] += knock(int(0.12 * SR), 900, 35) * 1.2
    out *= np.exp(-t * 4)
    write(path, out, 0.75)


if __name__ == '__main__':
    {'tap': tap, 'crack': crack}[sys.argv[1]](sys.argv[2])
