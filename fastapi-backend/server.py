from fastapi import FastAPI
from pydantic import BaseModel
from fastapi.middleware.cors import CORSMiddleware
import chromadb
from uuid import uuid4

app = FastAPI()
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"], allow_methods=["*"], allow_headers=["*"]
)

client = chromadb.PersistentClient(path="./chroma")
player_messages = client.get_or_create_collection(name="player_messages")
npc_memory = client.get_or_create_collection(name="npc_memory")

# Request schema for NPC replyuvicorn fastapi:app --reload
class NPCRequest(BaseModel):
    player_text: str
    round_id: str = "r1"
    imitate_player_id: str = "p1"

# Simple NPC reply logic (replace with your LLM call)

@app.post("/npc/reply")
def npc_reply(req: NPCRequest):
    # Query player messages for context
    result = player_messages.query(
        query_texts=[req.player_text],
        n_results=3,
        where={"round_id": req.round_id}
    )
    context = [doc for doc in result['documents'][0]]
    
    # Check last NPC memory for continuity
    last_npc = npc_memory.query(
        query_texts=[req.player_text],
        n_results=1,
        where={"round_id": req.round_id}
    )
    last_context = [doc for doc in last_npc['documents'][0]]
    
    # Build a simple placeholder reply (replace with your LLM call)
    reply_text = f"NPC imitating {req.imitate_player_id}: responding to '{req.player_text}'"
    
    # Save reply into NPC memory
    npc_memory.add(
        ids=[f"npc-{uuid4()}"],
        documents=[reply_text],
        metadatas=[{"memory_type": "said", "round_id": req.round_id}]
    )
    
    return {"text": reply_text}
