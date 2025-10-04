# fastapi_chat.py
from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
import uvicorn

app = FastAPI()

# Allow Unity (localhost:5173 or Unity Editor) to connect
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # you can later restrict this to Unity build IP
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.post("/chat")
def receive_message(player_id: str = Body(...), message: str = Body(...)):
    print(f"🧍 Player {player_id}: {message}")
    
    # For now, send back a dummy NPC reply
    npc_reply = f"NPC: I heard you, {player_id}! You said '{message}'"
    print("🤖", npc_reply)

    return {"reply": npc_reply}

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)
