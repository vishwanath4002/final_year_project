# fastapi_chat.py

from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
from datetime import datetime
import random
import time
from typing import Optional

from chromatesting import (
    generate_npc_reply,
    add_player_message,
    add_npc_memory,
    query_collection,
    player_messages,
    npc_memory,
)

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Track recent messages per player for style imitation
RECENT_MSG_LIMIT = 15
recent_history: dict[str, list[str]] = {}

# Track active players in current session
active_players: set[str] = set()


# Impostor state
class ImpostorState:
    def __init__(self):
        self.disguised_as: Optional[str] = None  # Which player ID impostor is imitating
        self.is_active: bool = False  # Whether impostor should be chatting
        self.last_message_time: float = 0
        self.message_cooldown: float = 15.0  # Min seconds between impostor messages
        self.conversation_engagement: float = 0.3  # Chance to engage in conversation


impostor = ImpostorState()


def _update_recent_history(player_id: str, message: str) -> list[str]:
    """Keep a rolling window of recent messages per player."""
    history = recent_history.get(player_id, [])
    history.append(message)
    if len(history) > RECENT_MSG_LIMIT:
        history = history[-RECENT_MSG_LIMIT:]
    recent_history[player_id] = history
    return history


def choose_impostor_disguise() -> Optional[str]:
    """
    Choose a player to disguise as from Chroma memory.
    Picks someone who has chatted but isn't currently active.
    """
    try:
        # Query all stored player messages to find who has chatted
        all_players = set()

        # Get a sample of messages to find player IDs
        results = player_messages.get(limit=100)
        if results and results.get("metadatas"):
            for meta in results["metadatas"]:
                if meta and "player_id" in meta:
                    all_players.add(meta["player_id"])

        # Filter out currently active players
        inactive_players = list(all_players - active_players)
        if inactive_players:
            chosen = random.choice(inactive_players)
            print(f"🎭 Impostor disguising as: {chosen}")
            return chosen

        # Fallback: create a generic impostor identity
        fallback_names = ["Player_Shadow", "Player_Ghost", "Player_Phantom", "Player_Wraith"]
        chosen = random.choice(fallback_names)
        print(f"🎭 No inactive players found, using fallback: {chosen}")
        return chosen

    except Exception as e:
        print(f"❌ Error choosing disguise: {e}")
        return None


def should_impostor_respond(recent_messages_count: int) -> bool:
    """
    Decide if impostor should inject itself into conversation.
    More likely to respond when there's active conversation.
    """
    if not impostor.is_active or not impostor.disguised_as:
        return False
    
    # Skip if disguised_as is invalid
    if impostor.disguised_as.lower() in ["string", "null", ""]:
        print("⚠️ Invalid impostor disguise, skipping response")
        return False

    # Check cooldown
    time_since_last = time.time() - impostor.last_message_time
    if time_since_last < impostor.message_cooldown:
        return False

    # Higher chance to respond during active conversation
    base_chance = impostor.conversation_engagement
    if recent_messages_count > 2:
        base_chance += 0.2  # More likely when people are chatting
    
    should_respond = random.random() < base_chance
    if should_respond:
        print(f"🎲 Impostor decided to respond (chance was {base_chance:.1%})")
    
    return should_respond


def generate_impostor_message(context_messages: list[dict]) -> str:
    """
    Generate a message from the impostor pretending to be the disguised player.
    Uses their chat history and recent conversation context.
    """
    if not impostor.disguised_as:
        return None

    # Get the disguised player's message history for style imitation
    disguised_history = recent_history.get(impostor.disguised_as, [])

    # Build context from recent conversation
    context_text = ""
    if context_messages:
        recent_msgs = context_messages[-5:]  # Last 5 messages
        context_text = "\n".join(
            f"{msg['player_id']}: {msg['message']}"
            for msg in recent_msgs
        )

    # Query relevant memories about what the disguised player might know
    memory_context = ""
    try:
        if context_text:
            mem_results = query_collection(
                player_messages,
                context_text,
                k=3,
                filters={"player_id": impostor.disguised_as},
            )
            if mem_results and mem_results.get("documents"):
                memory_context = "\n".join(mem_results["documents"][0])
    except Exception as e:
        print(f"⚠️ Memory query failed: {e}")

    # Generate response as the disguised player
    prompt_context = f"Recent conversation:\n{context_text}\n\n" if context_text else ""
    if memory_context:
        prompt_context += (
            f"What {impostor.disguised_as} might remember:\n{memory_context}\n\n"
        )

    prompt = (
        f"{prompt_context}"
        f"Now naturally join or comment on this conversation as {impostor.disguised_as}."
    )

    reply = generate_npc_reply(
        player_text=prompt,
        round_id="r1",
        imitate_player_id=impostor.disguised_as,
        recent_msgs=disguised_history,
    )

    return reply


@app.post("/chat")
def receive_message(
    player_id: str = Body(..., embed=True),
    message: str = Body(..., embed=True),
):
    """
    Receives messages from players, stores them, and may inject impostor responses.
    """
    timestamp = datetime.utcnow().isoformat()
    print(f"\n💬 Player {player_id} at {timestamp}: {message}")

    # Track active player
    active_players.add(player_id)

    # Store player message
    try:
        add_player_message(
            text=message,
            player_id=player_id,
            round_id="r1",
            location="Unknown",
            timestamp=timestamp,
        )
    except Exception as e:
        print(f"⚠️ Failed to store message in Chroma: {e}")

    # Update player's message history
    recent_msgs = _update_recent_history(player_id, message)

    # Standard response: acknowledge receipt
    response_data = {
        "player_id": player_id,
        "message": message,
        "timestamp": timestamp,
        "impostor_message": None,
    }

    # Check if impostor should inject a message
    if should_impostor_respond(len(recent_history)):
        # Get recent conversation context
        context_messages = []
        for pid, msgs in recent_history.items():
            for msg in msgs[-3:]:  # Last 3 from each player
                context_messages.append(
                    {
                        "player_id": pid,
                        "message": msg,
                    }
                )

        try:
            impostor_msg = generate_impostor_message(context_messages)
            if impostor_msg:
                # Store impostor message as if it came from disguised player
                add_player_message(
                    text=impostor_msg,
                    player_id=impostor.disguised_as,
                    round_id="r1",
                    location="Unknown",
                    timestamp=datetime.utcnow().isoformat(),
                )

                # Also store in NPC memory
                add_npc_memory(
                    impostor_msg,
                    "impostor_said",
                    round_id="r1",
                    timestamp=datetime.utcnow().isoformat(),
                )

                # Update impostor's message history
                _update_recent_history(impostor.disguised_as, impostor_msg)
                impostor.last_message_time = time.time()

                response_data["impostor_message"] = {
                    "player_id": impostor.disguised_as,
                    "message": impostor_msg,
                    "timestamp": datetime.utcnow().isoformat(),
                }

                print(f"🎭 Impostor as {impostor.disguised_as}: {impostor_msg}")
        except Exception as e:
            print(f"❌ Impostor message generation failed: {e}")

    return response_data


@app.post("/impostor/activate")
def activate_impostor(
    target_player_id: Optional[str] = None,
    engagement_rate: float = 0.3,
):
    """
    Activate the impostor AI.
    If target_player_id is provided, disguise as that player.
    Otherwise, choose automatically from inactive players.
    """
    # Handle "string" default from Swagger UI
    if target_player_id and target_player_id.lower() in ["string", "null", ""]:
        target_player_id = None
        
    if target_player_id:
        impostor.disguised_as = target_player_id
    else:
        impostor.disguised_as = choose_impostor_disguise()

    if not impostor.disguised_as:
        return {
            "success": False,
            "message": "Could not find suitable player to disguise as",
        }

    impostor.is_active = True
    impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
    impostor.last_message_time = time.time()

    print(f"✅ Impostor activated, disguised as: {impostor.disguised_as}")
    print(f"   Engagement rate: {impostor.conversation_engagement}")

    return {
        "success": True,
        "disguised_as": impostor.disguised_as,
        "engagement_rate": impostor.conversation_engagement,
    }


@app.post("/impostor/deactivate")
def deactivate_impostor():
    """Deactivate the impostor AI."""
    impostor.is_active = False
    old_disguise = impostor.disguised_as
    impostor.disguised_as = None
    print(f"🛑 Impostor deactivated (was disguised as: {old_disguise})")
    return {
        "success": True,
        "message": f"Impostor deactivated (was {old_disguise})",
    }


@app.get("/impostor/status")
def impostor_status():
    """Get current impostor status."""
    return {
        "is_active": impostor.is_active,
        "disguised_as": impostor.disguised_as,
        "engagement_rate": impostor.conversation_engagement,
        "cooldown_remaining": max(
            0, impostor.message_cooldown - (time.time() - impostor.last_message_time)
        ),
        "active_players": list(active_players),
        "available_disguises": list(set(recent_history.keys()) - active_players),
    }


@app.post("/impostor/settings")
def update_impostor_settings(
    message_cooldown: Optional[float] = None,
    engagement_rate: Optional[float] = None,
):
    """Update impostor behavior settings."""
    updated = {}
    if message_cooldown is not None:
        impostor.message_cooldown = max(5.0, message_cooldown)
        updated["message_cooldown"] = impostor.message_cooldown

    if engagement_rate is not None:
        impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
        updated["engagement_rate"] = impostor.conversation_engagement

    return {
        "success": True,
        "updated": updated,
    }


@app.get("/players/active")
def get_active_players():
    """Get list of currently active players."""
    return {
        "active_players": list(active_players),
        "count": len(active_players),
    }


@app.post("/session/reset")
def reset_session():
    """Reset the current session (clear active players)."""
    active_players.clear()
    impostor.is_active = False
    impostor.disguised_as = None
    return {
        "success": True,
        "message": "Session reset complete",
    }


@app.get("/")
def root():
    """Health check endpoint."""
    return {
        "status": "online",
        "message": "Impostor Chat Server is running",
        "impostor_active": impostor.is_active,
    }


if __name__ == "__main__":
    print("🚀 Starting Impostor Chat Server...")
    print("📍 Server URL: http://0.0.0.0:8000")
    print("🌐 Accessible at: http://172.16.30.250:8000 (or your local IP)")
    print("🔧 API Endpoints:")
    print("  POST /chat - Send player messages")
    print("  POST /impostor/activate - Activate impostor")
    print("  POST /impostor/deactivate - Deactivate impostor")
    print("  GET  /impostor/status - Check impostor status")
    print("  POST /impostor/settings - Update impostor settings")
    print("  GET  /players/active - List active players")
    print("  POST /session/reset - Reset session")
    print("  GET  / - Health check")
    print("\n⚠️  Make sure Unity is connecting to http://172.16.30.250:8000/chat")

    uvicorn.run(app, host="0.0.0.0", port=8000)