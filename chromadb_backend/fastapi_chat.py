# fastapi_chat.py - COMPLETE VERSION with Unity endpoints

from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
from datetime import datetime, timezone
import random
import time
from typing import Optional, Dict, List, Tuple

from chromatesting import (
    generate_npc_reply,
    add_player_message_with_group,
    add_npc_memory,
    query_collection,
    player_messages,
    npc_memory,
    global_summary_manager,
)
from stylometric import summarize_player_style

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Track recent messages per player for style imitation
RECENT_MSG_LIMIT = 20
recent_history: Dict[str, List[str]] = {}

# Track active players
active_players: set[str] = set()

# Track current group states from Unity
current_groups: Dict[str, Dict] = {}  # group_id -> {members, position, size}


# ---- Conversation state ----
class ConversationState:
    def __init__(self, group_id: str, disguise_player_id: str):
        self.group_id = group_id
        self.disguise_player_id = disguise_player_id
        self.buffer: List[Dict] = []
        self.started_at = time.time()
        self.last_activity = time.time()
        self.goodbye_detected = False
        self.max_messages = 30
        
        # Cache style summary once per conversation
        self.style_summary_cached = False
        self.style_summary = ""

    def add_message(self, player_id: str, message: str, is_impostor: bool):
        self.buffer.append({
            "player_id": player_id,
            "message": message,
            "is_impostor": is_impostor,
            "timestamp": time.time(),
        })
        self.last_activity = time.time()

    def is_finished(self) -> bool:
        if self.goodbye_detected:
            return True
        if len(self.buffer) >= self.max_messages:
            return True
        if time.time() - self.last_activity > 90:
            return True
        return False
    
    def get_or_create_style_summary(self, recent_msgs: List[str]) -> str:
        """Cache style summary once per conversation"""
        if not self.style_summary_cached:
            try:
                self.style_summary = summarize_player_style(
                    self.disguise_player_id, 
                    recent_msgs
                )
                self.style_summary_cached = True
                print(f"✅ Cached style for {self.disguise_player_id}")
            except Exception as e:
                print(f"⚠️ Style caching failed: {e}")
                self.style_summary = "casual game chat, short responses"
                self.style_summary_cached = True
        
        return self.style_summary


active_conversations: Dict[str, ConversationState] = {}


# Impostor state
class ImpostorState:
    def __init__(self):
        self.disguised_as: Optional[str] = None
        self.is_active: bool = False
        self.last_message_time: float = 0
        self.message_cooldown: float = 15.0
        self.conversation_engagement: float = 0.3
        self.target_group_id: Optional[str] = None
        self.conversation_start_time: float = 0
        self.min_conversation_duration: float = 30.0  # At least 30 seconds
        self.max_conversation_duration: float = 120.0  # At most 2 minutes

    def is_disguised_as_active_player(self) -> bool:
        return self.disguised_as in active_players
    
    def should_end_conversation(self) -> bool:
        """Check if conversation should end based on time"""
        if not self.is_active:
            return False
        
        duration = time.time() - self.conversation_start_time
        
        # Must stay at least min duration
        if duration < self.min_conversation_duration:
            return False
        
        # Must leave after max duration
        if duration > self.max_conversation_duration:
            return True
        
        return False


impostor = ImpostorState()


def _update_recent_history(player_id: str, message: str) -> List[str]:
    """Keep rolling window of recent messages per player"""
    history = recent_history.get(player_id, [])
    history.append(message)
    if len(history) > RECENT_MSG_LIMIT:
        history = history[-RECENT_MSG_LIMIT:]
    recent_history[player_id] = history
    return history


def choose_impostor_disguise(target_group_id: Optional[str] = None) -> Optional[str]:
    """Choose which player to disguise as based on groups"""
    try:
        if not current_groups:
            print("📝 No group data available")
            return None
        
        # If we have a target group, find farthest group from it
        if target_group_id and target_group_id in current_groups:
            target_pos = current_groups[target_group_id]["center_position"]
            
            farthest_group_id = None
            farthest_distance = -1
            
            for gid, group_data in current_groups.items():
                if gid == target_group_id:
                    continue
                
                other_pos = group_data["center_position"]
                dist = sum((a - b) ** 2 for a, b in zip(target_pos, other_pos)) ** 0.5
                
                if dist > farthest_distance:
                    farthest_distance = dist
                    farthest_group_id = gid
            
            if farthest_group_id:
                members = current_groups[farthest_group_id]["player_ids"]
                inactive_members = [m for m in members if m not in active_players]
                
                if inactive_members:
                    chosen = random.choice(inactive_members)
                    print(f"🎭 Disguise from farthest group: {chosen}")
                    return chosen
        
        # Fallback: any inactive player
        all_players = set()
        for group_data in current_groups.values():
            all_players.update(group_data["player_ids"])
        
        inactive = list(all_players - active_players)
        if inactive:
            chosen = random.choice(inactive)
            print(f"🎭 Fallback disguise: {chosen}")
            return chosen
        
        return None
    
    except Exception as e:
        print(f"❌ Error choosing disguise: {e}")
        return None


def _get_or_create_conversation(group_id: str, disguise_player_id: str) -> ConversationState:
    """Get or create conversation state"""
    conv = active_conversations.get(group_id)
    if conv and conv.disguise_player_id != disguise_player_id:
        print(f"⚠️ Different disguise for group {group_id}, resetting")
        conv = None

    if not conv:
        conv = ConversationState(group_id=group_id, disguise_player_id=disguise_player_id)
        active_conversations[group_id] = conv
        print(f"💬 New conversation for group {group_id} as {disguise_player_id}")
    
    return conv


def _detect_goodbye(message: str) -> bool:
    """Detect goodbye messages"""
    msg = message.lower()
    goodbye_keywords = ["bye", "gtg", "got to go", "see you", "seeya", "good night", "goodbye", "gotta run", "later"]
    return any(k in msg for k in goodbye_keywords)


def should_impostor_respond(recent_messages_count: int, last_msg_player_id: str) -> bool:
    """Decide if impostor should respond"""
    if not impostor.is_active or not impostor.disguised_as:
        return False

    if impostor.is_disguised_as_active_player():
        print(f"⚠️ Disguised as active player {impostor.disguised_as}, skipping")
        return False

    if last_msg_player_id == impostor.disguised_as:
        print("⚠️ Won't respond to own message")
        return False

    time_since_last = time.time() - impostor.last_message_time
    if time_since_last < impostor.message_cooldown:
        return False

    base_chance = impostor.conversation_engagement
    if recent_messages_count > 2:
        base_chance += 0.2

    should_respond = random.random() < base_chance
    if should_respond:
        print(f"🎲 Impostor responding (chance: {base_chance:.1%})")
    
    return should_respond


def generate_impostor_message(
    current_speaker: str,
    last_message: str,
    group_id: str
) -> Optional[str]:
    """Generate impostor message using proper memory"""
    if not impostor.disguised_as or not impostor.target_group_id:
        return None

    disguise_player_id = impostor.disguised_as
    conv = _get_or_create_conversation(group_id, disguise_player_id)

    # Get cached style summary
    recent_msgs = recent_history.get(disguise_player_id, [])
    style_summary = conv.get_or_create_style_summary(recent_msgs)

    # Get global summary
    global_summary = global_summary_manager.get_summary()

    # Generate reply
    reply = generate_npc_reply(
        conversation_buffer=conv.buffer,
        disguise_player_id=disguise_player_id,
        group_id=group_id,
        style_summary=style_summary,
        global_summary=global_summary,
        current_speaker=current_speaker,
        last_message=last_message,
        round_id="r1"
    )

    return reply


@app.post("/chat")
def receive_message(
    player_id: str = Body(..., embed=True),
    message: str = Body(..., embed=True),
    group_id: str = Body("solo", embed=True),
):
    """Receive player messages"""
    player_id = player_id.strip()
    group_id = group_id.strip() if group_id else "solo"
    timestamp = datetime.now(timezone.utc).isoformat()

    print(f"\n💬 {player_id} in group '{group_id}': {message}")

    active_players.add(player_id)
    global_summary_manager.add_message(player_id, message)

    # Check if real player conflicts with impostor
    if impostor.is_active and impostor.disguised_as == player_id:
        print(f"⚠️ Real {player_id} active! Impostor compromised.")
        impostor.disguised_as = None
        impostor.is_active = False
        impostor.target_group_id = None

    # Store message
    try:
        add_player_message_with_group(
            text=message,
            player_id=player_id,
            round_id="r1",
            group_id=group_id,
            location="Unknown",
            timestamp=timestamp,
        )
    except Exception as e:
        print(f"⚠️ Failed to store message: {e}")

    _update_recent_history(player_id, message)

    # Update conversation buffer
    if impostor.is_active and impostor.target_group_id == group_id and impostor.disguised_as:
        conv = _get_or_create_conversation(group_id, impostor.disguised_as)
        conv.add_message(player_id, message, is_impostor=False)
        if _detect_goodbye(message):
            conv.goodbye_detected = True
            print(f"👋 Goodbye detected in group {group_id}")

    response_data = {
        "player_id": player_id,
        "message": message,
        "timestamp": timestamp,
        "group_id": group_id,
        "impostor_message": None,
    }

    # Impostor response
    if (
        impostor.is_active
        and impostor.target_group_id == group_id
        and should_impostor_respond(len(recent_history), player_id)
    ):
        try:
            impostor_msg = generate_impostor_message(player_id, message, group_id)
            
            if impostor_msg:
                impostor_timestamp = datetime.now(timezone.utc).isoformat()
                impostor_player_id = f"impostor_{impostor.disguised_as}"

                add_player_message_with_group(
                    text=impostor_msg,
                    player_id=impostor_player_id,
                    round_id="r1",
                    group_id=group_id,
                    location="Unknown",
                    timestamp=impostor_timestamp,
                )

                add_npc_memory(
                    impostor_msg,
                    "impostor_said",
                    round_id="r1",
                    timestamp=impostor_timestamp,
                )

                conv = _get_or_create_conversation(group_id, impostor.disguised_as)
                conv.add_message(impostor_player_id, impostor_msg, is_impostor=True)
                global_summary_manager.add_message(impostor.disguised_as, impostor_msg)
                impostor.last_message_time = time.time()

                response_data["impostor_message"] = {
                    "player_id": impostor_player_id,
                    "message": impostor_msg,
                    "timestamp": impostor_timestamp,
                }

                print(f"🎭 Impostor as {impostor.disguised_as}: {impostor_msg}")

                # Check if conversation should end
                if conv.is_finished():
                    print(f"👋 Conversation finished for group {group_id}")
                    del active_conversations[group_id]
                    impostor.is_active = False
                    impostor.disguised_as = None
                    impostor.target_group_id = None
        
        except Exception as e:
            print(f"❌ Impostor message failed: {e}")
            import traceback
            traceback.print_exc()

    return response_data


# NEW: Endpoint for Unity to sync groups
@app.post("/groups/sync")
def sync_groups(
    groups: List[Dict] = Body(..., embed=True),
    timestamp: str = Body(..., embed=True)
):
    """Sync group data from Unity"""
    global current_groups
    
    current_groups.clear()
    
    for group_data in groups:
        group_id = group_data.get("group_id")
        if group_id:
            current_groups[group_id] = {
                "player_ids": group_data.get("player_ids", []),
                "center_position": group_data.get("center_position", [0, 0, 0]),
                "size": group_data.get("size", 0)
            }
    
    print(f"📊 Groups synced: {len(current_groups)} groups")
    
    return {"success": True, "groups_received": len(current_groups)}


# NEW: Endpoint for Unity to check if impostor should spawn
@app.get("/impostor/check_spawn")
def check_impostor_spawn():
    """Check if impostor should spawn or despawn"""
    
    # Should despawn if conversation time exceeded or finished
    if impostor.is_active:
        should_despawn = impostor.should_end_conversation()
        
        if should_despawn:
            duration = time.time() - impostor.conversation_start_time
            return {
                "should_spawn": False,
                "should_despawn": True,
                "reason": f"Conversation duration exceeded ({duration:.0f}s)",
                "target_group_id": impostor.target_group_id,
                "disguise_as": impostor.disguised_as,
                "conversation_duration": duration
            }
    
    # Should spawn if there are groups and impostor is not active
    if not impostor.is_active and len(current_groups) > 1:
        # Find a suitable target group (prefer smaller groups)
        target_group = None
        target_group_id = None
        
        for gid, group_data in current_groups.items():
            if group_data["size"] >= 2:  # Only spawn for groups of 2+
                if target_group is None or group_data["size"] < target_group["size"]:
                    target_group = group_data
                    target_group_id = gid
        
        if target_group:
            # Choose disguise
            disguise = choose_impostor_disguise(target_group_id)
            
            if disguise and disguise not in active_players:
                return {
                    "should_spawn": True,
                    "should_despawn": False,
                    "reason": "Suitable group found",
                    "target_group_id": target_group_id,
                    "target_group_position": target_group["center_position"],
                    "target_group_members": target_group["player_ids"],
                    "disguise_as": disguise,
                    "engagement_rate": 0.4
                }
    
    # Default: no action needed
    return {
        "should_spawn": False,
        "should_despawn": False,
        "reason": "No action needed",
        "current_groups": len(current_groups),
        "impostor_active": impostor.is_active
    }


@app.post("/impostor/activate")
def activate_impostor(
    target_player_id: Optional[str] = None,
    target_group_id: Optional[str] = None,
    engagement_rate: float = 0.3,
):
    """Activate impostor"""
    if target_player_id and target_player_id in active_players:
        return {
            "success": False,
            "message": f"{target_player_id} is active",
        }

    if target_player_id:
        impostor.disguised_as = target_player_id
    else:
        impostor.disguised_as = choose_impostor_disguise(target_group_id)

    if not impostor.disguised_as:
        return {"success": False, "message": "No suitable disguise"}

    impostor.is_active = True
    impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
    impostor.last_message_time = time.time()
    impostor.conversation_start_time = time.time()
    impostor.target_group_id = target_group_id

    if target_group_id and target_group_id in active_conversations:
        del active_conversations[target_group_id]

    print(f"✅ Impostor activated as: {impostor.disguised_as}")
    print(f"   Target group: {target_group_id}")

    return {
        "success": True,
        "disguised_as": impostor.disguised_as,
        "target_group": target_group_id,
        "engagement_rate": impostor.conversation_engagement,
    }


@app.post("/impostor/deactivate")
def deactivate_impostor():
    """Deactivate impostor"""
    group_id = impostor.target_group_id
    impostor.is_active = False
    old_disguise = impostor.disguised_as
    impostor.disguised_as = None
    if group_id and group_id in active_conversations:
        del active_conversations[group_id]
    impostor.target_group_id = None

    print(f"🛑 Impostor deactivated (was: {old_disguise})")
    return {"success": True, "message": f"Deactivated (was {old_disguise})"}


@app.get("/impostor/status")
def impostor_status():
    """Get impostor status"""
    return {
        "is_active": impostor.is_active,
        "disguised_as": impostor.disguised_as,
        "engagement_rate": impostor.conversation_engagement,
        "target_group_id": impostor.target_group_id,
        "active_conversations": list(active_conversations.keys()),
        "global_summary": global_summary_manager.get_summary(),
        "conversation_duration": time.time() - impostor.conversation_start_time if impostor.is_active else 0,
    }


@app.post("/session/reset")
def reset_session():
    """Reset session"""
    active_players.clear()
    recent_history.clear()
    active_conversations.clear()
    current_groups.clear()
    impostor.is_active = False
    impostor.disguised_as = None
    impostor.target_group_id = None
    global_summary_manager.global_summary = "Game just started."
    global_summary_manager.message_buffer.clear()
    global_summary_manager.message_count = 0
    return {"success": True, "message": "Session reset"}


@app.post("/database/clear")
def clear_database():
    """Clear ChromaDB"""
    try:
        player_messages.delete(where={})
        npc_memory.delete(where={})
        active_players.clear()
        recent_history.clear()
        active_conversations.clear()
        current_groups.clear()
        impostor.is_active = False
        impostor.disguised_as = None
        impostor.target_group_id = None
        global_summary_manager.global_summary = "Game just started."
        global_summary_manager.message_buffer.clear()
        global_summary_manager.message_count = 0
        print("🗑️ Database cleared")
        return {"success": True, "message": "All data cleared"}
    except Exception as e:
        return {"success": False, "message": f"Failed: {str(e)}"}


@app.get("/")
def root():
    """Health check"""
    return {
        "status": "online",
        "message": "Impostor Chat Server (COMPLETE VERSION)",
        "impostor_active": impostor.is_active,
        "current_groups": len(current_groups),
    }


if __name__ == "__main__":
    print("🚀 Starting COMPLETE Impostor Chat Server...")
    print("📝 Endpoints available:")
    print("  - POST /chat (player messages)")
    print("  - POST /groups/sync (Unity group sync)")
    print("  - GET  /impostor/check_spawn (Unity polling)")
    print("  - POST /impostor/activate")
    print("  - POST /impostor/deactivate")
    print("  - GET  /impostor/status")
    uvicorn.run(app, host="0.0.0.0", port=8000)