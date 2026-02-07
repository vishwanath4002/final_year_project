# fastapi_chat.py - COMPLETE VERSION WITH ALL CRITICAL FIXES APPLIED

from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
from typing import Dict, List, Optional, Tuple
import time
import random
from datetime import datetime, timezone
import uvicorn
from chromatesting import (
    generate_npc_reply,
    add_player_message_with_group,
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

# ========== GLOBAL STATE ==========

RECENT_MSG_LIMIT = 20
recent_history: Dict[str, List[str]] = {}
active_players: set[str] = set()
current_groups: Dict[str, Dict] = {}
last_group_update_time: float = 0.0

# ========== CONVERSATION STATE ==========

class ConversationState:
    def __init__(self, group_id: str):
        self.group_id = group_id
        self.buffer: List[Dict] = []
        self.started_at: float = time.time()
        self.last_activity: float = time.time()
        self.goodbye_detected: bool = False
        self.max_messages = 30
        self.message_count = 0
        self.impostor_message_count = 0

    def add_message(self, player_id: str, message: str, is_impostor: bool):
        self.buffer.append({
            "player_id": player_id,
            "message": message,
            "is_impostor": is_impostor,
            "timestamp": time.time(),
        })
        self.last_activity = time.time()
        self.message_count += 1
        if is_impostor:
            self.impostor_message_count += 1

    def is_finished(self) -> bool:
        if self.goodbye_detected:
            print(f"[ConvEnd] {self.group_id}: Goodbye detected")
            return True

        if self.message_count >= self.max_messages:
            print(f"[ConvEnd] {self.group_id}: Max messages reached ({self.max_messages})")
            return True

        idle_time = time.time() - self.last_activity
        if idle_time > 90:
            print(f"[ConvEnd] {self.group_id}: Idle timeout ({idle_time:.1f}s)")
            return True

        if self.impostor_message_count >= 8:
            print(f"[ConvEnd] {self.group_id}: Impostor has sent enough messages ({self.impostor_message_count})")
            return True

        return False

    def get_duration(self) -> float:
        return time.time() - self.started_at

active_conversations: Dict[str, ConversationState] = {}
stylometry_cache: Dict[Tuple[str, str], str] = {}

# ========== IMPOSTOR STATE ==========

class ImpostorState:
    def __init__(self):
        self.disguised_as: Optional[str] = None
        self.is_active: bool = False
        self.last_message_time: float = 0
        self.message_cooldown: float = 15.0
        self.conversation_engagement: float = 0.3
        self.target_group_id: Optional[str] = None
        self.has_sent_goodbye: bool = False

    def is_disguised_as_active_player(self) -> bool:
        return self.disguised_as in active_players

    def reset(self):
        self.disguised_as = None
        self.is_active = False
        self.target_group_id = None
        self.has_sent_goodbye = False

# ✅ FIX #1: CRITICAL SPAWN CONTROL FIXES
class ImpostorSpawnControl:
    def __init__(self):
        self.spawn_interval: float = 10.0  # ✅ Faster for testing (10s instead of 30s)
        self.last_spawn_time: float = time.time()  # ✅ Start from current time
        self.min_group_size: int = 1  # ✅ Allow solo players (need only 2 players total)
        self.max_spawn_distance: float = 100.0

    def should_spawn_now(self) -> bool:
        if impostor.is_active:
            return False

        elapsed = time.time() - self.last_spawn_time
        
        if elapsed < self.spawn_interval:
            return False

        valid_groups = [
            g for g in current_groups.values()
            if g.get('size', 0) >= self.min_group_size
        ]

        return len(valid_groups) > 0

    def record_spawn(self):
        self.last_spawn_time = time.time()

impostor = ImpostorState()
spawn_control = ImpostorSpawnControl()

# ========== HELPER FUNCTIONS ==========

def normalize_player_id(player_id: str) -> str:
    """Normalize player IDs to handle variations (case, spaces)"""
    if not player_id:
        return ""
    normalized = player_id.lower().replace(" ", "_")
    return normalized

def is_valid_player_id(pid) -> bool:
    """Validate player ID - reject numbers, empty strings, etc."""
    if not pid or not isinstance(pid, str):
        return False
    pid_str = str(pid).strip()
    if len(pid_str) < 2:
        return False
    if pid_str.isdigit():
        return False
    if pid_str.lower() in ["string", "null", "none", "0", ""]:
        return False
    if pid_str.startswith("impostor_"):
        return False
    return True

# ✅ FIX #3: IMPROVED choose_target_group WITH BETTER LOGGING
def choose_target_group() -> Optional[Dict]:
    """Choose target group for impostor - needs 2+ groups"""
    if len(current_groups) < 2:
        print(f"   ⚠️ choose_target_group: Need 2+ groups, have {len(current_groups)}")
        return None

    valid_groups = [
        g for g in current_groups.values()
        if g['size'] >= spawn_control.min_group_size
    ]

    if not valid_groups:
        print(f"   ⚠️ choose_target_group: No valid groups (min size: {spawn_control.min_group_size})")
        return None

    # Prefer smallest groups
    smallest = min(valid_groups, key=lambda g: g['size'])
    
    print(f"   ✅ Chose smallest group: {smallest['group_id']} with {smallest['size']} player(s)")
    
    return smallest

def choose_impostor_disguise(target_group_id: Optional[str] = None) -> Optional[str]:
    """Choose which player to disguise as from OTHER groups"""
    try:
        # Get target group members (normalized)
        target_members = set()
        if target_group_id and target_group_id in current_groups:
            raw_members = current_groups[target_group_id].get('player_ids', [])
            target_members = {normalize_player_id(pid) for pid in raw_members if is_valid_player_id(pid)}
            print(f"🎯 Target group '{target_group_id}' has members: {raw_members}")
        
        # Get players from OTHER groups
        candidate_players = []
        for gid, gdata in current_groups.items():
            if gid == target_group_id:
                continue
            
            for pid in gdata.get('player_ids', []):
                if not is_valid_player_id(pid):
                    continue
                
                normalized_pid = normalize_player_id(pid)
                
                if normalized_pid in target_members:
                    continue
                
                normalized_active = {normalize_player_id(p) for p in active_players}
                if normalized_pid in normalized_active:
                    continue
                
                candidate_players.append(pid)
        
        if candidate_players:
            chosen = random.choice(candidate_players)
            print(f"🎭 Impostor will disguise as: {chosen}")
            
            # Load chat history
            try:
                results = player_messages.get(limit=50, where={"player_id": chosen})
                if results and results.get("documents"):
                    if chosen not in recent_history:
                        recent_history[chosen] = []
                    for doc in results["documents"]:
                        if doc not in recent_history[chosen]:
                            recent_history[chosen].append(doc)
                    recent_history[chosen] = recent_history[chosen][-20:]
            except Exception as e:
                print(f"   ⚠️ Could not fetch chat history: {e}")
            
            return chosen
        
        print(f"⚠️ No valid players found, using default")
        return "Player_Default"
        
    except Exception as e:
        print(f"❌ Error choosing disguise: {e}")
        return "Player_Default"

def _get_or_create_conversation(group_id: str, disguise_player_id: str) -> ConversationState:
    conv = active_conversations.get(group_id)
    if not conv:
        conv = ConversationState(group_id)
        active_conversations[group_id] = conv
    return conv

def _update_recent_history(player_id: str, message: str):
    if player_id not in recent_history:
        recent_history[player_id] = []
    recent_history[player_id].append(message)
    recent_history[player_id] = recent_history[player_id][-RECENT_MSG_LIMIT:]

def _detect_goodbye(message: str) -> bool:
    goodbye_keywords = ["bye", "goodbye", "see ya", "later", "gotta go", "brb", "afk"]
    msg_lower = message.lower()
    return any(keyword in msg_lower for keyword in goodbye_keywords)

def should_impostor_respond(recent_messages_count: int, last_msg_player_id: str) -> bool:
    if not impostor.is_active or not impostor.disguised_as:
        return False

    if impostor.is_disguised_as_active_player():
        return False

    if last_msg_player_id == impostor.disguised_as:
        return False

    time_since_last = time.time() - impostor.last_message_time
    if time_since_last < impostor.message_cooldown:
        return False

    base_chance = impostor.conversation_engagement
    if recent_messages_count > 2:
        base_chance += 0.2

    return random.random() < base_chance

def generate_impostor_message(context_messages: List[Dict]) -> Optional[str]:
    """Generate impostor message"""
    if not impostor.disguised_as or not impostor.target_group_id:
        return None

    disguise_player_id = impostor.disguised_as
    group_id = impostor.target_group_id
    conv = _get_or_create_conversation(group_id, disguise_player_id)

    # Build conversation context
    convo_lines = []
    for m in conv.buffer[-20:]:
        speaker = m["player_id"]
        text = m["message"]
        convo_lines.append(f"{speaker}: {text}")
    
    convo_text = "\n".join(convo_lines) if convo_lines else "(conversation just started)"
    
    # Get style info
    msgs = recent_history.get(disguise_player_id, [])
    if msgs:
        style_summary = f"Chat history available: {len(msgs)} messages"
    else:
        style_summary = "Default gamer persona: casual, friendly, short messages"
    
    full_prompt = f"You are {disguise_player_id}. Style: {style_summary}\n\nConversation:\n{convo_text}\n\nRespond naturally (5-15 words):"
    
    reply = generate_npc_reply(
        player_text=full_prompt,
        round_id="r1",
        imitate_player_id=disguise_player_id,
        recent_msgs=recent_history.get(disguise_player_id, []),
    )
    
    return reply

# ========== API ENDPOINTS ==========

@app.post("/chat")
def receive_message(
    player_id: str = Body(..., embed=True),
    message: str = Body(..., embed=True),
    group_id: str = Body("solo", embed=True),
):
    player_id = player_id.strip()
    message = message.strip()
    group_id = group_id.strip() if group_id else "solo"
    timestamp = datetime.now(timezone.utc).isoformat()

    print(f"\n💬 {player_id} in '{group_id}': {message}")
    active_players.add(player_id)

    # Check if real player conflicts with impostor
    if impostor.is_active and impostor.disguised_as == player_id:
        print(f"⚠️ Real {player_id} is active! Impostor compromised.")
        impostor.reset()

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

    # Update conversation
    if impostor.is_active and impostor.target_group_id == group_id and impostor.disguised_as:
        conv = _get_or_create_conversation(group_id, impostor.disguised_as)
        conv.add_message(player_id, message, is_impostor=False)

        if _detect_goodbye(message):
            conv.goodbye_detected = True

    response_data = {
        "player_id": player_id,
        "message": message,
        "timestamp": timestamp,
        "group_id": group_id,
        "impostor_message": None,
    }

    # Impostor response
    if (impostor.is_active and impostor.target_group_id == group_id and 
        should_impostor_respond(len(recent_history), player_id)):
        try:
            impostor_msg = generate_impostor_message([])
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

                conv = _get_or_create_conversation(group_id, impostor.disguised_as)
                conv.add_message(impostor_player_id, impostor_msg, is_impostor=True)

                impostor.last_message_time = time.time()

                response_data["impostor_message"] = {
                    "player_id": impostor_player_id,
                    "message": impostor_msg,
                    "timestamp": impostor_timestamp,
                }

                print(f"🎭 Impostor as {impostor.disguised_as}: {impostor_msg}")

                if conv.is_finished():
                    print(f"👋 Conversation finished")
                    del active_conversations[group_id]
                    impostor.reset()

        except Exception as e:
            print(f"❌ Impostor message failed: {e}")

    return response_data

@app.post("/groups/sync")
def sync_groups(groups: List[Dict] = Body(..., embed=True), timestamp: str = Body(..., embed=True)):
    global current_groups, last_group_update_time

    current_groups.clear()

    for group_data in groups:
        group_id = group_data.get('group_id')
        if group_id:
            current_groups[group_id] = {
                'group_id': group_id,
                'player_ids': group_data.get('player_ids', []),
                'center_position': group_data.get('center_position', [0, 0, 0]),
                'size': group_data.get('size', 0)
            }

    last_group_update_time = time.time()

    print(f"\n📊 Groups synced: {len(current_groups)} groups")
    for gid, gdata in current_groups.items():
        print(f"  • {gid}: {gdata['size']} players at {gdata['center_position']}")

    return {'success': True, 'groups_received': len(current_groups)}

# ✅ FIX #2: ENHANCED check_impostor_spawn WITH DETAILED DEBUG LOGGING
@app.get("/impostor/check_spawn")
def check_impostor_spawn():
    """Check if impostor should spawn - ENHANCED LOGGING"""
    
    print(f"\n{'='*60}")
    print(f"🔍 CHECK_SPAWN DETAILED DEBUG")
    print(f"{'='*60}")
    print(f"   Current time: {time.time():.2f}")
    print(f"   Last spawn time: {spawn_control.last_spawn_time:.2f}")
    print(f"   Time since last spawn: {time.time() - spawn_control.last_spawn_time:.2f}s")
    print(f"   Spawn interval required: {spawn_control.spawn_interval}s")
    print(f"   Groups count: {len(current_groups)}")
    print(f"   Impostor active: {impostor.is_active}")
    
    # ✅ NEW: Show all current groups
    if current_groups:
        print(f"\n   📊 Current Groups:")
        for gid, gdata in current_groups.items():
            members = gdata.get('player_ids', [])
            size = gdata.get('size', 0)
            pos = gdata.get('center_position', [0, 0, 0])
            print(f"      • {gid}: {size} player(s) = {members}")
            print(f"        Position: ({pos[0]:.1f}, {pos[1]:.1f}, {pos[2]:.1f})")
    else:
        print(f"   ⚠️ NO GROUPS RECEIVED YET")
    print(f"")
    
    # Check if should despawn
    if impostor.is_active and impostor.target_group_id:
        conv = active_conversations.get(impostor.target_group_id)
        if conv and conv.is_finished():
            print(f"   → DECISION: DESPAWN (conversation finished)")
            print(f"{'='*60}\n")
            return {
                'should_spawn': False,
                'should_despawn': True,
                'reason': 'Conversation ended'
            }
        else:
            print(f"   → DECISION: No change (impostor still active)")
            print(f"{'='*60}\n")
            return {
                'should_spawn': False,
                'should_despawn': False,
                'reason': 'Impostor still active'
            }

    # Check if can spawn - NEED 2+ GROUPS
    if len(current_groups) < 2:
        print(f"   ❌ CANNOT SPAWN: Need 2+ groups, currently have {len(current_groups)}")
        print(f"   Tip: Separate players by >12 units to create multiple groups")
        print(f"{'='*60}\n")
        return {
            'should_spawn': False,
            'should_despawn': False,
            'reason': f'Need 2+ groups (current: {len(current_groups)})'
        }

    # ✅ NEW: Check valid groups
    valid_groups = [
        g for g in current_groups.values()
        if g.get('size', 0) >= spawn_control.min_group_size
    ]
    
    print(f"   Valid groups (size >= {spawn_control.min_group_size}): {len(valid_groups)}")
    
    if not valid_groups:
        print(f"   ❌ CANNOT SPAWN: No groups meet size requirement")
        print(f"{'='*60}\n")
        return {
            'should_spawn': False,
            'should_despawn': False,
            'reason': f'No groups with {spawn_control.min_group_size}+ players'
        }

    if spawn_control.should_spawn_now():
        target_group = choose_target_group()

        if target_group:
            disguise_player = choose_impostor_disguise(target_group['group_id'])

            print(f"   ✅✅✅ SPAWNING IMPOSTOR ✅✅✅")
            print(f"   Target Group: {target_group['group_id']}")
            print(f"   Group Size: {target_group['size']} players")
            print(f"   Group Members: {target_group['player_ids']}")
            print(f"   Disguise As: {disguise_player}")
            print(f"   Target Position: {target_group['center_position']}")
            print(f"{'='*60}\n")

            spawn_control.record_spawn()

            return {
                'should_spawn': True,
                'should_despawn': False,
                'target_group_id': target_group['group_id'],
                'target_group_position': target_group['center_position'],
                'target_group_members': target_group['player_ids'],
                'disguise_as': disguise_player,
                'engagement_rate': 0.4,
                'conversation_duration': 60.0
            }
    
    time_remaining = spawn_control.spawn_interval - (time.time() - spawn_control.last_spawn_time)
    print(f"   ⏳ Waiting for spawn interval ({time_remaining:.1f}s remaining)")
    print(f"{'='*60}\n")
    
    return {
        'should_spawn': False,
        'should_despawn': False,
        'reason': f'Waiting for spawn interval ({time_remaining:.1f}s remaining)'
    }

@app.post("/impostor/activate")
def activate_impostor(
    target_player_id: Optional[str] = Body(None),
    target_group_id: Optional[str] = Body(None),
    engagement_rate: float = Body(0.3),
):
    if target_player_id and target_player_id in active_players:
        return {"success": False, "message": f"{target_player_id} is active"}

    if not target_player_id:
        impostor.disguised_as = choose_impostor_disguise(target_group_id)
    else:
        impostor.disguised_as = target_player_id

    if not impostor.disguised_as:
        return {"success": False, "message": "No suitable disguise"}

    impostor.is_active = True
    impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
    impostor.last_message_time = time.time()
    impostor.target_group_id = target_group_id
    impostor.has_sent_goodbye = False

    print(f"✅ Impostor activated as: {impostor.disguised_as}")

    return {
        "success": True,
        "disguised_as": impostor.disguised_as,
        "target_group_id": target_group_id,
    }

@app.post("/impostor/deactivate")
def deactivate_impostor():
    old_disguise = impostor.disguised_as
    impostor.reset()

    print(f"🛑 Impostor deactivated (was: {old_disguise})")

    return {"success": True, "message": f"Deactivated (was {old_disguise})"}

@app.get("/impostor/status")
def impostor_status():
    return {
        "is_active": impostor.is_active,
        "disguised_as": impostor.disguised_as,
        "target_group_id": impostor.target_group_id,
        "active_conversations": list(active_conversations.keys()),
    }

@app.post("/session/reset")
def reset_session():
    active_players.clear()
    recent_history.clear()
    active_conversations.clear()
    stylometry_cache.clear()
    impostor.reset()

    print("🗑️ Session reset")

    return {"success": True, "message": "Session reset"}

@app.post("/database/clear")
def clear_database():
    try:
        player_messages.delete(where={})
        npc_memory.delete(where={})
        active_players.clear()
        recent_history.clear()
        active_conversations.clear()
        impostor.reset()

        print("🗑️ Database cleared")

        return {"success": True, "message": "Database cleared"}
    except Exception as e:
        return {"success": False, "message": str(e)}

@app.get("/")
def root():
    return {
        "status": "online",
        "message": "Impostor Chat Server - FIXED VERSION",
        "impostor_active": impostor.is_active,
        "tracked_groups": len(current_groups),
    }

if __name__ == "__main__":
    print("🚀 Starting Impostor Chat Server (FIXED VERSION)...")
    print("📍 Port: 8000")
    print("✅ Key Changes:")
    print("   • Spawn interval: 10s (was 30s)")
    print("   • Min group size: 1 player (was 2)")
    print("   • Enhanced debug logging")
    print("   • Better spawn timing")
    print("\n✅ Server ready!\n")
    
    uvicorn.run(app, host="0.0.0.0", port=8000, log_level="warning")
