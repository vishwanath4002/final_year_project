# chromatesting.py - OPTIMIZED WITH GAME CONTEXT (ORIGINAL MEMORY SETTINGS)
import chromadb
import time
import re
import unicodedata
from uuid import uuid4
from datetime import datetime

from langchain_ollama import OllamaEmbeddings, ChatOllama
from game_context import get_game_context_prompt, get_response_templates, validate_response, get_contextual_facts

# --- Ollama base URL (set explicitly) ---
OLLAMA_BASE = "http://127.0.0.1:11434"

# 📍 Wrapper to make Ollama embeddings Chroma-compatible
class OllamaWrapper:
    def __init__(self, model_name: str):
        self.embedder = OllamaEmbeddings(model=model_name, base_url=OLLAMA_BASE)

    def __call__(self, input: list[str]):
        return self.embedder.embed(input)

    def name(self):
        return "ollama"


# 📍 Start ChromaDB client (persistent store)
client = chromadb.PersistentClient(path="./chroma")

# 📍 Embedding function
embed = OllamaWrapper("snowflake-arctic-embed")

# 📍 Collections (create or get)
def safe_get_collection(name, embedding_function):
    names = [c.name for c in client.list_collections()]
    if name in names:
        return client.get_or_create_collection(name=name)
    else:
        return client.get_or_create_collection(
            name=name, embedding_function=embedding_function
        )


player_messages = safe_get_collection("player_messages", embed)
game_events = safe_get_collection("game_events", embed)
npc_memory = safe_get_collection("npc_memory", embed)


def strip_non_ascii(text: str) -> str:
    """
    Remove all emoji, unicode symbols, and non-ASCII characters from text.
    Keeps standard Latin characters, digits, punctuation, and whitespace only.
    """
    # Normalize to decomposed form first
    text = unicodedata.normalize('NFKD', text)
    # Remove any character that is not basic ASCII printable (0x20-0x7E)
    text = re.sub(r'[^ -~]', '', text)
    # Collapse multiple spaces that may result from removal
    text = re.sub(r'  +', ' ', text).strip()
    return text



# --- Add helpers ---
def add_player_message_with_group(text, player_id, round_id, group_id, location="Unknown", timestamp=None):
    """Store player message WITH group information"""
    if timestamp is None:
        timestamp = datetime.utcnow().isoformat()
    msg_id = f"msg-{uuid4()}"
    player_messages.add(
        documents=[text],
        metadatas=[
            {
                "player_id": player_id,
                "round_id": round_id,
                "group_id": group_id,
                "location": location,
                "timestamp": timestamp,
            }
        ],
        ids=[msg_id],
    )
    return msg_id


def query_collection(collection, query, k=3, filters=None):
    if filters:
        return collection.query(query_texts=[query], n_results=k, where=filters)
    else:
        return collection.query(query_texts=[query], n_results=k)


# --- MEMORY-FRIENDLY LLM (ORIGINAL SETTINGS THAT WORKED) ---
llm = ChatOllama(
    model="llama3.2:3b",
    temperature=0.8,
    base_url=OLLAMA_BASE,
    num_ctx=512,      # 512 gives enough room for the prompt + conversation context
    num_predict=40,   # Allow slightly longer replies before truncation
    top_p=0.9,
    top_k=20,
)

VALID_LOCATIONS = ["Pavilion", "Church", "Mansion", "Greenhouse", "Sheds", "Barns"]
VALID_TASKS = ["collecting mushrooms", "collecting wood", "fighting aliens", "burning mushrooms", "burning wood", "bringing food cans"]


def generate_npc_reply_fast(
    disguise_name: str,
    style_summary: str,
    global_context: str,
    conversation: str,
    recent_msgs: list,
    strategy_mode: str = "casual",
    strategic_response: str = None
) -> str:
    """
    ⚡ FAST generation with GAME CONTEXT + MEMORY-FRIENDLY settings

    Bug 1 fix: when a strategic_response is provided, it is injected into the
    prompt as a directive ("Say this but in your natural style: …") so the LLM
    actually follows the deception strategy instead of ignoring it.
    """

    if strategic_response and strategy_mode != "casual":
        # Bug 1 fix: strategic intent is now the centrepiece of the prompt.
        # The LLM is asked to rephrase it in the player's voice, not ignore it.
        prompt = f"""You are {disguise_name} in a Chernobyl survival game. {style_summary}

Game locations: Sheds, Barns, Greenhouse, Church, Pavilion.
Actions: collecting wood/mushrooms, taking cans, shooting aliens. Limited ammo.
NEVER mention: day/night, knives, caves, inventory, upgrades. No emojis or special characters.

Recent chat:
{conversation[-200:]}

Rephrase this in {disguise_name}'s casual style (1 sentence, sound like a real player):
{strategic_response}

Reply:"""

    else:
        # Generate from scratch with compact game context
        context_facts = get_contextual_facts(recent_msgs, {})

        prompt = f"""You're {disguise_name} in survival game.
Locations: Sheds, Church, Greenhouse, Pavilion
Actions: collecting wood/mushrooms, shooting aliens
Gun has limited ammo. Hold ONE item.
NO: day/night, knives, caves, emojis

{context_facts}

{conversation[-100:]}

Reply 1 sentence as {disguise_name}:"""
    
    try:
        response = llm.invoke(prompt)
        reply = (response.content or "").strip()
        
        # Clean up response
        # Remove any preamble
        if ':' in reply and reply.index(':') < 20:
            reply = reply.split(':', 1)[1].strip()
        
        # Force brevity - max 2 sentences
        sentences = reply.split('. ')
        if len(sentences) > 2:
            reply = '. '.join(sentences[:2])
            if not reply.endswith('.'):
                reply += '.'
        
        # Strip any emoji or unicode the LLM may have added
        reply = strip_non_ascii(reply)

        # Validate response follows game rules
        is_valid, error = validate_response(reply)
        if not is_valid:
            print(f"   ⚠️ Invalid response ({error}), using template")
            # Fall back to template
            templates = get_response_templates(strategy_mode)
            import random
            reply = random.choice(templates)
        
        return reply
        
    except Exception as e:
        print(f"   ❌ LLM error: {e}")
        templates = get_response_templates(strategy_mode)
        import random
        return strip_non_ascii(random.choice(templates))


# Fallback for old code
def generate_npc_reply(player_text, round_id="r1", imitate_player_id=None, recent_msgs=None):
    """Backward compatibility wrapper"""
    return generate_npc_reply_fast(
        disguise_name=imitate_player_id or "Player",
        style_summary="Casual gamer",
        global_context="Game in progress",
        conversation=player_text,
        recent_msgs=recent_msgs or [],
        strategy_mode="casual"
    )