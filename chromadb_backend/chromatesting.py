# chromatesting.py - ULTRA-OPTIMIZED FOR SPEED
import chromadb
import time
from uuid import uuid4
from datetime import datetime

from langchain_ollama import OllamaEmbeddings, ChatOllama

# --- Ollama base URL (set explicitly) ---
OLLAMA_BASE = "http://127.0.0.1:11434"

# 🔹 Wrapper to make Ollama embeddings Chroma-compatible
class OllamaWrapper:
    def __init__(self, model_name: str):
        self.embedder = OllamaEmbeddings(model=model_name, base_url=OLLAMA_BASE)

    def __call__(self, input: list[str]):
        return self.embedder.embed(input)

    def name(self):
        return "ollama"


# 🔹 Start ChromaDB client (persistent store)
client = chromadb.PersistentClient(path="./chroma")

# 🔹 Embedding function
embed = OllamaWrapper("snowflake-arctic-embed")

# 🔹 Collections (create or get)
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


# --- ULTRA-FAST LLM with maximum optimization ---
llm = ChatOllama(
    model="llama3.2:3b",
    temperature=0.8,  # Higher for more natural variation
    base_url=OLLAMA_BASE,
    num_ctx=256,  # ⚡ REDUCED from 512 to 256 for speed
    num_predict=30,  # ⚡ STRICT LIMIT: Only 30 tokens (~1-2 sentences)
    top_p=0.9,
    top_k=20,  # ⚡ Limit sampling for speed
)

VALID_LOCATIONS = ["Pavillion", "Church", "Mansion", "Greenhouse", "Sheds"]
VALID_TASKS = ["collecting mushrooms", "collecting wood", "fighting aliens", "burning mushrooms"]


def generate_npc_reply_fast(
    disguise_name: str,
    style_summary: str,
    global_context: str,
    conversation: str,
    recent_msgs: list
) -> str:
    """
    ⚡ ULTRA-FAST generation with minimal prompt
    
    NO STYLOMETRIC ANALYSIS - too slow!
    Uses simple rules-based approach instead.
    """
    
    # Quick style analysis from recent messages
    style_hint = ""
    if recent_msgs:
        avg_len = sum(len(m.split()) for m in recent_msgs) / len(recent_msgs)
        if avg_len < 5:
            style_hint = "Very brief."
        elif avg_len < 10:
            style_hint = "Short casual."
        else:
            style_hint = "Conversational."
    
    # MINIMAL PROMPT for maximum speed
    prompt = f"""You are {disguise_name}. {style_summary} {style_hint}

Recent: {global_context}

{conversation}

Reply in 1 short sentence as {disguise_name}:"""
    
    try:
        response = llm.invoke(prompt)
        reply = (response.content or "").strip()
        
        # Force brevity
        if '. ' in reply:
            reply = reply.split('. ')[0] + '.'
        
        # Remove any preamble
        if ':' in reply and reply.index(':') < 20:
            reply = reply.split(':', 1)[1].strip()
        
        return reply
        
    except Exception as e:
        print(f"   ❌ LLM error: {e}")
        return "Yeah."


# Fallback for old code
def generate_npc_reply(player_text, round_id="r1", imitate_player_id=None, recent_msgs=None):
    """Backward compatibility wrapper"""
    return generate_npc_reply_fast(
        disguise_name=imitate_player_id or "Player",
        style_summary="Casual gamer",
        global_context="Game in progress",
        conversation=player_text,
        recent_msgs=recent_msgs or []
    )