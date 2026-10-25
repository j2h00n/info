"""8-bit style boss battle loop for the Snake Boss.

160 BPM, E minor, 16 bars (~24 s), seamless loop.
Parts: galloping square bass, pulse-wave lead, 16th-note arpeggio, synth drums.
Usage: python make_boss_bgm.py <output.wav>
"""
import sys
import wave
import numpy as np

SR = 44100
BPM = 160
SIXTEENTH = 60.0 / BPM / 4
BARS = 16
TOTAL = int(round(BARS * 16 * SIXTEENTH * SR))
rng = np.random.default_rng(7)


def freq(midi):
    return 440.0 * 2 ** ((midi - 69) / 12.0)


def pulse(f, n, duty):
    t = np.arange(n) / SR
    phase = (t * f) % 1.0
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
    buf[start:end] += sig[: end - start]


def note(buf, step, length, midi, duty, vol, **env):
    n = int(length * SIXTEENTH * SR)
    sig = pulse(freq(midi), n, duty) * envelope(n, **env) * vol
    add(buf, int(step * SIXTEENTH * SR), sig)


# ── 화성 진행: 2마디씩 Em → C → D → B (B는 D#이 들어가 긴장감) ──
E2, C2, D2, B1 = 40, 36, 38, 35
roots = [E2, E2, C2, C2, D2, D2, B1, B1] * 2
chords = {
    E2: [64, 67, 71],      # E G B
    C2: [60, 64, 67],      # C E G
    D2: [62, 66, 69],      # D F# A
    B1: [59, 63, 66],      # B D# F#
}

# ── 멜로디 (8분음표 단위, None = 쉼표, '-' = 앞 음 유지) ──
N = None
melody = [
    [76, '-', 79, '-', 83, '-', 81, 79],        # Em
    [78, '-', 76, '-', 74, '-', 76, '-'],
    [72, '-', 76, '-', 79, '-', 78, 76],        # C
    [74, '-', '-', '-', 71, '-', '-', '-'],
    [74, '-', 78, '-', 81, '-', 79, 78],        # D
    [76, '-', 74, '-', 72, '-', 74, '-'],
    [71, '-', 75, '-', 78, '-', 81, '-'],       # B
    [79, 78, 76, 75, 71, '-', N, N],
]

bass = np.zeros(TOTAL)
lead = np.zeros(TOTAL)
arp = np.zeros(TOTAL)
drums = np.zeros(TOTAL)

for bar in range(BARS):
    base = bar * 16
    root = roots[bar]

    # 베이스: 다그닥다그닥 (8분 + 16분 갤럽), 마디 끝에 옥타브 점프
    pattern = [(0, 2, 0), (2, 1, 0), (3, 1, 0), (4, 2, 12), (6, 1, 0), (7, 1, 0),
               (8, 2, 0), (10, 1, 0), (11, 1, 0), (12, 2, 12), (14, 2, 7)]
    for s, l, off in pattern:
        note(bass, base + s, l * 0.9, root + off, 0.25, 0.55, decay=0.05, sustain=0.7)

    # 아르페지오: 16분음표로 화음 구성음을 빠르게 (후반 8마디는 한 옥타브 위)
    tones = chords[root]
    up = 12 if bar >= 8 else 0
    for s in range(16):
        tone = tones[[0, 1, 2, 1][s % 4]] + up
        note(arp, base + s, 0.8, tone, 0.125, 0.12 if bar < 8 else 0.16, decay=0.04, sustain=0.3)

    # 멜로디: 후반 8마디는 한 옥타브 아래 음을 겹쳐 더 두껍게
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
        note(lead, base + i * 2, length * 2 * 0.95, m, 0.5, 0.30, decay=0.12, sustain=0.6, release=0.04)
        if bar >= 8:
            note(lead, base + i * 2, length * 2 * 0.95, m - 12, 0.25, 0.16, decay=0.12, sustain=0.6, release=0.04)
        i += length

    # 드럼: 킥(1, 3박 + 당김음), 스네어(2, 4박), 하이햇(8분)
    for s in [0, 6, 8, 11]:
        n = int(0.13 * SR)
        t = np.arange(n) / SR
        f = 45 + 110 * np.exp(-t * 35)
        kick = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 18) * 0.9
        add(drums, int((base + s) * SIXTEENTH * SR), kick)
    for s in [4, 12]:
        n = int(0.12 * SR)
        t = np.arange(n) / SR
        snare = (rng.uniform(-1, 1, n) * 0.6 + np.sin(2 * np.pi * 190 * t) * 0.4) * np.exp(-t * 28) * 0.55
        add(drums, int((base + s) * SIXTEENTH * SR), snare)
    for s in range(0, 16, 2):
        n = int(0.035 * SR)
        hat = np.diff(rng.uniform(-1, 1, n + 1)) * np.exp(-np.arange(n) / SR * 120) * 0.12
        add(drums, int((base + s) * SIXTEENTH * SR), hat)
    # 4마디마다 끝에 스네어 필인
    if bar % 4 == 3:
        for s in [13, 14, 15]:
            n = int(0.08 * SR)
            t = np.arange(n) / SR
            fill = rng.uniform(-1, 1, n) * np.exp(-t * 35) * 0.4
            add(drums, int((base + s) * SIXTEENTH * SR), fill)

mix = bass * 0.9 + lead + arp + drums
mix = np.tanh(mix * 1.1)                 # 부드러운 클리핑
# 기존 BGM(RMS 0.086)보다 살짝 박력 있게, 하지만 귀 아프지 않게 RMS 0.13에 맞춤
mix *= 0.13 / np.sqrt(np.mean(mix ** 2))
mix = np.clip(mix, -0.95, 0.95)

# 루프 이음새 클릭 방지: 끝 5ms를 살짝 페이드
fade = int(0.005 * SR)
mix[-fade:] *= np.linspace(1, 0, fade)

pcm = (mix * 32767).astype(np.int16)
with wave.open(sys.argv[1], 'wb') as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())

print(f"wrote {sys.argv[1]}: {TOTAL / SR:.2f}s, peak {np.max(np.abs(mix)):.2f}, rms {np.sqrt(np.mean(mix**2)):.3f}")
