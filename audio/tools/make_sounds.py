"""Собирает звуки audio/Sounds из бесплатных CC0-ассетов OpenGameArt (см. audio/Sounds/LICENSE.txt).

    python3 audio/tools/make_sounds.py <папка с распакованными архивами>

В папке должны лежать (как в LICENSE.txt):
  independent_nu_ljudbank-hits_and_punches/   (архив .7z)
  swishes/                                     (swishes.zip)
  corsica_s-walking_in_snow/                   (архив .7z)
  water-splash-slime-sfx/                      (water-splash-slime-sfx.zip)
  RPGsounds_Kenney/                            (RPGsounds_Kenney.zip)
  fire.wav  fire-1_0.ogg  death_pain_grunts.wav  explosion_dull.flac  battleThemeA.mp3
  boss_battle_#2_metal_loop.wav (boss_battle_#2_metal_pack.zip)  cold_silence.ogg  Heavenly_Loop_0.ogg
  hells_godedit_1.ogg  dark_ambient_drone_loop.flac

    python3 audio/tools/make_sounds.py <папка> --only=music   — пересобрать только музыку

Что делает: моно 44,1 кГц, обрезает тишину по краям, гасит щелчки на краях, выравнивает громкость,
режет запись стонов на отдельные звуки, делает из треска костра бесшовную петлю.
Файлы называются <банк>_NN.ogg: банк — это имя звука в коде (CalabashSfx.Play("punch_light")).
Нужны numpy и soundfile.
"""
import glob
import os
import shutil
import sys

import numpy as np
import soundfile as sf

ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
SRC = ARGS[0] if ARGS else "."
# --only=music — пересобрать только банки с этими префиксами (остальные файлы не трогать)
ONLY = next((a.split("=", 1)[1].split(",") for a in sys.argv[1:] if a.startswith("--only=")), None)


def want(name):
    return ONLY is None or any(name.startswith(o) for o in ONLY)
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Sounds")
SR = 44100


def load(path):
    d, sr = sf.read(path, always_2d=True, dtype="float32")
    m = d.mean(axis=1)
    if sr != SR:
        n = int(len(m) * SR / sr)
        m = np.interp(np.linspace(0, len(m) - 1, n), np.arange(len(m)), m).astype(np.float32)
    return m


def trim(m, thr=0.02, pad=0.01, max_len=None):
    a = np.abs(m)
    idx = np.where(a > thr * a.max())[0]
    if len(idx) == 0:
        return m
    s = max(0, idx[0] - int(pad * SR))
    e = min(len(m), idx[-1] + int(pad * 4 * SR))
    m = m[s:e]
    if max_len:
        m = m[:int(max_len * SR)]
    return m


def fades(m, fin=0.003, fout=0.03):
    m = m.copy()
    a, b = int(fin * SR), min(len(m) // 2, int(fout * SR))
    if a:
        m[:a] *= np.linspace(0, 1, a)
    if b:
        m[-b:] *= np.linspace(1, 0, b) ** 2
    return m


def norm(m, peak=0.9, rms=None):
    if rms:
        r = np.sqrt(np.mean(m ** 2)) + 1e-9
        m = m * (rms / r)
    p = np.abs(m).max() + 1e-9
    if p > peak or not rms:
        m = m * (peak / p)
    return m


def save(m, bank, i):
    path = os.path.join(OUT, f"{bank}_{i:02d}.ogg")
    sf.write(path, np.clip(m, -1, 1), SR, format="OGG", subtype="VORBIS")
    return os.path.getsize(path)


def bank(name, files, peak=0.9, rms=None, max_len=None, thr=0.02, gain=1.0):
    if not want(name):
        return
    total = 0
    for i, f in enumerate(files, 1):
        m = fades(trim(load(f), thr=thr, max_len=max_len))
        total += save(norm(m, peak, rms) * gain, name, i)
    print(f"{name}: {len(files)} файлов, {total // 1024} KB")


def P(*parts):
    return os.path.join(SRC, *parts)


def split_by_silence(m, thr=0.04, gap=0.18, min_len=0.15):
    """Режет длинную запись на куски по паузам."""
    win = int(0.01 * SR)
    env = np.sqrt(np.convolve(m ** 2, np.ones(win) / win, mode="same"))
    on = env > thr * env.max()
    out, start, quiet = [], None, 0
    for i in range(0, len(m), win):
        if on[i]:
            if start is None:
                start = i
            quiet = 0
        elif start is not None:
            quiet += win
            if quiet > gap * SR:
                end = i - quiet + win
                if end - start > min_len * SR:
                    out.append(m[max(0, start - win):end + 4 * win])
                start, quiet = None, 0
    return out


def loop(m, seconds, xfade=1.0):
    """Бесшовная петля: хвост плавно переходит в начало."""
    n, x = int(seconds * SR), int(xfade * SR)
    m = m[:n + x]
    t = np.linspace(0, 1, x)
    head = m[:x] * np.sqrt(t) + m[n:n + x] * np.sqrt(1 - t)
    return np.concatenate([head, m[x:n]])


os.makedirs(OUT, exist_ok=True)
for f in glob.glob(os.path.join(OUT, "*.ogg")):
    if want(os.path.basename(f)):
        os.remove(f)

HITS = P("independent_nu_ljudbank-hits_and_punches", "hits", "hit{:02d}.mp3.flac")
bank("punch_light", [HITS.format(i) for i in (1, 3, 7, 9, 10, 12, 13, 17)], rms=0.12, max_len=0.5)
bank("punch_heavy", [HITS.format(i) for i in (19, 20, 21, 22, 24, 33, 35, 37)], rms=0.2, max_len=0.8)
bank("hurt_hit", [HITS.format(i) for i in (26, 28, 29, 30, 31, 32)], rms=0.15, max_len=0.6)
bank("swing", [P("swishes", "swishes", f"swish-{i}.wav") for i in (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)], peak=0.8)
bank("footstep_snow", [P("corsica_s-walking_in_snow", "Corsica_S-Walking_in_Snow",
                         f"Corsica_S-Walking_on_snow_covered_gravel_{i:02d}.flac") for i in range(5, 15)],
     rms=0.08, max_len=0.35, thr=0.1)
bank("land", [P("corsica_s-walking_in_snow", "Corsica_S-Walking_in_Snow",
                f"Corsica_S-Walking_on_snow_covered_gravel_and_ice_{i:02d}.flac") for i in (2, 6, 10, 14)],
     rms=0.14, max_len=0.4, thr=0.1)
bank("splash", [P("water-splash-slime-sfx", f"splash_{i:02d}.ogg") for i in (2, 4, 6, 8, 10, 13)], rms=0.12, max_len=0.9)
bank("explosion", [P("explosion_dull.flac")], peak=0.95)
bank("fire_burst", [P("fire-1_0.ogg")], rms=0.12, max_len=1.6)
K = lambda n: P("RPGsounds_Kenney", "OGG", n + ".ogg")
bank("metal_clank", [K("metalPot1"), K("metalPot2"), K("metalPot3")], rms=0.12, max_len=0.7)
bank("blade", [K("knifeSlice"), K("knifeSlice2")], rms=0.1)
bank("wood_hit", [K("chop")], rms=0.15)
bank("bow_release", [K("metalLatch"), K("drawKnife2")], rms=0.08)
bank("cloth", [K("cloth1"), K("cloth2"), K("cloth3"), K("cloth4")], rms=0.06)

# стоны: одна длинная запись → отдельные звуки; длинные — смерть, короткие — боль
parts = [] if not want("grunt") else split_by_silence(load(P("death_pain_grunts.wav")))
parts = [fades(p) for p in parts if 0.15 * SR < len(p) < 2.5 * SR]
parts.sort(key=len)
short, long_ = parts[:len(parts) * 2 // 3], parts[len(parts) * 2 // 3:]
for i, p in enumerate(short[:12], 1):
    save(norm(p, rms=0.1), "grunt", i)
for i, p in enumerate(long_[-8:], 1):
    save(norm(p, rms=0.1), "death", i)
if parts:
    print(f"grunt: {min(12, len(short))}, death: {min(8, len(long_))} (из {len(parts)} кусков)")

# треск огня: петля 8 с из длинной записи камина
if want("fire_loop"):
    fire = load(P("fire.wav"))
    fire = fire[int(4 * SR):]
    save(norm(fades(loop(fire, 8.0), 0, 0), rms=0.08), "fire_loop", 1)
    print("fire_loop: 8 с")

def music(src, name, seconds=None, gain=0.8, xfade=2.0):
    """Музыка: стерео как есть, перекодирована в ogg блоками (libsndfile падает на длинной записи разом).
    seconds — взять столько секунд и склеить в петлю."""
    if not want(name):
        return
    out = os.path.join(OUT, f"{name}_01.ogg")
    f = sf.SoundFile(P(src))
    if seconds:
        n, x = int(seconds * f.samplerate), int(xfade * f.samplerate)
        d = f.read(n + x, dtype="float32", always_2d=True)
        t = np.linspace(0, 1, x)[:, None]
        d = np.concatenate([d[:x] * np.sqrt(t) + d[n:n + x] * np.sqrt(1 - t), d[x:n]])
        with sf.SoundFile(out, "w", f.samplerate, d.shape[1], format="OGG", subtype="VORBIS") as dst:
            for k in range(0, len(d), 4096):
                dst.write(d[k:k + 4096] * gain)
    else:
        with sf.SoundFile(out, "w", f.samplerate, f.channels, format="OGG", subtype="VORBIS") as dst:
            for block in f.blocks(blocksize=4096, dtype="float32", always_2d=True):
                dst.write(block * gain)
    print(f"{name}: {os.path.getsize(out) // 1024} KB")


music("battleThemeA.mp3", "music_battle")
music("boss_battle_#2_metal_loop.wav", "music_boss")
music("cold_silence.ogg", "music_limbo", seconds=80)
music("Heavenly_Loop_0.ogg", "music_heaven")
music("hells_godedit_1.ogg", "music_hell")
music("dark_ambient_drone_loop.flac", "music_menu", seconds=60)
