# chromatesting.py - OPTIMIZED FOR SPEED
import chromadb
import time
from uuid import uuid4
from datetime import datetime

from stylometric import summarize_player_style
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


# --- Reply generator (LLM) - OPTIMIZED FOR SPEED ---
llm = ChatOllama(
    model="llama3.2:3b",
    temperature=0.7,
    base_url=OLLAMA_BASE,
    num_ctx=512,
    num_predict=50,  # ✅ SPEED: Limit to ~50 tokens (1-2 sentences)
    top_p=0.9,  # ✅ SPEED: Nucleus sampling for faster, focused generation
)

# 🔹 Valid map locations
VALID_LOCATIONS = ["Pavillion", "Church", "Mansion", "Greenhouse", "Sheds"]

# 🔹 Valid tasks in your game
VALID_TASKS = ["collecting mushrooms", "collecting wood", "fighting aliens", "burning mushrooms"]


def generate_npc_reply(player_text, round_id="r1", imitate_player_id=None, recent_msgs=None):
    """
    Generates an NPC reply using memory + optional style imitation.
    OPTIMIZED FOR SPEED.
    """

    t0 = time.time()

    # Generate player style summary if imitation requested
    style_instructions = ""
    if imitate_player_id and recent_msgs and len(recent_msgs) >= 3:
        try:
            style_summary = summarize_player_style(imitate_player_id, recent_msgs)
            style_instructions = f"\nYou are {imitate_player_id}. Style: {style_summary}\n"
        except Exception as e:
            print(f"   ⚠️ Style failed: {e}")
            style_instructions = f"\nYou are {imitate_player_id}.\n"
    elif imitate_player_id:
        style_instructions = f"\nYou are {imitate_player_id}. Chat casually.\n"

    # Build the prompt - SHORTER FOR SPEED
    prompt = f"""{player_text}

{style_instructions}

RULES:
1. 1-2 sentences max (SHORT!)
2. Locations: {', '.join(VALID_LOCATIONS)}
3. Tasks: {', '.join(VALID_TASKS)}
4. Sound natural, like a real player
5. No emojis, no roleplay

Reply:"""

    # Call LLM
    try:
        response = llm.invoke(prompt)
        t1 = time.time()
        reply = (response.content or "").strip()
        
        # Ensure brevity
        sentences = reply.split('. ')
        if len(sentences) > 2:
            reply = '. '.join(sentences[:2]) + '.'
        
        print(f"   ⏱️ LLM: {t1 - t0:.2f}s")
        
    except Exception as e:
        print(f"   ❌ LLM failed: {e}")
        reply = "Yeah, been busy collecting stuff."

    return reply