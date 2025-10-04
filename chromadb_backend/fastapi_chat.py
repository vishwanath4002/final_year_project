# fastapi_chat.py
from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
from datetime import datetime

# import your NPC logic from chromatesting
from chromatesting import generate_npc_reply, add_player_message, add_npc_memory

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # dev: allow all; restrict in production
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.post("/chat")
def receive_message(player_id: str = Body(...), message: str = Body(...)):
    timestamp = datetime.utcnow().isoformat()
    print(f"\n🧍 Player {player_id} at {timestamp}: {message}")

    # Store player message (with timestamp metadata)
    add_player_message(text=message, player_id=player_id, round_id="r1", location="Unknown", timestamp=timestamp)

    # Generate NPC reply (LLM-based). It's wrapped to avoid crashing the server.
    try:
        npc_reply = generate_npc_reply(player_text=message, round_id="r1", imitate_player_id=player_id, recent_msgs=[message])
    except Exception as e:
        print("❌ generate_npc_reply failed:", e)
        npc_reply = "Sorry, I can't respond right now."

    # store npc reply
    add_npc_memory(npc_reply, "said", round_id="r1", timestamp=datetime.utcnow().isoformat())

    print(f"🤖 NPC: {npc_reply}")

    return {"player_id": player_id, "message": message, "npc_reply": npc_reply, "timestamp": timestamp}

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)
