#!/usr/bin/env python3
# The Replay Monitor clip — com.xpturn.feedback/Samples~/GameFeelLabs/ReplayMonitor/ReplayClip.webm is written here with
# ffmpeg: three two-second chapters of one color each and a white bar that crosses the picture once in six seconds, so the
# bar's place is the play time. Silent VP8, 480x270, 24 fps, a key frame every 6 frames. One thread and bitexact output give
# the same bytes on one machine and ffmpeg; another ffmpeg or libvpx can give others, so --check reads the clip's form with
# ffprobe (codec, size, rate, frames, key frames, no audio, the size budget) and not its bytes.
#   replay_clip.py [--out <Samples~>] [--check]   (--check writes nothing, lists what differs and exits 1)
import argparse, json, os, shutil, subprocess, sys

WIDTH = 480
HEIGHT = 270
FPS = 24
CHAPTER_SECONDS = 2
CHAPTERS = ("0x16324f", "0xc8553d", "0x2a9d8f")
FRAMES = FPS * CHAPTER_SECONDS * len(CHAPTERS)
BAR = 24
KEY_EVERY = 6
MAX_BYTES = 300 * 1024
RELATIVE = os.path.join("GameFeelLabs", "ReplayMonitor", "ReplayClip.webm")
HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLES = os.path.normpath(os.path.join(HERE, "..", "..", "com.xpturn.feedback", "Samples~"))


def need(tool):
    if shutil.which(tool) is None:
        sys.exit(f"{tool} not found")


def write(path):
    need("ffmpeg")
    command = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y"]
    for color in CHAPTERS:
        command += ["-f", "lavfi", "-i", f"color=c={color}:s={WIDTH}x{HEIGHT}:r={FPS}:d={CHAPTER_SECONDS},format=yuv420p"]
    command += ["-f", "lavfi", "-i", f"color=c=white:s={BAR}x{HEIGHT}:r={FPS}:d={FRAMES // FPS},format=yuv420p"]
    n = len(CHAPTERS)
    chain = "".join(f"[{i}]" for i in range(n)) + f"concat=n={n}:v=1[b];[b][{n}]overlay=x='n*{WIDTH - BAR}/{FRAMES - 1}':y=0:format=yuv420[v]"
    command += ["-filter_complex", chain, "-map", "[v]", "-an", "-c:v", "libvpx", "-threads", "1", "-b:v", "350k", "-crf", "20",
                "-g", str(KEY_EVERY), "-pix_fmt", "yuv420p", "-flags", "+bitexact", "-fflags", "+bitexact", "-map_metadata", "-1", path]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    subprocess.run(command, check=True)


def problems(path):
    need("ffprobe")
    if not os.path.isfile(path):
        return [f"no clip at {path}"]
    found = []
    size = os.path.getsize(path)
    if size > MAX_BYTES:
        found.append(f"{size} bytes, the budget is {MAX_BYTES}")
    probe = subprocess.run(["ffprobe", "-v", "error", "-show_streams", "-show_packets", "-of", "json", path], capture_output=True, text=True)
    if probe.returncode != 0:
        return found + ["ffprobe cannot read it: " + probe.stderr.strip()]
    data = json.loads(probe.stdout)
    streams = data.get("streams", [])
    video = [s for s in streams if s.get("codec_type") == "video"]
    if len(video) != 1 or len(streams) != 1:
        found.append(f"{len(streams)} streams, {len(video)} video — one video stream and no audio")
    if not video:
        return found
    v = video[0]
    for key, want in (("codec_name", "vp8"), ("width", WIDTH), ("height", HEIGHT), ("r_frame_rate", f"{FPS}/1")):
        if v.get(key) != want:
            found.append(f"{key} {v.get(key)}, not {want}")
    packets = [p for p in data.get("packets", []) if p.get("stream_index") == v.get("index")]
    if len(packets) != FRAMES:
        found.append(f"{len(packets)} frames, not {FRAMES}")
    keys = [i for i, p in enumerate(packets) if "K" in p.get("flags", "")]
    if keys != list(range(0, len(packets), KEY_EVERY)):
        found.append(f"key frames not every {KEY_EVERY} frames")
    return found


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default=SAMPLES)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    path = os.path.join(args.out, RELATIVE)
    if not args.check:
        write(path)
    found = problems(path)
    for line in found:
        print("  " + line)
    print(f"{RELATIVE}: " + ("differs" if found else f"ok ({os.path.getsize(path)} bytes)"))
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
