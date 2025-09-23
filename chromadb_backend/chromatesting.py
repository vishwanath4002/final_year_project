import chromadb
from uuid import uuid4
from langchain_ollama import OllamaEmbeddings  # ✅ correct package
import os

# 🔹 0️⃣ Wrapper to make Ollama compatible with ChromaDB
class OllamaWrapper:
    def __init__(self, model_name):
        self.embedder = OllamaEmbeddings(model=model_name)

    # Chroma now expects __call__(self, input)
    def __call__(self, input: list[str]):
        return self.embedder.embed(input)

    def name(self):
        return "ollama"
    def __init__(self, model_name):
        self.embedder = OllamaEmbeddings(model=model_name)

    def __call__(self, texts):
        return self.embedder.embed(texts)

    def name(self):
        return "ollama"

# 🔹 1️⃣ Start ChromaDB client (persistent)
client = chromadb.PersistentClient(path="./chroma")

# 🔹 2️⃣ Embedding model wrapped
embed = OllamaWrapper("nomic-embed-text")

# 🔹 3️⃣ Helper to safely create or get collection
def safe_get_collection(name, embedding_function):
    """
    Returns an existing collection if present.
    Only assigns embedding_function if the collection is new.
    """
    if name in [c.name for c in client.list_collections()]:
        # Collection exists; do NOT pass embedding_function to avoid conflicts
        return client.get_or_create_collection(name=name)
    else:
        # Collection is new; safe to pass embedding_function
        return client.get_or_create_collection(name=name, embedding_function=embedding_function)

# Create collections safely
player_messages = safe_get_collection("player_messages", embed)
game_events = safe_get_collection("game_events", embed)
npc_memory = safe_get_collection("npc_memory", embed)

# --- Helpers to add data ---
def add_player_message(text, player_id, round_id, location):
    msg_id = f"msg-{uuid4()}"
    player_messages.add(
        documents=[text],
        metadatas=[{
            "player_id": player_id,
            "round_id": round_id,
            "location": location
        }],
        ids=[msg_id]
    )
    return msg_id

def add_game_event(text, event_type, round_id, location):
    evt_id = f"evt-{uuid4()}"
    game_events.add(
        documents=[text],
        metadatas=[{
            "event_type": event_type,
            "round_id": round_id,
            "location": location
        }],
        ids=[evt_id]
    )
    return evt_id

def add_npc_memory(text, memory_type, round_id):
    npc_id = f"npc-{uuid4()}"
    npc_memory.add(
        documents=[text],
        metadatas=[{
            "memory_type": memory_type,
            "round_id": round_id
        }],
        ids=[npc_id]
    )
    return npc_id

# --- Query helper ---
def query_collection(collection, query, k=3, filters=None):
    if filters:
        return collection.query(
            query_texts=[query],
            n_results=k,
            where=filters
        )
    else:
        return collection.query(
            query_texts=[query],
            n_results=k
        )
def format_results(results):
    docs = results['documents'][0]
    metas = results['metadatas'][0]
    return [f"{meta['player_id']} at {meta.get('location','Unknown')}: {doc}" 
            for doc, meta in zip(docs, metas)]



# --- Demo ---
if __name__ == "__main__":
    add_player_message("I was fixing wires in Reactor", "p1", "r1", "Reactor")
    add_player_message("I stayed in Admin the whole round", "p2", "r1", "Admin")
    add_npc_memory("Alien claimed it was at Storage", "said", "r1")

    print("\n🔍 Semantic query (location):")
    print(query_collection(player_messages, "where were you last round?", k=2))

    formatted = format_results(query_collection(player_messages, "where were you last round?", k=2))
    print(formatted)

