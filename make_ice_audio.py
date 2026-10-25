"""3스테이지 얼음 동굴 사운드 (make_stage_bgm.py와 같은 칩튠 도구 방식, numpy만 사용)

BGM
  icecave : 92 BPM, E 단조, 16마디(약 42초) 루프. 맑은 종소리 아르페지오 + 은은한 패드 + 반짝이는 고음 + 느린 심장박동 드럼
  escape  : 152 BPM, D 단조, 16마디(약 25초) 루프. 16분음표 베이스 + 급한 스타카토 화음 + 날카로운 리드 + 팀파니·크래시
효과음
  freeze  : 얼어붙는 소리 (차가운 바람 + 바삭바삭 결정이 자라는 소리 + 반짝임)
  crack   : 얼음에 금 가는 소리 (짧고 날카로움)
  shatter : 얼음이 산산조각 나는 소리
  rumble  : 동굴이 무너지는 낮은 땅울림
  icicle  : 고드름이 떨어져 부서지는 소리
  thud    : 얼음 덩어리가 바닥에 쿵 떨어지는 소리
Usage: python make_ice_audio.py <Assets/Audio 폴더>
"""
import os
import sys
import wave
import numpy as np

SR = 44100
rng = np.random.default_rng(23)


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
    if start >= len(buf):
        return
    end = min(len(buf), start + len(sig))
    buf[start:end] += sig[: end - start]


def pulse(f, n, duty):
    phase = (np.arange(n) / SR * f) % 1.0
    return np.where(phase < duty, 1.0, -1.0)


def triangle(f, n):
    phase = (np.arange(n) / SR * f) % 1.0
    return 4 * np.abs(phase - 0.5) - 1


def saw(f, n):
    phase = (np.arange(n) / SR * f) % 1.0
    return 2 * phase - 1


def bell(f, n):
    # 맑은 종/첼레스타: 사인 + 비정수배 배음, 높은 배음일수록 빨리 사라짐
    t = np.arange(n) / SR
    return (np.sin(2 * np.pi * f * t) * np.exp(-t * 2.5)
            + 0.45 * np.sin(2 * np.pi * f * 2.76 * t) * np.exp(-t * 6)
            + 0.25 * np.sin(2 * np.pi * f * 5.4 * t) * np.exp(-t * 11)
            + 0.12 * np.sin(2 * np.pi * f * 8.9 * t) * np.exp(-t * 18))


def lowpass(x, k):
    # 간단한 한 극 저역 통과 (k: 0~1, 작을수록 먹먹함)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += k * (x[i] - acc)
        y[i] = acc
    return y


def noise(n):
    return rng.uniform(-1, 1, n)


def kick(vol, low=45, punch=90):
    n = int(0.16 * SR)
    t = np.arange(n) / SR
    f = low + punch * np.exp(-t * 30)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 14) * vol


def hat(vol, length=0.03):
    n = int(length * SR)
    return np.diff(rng.uniform(-1, 1, n + 1)) * np.exp(-np.arange(n) / SR * 110) * vol


def snare(vol):
    n = int(0.18 * SR)
    t = np.arange(n) / SR
    body = np.sin(2 * np.pi * 190 * t) * np.exp(-t * 30)
    return (body * 0.5 + noise(n) * np.exp(-t * 22) * 0.7) * vol


def tom(vol, f0):
    n = int(0.3 * SR)
    t = np.arange(n) / SR
    f = f0 * (1 + 0.6 * np.exp(-t * 18))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 9) * vol


def crash(vol, length=1.4):
    n = int(length * SR)
    t = np.arange(n) / SR
    return np.diff(noise(n + 1)) * np.exp(-t * 3.2) * vol


def write(path, mix, rms=None, peak=0.95):
    mix = np.tanh(mix * 1.05)
    if rms is not None:
        mix *= rms / max(1e-9, np.sqrt(np.mean(mix ** 2)))
    else:
        mix *= peak / max(1e-9, np.max(np.abs(mix)))
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


# ───────────────────────── 얼음 동굴: 신비롭고 차가운 ─────────────────────────
def icecave(path):
    bpm = 92
    st = 60.0 / bpm / 4          # 16분음표 길이
    bars = 16
    total = int(round(bars * 16 * st * SR))
    # Em - Cmaj7 - Am9 - B7sus 반복 (뒤 8마디는 G - D/F# 로 살짝 밝게)
    prog = [
        (40, [64, 67, 71, 74]), (36, [64, 67, 71, 76]), (45, [64, 67, 71, 72]), (47, [66, 69, 71, 76]),
        (40, [64, 67, 71, 74]), (36, [64, 67, 71, 76]), (45, [64, 67, 72, 76]), (47, [63, 66, 71, 75]),
    ] * 2
    prog[8 + 1] = (43, [62, 67, 71, 74])   # G
    prog[8 + 5] = (42, [62, 66, 69, 74])   # D/F#
    N = None
    # 종소리 멜로디 (8분음표 칸, '-' 유지)
    melody = [
        [79, '-', 78, 76, 71, '-', '-', N],
        [76, '-', 79, '-', 83, '-', '-', N],
        [84, '-', 83, 79, 76, '-', 72, '-'],
        [78, '-', '-', '-', 75, '-', 71, '-'],
        [79, '-', 78, 76, 83, '-', '-', 81],
        [79, '-', 76, '-', 72, '-', 76, '-'],
        [81, '-', 79, 76, 72, '-', 76, 79],
        [78, '-', '-', '-', N, N, N, N],
    ]
    pad = np.zeros(total)
    arp = np.zeros(total)
    lead = np.zeros(total)
    sparkle = np.zeros(total)
    bass = np.zeros(total)
    drums = np.zeros(total)
    bar_n = int(16 * st * SR)

    for bar in range(bars):
        base = bar * 16
        root, tones = prog[bar]
        start = int(base * st * SR)
        # 패드: 살짝 어긋난 톱니파 두 개를 먹먹하게 → 얼음 동굴 속 울림
        n = int(bar_n * 1.08)
        t = np.arange(n) / SR
        p = np.zeros(n)
        for m in tones[:3]:
            p += saw(freq(m - 12) * 1.003, n) + saw(freq(m - 12) * 0.997, n)
        swell = np.clip(t / 0.6, 0, 1) * np.clip((n / SR - t) / 0.5, 0, 1)
        add(pad, start, p * swell * 0.05)
        # 베이스: 마디 첫 박에 길게 + 3박에 짧게 (삼각파)
        n = int(10 * st * SR)
        add(bass, start, triangle(freq(root), n) * envelope(n, attack=0.02, decay=0.4, sustain=0.5, release=0.2) * 0.55)
        n = int(4 * st * SR)
        add(bass, int((base + 10) * st * SR), triangle(freq(root + 12), n) * envelope(n, decay=0.2, sustain=0.3) * 0.35)
        # 아르페지오: 16분음표 종소리, 위아래로 굴러감
        order = [0, 1, 2, 3, 2, 1, 2, 3] * 2
        for s in range(16):
            n = int(st * SR * 4)
            m = tones[order[s]] + (12 if s % 8 == 3 else 0)
            add(arp, int((base + s) * st * SR), bell(freq(m), n) * (0.10 if s % 4 == 0 else 0.065))
        # 멜로디: 앞 8마디는 비우고 종소리 아르페지오만, 뒤 8마디에 종소리 멜로디
        if bar >= 8:
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
                n = int(length * 2 * st * SR * 2.2)
                add(lead, int((base + i * 2) * st * SR), bell(freq(m + 12), n) * 0.20)
                i += length
        # 반짝임: 아주 높은 음이 드문드문 (고드름에 빛이 튀는 느낌)
        for k in range(3):
            s = int(rng.integers(0, 16))
            n = int(0.6 * SR)
            m = tones[int(rng.integers(0, 4))] + 24
            add(sparkle, int((base + s) * st * SR), bell(freq(m), n) * 0.05)
        # 드럼: 느린 심장박동 킥(쿵-쿵) + 아주 약한 하이햇
        add(drums, start, kick(0.45, 40, 60))
        add(drums, int((base + 3) * st * SR), kick(0.25, 40, 50))
        if bar >= 4:
            for s in range(2, 16, 4):
                add(drums, int((base + s) * st * SR), hat(0.03, 0.05))

    # 동굴 메아리: 짧게 두 번 되울림
    mix = pad + arp + lead + sparkle + bass + drums
    # np.roll로 끝에서 넘친 메아리가 처음으로 돌아와 붙음 → 반복 재생할 때 이음새가 안 들림
    echo = np.zeros_like(mix)
    for delay, g in [(0.27, 0.32), (0.54, 0.15)]:
        echo += np.roll(mix, int(delay * SR)) * g
    write(path, mix + echo, rms=0.085)


# ───────────────────────── 탈출: 급박하게 ─────────────────────────
def escape(path):
    bpm = 152
    st = 60.0 / bpm / 4
    bars = 16
    total = int(round(bars * 16 * st * SR))
    # Dm - Bb - C - A (뒤 8마디는 Dm - Bb - Gm - A)
    prog = [(38, [62, 65, 69]), (34, [62, 65, 70]), (36, [64, 67, 72]), (33, [61, 64, 69])] * 2 \
         + [(38, [62, 65, 69]), (34, [62, 65, 70]), (31, [62, 67, 70]), (33, [61, 64, 69])] * 2
    N = None
    melody = [
        [74, '-', 72, 74, 77, '-', 76, 74],
        [74, '-', 70, '-', 69, '-', 70, 72],
        [72, '-', 70, 72, 76, '-', 74, 72],
        [73, '-', '-', '-', 76, '-', 73, '-'],
        [81, '-', 79, 77, 76, '-', 77, 79],
        [77, '-', 74, '-', 70, '-', 74, 77],
        [79, '-', 77, 74, 70, '-', 74, 79],
        [81, '-', 79, '-', 76, '-', 73, '-'],
    ]
    bass = np.zeros(total)
    stab = np.zeros(total)
    lead = np.zeros(total)
    drums = np.zeros(total)

    for bar in range(bars):
        base = bar * 16
        root, tones = prog[bar]
        # 베이스: 16분음표로 쉬지 않고 (근음, 옥타브 섞어서) → 쫓기는 느낌
        for s in range(16):
            n = int(st * SR * 0.9)
            m = root + (12 if s % 4 == 2 else 0)
            add(bass, int((base + s) * st * SR), pulse(freq(m), n, 0.5) * envelope(n, decay=0.04, sustain=0.5) * 0.22)
        # 스타카토 화음: 엇박으로 짧게 끊어서
        for s in [0, 3, 6, 8, 11, 14]:
            n = int(st * SR * 0.8)
            for m in tones:
                add(stab, int((base + s) * st * SR), pulse(freq(m), n, 0.25) * envelope(n, decay=0.03, sustain=0.2) * 0.07)
        # 리드: 날카로운 펄스파 (뒤 8마디는 한 옥타브 위로 겹침)
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
            n = int(length * 2 * st * SR * 0.92)
            sig = pulse(freq(m), n, 0.125) * envelope(n, attack=0.004, decay=0.08, sustain=0.6, release=0.03)
            if bar >= 8:
                sig = sig + pulse(freq(m + 12), n, 0.25) * envelope(n, decay=0.08, sustain=0.5) * 0.35
            add(lead, int((base + i * 2) * st * SR), sig * 0.13)
            i += length
        # 드럼: 킥 4분 + 스네어 2·4박 + 16분 하이햇, 4마디마다 탐탐 필인 + 크래시
        for s in [0, 4, 8, 12]:
            add(drums, int((base + s) * st * SR), kick(0.75))
        for s in [4, 12]:
            add(drums, int((base + s) * st * SR), snare(0.5))
        for s in range(16):
            add(drums, int((base + s) * st * SR), hat(0.05 if s % 2 else 0.08))
        if bar % 4 == 3:
            for s, f0 in [(12, 180), (13, 150), (14, 120), (15, 95)]:
                add(drums, int((base + s) * st * SR), tom(0.5, f0))
        if bar % 4 == 0:
            add(drums, int(base * st * SR), crash(0.25))

    write(path, bass + stab + lead + drums, rms=0.095)


# ───────────────────────── 효과음 ─────────────────────────
def sfx_freeze(path):
    n = int(1.1 * SR)
    t = np.arange(n) / SR
    wind = lowpass(noise(n), 0.08) * np.clip(t / 0.15, 0, 1) * np.exp(-t * 2.2) * 2.5
    out = wind
    # 결정이 자라는 바삭바삭: 짧은 딸깍이 점점 촘촘하게
    k = 0.0
    while k < 0.8:
        s = int(k * SR)
        m = int(0.012 * SR)
        add(out, s, np.diff(noise(m + 1)) * np.exp(-np.arange(m) / SR * 300) * rng.uniform(0.3, 0.7))
        k += 0.06 * (1 - k) + 0.008
    # 반짝임: 올라가는 종소리 3개
    for i, m in enumerate([88, 93, 98]):
        add(out, int((0.35 + i * 0.12) * SR), bell(freq(m), int(0.6 * SR)) * 0.35)
    write(path, out)


def sfx_crack(path):
    n = int(0.35 * SR)
    t = np.arange(n) / SR
    snap = np.diff(noise(n + 1)) * np.exp(-t * 60)
    ring = np.sin(2 * np.pi * 2300 * t) * np.exp(-t * 25) * 0.3 + np.sin(2 * np.pi * 3700 * t) * np.exp(-t * 40) * 0.2
    body = np.sin(2 * np.pi * np.cumsum(400 * np.exp(-t * 20) + 120) / SR) * np.exp(-t * 18) * 0.5
    write(path, snap + ring + body)


def sfx_shatter(path):
    n = int(1.2 * SR)
    t = np.arange(n) / SR
    out = np.diff(noise(n + 1)) * np.exp(-t * 9) * 0.9
    out += np.sin(2 * np.pi * np.cumsum(300 * np.exp(-t * 30) + 80) / SR) * np.exp(-t * 14) * 0.6
    # 유리 조각 튀는 소리: 무작위 높은 종소리 여러 개
    for _ in range(18):
        s = int(rng.uniform(0.0, 0.7) * SR)
        m = int(rng.uniform(84, 104))
        add(out, s, bell(freq(m), int(0.35 * SR)) * rng.uniform(0.08, 0.22))
    write(path, out)


def sfx_rumble(path):
    n = int(2.8 * SR)
    t = np.arange(n) / SR
    low = lowpass(noise(n), 0.006) * 9
    low *= np.clip(t / 0.3, 0, 1) * np.clip((n / SR - t) / 0.8, 0, 1)
    wobble = 1 + 0.5 * np.sin(2 * np.pi * 7 * t) * rng.uniform(0.6, 1)
    out = low * wobble + np.sin(2 * np.pi * 38 * t) * 0.35 * np.clip(t / 0.3, 0, 1) * np.clip((n / SR - t) / 0.8, 0, 1)
    # 중간중간 돌 떨어지는 소리
    for _ in range(9):
        s = int(rng.uniform(0.2, 2.2) * SR)
        m = int(0.12 * SR)
        tt = np.arange(m) / SR
        add(out, s, (np.diff(noise(m + 1)) * 0.5 + np.sin(2 * np.pi * 140 * tt)) * np.exp(-tt * 30) * rng.uniform(0.2, 0.45))
    write(path, out)


def sfx_icicle(path):
    n = int(0.9 * SR)
    t = np.arange(n) / SR
    # 휘익 떨어지는 소리 (높은 음이 내려감) → 0.25초에 부딪혀 산산조각
    fall_n = int(0.25 * SR)
    ft = np.arange(fall_n) / SR
    whistle = np.sin(2 * np.pi * np.cumsum(2400 - 3000 * ft) / SR) * np.clip(ft / 0.05, 0, 1) * 0.25
    whistle += lowpass(noise(fall_n), 0.3) * np.clip(ft / 0.25, 0, 1) * 0.4
    out = np.zeros(n)
    add(out, 0, whistle)
    hit = int(0.25 * SR)
    m = n - hit
    tt = np.arange(m) / SR
    add(out, hit, np.diff(noise(m + 1)) * np.exp(-tt * 14) * 0.8)
    add(out, hit, np.sin(2 * np.pi * np.cumsum(260 * np.exp(-tt * 25) + 70) / SR) * np.exp(-tt * 16) * 0.7)
    for _ in range(10):
        s = hit + int(rng.uniform(0, 0.4) * SR)
        add(out, s, bell(freq(int(rng.uniform(86, 102))), int(0.3 * SR)) * rng.uniform(0.06, 0.18))
    write(path, out)


def sfx_thud(path):
    n = int(1.4 * SR)
    t = np.arange(n) / SR
    boom = np.sin(2 * np.pi * np.cumsum(30 + 110 * np.exp(-t * 12)) / SR) * np.exp(-t * 4.5)
    dust = lowpass(noise(n), 0.03) * np.exp(-t * 3) * 3
    out = boom + dust
    for _ in range(14):
        s = int(rng.uniform(0.0, 0.5) * SR)
        add(out, s, bell(freq(int(rng.uniform(80, 100))), int(0.3 * SR)) * rng.uniform(0.05, 0.15))
    write(path, out)


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join('Assets', 'Audio')
    icecave(os.path.join(out, 'IceCaveBGM.wav'))
    escape(os.path.join(out, 'IceEscapeBGM.wav'))
    sfx_freeze(os.path.join(out, 'IceFreeze.wav'))
    sfx_crack(os.path.join(out, 'IceCrack.wav'))
    sfx_shatter(os.path.join(out, 'IceShatter.wav'))
    sfx_rumble(os.path.join(out, 'CaveRumble.wav'))
    sfx_icicle(os.path.join(out, 'IcicleFall.wav'))
    sfx_thud(os.path.join(out, 'IceThud.wav'))
