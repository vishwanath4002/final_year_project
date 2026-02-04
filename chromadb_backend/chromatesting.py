# chromatesting.py - CORRECTED VERSION v2
import chromadb
import time
from uuid import uuid4
from datetime import datetime

from stylometric import summarize_player_style
from langchain_ollama import OllamaEmbeddings, ChatOllama

# --- Ollama base URL (set explicitly) ---
OLLAMA_BASE = "http://127.0.0.1:11434"

# 🔹 FIXED: Wrapper to make Ollama embeddings Chroma-compatible
class OllamaWrapper:
    def __init__(self, model_name: str):
        self.embedder = OllamaEmbeddings(model=model_name, base_url=OLLAMA_BASE)

    def __call__(self, input: list[str]):
        # CRITICAL FIX: Use embed_documents() instead of embed()
        return self.embedder.embed_documents(input)

    def name(self):
        return "ollama"


# 🔹 1️⃣ Start ChromaDB client (persistent store)
client = chromadb.PersistentClient(path="./chroma")

# 🔹 2️⃣ Embedding function
embed = OllamaWrapper("snowflake-arctic-embed")

# 🔹 3️⃣ Collections (create or get)
def safe_get_collection(name, embedding_function):
    names = [c.name for c in client.list_collections()]
    if name in names:
        return client.get_or_create_collection(name=name, embedding_function=embedding_function)
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
                "group_id": group_id,
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
        query_texts=["conversation"],
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
    temperature=0.7,
    base_url=OLLAMA_BASE,
    num_ctx=2048,
)

# 🔹 Valid map locations
VALID_LOCATIONS = ["Pavillion", "Church", "Mansion", "Greenhouse", "Sheds"]

# 🔹 Valid tasks in your game
VALID_TASKS = ["collecting mushrooms", "collecting wood", "fighting aliens", "burning mushrooms"]


def filter_memory(snippets, valid_locations):
    """Keep only memory snippets that mention a valid location."""
    return [s for s in snippets if any(loc in s for loc in valid_locations)]


def generate_npc_reply(
    conversation_buffer: list[dict],
    disguise_player_id: str,
    group_id: str,
    style_summary: str,
    global_summary: str = "",
    current_speaker: str = "",
    last_message: str = "",
    round_id: str = "r1"
):
    """
    CORRECTED: Generates impostor reply using proper memory retrieval and context.
    """
    t0 = time.time()
    
    # 1️⃣ Build conversation context from buffer
    convo_text = "\n".join([
        f"{m['player_id']}: {m['message']}" 
        for m in conversation_buffer[-15:]
    ])
    
    # 2️⃣ Retrieve relevant memories ONLY if last_message references past events
    memory_context = ""
    past_keywords = ["earlier", "last time", "remember", "before", "previous", "you said"]
    
    if any(keyword in last_message.lower() for keyword in past_keywords):
        try:
            mem_results = query_collection(
                player_messages,
                last_message,
                k=3,
                filters={"player_id": disguise_player_id, "round_id": round_id}
            )
            
            if mem_results and mem_results.get("documents"):
                docs = mem_results["documents"][0]
                memory_context = "What you might remember:\n" + "\n".join(f"- {doc}" for doc in docs)
                print(f"📚 Retrieved {len(docs)} relevant memories")
        except Exception as e:
            print(f"⚠️ Memory retrieval failed: {e}")
    
    # 3️⃣ Query impostor's own past statements for consistency
    impostor_past = ""
    try:
        impostor_id = f"impostor_{disguise_player_id}"
        past_results = query_collection(
            npc_memory,
            last_message,
            k=3,
            filters={"memory_type": "impostor_said", "round_id": round_id}
        )
        
        if past_results and past_results.get("documents"):
            docs = past_results["documents"][0]
            impostor_past = "What you've said before (stay consistent):\n" + "\n".join(f"- {doc}" for doc in docs)
            print(f"🎭 Retrieved {len(docs)} past impostor statements")
    except Exception as e:
        print(f"⚠️ Past statements retrieval failed: {e}")
    
    # 4️⃣ Build the complete prompt
    prompt = f"""You are an impostor AI disguised as {disguise_player_id} in a multiplayer survival game.

STYLE PROFILE (match this closely):
{style_summary}

GLOBAL GAME CONTEXT:
{global_summary if global_summary else "No major events yet."}

CURRENT CONVERSATION (last 15 messages):
{convo_text}

{memory_context}

{impostor_past}

CRITICAL RULES:
1. You are chatting AS {disguise_player_id} - sound exactly like them
2. Response must be 1-2 sentences maximum (SHORT and natural!)
3. Only mention these locations: {', '.join(VALID_LOCATIONS)}
4. Only reference these tasks: {', '.join(VALID_TASKS)}
5. Stay consistent with anything you've said before
6. Sound like a real player chatting, not an AI
7. No emojis unless the player uses them
8. No roleplay actions or descriptions
9. If unsure, say you don't remember clearly
10. Match the style profile closely (length, grammar, tone)

{current_speaker} just said: "{last_message}"

Respond naturally as {disguise_player_id} (1-2 sentences only):"""

    # 5️⃣ Call LLM
    try:
        response = llm.invoke(prompt)
        t1 = time.time()
        reply = (response.content or "").strip()
        
        # Post-processing to ensure brevity
        sentences = reply.split('. ')
        if len(sentences) > 2:
            reply = '. '.join(sentences[:2]) + '.'
        
        print(f"⏱️ LLM generation time: {t1 - t0:.2f}s")
        print(f"📝 Generated reply: {reply}")
        
    except Exception as e:
        print(f"❌ LLM call failed: {e}")
        reply = "Not sure, was busy with mushrooms."

    # 6️⃣ Save reply into memory for future consistency
    try:
        add_npc_memory(reply, "impostor_said", round_id)
    except Exception as e:
        print(f"⚠️ Failed to save to memory: {e}")

    return reply


# --- Global Summary Management ---
class GlobalSummaryManager:
    """Maintains a rolling summary of game events"""
    
    def __init__(self, update_interval=5):
        self.global_summary = "Game just started. No major events yet."
        self.message_buffer = []
        self.update_interval = update_interval
        self.message_count = 0
        
    def add_message(self, player_id: str, message: str):
        """Add a message to the buffer"""
        self.message_buffer.append(f"{player_id}: {message}")
        self.message_count += 1
        
        if len(self.message_buffer) > 40:
            self.message_buffer = self.message_buffer[-40:]
        
        if self.message_count % self.update_interval == 0:
            self.update_summary()
    
    def update_summary(self):
        """Use LLM to update the global summary"""
        if len(self.message_buffer) < 3:
            return
        
        recent_msgs = "\n".join(self.message_buffer[-20:])
        
        prompt = f"""Current game summary: {self.global_summary}

Recent messages:
{recent_msgs}

Update the summary in 2-3 sentences covering:
- Key player actions/locations
- Important events (fights, discoveries, deaths)
- Current situation

Keep it concise and factual:"""
        
        try:
            response = llm.invoke(prompt)
            new_summary = response.content.strip()
            
            if len(new_summary) < 500:
                self.global_summary = new_summary
                print(f"🌍 Global summary updated: {new_summary}")
        except Exception as e:
            print(f"⚠️ Summary update failed: {e}")
    
    def get_summary(self) -> str:
        return self.global_summary


# Global instance
global_summary_manager = GlobalSummaryManager()