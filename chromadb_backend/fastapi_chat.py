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

# Simple in‑memory buffer of recent messages per player for better style + context.
# In production you’d likely pull these from Chroma instead of RAM.
RECENT_MSG_LIMIT = 5
recent_history: dict[str, list[str]] = {}


def _update_recent_history(player_id: str, message: str) -> list[str]:
    """Keep a small rolling window of recent messages per player."""
    history = recent_history.get(player_id, [])
    history.append(message)
    if len(history) > RECENT_MSG_LIMIT:
        history = history[-RECENT_MSG_LIMIT:]
    recent_history[player_id] = history
    return history


@app.post("/chat")
def receive_message(
    player_id: str = Body(..., embed=True),
    message: str = Body(..., embed=True),
):
    """
    Main chat endpoint.

    - Stores the player message in Chroma.
    - Updates a short recent-history buffer (for better style mimicry).
    - Asks the NPC LLM for a reply (which may sometimes include a question).
    - Stores the NPC reply as memory.
    """
    timestamp = datetime.utcnow().isoformat()
    print(f"\n🧍 Player {player_id} at {timestamp}: {message}")

    # Store player message (with timestamp metadata)
    add_player_message(
        text=message,
        player_id=player_id,
        round_id="r1",
        location="Unknown",
        timestamp=timestamp,
    )

    # Update short-term recent message history for this player
    recent_msgs = _update_recent_history(player_id, message)

    # Generate NPC reply (LLM-based). Wrapped to avoid crashing the server.
    try:
        npc_reply = generate_npc_reply(
            player_text=message,
            round_id="r1",
            imitate_player_id=player_id,
            recent_msgs=recent_msgs,
        )
    except Exception as e:
        print("❌ generate_npc_reply failed:", e)
        npc_reply = "Sorry, I can't respond right now."

    # Store NPC reply
    add_npc_memory(
        npc_reply,
        "said",
        round_id="r1",
        timestamp=datetime.utcnow().isoformat(),
    )

    print(f"🤖 NPC: {npc_reply}")

    return {
        "player_id": player_id,
        "message": message,
        "npc_reply": npc_reply,
        "timestamp": timestamp,
    }


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)
