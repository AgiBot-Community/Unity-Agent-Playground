"""PCM WAV output and bounded diagnostic formatting."""
import wave

def trunc(s, n=160):
    return s if len(s) <= n else s[:n] + "...(%d)" % len(s)


def save_wav(path, pcm, rate=16000):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(bytes(pcm))
