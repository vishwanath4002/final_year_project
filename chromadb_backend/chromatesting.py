# chromatesting.py - OPTIMIZED WITH GAME CONTEXT
# Key change: generate_npc_reply_fast() now takes a DeceptionIntent and renders
# it in the player's style.  The LLM is NOT responsible for strategy.

import chromadb
import time
import re
import unicodedata
from uuid import uuid4
from datetime import datetime

from langchain_ollama import OllamaEmbeddings, ChatOllama
from game_context import get_response_templates, validate_response

OLLAMA_BASE = "http://127.0.0.1:11434"


# ── ChromaDB setup ────────────────────────────────────────────────────────────

class OllamaWrapper:
    def __init__(self, model_name: str):
        self.embedder = OllamaEmbeddings(model=model_name, base_url=OLLAMA_BASE)
    def __call__(self, input: list[str]):
        return self.embedder.embed(input)
    def name(self):
        return "ollama"

client = chromadb.PersistentClient(path="./chroma")
embed  = OllamaWrapper("snowflake-arctic-embed")

def _safe_get(name, embedding_function):
    names = [c.name for c in client.list_collections()]
    if name in names:
        return client.get_or_create_collection(name=name)
    return client.get_or_create_collection(name=name, embedding_function=embedding_function)

player_messages = _safe_get("player_messages", embed)
game_events     = _safe_get("game_events",     embed)
npc_memory      = _safe_get("npc_memory",      embed)


def strip_non_ascii(text: str) -> str:
    text = unicodedata.normalize('NFKD', text)
    text = re.sub(r'[^ -~]', '', text)
    return re.sub(r'  +', ' ', text).strip()


# ── DB helpers ────────────────────────────────────────────────────────────────

def add_player_message_with_group(text, player_id, round_id, group_id,
                                   location="Unknown", timestamp=None):
    if timestamp is None:
        timestamp = datetime.utcnow().isoformat()
    player_messages.add(
        documents=[text],
        metadatas=[{"player_id": player_id, "round_id": round_id,
                    "group_id": group_id, "location": location,
                    "timestamp": timestamp}],
        ids=[f"msg-{uuid4()}"],
    )

def query_collection(collection, query, k=3, filters=None):
    if filters:
        return collection.query(query_texts=[query], n_results=k, where=filters)
    return collection.query(query_texts=[query], n_results=k)


# ── LLM (memory-friendly) ─────────────────────────────────────────────────────

llm = ChatOllama(
    model="llama3.2:3b",
    temperature=0.8,
    base_url=OLLAMA_BASE,
    num_ctx=256,
    num_predict=30,
    top_p=0.9,
    top_k=20,
)


def generate_npc_reply_fast(
    disguise_name: str,
    style_summary: str,
    conversation: str,
    intent_directive: str,          # DeceptionIntent.to_prompt_fragment()
    strategy_mode: str = "casual",
) -> str:
    """
    Render a DeceptionIntent as a single short message in the player's style.

    The prompt is split into two clear sections:
      1. ROLE + STYLE  — who the NPC is and how they talk
      2. DIRECTIVE     — what they need to say (from the strategy layer)

    The LLM's job is purely stylistic rendering, not strategic reasoning.
    This works reliably even at 3b / num_ctx=256.
    """
    prompt = f"""You are {disguise_name} in Koschei Station — abandoned Soviet post, scavengers everywhere.
Locations: Sheds, Barns, Greenhouse, Church, Pavilion.
Actions: collecting wood/mushrooms, taking cans to church, shooting scavengers. Limited ammo.
NPCs: Dr. Voss (gave the briefing), Dr. Petrov (rescued from lower levels).
Style: {style_summary}
NEVER use: day/night, knives, tunnels, inventory, emojis, the word Koschei. Plain text only.

Recent chat:
{conversation[-180:]}

Task: {intent_directive}
Reply as {disguise_name} in 1 short casual sentence:"""

    try:
        response = llm.invoke(prompt)
        reply = (response.content or "").strip()

        # Strip any role-play preamble ("Player2: ...")
        if ':' in reply and reply.index(':') < 20:
            reply = reply.split(':', 1)[1].strip()

        # Hard-cap at 2 sentences
        sentences = reply.split('. ')
        if len(sentences) > 2:
            reply = '. '.join(sentences[:2])
            if not reply.endswith('.'):
                reply += '.'

        reply = strip_non_ascii(reply)

        # Validate against game rules
        is_valid, error = validate_response(reply)
        if not is_valid:
            print(f"   ⚠️ Invalid response ({error}), using template")
            import random
            reply = random.choice(get_response_templates(strategy_mode))

        return reply

    except Exception as e:
        print(f"   ❌ LLM error: {e}")
        import random
        return strip_non_ascii(random.choice(get_response_templates(strategy_mode)))


# ── Backward compat ───────────────────────────────────────────────────────────

def generate_npc_reply(player_text, round_id="r1",
                        imitate_player_id=None, recent_msgs=None):
    return generate_npc_reply_fast(
        disguise_name=imitate_player_id or "Player",
        style_summary="Casual gamer",
        conversation=player_text,
        intent_directive="Say something casual about the game.",
        strategy_mode="casual",
    )