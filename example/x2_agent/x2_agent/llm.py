"""Reusable async HTTP/SSE connection for Ark chat completions."""
import json
import time

import httpx

URL = 'https://ark.cn-beijing.volces.com/api/v3/chat/completions'


class LlmClient:
    def __init__(self):
        self.http = httpx.AsyncClient(
            timeout=httpx.Timeout(60, connect=10),
            limits=httpx.Limits(max_connections=2, max_keepalive_connections=2,
                               keepalive_expiry=120))
        self.unsupported_thinking = set()
        self.last_used = 0.0

    async def warm(self, api_key):
        if time.perf_counter() - self.last_used < 60:
            return
        # HEAD 不触发模型推理。该端点返回 404 也能完成 TLS 并复用连接，
        # 已在方舟实测确认；真正的 POST 仍独立检查授权和状态码。
        await self.http.head(URL, headers={'Authorization': 'Bearer ' + api_key},
                             timeout=httpx.Timeout(3, connect=3))
        self.last_used = time.perf_counter()

    async def close(self):
        await self.http.aclose()

    async def stream(self, system_prompt, history, model, api_key, tools=None):
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
            async with self.http.stream('POST', URL, json=payload,
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
