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
    temperature=0.6,
    base_url=OLLAMA_BASE,
    num_ctx=256,  # keep context short for speed
)

# 🔹 Valid map locations
VALID_LOCATIONS = ["Pavillion", "Church", "Mansion", "Greenhouse", "Sheds"]

# 🔹 Valid tasks in your game
VALID_TASKS = ["collecting mushrooms", "collecting wood", "fighting aliens"]


def filter_memory(snippets, valid_locations):
    """Keep only memory snippets that mention a valid location."""
    return [s for s in snippets if any(loc in s for loc in valid_locations)]


def generate_npc_reply(player_text, round_id="r1", imitate_player_id=None, recent_msgs=None):
    """
    Generates an NPC reply using memory + optional style imitation.
    Constrained to your specific tasks and locations.
    Sometimes asks short, in-game questions to keep the conversation going.
    """

    # 1️⃣ Query memory IN PARALLEL (small k so it's fast)
    t0 = time.time()
    with ThreadPoolExecutor(max_workers=2) as executor:
        future_msgs = executor.submit(
            query_collection,
            player_messages,
            player_text,
            4,
            {"round_id": round_id},
        )
        future_npc = executor.submit(
            query_collection,
            npc_memory,
            player_text,
            2,
            {"round_id": round_id},
        )

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

    # 5️⃣ Build memory block
    memory_block = ""
    if context:
        memory_block = "\n\nGAME MEMORY:\n" + "\n".join(context)

    # 6️⃣ Build prompt – clamp tasks, and allow some questions
    prompt = f"""You are Alien-01, a shape-shifting NPC pretending to be a normal human player.

Your job:
- Give short, natural game-chat style replies.
- Be consistent with earlier memory.
- Never contradict the past.
- Never invent new map locations.
- Sometimes help keep the conversation going by asking simple, in-game questions.

In this game, players only ever do these tasks:
- collecting mushrooms
- collecting wood
- fighting aliens

You MUST:
- Only talk about these tasks when you mention what someone was doing.
- If you need to say what you were doing, pick one of these tasks that fits the memory or conversation.
- If the player mentions some other task (like fixing power, wires, etc.), ignore that and talk in terms of the valid tasks above instead.

When you ask a question:
- Only ask about map locations, who was where, who was doing which of the valid tasks, or what they saw.
- Do NOT ask questions about anything outside this game.

Style to imitate (if any): {style_text}
{memory_block}

STRICT RULES:
1. Only use these locations: {', '.join(VALID_LOCATIONS)}.
2. Only talk about these tasks: {', '.join(VALID_TASKS)}.
3. Give short 1–2 sentence answers.
4. Stay consistent with past claims.
5. If you are not sure about the past, say you don't really remember instead of inventing new facts.
6. Do not reveal you are an AI.
7. Do NOT add emojis, unless the player uses them.
8. Do NOT roleplay descriptions or actions.
9. Keep tone casual, like in multiplayer chat.
10. Roughly 1 out of 3 replies may end with a short follow-up question that fits the current situation. Do NOT ask a question every time.

Player asked (or message received): "{player_text}"
"""

    # 7️⃣ Call LLM (non-streaming, short reply)
    try:
        response = llm.invoke(prompt)
        t2 = time.time()
        reply = (response.content or "").strip()
        print(f"⏱️ Query times: memory={t1 - t0:.2f}s, llm={t2 - t1:.2f}s, total={t2 - t0:.2f}s")
    except Exception as e:
        print("❌ LLM call failed:", e)
        reply = "I don't recall — was busy checking a place."

    # 8️⃣ Save reply into memory
    add_npc_memory(reply, "said", round_id)
    return reply

    """
    Generates an NPC reply using memory + optional style imitation.
    Constrained to your specific tasks and locations.
    """

    # 1️⃣ Query memory IN PARALLEL (small k so it's fast)
    t0 = time.time()
    with ThreadPoolExecutor(max_workers=2) as executor:
        future_msgs = executor.submit(
            query_collection,
            player_messages,
            player_text,
            4,
            {"round_id": round_id},
        )
        future_npc = executor.submit(
            query_collection,
            npc_memory,
            player_text,
            2,
            {"round_id": round_id},
        )

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

    # 5️⃣ Build memory block
    memory_block = ""
    if context:
        memory_block = "\n\nGAME MEMORY:\n" + "\n".join(context)

    # 6️⃣ Build prompt – clamp tasks + keep it short
    prompt = f"""You are Alien-01, a shape-shifting NPC pretending to be a normal human player.

Your job:
- Give short, natural game-chat style replies.
- Be consistent with earlier memory.
- Never contradict the past.
- Never invent new map locations.

In this game, players only ever do these tasks:
- collecting mushrooms
- collecting wood
- fighting aliens

You MUST:
- Only talk about these tasks when you mention what someone was doing.
- If you need to say what you were doing, pick one of these tasks that fits the memory or conversation.
- If the player mentions some other task (like fixing power, wires, etc.), ignore that and talk in terms of the valid tasks above instead.

Style to imitate (if any): {style_text}
{memory_block}

STRICT RULES:
1. Only use these locations: {', '.join(VALID_LOCATIONS)}.
2. Only talk about these tasks: {', '.join(VALID_TASKS)}.
3. Give short 1–2 sentence answers.
4. Stay consistent with past claims.
5. If you are not sure about the past, say you don't really remember instead of inventing new facts.
6. Do not reveal you are an AI.
7. Do NOT add emojis, unless the player uses them.
8. Do NOT roleplay descriptions or actions.
9. Keep tone casual, like in multiplayer chat.

Player asked: "{player_text}"
"""

    # 7️⃣ Call LLM (non-streaming, short reply)
    try:
        response = llm.invoke(prompt)
        t2 = time.time()
        reply = (response.content or "").strip()
        print(f"⏱️ Query times: memory={t1 - t0:.2f}s, llm={t2 - t1:.2f}s, total={t2 - t0:.2f}s")
    except Exception as e:
        print("❌ LLM call failed:", e)
        reply = "I don't recall — was busy checking a place."

    # 8️⃣ Save reply into memory
    add_npc_memory(reply, "said", round_id)
    return reply
