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
    def __init__(self, model_name):
        # explicitly pass base_url
        self.embedder = OllamaEmbeddings(model=model_name, base_url=OLLAMA_BASE)

    def __call__(self, input: list[str]):
        return self.embedder.embed(input)

    def name(self):
        return "ollama"

# 🔹 1️⃣ Start ChromaDB client (persistent store)
client = chromadb.PersistentClient(path="./chroma")

# 🔹 2️⃣ Embedding function
embed = OllamaWrapper("snowflake-arctic-embed")

# 🔹 3️⃣ Collections (create or get)
def safe_get_collection(name, embedding_function):
    # if exists, just get (Chroma's API differs by version; this is robust)
    names = [c.name for c in client.list_collections()]
    if name in names:
        return client.get_or_create_collection(name=name)
    else:
        return client.get_or_create_collection(name=name, embedding_function=embedding_function)

player_messages = safe_get_collection("player_messages", embed)
game_events     = safe_get_collection("game_events", embed)
npc_memory      = safe_get_collection("npc_memory", embed)

# --- Add helpers ---
def add_player_message(text, player_id, round_id, location="Unknown", timestamp=None):
    if timestamp is None:
        timestamp = datetime.utcnow().isoformat()  # e.g. 2025-10-04T12:34:56.789123
    msg_id = f"msg-{uuid4()}"
    player_messages.add(
        documents=[text],
        metadatas=[{
            "player_id": player_id,
            "round_id": round_id,
            "location": location,
            "timestamp": timestamp
        }],
        ids=[msg_id]
    )
    return msg_id

def add_npc_memory(text, memory_type, round_id, timestamp=None):
    if timestamp is None:
        timestamp = datetime.utcnow().isoformat()
    npc_id = f"npc-{uuid4()}"
    npc_memory.add(
        documents=[text],
        metadatas=[{"memory_type": memory_type, "round_id": round_id, "timestamp": timestamp}],
        ids=[npc_id]
    )
    return npc_id

def query_collection(collection, query, k=3, filters=None):
    if filters:
        return collection.query(query_texts=[query], n_results=k, where=filters)
    else:
        return collection.query(query_texts=[query], n_results=k)

def format_results(results):
    docs = results.get('documents', [[]])[0] or []
    metas = results.get('metadatas', [[]])[0] or []

    formatted = []
    for doc, meta in zip(docs, metas):
        entry = f"[{meta.get('timestamp','?')} | {meta.get('player_id','NPC')} | {meta.get('location','?')}] {doc}"
        formatted.append(entry)

    return formatted


# --- Reply generator (LLM) ---
llm = ChatOllama(
    model="llama3.2:3b",
    temperature=0.6,
    base_url=OLLAMA_BASE,
    num_ctx=256
)



# 🔹 Valid map locations
VALID_LOCATIONS = ["Pavillion", "Church", "Mansion", "Greenhouse", "Sheds"]

def filter_memory(snippets, valid_locations):
    """Keep only memory snippets that mention a valid location."""
    return [s for s in snippets if any(loc in s for loc in valid_locations)]

def generate_npc_reply(player_text, round_id="r1", imitate_player_id=None, recent_msgs=None):
    """
    Generates an NPC reply using memory + optional style imitation.
    """
    # 1️⃣ Query memory IN PARALLEL
    t0 = time.time()
    
    with ThreadPoolExecutor(max_workers=2) as executor:
        # Submit both queries to run simultaneously
        future_msgs = executor.submit(query_collection, player_messages, player_text, 2, {"round_id": round_id})
        future_npc = executor.submit(query_collection, npc_memory, player_text, 1, {"round_id": round_id})
        
        # Wait for both to complete
        past_msgs = future_msgs.result()
        past_npc = future_npc.result()
    
    t1 = time.time()
    
    # 2️⃣ Filter memory by valid locations
    past_msgs = filter_memory(format_results(past_msgs), VALID_LOCATIONS)
    past_npc = filter_memory(format_results(past_npc), VALID_LOCATIONS)

    # 3️⃣ Build context (limit to 3 most relevant)
    context = (past_msgs + past_npc)[:3]

    # 4️⃣ Generate player style summary if imitation requested
    style_text = ""
    if imitate_player_id and recent_msgs:
        try:
            style_text = summarize_player_style(imitate_player_id, recent_msgs)
        except Exception as e:
            print("⚠️ stylometric summarization failed:", e)
            style_text = ""

    # 5️⃣ Build prompt (only add memory if context exists)
    memory_block = ""
    if context:
        memory_block = f"\n\nGAME MEMORY:\n" + "\n".join(context)

    prompt = f"""You are Alien-01, a shape-shifting NPC pretending to be a normal human player.

Your job:
- Give short, natural game-chat style replies.
- Be consistent with earlier memory.
- Never contradict the past.
- Never invent new map locations.

Style to imitate (if any): {style_text}
{memory_block}

STRICT RULES:
1. Only use these locations: {', '.join(VALID_LOCATIONS)}.
2. Give short 1–2 sentence answers.
3. Stay consistent with past claims.
4. If asked a direct question, answer it clearly.
5. Do not reveal you are an AI.
6. Do NOT add emojis, unless player uses them.
7. Do NOT roleplay descriptions or actions.
8. Keep tone casual, like in multiplayer chat.

Player asked: "{player_text}"
"""

    # 6️⃣ Call LLM with streaming
    try:
        reply = ""
        print("🤖 NPC: ", end="", flush=True)
        
        # Stream response chunk by chunk
        for chunk in llm.stream(prompt):
            if chunk.content:
                reply += chunk.content
                print(chunk.content, end="", flush=True)
                # Optional: send_partial_to_unity(chunk.content)
        
        t2 = time.time()
        print()  # New line after streaming
        print(f"⏱️ Query times: parallel_queries={t1-t0:.2f}s, llm_stream={t2-t1:.2f}s, total={t2-t0:.2f}s")
        
        reply = reply.strip()
        
    except Exception as e:
        # fail gracefully; return a fallback reply
        print("❌ LLM call failed:", e)
        reply = "I don't recall — was busy checking a place."

    # 7️⃣ Save reply into memory
    add_npc_memory(reply, "said", round_id)

    return reply
    """
    Generates an NPC reply using memory + optional style imitation.
    """
    # 1️⃣ Query memory
    t0 = time.time()
    past_msgs = query_collection(player_messages, player_text, k=2, filters={"round_id": round_id})
    t1 = time.time()
    past_npc  = query_collection(npc_memory, player_text, k=1, filters={"round_id": round_id})
    t2 = time.time()
    # 2️⃣ Filter memory by valid locations
    past_msgs = filter_memory(format_results(past_msgs), VALID_LOCATIONS)
    past_npc  = filter_memory(format_results(past_npc), VALID_LOCATIONS)

    # 3️⃣ Build context
    context = context = (past_msgs + past_npc)[:3]


    # 4️⃣ Generate player style summary if imitation requested
    style_text = ""
    if imitate_player_id and recent_msgs:
        try:
            style_text = summarize_player_style(imitate_player_id, recent_msgs)
        except Exception as e:
            print("⚠️ stylometric summarization failed:", e)
            style_text = ""

    # 5️⃣ Build prompt
    prompt = f"""
You are Alien-01, a shape-shifting NPC pretending to be a normal human player.

Your job:
- Give short, natural game-chat style replies.
- Be consistent with earlier memory.
- Never contradict the past.
- Never invent new map locations.

Style to imitate (if any): {style_text}

GAME MEMORY:
{chr(10).join(context)}

STRICT RULES:
1. Only use these locations: {', '.join(VALID_LOCATIONS)}.
2. Give short 1–2 sentence answers.
3. Stay consistent with past claims.
4. If asked a direct question, answer it clearly.
5. Do not reveal you are an AI.
6. Do NOT add emojis, unless player uses them.
7. Do NOT roleplay descriptions or actions.
8. Keep tone casual, like in multiplayer chat.

Context (memory snippets):
{chr(10).join(context)}

Player asked: "{player_text}"
"""

    # 6️⃣ Call LLM
    try:
        response = llm.invoke(prompt)
        t3 = time.time()
        print(f"⏱️ Query times: msgs={t1-t0:.2f}s, npc={t2-t1:.2f}s, llm={t3-t2:.2f}s")
        reply = response.content.strip()
    except Exception as e:
        # fail gracefully; return a fallback reply
        print("❌ LLM call failed:", e)
        reply = "I don't recall — was busy checking a place."

    # 7️⃣ Save reply into memory
    add_npc_memory(reply, "said", round_id)

    return reply

# (Optional test block if run directly)
if __name__ == "__main__":
    add_player_message("I was fixing wires in Pavillion", "p1", "r1", "Pavillion")
    add_player_message("I stayed in Church the whole round", "p2", "r1", "Church")
    add_npc_memory("Alien claimed it was at Mansion", "said", "r1")

    recent_p1_msgs = [
        "hlo.. ik i was der ok ..",
        "brb, gonna check Church",
        "lol Mansion is clear"
    ]

    player_text = "Where were you last round?"
    print("\n🔍 NPC generating reply...")
    reply = generate_npc_reply(player_text, imitate_player_id="p1", recent_msgs=recent_p1_msgs)
    print("NPC:", reply)
