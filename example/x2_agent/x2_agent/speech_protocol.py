"""Doubao V3 binary request and ASR response framing."""
import gzip
import json
import struct

# V3 二进制协议：4B header + 4B 大端 payload size + payload
#   byte0: version(4b)=1 | header_size(4b)=1            → 0x11
#   byte1: message_type(4b) | flags(4b)
#   byte2: serialization(4b) | compression(4b)          1=JSON, 1=gzip
#   byte3: reserved
MSG_FULL_CLIENT = 0x1     # 请求参数（JSON）
MSG_AUDIO_ONLY = 0x2      # 音频数据
MSG_SERVER_FULL = 0x9     # 服务端 JSON 响应
MSG_SERVER_AUDIO = 0xB    # 服务端音频数据
MSG_ERROR = 0xF           # 错误
FLAG_LAST_PACKET = 0x2    # 最后一包（负包）


def _header(msg_type, flags, serialization, compression):
    return bytes([(1 << 4) | 1, (msg_type << 4) | flags,
                  (serialization << 4) | compression, 0x00])


def build_full_request(obj, use_gzip=True):
    payload = json.dumps(obj, ensure_ascii=False).encode("utf-8")
    if use_gzip:
        payload = gzip.compress(payload)
        head = _header(MSG_FULL_CLIENT, 0, 1, 1)
    else:
        head = _header(MSG_FULL_CLIENT, 0, 1, 0)
    return head + struct.pack(">I", len(payload)) + payload


def build_audio_packet(pcm: bytes, last=False):
    # 音频不压缩；last=True 为负包（size 可为 0）
    head = _header(MSG_AUDIO_ONLY, FLAG_LAST_PACKET if last else 0, 0, 0)
    return head + struct.pack(">I", len(pcm)) + pcm


def parse_server_message(data: bytes):
    """返回 (msg_type, payload)；gzip 自动解压。

    布局（实测确认，2026-09-16 asr_probe 探测）：
      byte0 = version(高4位)=1 | header_size(低4位)=1
      byte1 = message_type(高4位) | flags(低4位)
      byte2 = serialization(高4位) | compression(低4位)
      byte3 = reserved
      flags bit0=1（带 sequence）：data[4:8]=sequence, data[8:12]=payload size,
                                  data[12:12+size]=payload
      flags bit0=0：data[4:8]=payload size, data[8:8+size]=payload
    """
    if len(data) < 8:
        raise ValueError("server message too short: %d" % len(data))
    msg_type = (data[1] >> 4) & 0xF
    flags = data[1] & 0xF
    serialization = (data[2] >> 4) & 0xF
    compression = data[2] & 0xF
    if flags & 0x1:
        if len(data) < 12:
            raise ValueError("server message too short: %d" % len(data))
        (size,) = struct.unpack(">I", data[8:12])
        payload = data[12:12 + size]
    else:
        (size,) = struct.unpack(">I", data[4:8])
        payload = data[8:8 + size]
    if compression == 1 and payload:
        payload = gzip.decompress(payload)
    if serialization == 1 and msg_type in (MSG_SERVER_FULL, MSG_ERROR):
        return msg_type, json.loads(payload.decode("utf-8")) if payload else {}
    return msg_type, payload
