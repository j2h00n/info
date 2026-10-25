"""Sand Eagle boss sound effects.

screech: hawk-style "kee-eeer" scream, ~1.3 s (falling pitch, rough edge, breathy noise)
whoosh : air rush when the eagle dives past, ~0.7 s (band-passed noise sweeping up then down)
Usage: python make_eagle_sfx.py screech|whoosh <output.wav>
"""
import sys
import wave
import numpy as np

SR = 44100
rng = np.random.default_rng(5)


def write(path, sig, peak=0.8):
    sig = sig / np.max(np.abs(sig)) * peak
    fade = int(0.004 * SR)
    sig[:fade] *= np.linspace(0, 1, fade)
    sig[-fade:] *= np.linspace(1, 0, fade)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((sig * 32767).astype(np.int16).tobytes())
    print(f"wrote {path}: {len(sig) / SR:.2f}s")


def bandpass(noise, center, width):
    """주파수 영역에서 가우시안 대역 통과 (center/width는 시간마다 바뀌는 대신 구간별로 처리)"""
    spec = np.fft.rfft(noise)
    f = np.fft.rfftfreq(len(noise), 1 / SR)
    spec *= np.exp(-((f - center) / width) ** 2)
    return np.fft.irfft(spec, len(noise))


def screech(path):
    dur = 1.35
    n = int(dur * SR)
    t = np.arange(n) / SR
    # 음높이: 짧게 치솟았다가 길게 떨어짐 (키이이-어어)
    f0 = np.interp(t, [0, 0.08, 0.35, 1.0, dur], [2300, 3100, 2900, 2200, 1700])
    # 목 떨림: 빠른 비브라토 + 불규칙한 흔들림
    jitter = np.cumsum(rng.normal(0, 1, n)) / SR
    jitter = (jitter - np.convolve(jitter, np.ones(800) / 800, 'same')) * 40
    f = f0 * (1 + 0.025 * np.sin(2 * np.pi * 38 * t) + 0.01 * jitter)
    ph = 2 * np.pi * np.cumsum(f) / SR
    # 거친 목소리: 배음 여러 개 + 위상 왜곡
    voice = (np.sin(ph + 0.8 * np.sin(2 * ph)) + 0.55 * np.sin(2 * ph) + 0.3 * np.sin(3 * ph + 0.5) + 0.15 * np.sin(4 * ph))
    # 숨소리 섞인 잡음 (음높이 근처 대역)
    breath = np.zeros(n)
    seg = int(0.05 * SR)
    for s in range(0, n, seg):
        e = min(n, s + seg)
        chunk = rng.uniform(-1, 1, e - s)
        breath[s:e] = bandpass(chunk, f0[s], 900)
    breath /= np.max(np.abs(breath)) + 1e-9
    env = np.interp(t, [0, 0.03, 0.12, 0.9, dur], [0, 1, 0.85, 0.6, 0]) ** 1.2
    # 거칠게 끊기는 느낌 (진폭 떨림)
    rough = 0.8 + 0.2 * np.sign(np.sin(2 * np.pi * 95 * t))
    sig = (voice * 0.75 + breath * 0.35) * env * rough
    sig = np.tanh(sig * 1.8)
    # 메아리 살짝 (넓은 하늘 느낌)
    echo = np.zeros(n + int(0.18 * SR))
    echo[:n] += sig
    echo[int(0.18 * SR):] += sig * 0.25
    write(path, echo, 0.75)


def whoosh(path):
    dur = 0.75
    n = int(dur * SR)
    t = np.arange(n) / SR
    noise = rng.uniform(-1, 1, n)
    out = np.zeros(n)
    seg = int(0.02 * SR)
    # 바람 소리 대역이 다가올 때 올라가고 지나가면 내려감 (도플러 느낌)
    center = np.interp(t, [0, 0.3, dur], [500, 1600, 350])
    for s in range(0, n, seg):
        e = min(n, s + seg * 2)
        chunk = noise[s:e] * np.hanning(e - s)
        out[s:e] += bandpass(chunk, center[s], center[s] * 0.6)
    env = np.exp(-((t - 0.3) / 0.16) ** 2)
    write(path, out * env, 0.7)


if __name__ == '__main__':
    {'screech': screech, 'whoosh': whoosh}[sys.argv[1]](sys.argv[2])
