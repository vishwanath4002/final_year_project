# fastapi_chat.py

from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
from datetime import datetime, timezone
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
        self.disguised_as: Optional[str] = None
        self.is_active: bool = False
        self.last_message_time: float = 0
        self.message_cooldown: float = 15.0
        self.conversation_engagement: float = 0.3

    def is_disguised_as_active_player(self) -> bool:
        """Check if impostor is disguised as a currently active player."""
        return self.disguised_as in active_players


impostor = ImpostorState()


def _update_recent_history(player_id: str, message: str) -> list[str]:
    """Keep a rolling window of recent messages per player."""
    history = recent_history.get(player_id, [])
    history.append(message)
    if len(history) > RECENT_MSG_LIMIT:
        history = history[-RECENT_MSG_LIMIT:]
    recent_history[player_id] = history
    return history



    """
    Choose a player to disguise as from Chroma memory.
    FIXED: Only picks inactive players who have chatted before.
    """
    try:
        all_players = set()

        # Get a sample of messages to find player IDs
        results = player_messages.get(limit=100)
        if results and results.get("metadatas"):
            for meta in results["metadatas"]:
                if meta and "player_id" in meta:
                    player_id = meta["player_id"]
                    # Skip any impostor messages or invalid IDs
                    if player_id and not player_id.startswith("Player_"):
                        all_players.add(player_id)

        print(f"🔍 Found historical players: {all_players}")
        print(f"🔍 Currently active players: {active_players}")
        
        # CRITICAL FIX: Filter out currently active players
        inactive_players = list(all_players - active_players)
        
        print(f"🔍 Available inactive players: {inactive_players}")
        
        if inactive_players:
            chosen = random.choice(inactive_players)
            print(f"🎭 Impostor disguising as: {chosen} (inactive)")
            return chosen

        # If no inactive players, use a unique fallback name
        # Generate a unique name that won't conflict
        fallback_names = [
            f"Player_Shadow_{random.randint(1000, 9999)}",
            f"Player_Ghost_{random.randint(1000, 9999)}",
            f"Player_Phantom_{random.randint(1000, 9999)}",
        ]
        chosen = random.choice(fallback_names)
        print(f"🎭 No inactive players found, using fallback: {chosen}")
        return chosen

    except Exception as e:
        print(f"❌ Error choosing disguise: {e}")
        return None

# REPLACE the existing choose_impostor_disguise() function in fastapi_chat.py with this:

def choose_impostor_disguise(target_group_id: Optional[str] = None) -> Optional[str]:
    """
    Choose a player to disguise as from Chroma memory.
    If target_group_id is provided, prefer players from DIFFERENT groups.
    FIXED: Only picks inactive players who have chatted before.
    """
    try:
        all_players = {}  # player_id -> most_recent_group_id

        # Get a sample of messages to find player IDs and their groups
        results = player_messages.get(limit=100)
        if results and results.get("metadatas"):
            for meta in results["metadatas"]:
                if meta and "player_id" in meta:
                    player_id = meta["player_id"]
                    
                    # Skip impostor messages or invalid IDs
                    if player_id.startswith("impostor_") or player_id.startswith("Player_"):
                        continue
                    
                    # Track player's most recent group
                    group_id = meta.get("group_id", "solo")
                    all_players[player_id] = group_id

        print(f"🔍 Found historical players by group: {all_players}")
        print(f"🔍 Currently active players: {active_players}")
        
        # CRITICAL FIX: Filter out currently active players
        inactive_players = {
            pid: gid for pid, gid in all_players.items() 
            if pid not in active_players
        }
        
        print(f"🔍 Available inactive players: {inactive_players}")
        
        # If target_group_id provided, prefer players from different groups
        if target_group_id and inactive_players:
            different_group_players = {
                pid: gid for pid, gid in inactive_players.items()
                if gid != target_group_id
            }
            
            if different_group_players:
                chosen = random.choice(list(different_group_players.keys()))
                chosen_group = different_group_players[chosen]
                print(f"🎭 Impostor disguising as: {chosen} (from group '{chosen_group}', target was '{target_group_id}')")
                return chosen
            else:
                print(f"⚠️ No inactive players from different group than '{target_group_id}'")
        
        # Fallback: any inactive player
        if inactive_players:
            chosen = random.choice(list(inactive_players.keys()))
            print(f"🎭 Impostor disguising as: {chosen} (inactive, any group)")
            return chosen

        # If no inactive players, use a unique fallback name
        fallback_names = [
            f"Player_Shadow_{random.randint(1000, 9999)}",
            f"Player_Ghost_{random.randint(1000, 9999)}",
            f"Player_Phantom_{random.randint(1000, 9999)}",
        ]
        chosen = random.choice(fallback_names)
        print(f"🎭 No inactive players found, using fallback: {chosen}")
        return chosen

    except Exception as e:
        print(f"❌ Error choosing disguise: {e}")
        return None


# UPDATE the /impostor/activate endpoint to pass target group:

@app.post("/impostor/activate")
def activate_impostor(
    target_player_id: Optional[str] = None,
    target_group_id: Optional[str] = None,  # NEW: Allow specifying target group
    engagement_rate: float = 0.3,
):
    """
    Activate the impostor AI.
    Can optionally specify which group to target and prefer disguises from other groups.
    FIXED: Validates target isn't active, resets if becomes active.
    """
    # Handle "string" default from Swagger UI
    if target_player_id and target_player_id.lower() in ["string", "null", ""]:
        target_player_id = None
    
    if target_group_id and target_group_id.lower() in ["string", "null", ""]:
        target_group_id = None
    
    # CRITICAL FIX: Don't allow disguising as active players
    if target_player_id:
        if target_player_id in active_players:
            return {
                "success": False,
                "message": f"{target_player_id} is currently active, cannot disguise as them",
                "active_players": list(active_players),
            }
        impostor.disguised_as = target_player_id
        print(f"🎭 Manual disguise selected: {target_player_id}")
    else:
        # NEW: Pass target_group_id to choose_impostor_disguise
        impostor.disguised_as = choose_impostor_disguise(target_group_id)

    if not impostor.disguised_as:
        return {
            "success": False,
            "message": "Could not find suitable player to disguise as",
        }
    
    # FINAL SAFETY CHECK: Verify chosen disguise isn't active
    if impostor.disguised_as in active_players:
        print(f"⚠️ Selected disguise {impostor.disguised_as} is active! Aborting.")
        impostor.disguised_as = None
        return {
            "success": False,
            "message": f"Cannot activate: chosen identity is currently active",
            "active_players": list(active_players),
        }

    impostor.is_active = True
    impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
    impostor.last_message_time = time.time()

    print(f"✅ Impostor activated, disguised as: {impostor.disguised_as}")
    print(f"   Target group: {target_group_id or 'any'}")
    print(f"   Engagement rate: {impostor.conversation_engagement}")
    print(f"   Active players at activation: {active_players}")

    return {
        "success": True,
        "disguised_as": impostor.disguised_as,
        "target_group": target_group_id,
        "engagement_rate": impostor.conversation_engagement,
        "active_players": list(active_players),
    }


def should_impostor_respond(recent_messages_count: int, last_msg_player_id: str) -> bool:
    """
    Decide if impostor should inject itself into conversation.
    FIXED: Don't respond if disguised as active player or responding to self.
    """
    if not impostor.is_active or not impostor.disguised_as:
        return False
    
    # CRITICAL FIX: Don't respond if disguised as an active player
    if impostor.is_disguised_as_active_player():
        print(f"⚠️ Impostor disguised as active player {impostor.disguised_as}, skipping response")
        return False
    
    # CRITICAL FIX: Don't respond to own messages
    if last_msg_player_id == impostor.disguised_as:
        print(f"⚠️ Impostor won't respond to its own message")
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
        base_chance += 0.2
    
    should_respond = random.random() < base_chance
    if should_respond:
        print(f"🎲 Impostor decided to respond (chance was {base_chance:.1%})")
    
    return should_respond


def generate_impostor_message(context_messages: list[dict]) -> str:
    """
    Generate a message from the impostor pretending to be the disguised player.
    FIXED: Filters out impostor's own messages from context.
    """
    if not impostor.disguised_as:
        return None

    # Get the disguised player's message history for style imitation
    # FILTER OUT impostor's own recent messages
    disguised_history = recent_history.get(impostor.disguised_as, [])

    # Build context from recent conversation
    # CRITICAL FIX: Exclude impostor's own messages from context
    context_text = ""
    if context_messages:
        recent_msgs = [
            msg for msg in context_messages[-5:]
            if msg['player_id'] != impostor.disguised_as  # Don't include own messages
        ]
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


# UPDATE the ChatPayload class at the top:

@app.post("/chat")
def receive_message(
    player_id: str = Body(..., embed=True),
    message: str = Body(..., embed=True),
    group_id: str = Body("solo", embed=True),  # NEW: Accept group info
):
    """
    Receives messages from players, stores them with group info, 
    and may inject impostor responses.
    """
    # NORMALIZE player ID to prevent duplicates like "p1" vs "player_1"
    player_id = player_id.strip()
    group_id = group_id.strip() if group_id else "solo"
    
    timestamp = datetime.now(timezone.utc).isoformat()
    print(f"\n💬 Player {player_id} in group '{group_id}' at {timestamp}: {message}")

    # Track active player
    active_players.add(player_id)
    
    # CRITICAL FIX: If impostor is disguised as this player, invalidate disguise
    if impostor.is_active and impostor.disguised_as == player_id:
        print(f" Real {player_id} is active! Impostor disguise compromised.")
        impostor.disguised_as = None
        impostor.is_active = False

    # Store player message WITH GROUP INFO
    try:
        # Import add_player_message_with_group from updated chromatesting
        from chromatesting import add_player_message_with_group
        add_player_message_with_group(
            text=message,
            player_id=player_id,
            round_id="r1",
            group_id=group_id,
            location="Unknown",
            timestamp=timestamp,
        )
    except ImportError:
        # Fallback to old method if not updated yet
        from chromatesting import add_player_message
        add_player_message(
            text=message,
            player_id=player_id,
            round_id="r1",
            location="Unknown",
            timestamp=timestamp,
        )
        print(" Using legacy message storage without group info")
    except Exception as e:
        print(f" Failed to store message in Chroma: {e}")

    # Update player's message history
    recent_msgs = _update_recent_history(player_id, message)

    # Standard response: acknowledge receipt
    response_data = {
        "player_id": player_id,
        "message": message,
        "timestamp": timestamp,
        "group_id": group_id,  # NEW: Include in response
        "impostor_message": None,
    }

    # Check if impostor should inject a message
    # PASS the last message sender to avoid self-response
    if should_impostor_respond(len(recent_history), player_id):
        # Get recent conversation context
        context_messages = []
        for pid, msgs in recent_history.items():
            for msg in msgs[-3:]:
                context_messages.append(
                    {
                        "player_id": pid,
                        "message": msg,
                    }
                )

        try:
            impostor_msg = generate_impostor_message(context_messages)
            if impostor_msg:
                impostor_timestamp = datetime.now(timezone.utc).isoformat()
                
                # Store impostor message WITH GROUP INFO
                try:
                    from chromatesting import add_player_message_with_group
                    add_player_message_with_group(
                        text=impostor_msg,
                        player_id=f"impostor_{impostor.disguised_as}",  # Special ID
                        round_id="r1",
                        group_id=group_id,  # Same group as conversation
                        location="Unknown",
                        timestamp=impostor_timestamp,
                    )
                except ImportError:
                    add_player_message(
                        text=impostor_msg,
                        player_id=impostor.disguised_as,
                        round_id="r1",
                        location="Unknown",
                        timestamp=impostor_timestamp,
                    )

                # Also store in NPC memory
                add_npc_memory(
                    impostor_msg,
                    "impostor_said",
                    round_id="r1",
                    timestamp=impostor_timestamp,
                )

                # Update impostor's message history
                _update_recent_history(impostor.disguised_as, impostor_msg)
                impostor.last_message_time = time.time()

                response_data["impostor_message"] = {
                    "player_id": impostor.disguised_as,
                    "message": impostor_msg,
                    "timestamp": impostor_timestamp,
                }

                print(f" Impostor as {impostor.disguised_as}: {impostor_msg}")
        except Exception as e:
            print(f" Impostor message generation failed: {e}")

    return response_data

@app.post("/impostor/activate")
def activate_impostor(
    target_player_id: Optional[str] = None,
    engagement_rate: float = 0.3,
):
    """
    Activate the impostor AI.
    FIXED: Validates target isn't active, resets if becomes active.
    """
    # Handle "string" default from Swagger UI
    if target_player_id and target_player_id.lower() in ["string", "null", ""]:
        target_player_id = None
    
    # CRITICAL FIX: Don't allow disguising as active players
    if target_player_id:
        if target_player_id in active_players:
            return {
                "success": False,
                "message": f"{target_player_id} is currently active, cannot disguise as them",
                "active_players": list(active_players),
            }
        impostor.disguised_as = target_player_id
        print(f"🎭 Manual disguise selected: {target_player_id}")
    else:
        impostor.disguised_as = choose_impostor_disguise()

    if not impostor.disguised_as:
        return {
            "success": False,
            "message": "Could not find suitable player to disguise as",
        }
    
    # FINAL SAFETY CHECK: Verify chosen disguise isn't active
    if impostor.disguised_as in active_players:
        print(f" Selected disguise {impostor.disguised_as} is active! Aborting.")
        impostor.disguised_as = None
        return {
            "success": False,
            "message": f"Cannot activate: chosen identity is currently active",
            "active_players": list(active_players),
        }

    impostor.is_active = True
    impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
    impostor.last_message_time = time.time()

    print(f" Impostor activated, disguised as: {impostor.disguised_as}")
    print(f"   Engagement rate: {impostor.conversation_engagement}")
    print(f"   Active players at activation: {active_players}")

    return {
        "success": True,
        "disguised_as": impostor.disguised_as,
        "engagement_rate": impostor.conversation_engagement,
        "active_players": list(active_players),
    }


@app.post("/impostor/deactivate")
def deactivate_impostor():
    """Deactivate the impostor AI."""
    impostor.is_active = False
    old_disguise = impostor.disguised_as
    impostor.disguised_as = None
    print(f" Impostor deactivated (was disguised as: {old_disguise})")
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
        "is_disguised_as_active": impostor.is_disguised_as_active_player(),
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
    """Reset the current session (clear active players and recent history)."""
    active_players.clear()
    recent_history.clear()  # ADDED: Clear recent message history too
    impostor.is_active = False
    impostor.disguised_as = None
    return {
        "success": True,
        "message": "Session reset complete (active players and history cleared)",
    }


# NEW ENDPOINT: Clear all ChromaDB data
@app.post("/database/clear")
def clear_database():
    """
    DANGER: Clears ALL stored messages and memories from ChromaDB.
    This action cannot be undone!
    """
    try:
        # Clear all collections
        player_messages.delete(where={})  # Delete all documents
        npc_memory.delete(where={})
        
        # Also reset session state
        active_players.clear()
        recent_history.clear()
        impostor.is_active = False
        impostor.disguised_as = None
        
        print("🗑️ Database cleared: All messages and memories deleted")
        
        return {
            "success": True,
            "message": "All ChromaDB data cleared successfully",
            "collections_cleared": ["player_messages", "npc_memory"],
        }
    except Exception as e:
        print(f" Error clearing database: {e}")
        return {
            "success": False,
            "message": f"Failed to clear database: {str(e)}",
        }


# NEW ENDPOINT: Debug - see what's in the database
@app.get("/database/inspect")
def inspect_database():
    """
    Debug endpoint to see what player IDs are stored in ChromaDB.
    """
    try:
        results = player_messages.get(limit=200)
        
        player_ids = set()
        message_count = {}
        
        if results and results.get("metadatas"):
            for meta in results["metadatas"]:
                if meta and "player_id" in meta:
                    pid = meta["player_id"]
                    player_ids.add(pid)
                    message_count[pid] = message_count.get(pid, 0) + 1
        
        return {
            "total_messages": len(results.get("ids", [])),
            "unique_player_ids": sorted(list(player_ids)),
            "message_count_per_player": message_count,
            "currently_active": list(active_players),
            "recent_history_players": list(recent_history.keys()),
        }
    except Exception as e:
        return {
            "error": str(e),
        }


@app.get("/")
def root():
    """Health check endpoint."""
    return {
        "status": "online",
        "message": "Impostor Chat Server is running",
        "impostor_active": impostor.is_active,
        "ollama_connection": " Check if Ollama is running on port 11434",
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
    print("  POST /database/clear - ⚠️ CLEAR ALL DATA")
    print("  GET  /database/inspect - 🔍 DEBUG: See stored player IDs")
    print("  GET  / - Health check")
    print("\n⚠️  Make sure Unity is connecting to http://172.16.30.250:8000/chat")
    print("⚠️  Make sure Ollama is running: ollama serve")

    uvicorn.run(app, host="0.0.0.0", port=8000)