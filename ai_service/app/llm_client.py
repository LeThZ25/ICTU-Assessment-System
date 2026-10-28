import os
import re
import json
import logging
import asyncio
from typing import Optional, AsyncGenerator, Dict, List
import httpx

logger = logging.getLogger("ai_service")

# Auto-load .env file if present
def _load_env_file():
    env_path = os.path.join(os.path.dirname(os.path.dirname(__file__)), ".env")
    if os.path.exists(env_path):
        try:
            with open(env_path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line and not line.startswith("#") and "=" in line:
                        k, v = line.split("=", 1)
                        os.environ.setdefault(k.strip(), v.strip())
        except Exception:
            pass

_load_env_file()

# Groq API Configuration (Securely loaded from environment or .env)
GROQ_API_KEY = os.environ.get("GROQ_API_KEY", "")
GROQ_URL = "https://api.groq.com/openai/v1/chat/completions"
GROQ_DEFAULT_MODEL = "qwen/qwen3.8-27b"

# Local Ollama URL
OLLAMA_URL = os.environ.get("OLLAMA_URL", "http://127.0.0.1:11434/api/generate")

async def check_ollama_available() -> bool:
    try:
        async with httpx.AsyncClient(timeout=1.0) as client:
            resp = await client.get("http://127.0.0.1:11434/api/tags")
            return resp.status_code == 200
    except Exception:
        return False

async def query_groq(prompt: str, system_prompt: str = "", max_tokens: int = 350) -> Optional[str]:
    """Query Groq API with token optimization and fast response."""
    if not GROQ_API_KEY:
        return None

    headers = {
        "Authorization": f"Bearer {GROQ_API_KEY}",
        "Content-Type": "application/json"
    }

    messages = []
    if system_prompt:
        messages.append({"role": "system", "content": system_prompt})
    messages.append({"role": "user", "content": prompt})

    payload = {
        "model": GROQ_DEFAULT_MODEL,
        "messages": messages,
        "temperature": 0.4,
        "max_tokens": max_tokens
    }

    try:
        async with httpx.AsyncClient(timeout=15.0) as client:
            resp = await client.post(GROQ_URL, headers=headers, json=payload)
            if resp.status_code == 200:
                data = resp.json()
                choices = data.get("choices", [])
                if choices:
                    return choices[0].get("message", {}).get("content", "").strip()
            else:
                logger.warning(f"Groq API returned status {resp.status_code}: {resp.text}")
    except Exception as e:
        logger.warning(f"Groq API connection error: {e}")
    return None

async def query_llm(prompt: str, system_prompt: str = "", max_tokens: int = 350) -> Optional[str]:
    """Universal LLM query: Priority Groq API -> Priority Local Ollama -> None."""
    # 1. Groq API (Extremely fast, token-efficient, zero GPU needed on client)
    groq_res = await query_groq(prompt, system_prompt, max_tokens)
    if groq_res:
        return groq_res

    # 2. Local Ollama (if active on machine)
    try:
        async with httpx.AsyncClient(timeout=15.0) as client:
            payload = {
                "model": "qwen2.5:1.5b",
                "prompt": f"{system_prompt}\n{prompt}" if system_prompt else prompt,
                "stream": False,
                "options": {"num_predict": max_tokens}
            }
            resp = await client.post(OLLAMA_URL, json=payload)
            if resp.status_code == 200:
                data = resp.json()
                return data.get("response", "").strip()
    except Exception:
        pass

    return None

async def stream_groq(prompt: str, system_prompt: str = "") -> AsyncGenerator[str, None]:
    """Stream Groq response tokens for real-time oral exam feedback."""
    headers = {
        "Authorization": f"Bearer {GROQ_API_KEY}",
        "Content-Type": "application/json"
    }
    messages = []
    if system_prompt:
        messages.append({"role": "system", "content": system_prompt})
    messages.append({"role": "user", "content": prompt})

    payload = {
        "model": GROQ_DEFAULT_MODEL,
        "messages": messages,
        "temperature": 0.5,
        "max_tokens": 250,
        "stream": True
    }

    try:
        async with httpx.AsyncClient(timeout=30.0) as client:
            async with client.stream("POST", GROQ_URL, headers=headers, json=payload) as response:
                async for line in response.aiter_lines():
                    if line.startswith("data: "):
                        data_str = line[6:].strip()
                        if data_str == "[DONE]":
                            break
                        try:
                            chunk = json.loads(data_str)
                            delta = chunk["choices"][0]["delta"].get("content", "")
                            if delta:
                                yield delta
                        except Exception:
                            continue
        return
    except Exception as e:
        logger.warning(f"Groq streaming error: {e}")

    # Fallback simulation
    text = "AI Giám Khảo ICTU đang đồng hành cùng bạn. Hãy tiếp tục trình bày luận điểm."
    for w in text.split(" "):
        await asyncio.sleep(0.04)
        yield w + " "

stream_llm = stream_groq

