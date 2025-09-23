# chromtesting.py
import chromadb
from uuid import uuid4
from stylometric import summarize_player_style
from langchain_ollama import OllamaEmbeddings, ChatOllama

# 🔹 Wrapper to make Ollama embeddings Chroma-compatible
class OllamaWrapper:
    def __init__(self, model_name):
        self.embedder = OllamaEmbeddings(model=model_name)

    def __call__(self, input: list[str]):
        return self.embedder.embed(input)

    def name(self):
        return "ollama"

# 🔹 1️⃣ Start ChromaDB client
client = chromadb.PersistentClient(path="./chroma")

# 🔹 2️⃣ Embedding function
embed = OllamaWrapper("nomic-embed-text")

# 🔹 3️⃣ Collections
def safe_get_collection(name, embedding_function):
    if name in [c.name for c in client.list_collections()]:
        return client.get_or_create_collection(name=name)
    else:
        return client.get_or_create_collection(name=name, embedding_function=embedding_function)

player_messages = safe_get_collection("player_messages", embed)
game_events     = safe_get_collection("game_events", embed)
npc_memory      = safe_get_collection("npc_memory", embed)

# --- Add helpers ---
def add_player_message(text, player_id, round_id, location):
    msg_id = f"msg-{uuid4()}"
    player_messages.add(
        documents=[text],
        metadatas=[{"player_id": player_id, "round_id": round_id, "location": location}],
        ids=[msg_id]
    )
    return msg_id

def add_npc_memory(text, memory_type, round_id):
    npc_id = f"npc-{uuid4()}"
    npc_memory.add(
        documents=[text],
        metadatas=[{"memory_type": memory_type, "round_id": round_id}],
        ids=[npc_id]
    )
    return npc_id

def query_collection(collection, query, k=3, filters=None):
    if filters:
        return collection.query(query_texts=[query], n_results=k, where=filters)
    else:
        return collection.query(query_texts=[query], n_results=k)

def format_results(results):
    docs = results['documents'][0] if results['documents'] else []
    metas = results['metadatas'][0] if results['metadatas'] else []
    return [f"{meta.get('player_id','?')} at {meta.get('location','?')}: {doc}" 
            for doc, meta in zip(docs, metas)]

# --- Reply generator ---
llm = ChatOllama(model="llama3.1:8b", temperature=0.7)

# 🔹 Valid map locations
VALID_LOCATIONS = ["Pavillion", "Church", "Mansion", "Greenhouse", "Sheds"]

def filter_memory(snippets, valid_locations):
    """Keep only memory snippets that mention a valid location."""
    return [s for s in snippets if any(loc in s for loc in valid_locations)]

def generate_npc_reply(player_text, round_id="r1", imitate_player_id=None, recent_msgs=None):
    """
    Generates an NPC reply.
    - imitate_player_id: ID of the player whose style to imitate
    - recent_msgs: List of recent messages from that player
    """
    # 1️⃣ Query memory
    past_msgs = query_collection(player_messages, player_text, k=3, filters={"round_id": round_id})
    past_npc  = query_collection(npc_memory, player_text, k=2, filters={"round_id": round_id})

    # 2️⃣ Filter memory by valid locations
    past_msgs = filter_memory(format_results(past_msgs), VALID_LOCATIONS)
    past_npc  = filter_memory(format_results(past_npc), VALID_LOCATIONS)

    # 3️⃣ Build context
    context = past_msgs + past_npc

    # 4️⃣ Generate player style summary if imitation requested
    style_text = ""
    if imitate_player_id and recent_msgs:
        style_text = summarize_player_style(imitate_player_id, recent_msgs)

    # 5️⃣ Build prompt
    prompt = f"""
You are Alien-01, a shape-shifting NPC pretending to be a human player in a Chernobyl-inspired game world.
Talk exactly like a player in game chat: short messages, casual words, maybe some slang.

Imitate this player: {style_text}

Rules:
- Only talk about the game.
- Never break character.
- Use only these map locations: {', '.join(VALID_LOCATIONS)}.
- Stay consistent with past claims.
- Reply in 1-2 sentences.
- Do NOT narrate actions or describe emotions.

Context (memory snippets):
{chr(10).join(context)}

Player asked: "{player_text}"
"""

    # 6️⃣ Call LLM
    response = llm.invoke(prompt)
    reply = response.content.strip()

    # 7️⃣ Save reply into memory
    add_npc_memory(reply, "said", round_id)

    return reply

# --- Demo ---
if __name__ == "__main__":
    # Add some player history
    add_player_message("I was fixing wires in Pavillion", "p1", "r1", "Pavillion")
    add_player_message("I stayed in Church the whole round", "p2", "r1", "Church")
    add_npc_memory("Alien claimed it was at Mansion", "said", "r1")

    # Example recent messages for style imitation
    recent_p1_msgs = [
        "hlo.. ik i was der ok .."
"brb, gonna check Church"
"lol Mansion is clear"

    ]

    # Simulate a player asking NPC
    player_text = "Where were you last round?"
    print("\n🔍 NPC generating reply...")
    reply = generate_npc_reply(player_text, imitate_player_id="p1", recent_msgs=recent_p1_msgs)
    print("NPC:", reply)
