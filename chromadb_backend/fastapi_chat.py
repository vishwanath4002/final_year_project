from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
from datetime import datetime

# Import NPC logic
from chromatesting import generate_npc_reply, add_player_message

app = FastAPI()

# Allow Unity (or browser) to connect
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # can restrict later
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.post("/chat")
def receive_message(
    player_id: str = Body(...),
    message: str = Body(...),
):
    """
    Receives a chat message from Unity, stores it with metadata,
    generates a real NPC reply using the Ollama LLM pipeline,
    and returns it to Unity.
    """
    timestamp = datetime.utcnow().isoformat()

    print(f"\n🧍 Player {player_id} at {timestamp}: {message}")

    # Store player message with metadata (timestamp)
    add_player_message(
        text=message,
        player_id=player_id,
        round_id="r1",
        location="Unknown"  # can keep placeholder for now
    )

    # Generate reply using the main NPC LLM
    try:
        npc_reply = generate_npc_reply(
            player_text=message,
            imitate_player_id=player_id,
            recent_msgs=[message]
        )
    except Exception as e:
        npc_reply = f"[Error generating reply: {e}]"
        print("❌ NPC generation failed:", e)
    
    # Print and return
    print(f"🤖 NPC at {timestamp}: {npc_reply}")
    return {
        "reply": npc_reply,
        "timestamp": timestamp,
        "npc_id": "Alien-01"
    }

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)
