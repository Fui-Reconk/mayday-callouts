# -*- coding: utf-8 -*-
# 自制告警音效包生成器：全部为程序合成波形（无版权问题）
# 输出 16bit mono WAV 到 Audio/ 目录
import wave, math, struct, os

SR = 44100
BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'Audio')
os.makedirs(BASE, exist_ok=True)

def write_wav(name, samples):
    path = os.path.join(BASE, name)
    with wave.open(path, 'w') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        frames = b''.join(struct.pack('<h', max(-32767, min(32767, int(s * 32767)))) for s in samples)
        w.writeframes(frames)
    print(name, os.path.getsize(path), 'bytes')

def env(i, n, attack=0.01, release=0.5):
    a = int(SR * attack); r = int(SR * release)
    if i < a: return i / max(1, a)
    if i > n - r: return max(0.0, (n - i) / max(1, r))
    return 1.0

def tone(freq, dur, vol=0.8, kind='sine', decay=None, attack=0.005, release=0.4):
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        if kind == 'saw':
            v = 2.0 * ((t * freq) % 1.0) - 1.0
        elif kind == 'square':
            v = 1.0 if (t * freq) % 1.0 < 0.5 else -1.0
        else:
            v = math.sin(2 * math.pi * freq * t)
        g = env(i, n, attack, release)
        if decay is not None:
            g *= math.exp(-t * decay)
        out.append(v * vol * g)
    return out

def mix(*tracks):
    n = max(len(t) for t in tracks)
    return [sum(t[i] if i < len(t) else 0.0 for t in tracks) / max(1, len(tracks)) for i in range(n)]

def concat(*tracks, gap=0.0):
    out = []
    g = [0.0] * int(SR * gap)
    for t in tracks:
        out.extend(t)
        out.extend(g)
    return out

# 1. 安全带叮咚：两个衰减正弦（高->低），真机风格
high = tone(830, 1.2, 0.55, decay=3.5)
low = tone(590, 1.6, 0.55, decay=2.8)
write_wav('chime_seatbelt.wav', concat(high, low, gap=0.12))

# 2. 通用通知叮咚
write_wav('chime_notify.wav', tone(988, 0.9, 0.5, decay=4.0))

# 3. 主警告：三连快速 beep
beep = tone(880, 0.09, 0.6, 'square', attack=0.002)
write_wav('master_caution.wav', concat(beep, beep, beep, gap=0.07))

# 4. 失速喇叭：连续刺耳锯齿波带 8Hz 颤音（1.5s 循环）
n = int(SR * 1.5); out = []
for i in range(n):
    t = i / SR
    vib = 1.0 + 0.03 * math.sin(2 * math.pi * 8 * t)
    f = 340 * vib
    v = (2.0 * ((t * f) % 1.0) - 1.0) * 0.9
    v += 0.35 * math.sin(2 * math.pi * f * 2 * t)
    out.append(v * 0.8)
write_wav('stall_horn.wav', out)

# 5. 起落架警告：220+277Hz 拍频低鸣（2s 循环）
n = int(SR * 2.0); out = []
for i in range(n):
    t = i / SR
    v = (math.sin(2 * math.pi * 220 * t) + math.sin(2 * math.pi * 277 * t)) * 0.5
    out.append(v * 0.75)
write_wav('gear_horn.wav', out)

# 6. 超速抖杆：20Hz 咔哒串（1.5s 循环）
n = int(SR * 1.5); out = []
for i in range(n):
    t = i / SR
    clack = 1.0 if (t * 20) % 1.0 < 0.3 else 0.0
    out.append(math.sin(2 * math.pi * 90 * t) * clack * 0.85)
write_wav('overspeed.wav', out)

# 7. 火警铃：钟声双敲（2s 循环）
bell1 = mix(tone(1180, 0.8, 0.5, decay=6.0), tone(2360, 0.8, 0.2, decay=8.0), tone(590, 0.8, 0.25, decay=4.0))
bell2 = mix(tone(1180, 1.0, 0.5, decay=5.0), tone(2360, 1.0, 0.2, decay=7.0), tone(590, 1.0, 0.25, decay=3.5))
write_wav('fire_bell.wav', concat(bell1, bell2, gap=0.15))

# 8. 低油量：三短 beep
b = tone(620, 0.12, 0.55, 'square', attack=0.002)
write_wav('low_fuel.wav', concat(b, b, b, gap=0.18))

# 9. 释压喇叭：440+554Hz 双音哨（2s 循环）
n = int(SR * 2.0); out = []
for i in range(n):
    t = i / SR
    out.append((math.sin(2 * math.pi * 440 * t) + math.sin(2 * math.pi * 554 * t)) * 0.4)
write_wav('depress_horn.wav', out)

print('done')
