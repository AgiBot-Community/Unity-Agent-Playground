"""Reusable async HTTP/SSE connection for Ark chat completions."""
import json
import os
import time
from typing import AsyncIterator, Dict, Any, List, Optional, Set, Tuple

import httpx

# 火山方舟 API 端点 - 可通过环境变量覆盖
ARK_API_URL = 'https://ark.cn-beijing.volces.com/api/v3/chat/completions'

# HTTP 连接参数
HTTP_TIMEOUT_SECONDS = 60
HTTP_CONNECT_TIMEOUT_SECONDS = 10
HTTP_MAX_CONNECTIONS = 2
HTTP_KEEPALIVE_EXPIRY_SECONDS = 120
HTTP_WARM_INTERVAL_SECONDS = 60  # 连接预热的最小间隔


class LlmClient:
    def __init__(self, endpoint: Optional[str] = None):
        self.endpoint = endpoint or os.environ.get("ARK_API_URL", ARK_API_URL)
        self.http = httpx.AsyncClient(
            timeout=httpx.Timeout(HTTP_TIMEOUT_SECONDS, connect=HTTP_CONNECT_TIMEOUT_SECONDS),
            limits=httpx.Limits(max_connections=HTTP_MAX_CONNECTIONS,
                               max_keepalive_connections=HTTP_MAX_CONNECTIONS,
                               keepalive_expiry=HTTP_KEEPALIVE_EXPIRY_SECONDS))
        self.unsupported_thinking: Set[str] = set()
        self.last_used = 0.0

    async def warm(self, api_key: str) -> None:
        if time.perf_counter() - self.last_used < HTTP_WARM_INTERVAL_SECONDS:
            return
        # HEAD 不触发模型推理。该端点返回 404 也能完成 TLS 并复用连接，
        # 已在方舟实测确认；真正的 POST 仍独立检查授权和状态码。
        await self.http.head(self.endpoint, headers={'Authorization': 'Bearer ' + api_key},
                             timeout=httpx.Timeout(3, connect=3))
        self.last_used = time.perf_counter()

    async def close(self) -> None:
        await self.http.aclose()

    async def stream(self, system_prompt: str, history: List[Dict[str, Any]],
                     model: str, api_key: str,
                     tools: Optional[List[Dict[str, Any]]] = None) -> AsyncIterator[Tuple[str, Any]]:
        payload = {'model': model,
                   'messages': [{'role': 'system', 'content': system_prompt}] + list(history),
                   'temperature': 0.7, 'stream': True}
        if tools:
            payload['tools'] = tools
        if model not in self.unsupported_thinking:
            payload['thinking'] = {'type': 'disabled'}
        started = time.perf_counter()
        first = True
        tc_acc = {}
        for attempt in range(2):
            async with self.http.stream('POST', self.endpoint, json=payload,
                                        headers={'Authorization': 'Bearer ' + api_key}) as resp:
                if resp.status_code >= 400:
                    error = (await resp.aread()).decode('utf-8', errors='replace')
                    lower = error.lower()
                    if (attempt == 0 and resp.status_code == 400 and 'thinking' in payload
                            and 'thinking' in lower and any(word in lower for word in
                            ('not support', 'unsupported', 'unknown', 'unrecognized'))):
                        self.unsupported_thinking.add(model)
                        payload.pop('thinking')
                        print('agent     模型不支持 thinking 参数，重试一次并缓存结果')
                        continue
                    raise RuntimeError('LLM HTTP %s: %s' % (resp.status_code, error[:500]))
                print('agent     [LLM HTTP %.0fms]' % ((time.perf_counter() - started) * 1000))
                async for line in resp.aiter_lines():
                    if not line.startswith('data:'):
                        continue
                    data = line[5:].strip()
                    if data == '[DONE]':
                        # Consume the complete HTTP body to return the socket to the pool.
                        continue
                    chunk = json.loads(data)
                    choices = chunk.get('choices') or []
                    if not choices:
                        continue
                    delta = choices[0].get('delta') or {}
                    if first and (delta.get('content') or delta.get('tool_calls')):
                        first = False
                        print('agent     [LLM 首 token %.0fms] %s'
                              % ((time.perf_counter() - started) * 1000,
                                 '文本' if delta.get('content') else '工具调用'))
                    if delta.get('content'):
                        yield 'text', delta['content']
                    for call in delta.get('tool_calls') or []:
                        slot = tc_acc.setdefault(call.get('index', 0),
                                                 {'id': '', 'name': '', 'arguments': ''})
                        if call.get('id'):
                            slot['id'] = call['id']
                        fn = call.get('function') or {}
                        slot['name'] += fn.get('name') or ''
                        slot['arguments'] += fn.get('arguments') or ''
                self.last_used = time.perf_counter()
                break
        if tc_acc:
            yield 'tool_calls', [tc_acc[i] for i in sorted(tc_acc)]
