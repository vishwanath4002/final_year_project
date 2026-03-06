# chromatesting.py - OPTIMIZED WITH GAME CONTEXT
# generate_npc_reply_fast: response-first natural conversation,
# strategic intent woven in only where appropriate.

import chromadb
import time
import re
import unicodedata
import random
from uuid import uuid4
from datetime import datetime
from typing import TYPE_CHECKING

from langchain_ollama import OllamaEmbeddings, ChatOllama
from game_context import get_response_templates, validate_response

if TYPE_CHECKING:
    from deception_strategy import DeceptionIntent

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
    num_predict=35,
    top_p=0.9,
    top_k=20,
)


def generate_npc_reply_fast(
    disguise_name: str,
    style_summary: str,
    conversation: str,
    last_message: str,
    speaker_name: str,
    group_members: list,
    intent: "DeceptionIntent",
    strategy_mode: str = "casual",
) -> str:
    """
    Generate a natural reply that:
      1. Responds to what the speaker just said — not a scripted directive
      2. Weaves in strategic intent only where it fits naturally:
         - gather_info / build_trust / casual: respond first, question/confirm
           as a light nudge — and only ask a question ~35% of the time
         - defend_self / seed_doubt / accuse_other: intent is the priority
           but must still sound like a real player talking to someone nearby

    The LLM knows:
      - Who it is (disguise_name)
      - Who just spoke (speaker_name) — proximity chat, face-to-face
      - Who else is physically nearby (group_members)
      - The full recent conversation
      - What was just said
      - What it wants to accomplish strategically
    """
    # Build nearby context
    nearby = [p for p in (group_members or [])
              if p != disguise_name and p != speaker_name]
    if nearby:
        nearby_line = f"Others nearby: {', '.join(nearby)}."
    elif group_members:
        nearby_line = f"Just you and {speaker_name} here right now."
    else:
        nearby_line = f"You are talking directly to {speaker_name}."

    directive = intent.to_prompt_fragment()

    # Natural modes: respond first, intent is optional and light
    # Strategic modes: intent drives the reply, but still sounds human
    natural_modes = {'gather_info', 'build_trust', 'casual'}

    if strategy_mode in natural_modes:
        # Skip the question most of the time in natural modes
        if intent.action == 'ask_location' and random.random() > 0.35:
            task_line = f"Respond naturally to what {speaker_name} said."
        else:
            task_line = (
                f"Respond naturally to what {speaker_name} said. "
                f"If it fits, also: {directive}"
            )
    else:
        # Strategic intent is primary
        task_line = f"Respond to {speaker_name}. Your goal: {directive}"

    prompt = (
        f"You are {disguise_name} in Koschei Station — abandoned Soviet post, scavengers everywhere.\n"
        f"You are talking face-to-face with {speaker_name}. {nearby_line}\n"
        f"Locations: Sheds, Barns, Greenhouse, Church, Pavilion.\n"
        f"Actions: collecting wood/mushrooms, taking cans to church, shooting scavengers. Limited ammo.\n"
        f"NPCs: Dr. Voss (gave briefing), Dr. Petrov (rescued from lower levels).\n"
        f"Style: {style_summary}\n"
        f"NEVER use: day/night, knives, tunnels, inventory, emojis, the word Koschei. Plain text only.\n"
        f"Do NOT put quotes around your reply.\n"
        f"\n"
        f"Recent chat:\n"
        f"{conversation[-200:]}\n"
        f"\n"
        f"{speaker_name} just said: {last_message}\n"
        f"\n"
        f"{task_line}\n"
        f"Reply as {disguise_name} in 1-2 short casual sentences (no quotes):"
    )

    try:
        response = llm.invoke(prompt)
        reply = (response.content or "").strip()

        # Strip wrapping quotes
        if (reply.startswith('"') and reply.endswith('"')) or \
           (reply.startswith("'") and reply.endswith("'")):
            reply = reply[1:-1].strip()
        reply = re.sub(r'^["\']|["\']$', '', reply).strip()

        # Strip role-play preamble ("Player2: ...")
        if ':' in reply and reply.index(':') < 20:
            reply = reply.split(':', 1)[1].strip()

        # Hard-cap at 2 sentences
        sentences = reply.split('. ')
        if len(sentences) > 2:
            reply = '. '.join(sentences[:2])
            if not reply.endswith('.'):
                reply += '.'

        reply = strip_non_ascii(reply)

        is_valid, error = validate_response(reply)
        if not is_valid:
            print(f"   \u26a0\ufe0f Invalid response ({error}), using template")
            reply = random.choice(get_response_templates(strategy_mode))

        return reply

    except Exception as e:
        print(f"   \u274c LLM error: {e}")
        return strip_non_ascii(random.choice(get_response_templates(strategy_mode)))


# ── Backward compat ───────────────────────────────────────────────────────────

class _CasualIntent:
    """Minimal intent stub for backward-compat calls."""
    action = 'casual'
    def to_prompt_fragment(self):
        return "Say something casual about the game."

def generate_npc_reply(player_text, round_id="r1",
                        imitate_player_id=None, recent_msgs=None):
    return generate_npc_reply_fast(
        disguise_name=imitate_player_id or "Player",
        style_summary="Casual gamer",
        conversation=player_text,
        last_message=player_text,
        speaker_name="someone",
        group_members=[],
        intent=_CasualIntent(),
        strategy_mode="casual",
    )