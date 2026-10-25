"""Sand Eagle chase BGM: fast, tense desert chase loop.

164 BPM, D Phrygian dominant (D Eb F# G A Bb C), 16 bars (~23 s), seamless loop.
Parts: galloping pulse bass, 16th-note arpeggio, nasal pulse lead (octave-doubled in the 2nd half),
darbuka-style hand drums on top of a kick/snare backbeat, rising snare roll at the loop end.
Usage: python make_eagle_bgm.py <output.wav>
"""
import sys
import wave
import numpy as np

SR = 44100
BPM = 164
SIXTEENTH = 60.0 / BPM / 4
BARS = 16
TOTAL = int(round(BARS * 16 * SIXTEENTH * SR))
rng = np.random.default_rng(23)


def freq(midi):
    return 440.0 * 2 ** ((midi - 69) / 12.0)


def pulse(f, n, duty):
    phase = (np.arange(n) / SR * f) % 1.0
    return np.where(phase < duty, 1.0, -1.0)


def envelope(n, attack=0.003, decay=0.08, sustain=0.65, release=0.02):
    env = np.full(n, sustain)
    a = max(1, int(attack * SR))
    d = max(1, int(decay * SR))
    r = max(1, int(release * SR))
    env[:a] = np.linspace(0, 1, a)
    end_d = min(n, a + d)
    env[a:end_d] = np.linspace(1, sustain, end_d - a)
    if n > r:
        env[-r:] *= np.linspace(1, 0, r)
    return env


def add(buf, start, sig):
    end = min(len(buf), start + len(sig))
    if start < len(buf):
        buf[start:end] += sig[: end - start]


def note(buf, step, length, midi, duty, vol, **env):
    n = int(length * SIXTEENTH * SR)
    add(buf, int(step * SIXTEENTH * SR), pulse(freq(midi), n, duty) * envelope(n, **env) * vol)


# ── 화성: 2마디씩 D → Eb(긴장) → C → D, 뒤 8마디는 반복하며 더 세게 ──
D2, Eb2, C2 = 38, 39, 36
roots = [D2, D2, Eb2, D2, C2, C2, Eb2, D2] * 2
chords = {D2: [62, 66, 69], Eb2: [63, 67, 70], C2: [60, 63, 67]}

N = None
melody = [
    [74, '-', 75, 78, 79, '-', 78, 75],
    [74, '-', '-', '-', 81, '-', 79, 78],
    [79, '-', 82, '-', 81, 79, 78, '-'],
    [78, 75, 74, '-', '-', '-', N, N],
    [72, '-', 75, '-', 79, '-', 84, '-'],
    [82, '-', 81, 79, 81, '-', 82, '-'],
    [84, 82, 81, 79, 78, '-', 75, '-'],
    [74, '-', '-', '-', N, N, 69, 70],
]

bass = np.zeros(TOTAL)
lead = np.zeros(TOTAL)
arp = np.zeros(TOTAL)
drums = np.zeros(TOTAL)


def doum(vol):
    n = int(0.16 * SR)
    t = np.arange(n) / SR
    return np.sin(2 * np.pi * np.cumsum(85 + 70 * np.exp(-t * 28)) / SR) * np.exp(-t * 14) * vol


def tek(vol):
    n = int(0.045 * SR)
    t = np.arange(n) / SR
    return (rng.uniform(-1, 1, n) * 0.5 + np.sin(2 * np.pi * 900 * t) * 0.5) * np.exp(-t * 75) * vol


for bar in range(BARS):
    base = bar * 16
    root = roots[bar]
    late = bar >= 8

    # 베이스: 다그닥 질주 (16분 갤럽), 마디 끝은 반음 위(Eb)로 찔러 긴장
    for s, l, off in [(0, 2, 0), (2, 1, 0), (3, 1, 0), (4, 2, 12), (6, 1, 0), (7, 1, 0),
                      (8, 2, 0), (10, 1, 0), (11, 1, 0), (12, 2, 12), (14, 1, 1), (15, 1, 0)]:
        note(bass, base + s, l * 0.9, root + off, 0.25, 0.55, decay=0.05, sustain=0.7)

    # 아르페지오: 16분음표로 화음을 빠르게 굴림 (뒤 8마디는 한 옥타브 위)
    tones = chords[root]
    for s in range(16):
        tone = tones[[0, 1, 2, 1][s % 4]] + (12 if late else 0)
        note(arp, base + s, 0.8, tone, 0.125, 0.11 if not late else 0.14, decay=0.04, sustain=0.3)

    # 멜로디: 콧소리 나는 좁은 펄스 (사막 피리 느낌), 뒤 8마디는 옥타브 아래를 겹침
    line = melody[bar % 8]
    i = 0
    while i < 8:
        m = line[i]
        if m is None or m == '-':
            i += 1
            continue
        length = 1
        while i + length < 8 and line[i + length] == '-':
            length += 1
        note(lead, base + i * 2, length * 2 * 0.95, m, 0.3, 0.30, decay=0.1, sustain=0.65, release=0.04)
        if late:
            note(lead, base + i * 2, length * 2 * 0.95, m - 12, 0.25, 0.15, decay=0.1, sustain=0.6, release=0.04)
        i += length

    # 드럼: 킥(1, 3박 + 당김) + 스네어(2, 4박) + 손북 둠/텍 + 하이햇
    for s in [0, 6, 8, 11]:
        n = int(0.13 * SR)
        t = np.arange(n) / SR
        add(drums, int((base + s) * SIXTEENTH * SR), np.sin(2 * np.pi * np.cumsum(45 + 110 * np.exp(-t * 35)) / SR) * np.exp(-t * 18) * 0.85)
    for s in [4, 12]:
        n = int(0.12 * SR)
        t = np.arange(n) / SR
        add(drums, int((base + s) * SIXTEENTH * SR), (rng.uniform(-1, 1, n) * 0.6 + np.sin(2 * np.pi * 190 * t) * 0.4) * np.exp(-t * 28) * 0.5)
    for s in [3, 10]:
        add(drums, int((base + s) * SIXTEENTH * SR), doum(0.45))
    for s in [1, 5, 7, 9, 13, 14, 15] if late else [5, 9, 13, 15]:
        add(drums, int((base + s) * SIXTEENTH * SR), tek(0.28))
    for s in range(0, 16, 2):
        n = int(0.03 * SR)
        add(drums, int((base + s) * SIXTEENTH * SR), np.diff(rng.uniform(-1, 1, n + 1)) * np.exp(-np.arange(n) / SR * 120) * 0.1)
    # 마지막 마디: 점점 커지는 스네어 롤 → 처음으로 돌아가며 폭발
    if bar == BARS - 1:
        for k, s in enumerate(range(8, 16)):
            n = int(0.07 * SR)
            t = np.arange(n) / SR
            add(drums, int((base + s) * SIXTEENTH * SR), rng.uniform(-1, 1, n) * np.exp(-t * 35) * (0.2 + k * 0.05))

mix = bass * 0.9 + lead + arp + drums
mix = np.tanh(mix * 1.1)
mix *= 0.13 / np.sqrt(np.mean(mix ** 2))   # 뱀 보스 BGM과 같은 크기
mix = np.clip(mix, -0.95, 0.95)
fade = int(0.005 * SR)
mix[-fade:] *= np.linspace(1, 0, fade)

with wave.open(sys.argv[1], 'wb') as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((mix * 32767).astype(np.int16).tobytes())
print(f"wrote {sys.argv[1]}: {TOTAL / SR:.2f}s, peak {np.max(np.abs(mix)):.2f}, rms {np.sqrt(np.mean(mix ** 2)):.3f}")
