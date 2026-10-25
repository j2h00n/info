"""Story-mode stage BGM loops (forest / desert), same chiptune toolkit as make_boss_bgm.py.

forest: 112 BPM, G major, 16 bars (~34 s). Soft triangle bass, plucky arpeggio, flute-like lead, light drums.
desert: 100 BPM, D Phrygian dominant, 16 bars (~38 s). Drone, oud-like plucked lead, darbuka-style hand drums.
Usage: python make_stage_bgm.py forest|desert <output.wav>
"""
import sys
import wave
import numpy as np

SR = 44100
rng = np.random.default_rng(11)


def freq(midi):
    return 440.0 * 2 ** ((midi - 69) / 12.0)


def envelope(n, attack=0.005, decay=0.1, sustain=0.6, release=0.03):
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


def pulse(f, n, duty):
    phase = (np.arange(n) / SR * f) % 1.0
    return np.where(phase < duty, 1.0, -1.0)


def triangle(f, n):
    phase = (np.arange(n) / SR * f) % 1.0
    return 4 * np.abs(phase - 0.5) - 1


def flute(f, n):
    # 사인파 + 살짝 늦게 들어오는 비브라토 + 약한 2배음
    t = np.arange(n) / SR
    vib = 1 + 0.006 * np.sin(2 * np.pi * 5.5 * t) * np.clip(t / 0.25, 0, 1)
    ph = 2 * np.pi * np.cumsum(f * vib) / SR
    return np.sin(ph) + 0.18 * np.sin(2 * ph)


def pluck(f, n, bright=0.5):
    # Karplus-Strong 현 뜯는 소리 (오우드/류트 느낌)
    period = max(2, int(SR / f))
    buf = rng.uniform(-1, 1, period)
    out = np.empty(n)
    for i in range(n):
        out[i] = buf[i % period]
        buf[i % period] = (buf[i % period] * bright + buf[(i + 1) % period] * (1 - bright)) * 0.996
    return out


def kick(vol):
    n = int(0.14 * SR)
    t = np.arange(n) / SR
    f = 45 + 90 * np.exp(-t * 30)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 16) * vol


def hat(vol, length=0.03):
    n = int(length * SR)
    return np.diff(rng.uniform(-1, 1, n + 1)) * np.exp(-np.arange(n) / SR * 110) * vol


def finish(mix, rms, path):
    mix = np.tanh(mix * 1.05)
    mix *= rms / np.sqrt(np.mean(mix ** 2))
    mix = np.clip(mix, -0.95, 0.95)
    fade = int(0.005 * SR)
    mix[:fade] *= np.linspace(0, 1, fade)
    mix[-fade:] *= np.linspace(1, 0, fade)
    pcm = (mix * 32767).astype(np.int16)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"wrote {path}: {len(mix) / SR:.2f}s, peak {np.max(np.abs(mix)):.2f}, rms {np.sqrt(np.mean(mix ** 2)):.3f}")


def play_line(buf, line, base, step_len, voice, vol, env, octave_doubling=None):
    """line: 8분음표 칸 목록 (MIDI, '-' = 앞 음 유지, None = 쉼표)"""
    i = 0
    while i < len(line):
        m = line[i]
        if m is None or m == '-':
            i += 1
            continue
        length = 1
        while i + length < len(line) and line[i + length] == '-':
            length += 1
        n = int(length * 2 * step_len * SR * 0.96)
        start = int((base + i * 2) * step_len * SR)
        add(buf, start, voice(freq(m), n) * envelope(n, **env) * vol)
        if octave_doubling is not None:
            add(buf, start, voice(freq(m + octave_doubling), n) * envelope(n, **env) * vol * 0.45)
        i += length


# ───────────────────────── 숲: 밝고 산뜻한 산책 ─────────────────────────
def forest(path):
    bpm = 112
    st = 60.0 / bpm / 4
    bars = 16
    total = int(round(bars * 16 * st * SR))
    G, Em, C, D = 43, 40, 36, 38
    roots = [G, Em, C, D] * 4
    chords = {G: [67, 71, 74], Em: [64, 67, 71], C: [64, 67, 72], D: [66, 69, 74]}
    N = None
    melody = [
        [74, '-', 71, 72, 74, '-', 79, '-'],     # G
        [76, '-', 74, '-', 71, '-', '-', N],     # Em
        [72, '-', 76, 74, 72, '-', 67, '-'],     # C
        [69, '-', 71, 72, 74, '-', '-', N],      # D
        [74, '-', 71, 72, 74, '-', 79, 81],      # G
        [83, '-', 81, 79, 76, '-', 79, '-'],     # Em
        [81, '-', 79, 76, 72, '-', 74, 76],      # C
        [74, '-', '-', '-', N, N, 71, 72],       # D
    ]
    bass = np.zeros(total)
    arp = np.zeros(total)
    lead = np.zeros(total)
    drums = np.zeros(total)

    for bar in range(bars):
        base = bar * 16
        root = roots[bar]
        # 베이스: 근음 - 5도 왔다갔다 (부드러운 삼각파)
        for s, off in [(0, 0), (4, 7), (8, 12), (12, 7)]:
            n = int(3.6 * st * SR)
            add(bass, int((base + s) * st * SR), triangle(freq(root + off), n) * envelope(n, decay=0.2, sustain=0.5) * 0.5)
        # 아르페지오: 8분음표로 통통 튀게
        tones = chords[root]
        for k, s in enumerate(range(0, 16, 2)):
            n = int(1.6 * st * SR)
            tone = tones[[0, 1, 2, 1][k % 4]] + (12 if bar >= 8 and k % 2 else 0)
            add(arp, int((base + s) * st * SR), pulse(freq(tone), n, 0.25) * envelope(n, decay=0.05, sustain=0.15) * 0.10)
        # 멜로디: 앞 8마디는 플루트만, 뒤 8마디는 한 옥타브 아래를 겹쳐 풍성하게
        play_line(lead, melody[bar % 8], base, st, flute, 0.32,
                  dict(attack=0.03, decay=0.15, sustain=0.75, release=0.06),
                  octave_doubling=-12 if bar >= 8 else None)
        # 드럼: 킥 1·3박, 하이햇 8분 (뒷박 살짝 크게), 4마디마다 작은 필인
        for s in [0, 8]:
            add(drums, int((base + s) * st * SR), kick(0.6))
        for s in range(0, 16, 2):
            add(drums, int((base + s) * st * SR), hat(0.06 if s % 4 == 0 else 0.1))
        if bar % 4 == 3:
            for s in [12, 14, 15]:
                add(drums, int((base + s) * st * SR), hat(0.14, 0.06))

    finish(bass + arp + lead + drums, 0.09, path)


# ───────────────────────── 사막: 이국적인 모래 언덕 ─────────────────────────
def desert(path):
    bpm = 100
    st = 60.0 / bpm / 4
    bars = 16
    total = int(round(bars * 16 * st * SR))
    # D 프리지안 도미넌트: D Eb F# G A Bb C
    D4, Eb4, Fs4, G4, A4, Bb4, C5, D5, Eb5 = 62, 63, 66, 67, 69, 70, 72, 74, 75
    N = None
    melody = [
        [D4, Eb4, Fs4, '-', G4, '-', Fs4, Eb4],
        [D4, '-', '-', '-', N, N, A4, Bb4],
        [A4, '-', G4, Fs4, G4, '-', A4, '-'],
        [Fs4, Eb4, D4, '-', '-', '-', N, N],
        [A4, Bb4, C5, '-', D5, '-', C5, Bb4],
        [A4, '-', Bb4, A4, G4, '-', Fs4, '-'],
        [G4, Fs4, Eb4, '-', Fs4, '-', G4, A4],
        [D4, '-', '-', '-', N, N, N, N],
    ]
    bass_roots = [38, 38, 36, 38, 38, 38, 39, 38] * 2   # D D C D D D Eb D

    drone = np.zeros(total)
    bass = np.zeros(total)
    lead = np.zeros(total)
    drums = np.zeros(total)

    # 드론: D + A 낮게 계속 깔림 (천천히 숨쉬듯 커졌다 작아짐)
    t = np.arange(total) / SR
    swell = 0.75 + 0.25 * np.sin(2 * np.pi * t / (16 * st * 4))
    drone += (triangle(freq(38), total) * 0.5 + triangle(freq(45), total) * 0.35) * swell * 0.22

    for bar in range(bars):
        base = bar * 16
        root = bass_roots[bar]
        # 베이스: 둠-(쉬고)-둠둠 리듬을 뜯는 소리로
        for s in [0, 6, 8, 12]:
            n = int(2.5 * st * SR)
            add(bass, int((base + s) * st * SR), pluck(freq(root), n, 0.3) * 0.55)
        # 멜로디: 오우드처럼 뜯는 소리, 뒤 8마디는 장식음(트레몰로) 추가
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
            start = base + i * 2
            if bar >= 8 and length >= 2:
                # 길게 끄는 음은 16분음표로 빠르게 반복해서 뜯음
                for r in range(length * 2):
                    n = int(st * SR * 1.2)
                    add(lead, int((start + r) * st * SR), pluck(freq(m), n, 0.5) * (0.42 if r == 0 else 0.28))
            else:
                n = int(length * 2 * st * SR * 1.1)
                add(lead, int(start * st * SR), pluck(freq(m), n, 0.5) * 0.42)
            i += length
        # 손북(다르부카 느낌): 둠(낮고 둥근 소리) / 텍(높고 짧은 소리)
        for s in [0, 7, 8]:
            n = int(0.18 * SR)
            tt = np.arange(n) / SR
            doum = np.sin(2 * np.pi * np.cumsum(90 + 60 * np.exp(-tt * 25)) / SR) * np.exp(-tt * 12) * 0.6
            add(drums, int((base + s) * st * SR), doum)
        for s in [4, 10, 12, 14] + ([13, 15] if bar % 2 else []):
            n = int(0.05 * SR)
            tt = np.arange(n) / SR
            tek = (rng.uniform(-1, 1, n) * 0.5 + np.sin(2 * np.pi * 820 * tt) * 0.5) * np.exp(-tt * 70) * 0.3
            add(drums, int((base + s) * st * SR), tek)
        # 탬버린 찰랑찰랑 (16분음표, 약하게)
        for s in range(16):
            add(drums, int((base + s) * st * SR), hat(0.035 if s % 2 else 0.05, 0.04))

    finish(drone + bass + lead + drums, 0.09, path)


if __name__ == '__main__':
    {'forest': forest, 'desert': desert}[sys.argv[1]](sys.argv[2])
