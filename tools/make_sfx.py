"""Generate the placeholder sound effects into assets/audio/sfx/*.wav (pure Python, no dependencies).

Every sound is synthesized here (noise bursts, sine sweeps, filtered noise, a few echoes), so there is no third-party
licence to track. They are placeholders: real recordings can replace any file later with the same name, or the sound can
be pointed at another file in data/audio/sfx.json. Deterministic: the same script writes the same bytes.

    python tools/make_sfx.py
"""
import math
import random
import struct
import wave
from pathlib import Path

SR = 32000
OUT = Path(__file__).resolve().parent.parent / "assets" / "audio" / "sfx"


# ---- building blocks ---------------------------------------------------------------------------

def noise(n, rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(n)]


def lowpass(x, a):
    y, out = 0.0, []
    for v in x:
        y += a * (v - y)
        out.append(y)
    return out


def highpass(x, a):
    lp = lowpass(x, a)
    return [v - l for v, l in zip(x, lp)]


def env_exp(n, decay_s, attack_s=0.0015):
    out = []
    for i in range(n):
        t = i / SR
        a = min(1.0, t / attack_s) if attack_s > 0 else 1.0
        out.append(a * math.exp(-t / decay_s))
    return out


def mul(x, e):
    return [a * b for a, b in zip(x, e)]


def add(*tracks):
    n = max(len(t) for t in tracks)
    out = [0.0] * n
    for t in tracks:
        for i, v in enumerate(t):
            out[i] += v
    return out


def scale(x, g):
    return [v * g for v in x]


def delay(x, seconds, gain, total):
    pad = int(seconds * SR)
    out = [0.0] * total
    for i, v in enumerate(x):
        if i + pad < total:
            out[i + pad] = v * gain
    return out


def sine_sweep(n, f0, f1, decay_s, rng_phase=0.0):
    out, phase = [], rng_phase
    for i in range(n):
        t = i / n
        f = f0 + (f1 - f0) * (1 - math.exp(-4.0 * t))
        phase += 2 * math.pi * f / SR
        out.append(math.sin(phase) * math.exp(-(i / SR) / decay_s))
    return out


def tone(n, freq, decay_s, attack_s=0.002):
    return [math.sin(2 * math.pi * freq * i / SR) * min(1.0, (i / SR) / attack_s) * math.exp(-(i / SR) / decay_s) for i in range(n)]


def silence(seconds):
    return [0.0] * int(seconds * SR)


def concat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def normalize(x, peak=0.9):
    m = max(abs(v) for v in x) or 1.0
    return [v / m * peak for v in x]


def fade_ends(x, ms=4):
    n = int(ms / 1000 * SR)
    for i in range(min(n, len(x))):
        g = i / n
        x[i] *= g
        x[-1 - i] *= g
    return x


def save(name, x, peak=0.9):
    OUT.mkdir(parents=True, exist_ok=True)
    x = normalize(fade_ends(list(x)), peak)
    with wave.open(str(OUT / f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in x))


# ---- sounds ------------------------------------------------------------------------------------

def gunshot(seed, rifle):
    rng = random.Random(seed)
    total = int(SR * (1.6 if rifle else 0.9))
    crack = mul(highpass(noise(total, rng), 0.35), env_exp(total, 0.012 if rifle else 0.008))
    boom_f0, boom_f1 = (150 + rng.uniform(-10, 10), 48) if rifle else (260 + rng.uniform(-15, 15), 95)
    boom = scale(sine_sweep(total, boom_f0, boom_f1, 0.22 if rifle else 0.1), 1.0)
    body = mul(lowpass(noise(total, rng), 0.09 if rifle else 0.16), env_exp(total, 0.16 if rifle else 0.07))
    dry = add(scale(crack, 1.0), scale(boom, 0.9), scale(body, 1.3))
    tail = add(dry,
               delay(dry, 0.07 + rng.uniform(0, 0.02), 0.28, total),
               delay(lowpass(dry, 0.25), 0.16 + rng.uniform(0, 0.03), 0.22, total),
               delay(lowpass(dry, 0.12), 0.33 + rng.uniform(0, 0.05), 0.14, total))
    return tail


def click(seed, freq=2200, length=0.004):
    rng = random.Random(seed)
    n = int(SR * 0.03)
    burst = mul(noise(n, rng), env_exp(n, length))
    ring = tone(n, freq, 0.006)
    return add(burst, scale(ring, 0.6))


def thud(freq, decay, n_s, seed, lp=0.2, noise_amt=0.6):
    rng = random.Random(seed)
    n = int(SR * n_s)
    return add(scale(sine_sweep(n, freq * 1.6, freq, decay), 1.0), scale(mul(lowpass(noise(n, rng), lp), env_exp(n, decay)), noise_amt))


def whiz(seed):
    rng = random.Random(seed)
    n = int(SR * 0.32)
    out, lo, hi = [], 0.0, 0.0
    for i in range(n):
        t = i / n
        cutoff = 0.5 - 0.3 * t  # downward sweep: the bullet passes
        v = rng.uniform(-1.0, 1.0)
        lo += cutoff * (v - lo)
        hi += (cutoff * 0.25) * (lo - hi)
        out.append((lo - hi) * math.sin(math.pi * t) ** 1.5)
    return out


def impact_flesh(seed):
    return thud(95 + seed * 6, 0.07, 0.22, seed, lp=0.12, noise_amt=0.8)


def impact_concrete(seed):
    rng = random.Random(seed)
    n = int(SR * 0.3)
    crack = mul(highpass(noise(n, rng), 0.4), env_exp(n, 0.012))
    chips = [0.0] * n
    for _ in range(4):
        at = int(rng.uniform(0.01, 0.16) * SR)
        for i in range(int(0.004 * SR)):
            if at + i < n:
                chips[at + i] += rng.uniform(-1, 1) * 0.5 * (1 - i / (0.004 * SR))
    return add(crack, chips, scale(thud(180, 0.04, 0.3, seed, 0.3, 0.2), 0.5))


def impact_dirt(seed):
    rng = random.Random(seed)
    n = int(SR * 0.28)
    return add(mul(lowpass(noise(n, rng), 0.1), env_exp(n, 0.06)), scale(thud(70, 0.05, 0.28, seed, 0.1, 0.2), 0.6))


def impact_metal(seed):
    n = int(SR * 0.5)
    rng = random.Random(seed)
    f = 1700 + seed * 220
    return add(tone(n, f, 0.1), scale(tone(n, f * 1.51, 0.07), 0.6), scale(tone(n, f * 2.37, 0.05), 0.35),
               scale(mul(highpass(noise(n, rng), 0.5), env_exp(n, 0.006)), 0.8))


def impact_armor(seed):
    n = int(SR * 0.3)
    rng = random.Random(seed)
    return add(thud(130, 0.06, 0.3, seed, 0.25, 0.4), scale(tone(n, 2400, 0.02), 0.4), scale(mul(highpass(noise(n, rng), 0.5), env_exp(n, 0.005)), 0.4))


def footstep(seed, hard):
    rng = random.Random(seed)
    n = int(SR * 0.16)
    soft = mul(lowpass(noise(n, rng), 0.07 + 0.03 * (seed % 3)), env_exp(n, 0.035))
    heel = scale(sine_sweep(n, 95, 60, 0.03), 0.7)
    grit = scale(mul(highpass(noise(n, rng), 0.3), env_exp(n, 0.012)), 0.25 if hard else 0.1)
    return add(soft, heel, grit)


def mag_out(seed=1):
    rng = random.Random(seed)
    release = click(seed, 1400)
    slide = mul(lowpass(noise(int(SR * 0.09), rng), 0.3), env_exp(int(SR * 0.09), 0.05))
    return concat(release, silence(0.03), scale(slide, 0.5), silence(0.02), scale(thud(110, 0.03, 0.1, seed, 0.3, 0.3), 0.5))


def mag_in(seed=2):
    return concat(scale(thud(150, 0.04, 0.12, seed, 0.25, 0.4), 1.0), silence(0.015), click(seed, 1800))


def bolt_rack(seed=3):
    rng = random.Random(seed)
    rasp = mul(lowpass(noise(int(SR * 0.14), rng), 0.35), [min(1.0, i / (SR * 0.14) * 3) * (1 - i / (SR * 0.14)) for i in range(int(SR * 0.14))])
    return concat(click(seed, 900), scale(rasp, 0.7), silence(0.05), click(seed + 1, 1600), scale(thud(120, 0.03, 0.08, seed, 0.3, 0.2), 0.6))


def pickup(seed=4):
    rng = random.Random(seed)
    n = int(SR * 0.25)
    out = [0.0] * n
    for k in range(5):
        at = int(rng.uniform(0, 0.15) * SR)
        for i in range(int(0.05 * SR)):
            if at + i < n:
                out[at + i] += rng.uniform(-1, 1) * (1 - i / (0.05 * SR)) * 0.5
    return lowpass(out, 0.4)


def ui_click():
    n = int(SR * 0.05)
    return tone(n, 1300, 0.012)


def ui_open():
    return concat(tone(int(SR * 0.05), 880, 0.015), tone(int(SR * 0.07), 1175, 0.02))


def door_slide():
    rng = random.Random(9)
    n = int(SR * 1.0)
    base = lowpass(noise(n, rng), 0.05)
    wobble = [0.7 + 0.3 * math.sin(2 * math.pi * 9 * i / SR) for i in range(n)]
    shape = [math.sin(math.pi * i / n) ** 0.6 for i in range(n)]
    rumble = [math.sin(2 * math.pi * 55 * i / SR) * 0.5 for i in range(n)]
    return mul(add(base, scale(rumble, 0.4)), [a * b for a, b in zip(wobble, shape)])


def lock_click():
    return concat(click(5, 1100), silence(0.06), click(6, 1700), scale(thud(140, 0.03, 0.1, 5, 0.3, 0.3), 0.5))


def mine_blast():
    rng = random.Random(11)
    n = int(SR * 3.0)
    boom = scale(sine_sweep(n, 90, 28, 0.9), 1.0)
    body = mul(lowpass(noise(n, rng), 0.05), env_exp(n, 0.7))
    crack = mul(highpass(noise(n, rng), 0.3), env_exp(n, 0.03))
    debris = [0.0] * n
    for _ in range(40):
        at = int(rng.uniform(0.15, 1.8) * SR)
        ln = int(rng.uniform(0.004, 0.02) * SR)
        for i in range(ln):
            if at + i < n:
                debris[at + i] += rng.uniform(-1, 1) * (1 - i / ln) * 0.35
    dry = add(boom, scale(body, 1.4), scale(crack, 1.0), debris)
    return add(dry, delay(lowpass(dry, 0.15), 0.25, 0.25, n), delay(lowpass(dry, 0.08), 0.6, 0.15, n))


def flare_fire():
    rng = random.Random(12)
    n = int(SR * 2.2)
    hiss = mul(highpass(noise(n, rng), 0.3), env_exp(n, 1.1, 0.05))
    pop = scale(thud(170, 0.04, 2.2, 12, 0.3, 0.3), 1.2)
    crackle = [0.0] * n
    for _ in range(60):
        at = int(rng.uniform(0.1, 2.0) * SR)
        crackle[at] = rng.uniform(-1, 1) * 0.4
    return add(scale(hiss, 0.6), pop, crackle)


def heli_land():
    rng = random.Random(13)
    n = int(SR * 6.0)
    out = []
    lp = 0.0
    for i in range(n):
        t = i / SR
        chop = 0.55 + 0.45 * math.sin(2 * math.pi * 21 * t) ** 2
        lp += 0.06 * (rng.uniform(-1, 1) - lp)
        fade = min(1.0, t / 1.5) * min(1.0, (6.0 - t) / 1.5)
        out.append((lp * 1.6 + 0.35 * math.sin(2 * math.pi * 62 * t)) * chop * fade)
    return out


def hurt():
    return add(thud(75, 0.1, 0.35, 21, 0.1, 0.9), scale(mul(lowpass(noise(int(SR * 0.35), random.Random(22)), 0.3), env_exp(int(SR * 0.35), 0.05)), 0.6))


def extract_tick():
    return tone(int(SR * 0.08), 760, 0.02)


def extract_done():
    return concat(tone(int(SR * 0.12), 660, 0.06), tone(int(SR * 0.12), 880, 0.06), tone(int(SR * 0.3), 1320, 0.12))


def wind_loop():
    rng = random.Random(31)
    n = int(SR * 10.0)
    fade = int(SR * 1.5)
    raw = lowpass(noise(n + fade, rng), 0.012)
    out = []
    for i, v in enumerate(raw):
        t = i / SR
        out.append(v * (0.6 + 0.4 * math.sin(2 * math.pi * 0.11 * t + 1.0) * math.sin(2 * math.pi * 0.07 * t)))
    # seamless loop: blend the extra tail into the head
    head = out[:n]
    for i in range(fade):
        g = i / fade
        head[i] = head[i] * g + out[n + i] * (1 - g)
    return head


def ear_ring():
    n = int(SR * 4.5)
    out = []
    for i in range(n):
        t = i / SR
        env = math.exp(-t / 1.8)
        out.append((math.sin(2 * math.pi * 4100 * t) * 0.6 + math.sin(2 * math.pi * 3300 * t + 1.0) * 0.4) * env)
    return out


def cough(seed):
    rng = random.Random(seed)
    parts = []
    for k in range(3):
        n = int(SR * (0.09 + 0.03 * k))
        burst = mul(lowpass(noise(n, rng), 0.25 - 0.04 * k), env_exp(n, 0.05))
        voice = scale(mul(sine_sweep(n, 220 - 20 * k, 150, 0.06), env_exp(n, 0.06)), 0.5)
        parts.append(add(burst, voice))
        parts.append(silence(0.07))
    return concat(*parts)


def fire_crackle():
    rng = random.Random(41)
    n = int(SR * 5.0)
    base = scale(lowpass(noise(n, rng), 0.03), 0.5)
    pops = [0.0] * n
    for _ in range(140):
        at = int(rng.uniform(0.0, 4.9) * SR)
        ln = int(rng.uniform(0.002, 0.012) * SR)
        for i in range(ln):
            if at + i < n:
                pops[at + i] += rng.uniform(-1, 1) * (1 - i / ln) * rng.uniform(0.3, 1.0)
    return add(base, pops)


def stun_bang():
    rng = random.Random(51)
    n = int(SR * 1.6)
    crack = mul(highpass(noise(n, rng), 0.5), env_exp(n, 0.02))
    boom = scale(sine_sweep(n, 180, 70, 0.12), 0.8)
    ring = scale(tone(n, 3800, 0.5), 0.35)
    dry = add(crack, boom, ring)
    return add(dry, delay(lowpass(dry, 0.2), 0.12, 0.3, n))


def main():
    for i in range(1, 4):
        save(f"shot_rifle_{i}", gunshot(100 + i, True))
        save(f"shot_pistol_{i}", gunshot(200 + i, False))
        save(f"impact_flesh_{i}", impact_flesh(i))
        save(f"impact_concrete_{i}", impact_concrete(i))
    for i in range(1, 3):
        save(f"impact_dirt_{i}", impact_dirt(i))
        save(f"impact_metal_{i}", impact_metal(i))
        save(f"impact_armor_{i}", impact_armor(i))
        save(f"whiz_{i}", whiz(300 + i))
    for i in range(1, 5):
        save(f"footstep_{i}", footstep(400 + i, hard=i % 2 == 0), peak=0.7)
    save("dry_fire", click(1, 2600, 0.003))
    save("mag_out", mag_out())
    save("mag_in", mag_in())
    save("bolt_rack", bolt_rack())
    save("mag_check", click(7, 1500))
    save("pickup", pickup(), peak=0.6)
    save("ui_click", ui_click(), peak=0.5)
    save("ui_open", ui_open(), peak=0.5)
    save("door_slide", door_slide(), peak=0.7)
    save("lock_click", lock_click())
    save("mine_blast", mine_blast(), peak=0.95)
    save("flare_fire", flare_fire())
    save("heli_land", heli_land(), peak=0.8)
    save("hurt", hurt())
    save("extract_tick", extract_tick(), peak=0.5)
    save("extract_done", extract_done(), peak=0.6)
    save("wind_loop", wind_loop(), peak=0.6)
    save("ear_ring", ear_ring(), peak=0.5)
    save("cough_1", cough(61), peak=0.7)
    save("cough_2", cough(62), peak=0.7)
    save("fire_crackle", fire_crackle(), peak=0.7)
    save("stun_bang", stun_bang(), peak=0.95)
    print("wrote", len(list(OUT.glob("*.wav"))), "files to", OUT)


if __name__ == "__main__":
    main()
