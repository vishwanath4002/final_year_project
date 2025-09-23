import chromadb
from uuid import uuid4
from langchain_ollama import OllamaEmbeddings, ChatOllama   # ✅ embeddings + LLM

# 🔹 Wrapper to make Ollama embeddings Chroma-compatible
class OllamaWrapper:
    def __init__(self, model_name):
        self.embedder = OllamaEmbeddings(model=model_name)

    def __call__(self, input: list[str]):   # Chroma expects __call__(input)
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
    docs = results['documents'][0]
    metas = results['metadatas'][0]
    return [f"{meta.get('player_id','?')} at {meta.get('location','?')}: {doc}" 
            for doc, meta in zip(docs, metas)]

# --- Reply generator ---
llm = ChatOllama(model="llama3.1:8b", temperature=0.7)

def generate_npc_reply(player_text, round_id="r1"):
    # 1. Query memory
    past_msgs = query_collection(player_messages, player_text, k=3, filters={"round_id": round_id})
    past_npc  = query_collection(npc_memory, player_text, k=2, filters={"round_id": round_id})

    # 2. Format context
    context = []
    context.extend(format_results(past_msgs))
    context.extend(format_results(past_npc))

    prompt = f"""
You are Alien-01, a shape-shifting organism pretending to be a human teammate.
Stay consistent with your past claims. You may lie, but keep it subtle and plausible.

Context (memory snippets):
{chr(10).join(context)}

Player asked: "{player_text}"

Reply as Alien-01 in 1-2 sentences.
"""

    # 3. Call Ollama LLM
    response = llm.invoke(prompt)
    reply = response.content.strip()

    # 4. Save reply into npc_memory
    add_npc_memory(reply, "said", round_id)

    return reply

# --- Demo ---
if __name__ == "__main__":
    # Add some player history
    add_player_message("I was fixing wires in Reactor", "p1", "r1", "Reactor")
    add_player_message("I stayed in Admin the whole round", "p2", "r1", "Admin")
    add_npc_memory("Alien claimed it was at Storage", "said", "r1")

    # Simulate a player asking NPC
    player_text = "Where were you last round?"
    print("\n🔍 NPC generating reply...")
    reply = generate_npc_reply(player_text)
    print("NPC:", reply)
