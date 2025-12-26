# chromatesting.py
import chromadb
import time
from concurrent.futures import ThreadPoolExecutor
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

# Create/connect to ChromaDB
client = chromadb.PersistentClient(path="./chroma")

# Get or create collections (auto-creates if missing)
player_messages = client.get_or_create_collection(
    name="player_messages",
    metadata={"description": "Player chat messages with group info"}
)

npc_memory = client.get_or_create_collection(
    name="npc_memory",
    metadata={"description": "NPC/Impostor memory"}
)


# 🔹 2️⃣ Embedding function
embed = OllamaWrapper("snowflake-arctic-embed")

# 🔹 3️⃣ Collections (create or get)
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
def add_player_message(text, player_id, round_id, location="Unknown", timestamp=None):
    if timestamp is None:
        timestamp = datetime.utcnow().isoformat()
    msg_id = f"msg-{uuid4()}"
    player_messages.add(
        documents=[text],
        metadatas=[
            {
                "player_id": player_id,
                "round_id": round_id,
                "location": location,
                "timestamp": timestamp,
            }
        ],
        ids=[msg_id],
    )
    return msg_id

def add_player_message_with_group(text, player_id, round_id, group_id, location="Unknown", timestamp=None):
    """
    Store player message WITH group information
    """
    if timestamp is None:
        timestamp = datetime.utcnow().isoformat()
    msg_id = f"msg-{uuid4()}"
    player_messages.add(
        documents=[text],
        metadatas=[
            {
                "player_id": player_id,
                "round_id": round_id,
                "group_id": group_id,  # NEW: Track which group message is from
                "location": location,
                "timestamp": timestamp,
            }
        ],
        ids=[msg_id],
    )
    return msg_id

def query_messages_by_group(group_id, k=10):
    """
    Retrieve recent messages from a specific group conversation
    """
    return player_messages.query(
        query_texts=["conversation"],  # Generic query
        n_results=k,
        where={"group_id": group_id}
    )

def add_npc_memory(text, memory_type, round_id, timestamp=None):
    if timestamp is None:
        timestamp = datetime.utcnow().isoformat()
    npc_id = f"npc-{uuid4()}"
    npc_memory.add(
        documents=[text],
        metadatas=[{"memory_type": memory_type, "round_id": round_id, "timestamp": timestamp}],
        ids=[npc_id],
    )
    return npc_id


def query_collection(collection, query, k=3, filters=None):
    if filters:
        return collection.query(query_texts=[query], n_results=k, where=filters)
    else:
        return collection.query(query_texts=[query], n_results=k)


def format_results(results):
    docs = results.get("documents", [[]])[0] or []
    metas = results.get("metadatas", [[]])[0] or []
    formatted = []
    for doc, meta in zip(docs, metas):
        entry = f"[{meta.get('timestamp','?')} | {meta.get('player_id','NPC')} | {meta.get('location','?')}] {doc}"
        formatted.append(entry)
    return formatted


# --- Reply generator (LLM) ---
llm = ChatOllama(
    model="llama3.2:3b",
    temperature=0.7,  # Slightly higher for more natural variation
    base_url=OLLAMA_BASE,
    num_ctx=512,  # Increased context window for better coherence
)

# 🔹 Valid map locations
VALID_LOCATIONS = ["Pavillion", "Church", "Mansion", "Greenhouse", "Sheds"]

# 🔹 Valid tasks in your game
VALID_TASKS = ["collecting mushrooms", "collecting wood", "fighting aliens", "burning mushrooms"]


def filter_memory(snippets, valid_locations):
    """Keep only memory snippets that mention a valid location."""
    return [s for s in snippets if any(loc in s for loc in valid_locations)]


def generate_npc_reply(player_text, round_id="r1", imitate_player_id=None, recent_msgs=None):
    """
    Generates an NPC reply using memory + optional style imitation.
    
    IMPORTANT: player_text now includes pre-built context from fastapi_chat.py
    including GAME_CONTEXT, recent conversation, and memory context.
    We just need to add style imitation if requested.
    """

    t0 = time.time()

    # 1️⃣ Generate player style summary if imitation requested
    style_instructions = ""
    if imitate_player_id and recent_msgs:
        try:
            style_summary = summarize_player_style(imitate_player_id, recent_msgs)
            style_instructions = f"""
IMPORTANT: You are imitating the chat style of {imitate_player_id}.
Their style: {style_summary}

Match their:
- Message length and structure
- Grammar/typo patterns
- Tone and attitude
- Any unique phrases or quirks
"""
        except Exception as e:
            print(f"⚠️ Style summarization failed: {e}")
            style_instructions = f"\nYou are chatting as {imitate_player_id}. Keep it natural.\n"

    # 2️⃣ Build the complete prompt
    # player_text already contains GAME_CONTEXT + conversation + memory from fastapi_chat.py
    prompt = f"""{player_text}

{style_instructions}

CRITICAL RULES:
1. Response must be 1-2 sentences maximum (SHORT!)
2. Only mention these locations: {', '.join(VALID_LOCATIONS)}
3. Only reference these tasks: {', '.join(VALID_TASKS)}
4. Sound like a real player chatting, not an AI
5. No emojis unless the player uses them
6. No roleplay actions or descriptions
7. Stay consistent with any past statements in memory
8. If unsure about something, say you don't remember clearly

Now respond naturally:"""

    # 3️⃣ Call LLM
    try:
        response = llm.invoke(prompt)
        t1 = time.time()
        reply = (response.content or "").strip()
        
        # Quick post-processing to ensure brevity
        sentences = reply.split('. ')
        if len(sentences) > 2:
            reply = '. '.join(sentences[:2]) + '.'
        
        print(f"⏱️ LLM generation time: {t1 - t0:.2f}s")
        print(f"📝 Generated reply: {reply}")
        
    except Exception as e:
        print(f"❌ LLM call failed: {e}")
        reply = "Not sure, was busy with mushrooms."

    # 4️⃣ Save reply into memory for future context
    try:
        add_npc_memory(reply, "said", round_id)
    except Exception as e:
        print(f"⚠️ Failed to save to memory: {e}")

    return reply