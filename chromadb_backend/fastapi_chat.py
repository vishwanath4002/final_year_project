# fastapi_chat.py - V2.0 FOLLOWING THE RULES EXACTLY

from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
from typing import Dict, List, Optional, Tuple
import time
import random
from datetime import datetime, timezone
from collections import deque
import uvicorn
from chromatesting import (
    generate_npc_reply,
    add_player_message_with_group,
    query_collection,
    player_messages,
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

# Per-player recent messages (for stylometry - max 20 messages)
recent_history: Dict[str, deque] = {}  # player_id -> deque of recent messages

# All active players
active_players: set[str] = set()

# Current groups from Unity
current_groups: Dict[str, Dict] = {}
last_group_update_time: float = 0.0

# Global summary for match events
global_message_buffer: deque = deque(maxlen=40)  # Last 40 messages from everyone
global_summary: str = "Match just started. Players are exploring."

# ========== CONVERSATION STATE ==========

class ConversationState:
    """Represents an active conversation between impostor and player(s)"""
    
    def __init__(self, group_id: str, disguise_player_id: str):
        self.group_id = group_id
        self.disguise_player_id = disguise_player_id
        
        # Conversation buffer (priority context - last 20 turns)
        self.buffer: deque = deque(maxlen=20)
        
        # Style summary (cached at conversation start, never changes)
        self.style_summary: Optional[str] = None
        
        # Timing
        self.started_at: float = time.time()
        self.last_activity: float = time.time()
        self.last_impostor_response: float = 0
        
        # Conversation state
        self.goodbye_detected: bool = False
        self.message_count = 0
        self.impostor_message_count = 0
        self.max_messages = 30
        self.idle_timeout = 90  # seconds
        
    def add_message(self, player_id: str, message: str, is_impostor: bool):
        """Add message to conversation buffer"""
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
            self.last_impostor_response = time.time()
    
    def get_buffer_text(self) -> str:
        """Format buffer as conversation text"""
        if not self.buffer:
            return "(conversation just started)"
        
        lines = []
        for msg in self.buffer:
            lines.append(f"{msg['player_id']}: {msg['message']}")
        return "\n".join(lines)
    
    def get_recent_player_messages(self) -> List[str]:
        """Get just player messages (not impostor) from buffer"""
        return [msg['message'] for msg in self.buffer if not msg['is_impostor']]
    
    def should_respond(self, last_msg_player_id: str) -> Tuple[bool, str]:
        """
        Determine if impostor should respond - SMART LOGIC
        
        Returns: (should_respond, reason)
        """
        # Don't respond to ourselves
        if last_msg_player_id == self.disguise_player_id:
            return False, "don't respond to self"
        
        # Check cooldown (minimum 5s between impostor messages)
        time_since_last = time.time() - self.last_impostor_response
        if self.last_impostor_response > 0 and time_since_last < 5.0:
            return False, f"cooldown {time_since_last:.1f}s/5s"
        
        # Count unique human players in conversation
        human_players = set()
        for msg in self.buffer:
            if not msg['is_impostor']:
                human_players.add(msg['player_id'])
        
        num_humans = len(human_players)
        
        # RULE: If 1-on-1, ALWAYS respond (100%)
        if num_humans == 1:
            return True, "1-on-1 conversation (100%)"
        
        # RULE: If 2 players, respond frequently (70%)
        if num_humans == 2:
            if random.random() < 0.7:
                return True, "2 players (70%)"
            else:
                return False, "2 players (30% skip)"
        
        # RULE: If 3+ players, respond moderately (40%)
        if random.random() < 0.4:
            return True, f"{num_humans} players (40%)"
        else:
            return False, f"{num_humans} players (60% skip)"
    
    def is_finished(self) -> Tuple[bool, str]:
        """Check if conversation should end"""
        
        if self.goodbye_detected:
            return True, "goodbye detected"
        
        if self.message_count >= self.max_messages:
            return True, f"max messages ({self.max_messages})"
        
        idle_time = time.time() - self.last_activity
        if idle_time > self.idle_timeout:
            return True, f"idle timeout ({idle_time:.1f}s)"
        
        if self.impostor_message_count >= 10:
            return True, f"impostor spoke enough ({self.impostor_message_count})"
        
        return False, "active"
    
    def get_duration(self) -> float:
        return time.time() - self.started_at

# Active conversations (one per group with impostor)
active_conversations: Dict[str, ConversationState] = {}

# ========== IMPOSTOR STATE ==========

class ImpostorState:
    def __init__(self):
        self.disguised_as: Optional[str] = None
        self.is_active: bool = False
        self.target_group_id: Optional[str] = None

    def reset(self):
        self.disguised_as = None
        self.is_active = False
        self.target_group_id = None

impostor = ImpostorState()

class ImpostorSpawnControl:
    def __init__(self):
        self.spawn_interval: float = 10.0
        self.last_spawn_time: float = time.time()
        self.min_group_size: int = 1

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

spawn_control = ImpostorSpawnControl()

# ========== HELPER FUNCTIONS ==========

def normalize_player_id(player_id: str) -> str:
    if not player_id:
        return ""
    return player_id.lower().replace(" ", "_")

def is_valid_player_id(pid) -> bool:
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

def update_player_history(player_id: str, message: str):
    """Update per-player recent message history (max 20)"""
    if player_id not in recent_history:
        recent_history[player_id] = deque(maxlen=20)
    recent_history[player_id].append(message)

def update_global_summary():
    """
    Update global summary based on last 40 messages
    Called every 5 messages
    """
    global global_summary
    
    if len(global_message_buffer) < 5:
        return  # Not enough data yet
    
    # Build summary of recent events
    recent_msgs = list(global_message_buffer)[-20:]  # Last 20 messages
    
    # Simple summary: extract key info
    players_mentioned = set()
    locations_mentioned = set()
    actions_mentioned = []
    
    VALID_LOCATIONS = ["Pavillion", "Church", "Mansion", "Greenhouse", "Sheds"]
    
    for msg in recent_msgs:
        pid = msg.get('player_id', '')
        text = msg.get('message', '').lower()
        
        if not pid.startswith('impostor_'):
            players_mentioned.add(pid)
        
        for loc in VALID_LOCATIONS:
            if loc.lower() in text:
                locations_mentioned.add(loc)
        
        # Extract actions
        if 'collected' in text or 'collecting' in text:
            actions_mentioned.append('collecting resources')
        if 'fight' in text or 'fighting' in text or 'killed' in text:
            actions_mentioned.append('fighting')
        if 'died' in text or 'dead' in text:
            actions_mentioned.append('player deaths')
    
    # Build summary
    parts = []
    if players_mentioned:
        parts.append(f"Active: {', '.join(list(players_mentioned)[:3])}")
    if locations_mentioned:
        parts.append(f"at {', '.join(list(locations_mentioned)[:2])}")
    if actions_mentioned:
        unique_actions = list(set(actions_mentioned))[:2]
        parts.append(f"doing {', '.join(unique_actions)}")
    
    if parts:
        global_summary = ". ".join(parts) + "."
    else:
        global_summary = "Players exploring the map."

def choose_target_group() -> Optional[Dict]:
    """Choose smallest group for impostor"""
    if len(current_groups) < 2:
        return None
    
    valid_groups = [
        g for g in current_groups.values()
        if g['size'] >= spawn_control.min_group_size
    ]
    
    if not valid_groups:
        return None
    
    # Prefer smallest groups
    smallest = min(valid_groups, key=lambda g: g['size'])
    return smallest

def choose_impostor_disguise(target_group_id: Optional[str] = None) -> Optional[str]:
    """
    Choose player from FARTHEST group (as per rules)
    """
    try:
        target_members = set()
        target_group_position = None
        
        if target_group_id and target_group_id in current_groups:
            raw_members = current_groups[target_group_id].get('player_ids', [])
            target_members = {normalize_player_id(pid) for pid in raw_members if is_valid_player_id(pid)}
            target_group_position = current_groups[target_group_id].get('center_position', [0, 0, 0])
        
        candidate_players_with_distance = []
        
        for gid, gdata in current_groups.items():
            if gid == target_group_id:
                continue
            
            group_position = gdata.get('center_position', [0, 0, 0])
            
            if target_group_position:
                distance = ((group_position[0] - target_group_position[0])**2 + 
                           (group_position[2] - target_group_position[2])**2) ** 0.5
            else:
                distance = 0
            
            for pid in gdata.get('player_ids', []):
                if not is_valid_player_id(pid):
                    continue
                
                normalized_pid = normalize_player_id(pid)
                if normalized_pid in target_members:
                    continue
                
                candidate_players_with_distance.append({
                    'player_id': pid,
                    'distance': distance,
                    'group_id': gid
                })
        
        if not candidate_players_with_distance:
            return "Player_Default"
        
        # Pick farthest
        candidate_players_with_distance.sort(key=lambda x: x['distance'], reverse=True)
        chosen_data = candidate_players_with_distance[0]
        
        return chosen_data['player_id']
        
    except Exception as e:
        print(f"❌ Error choosing disguise: {e}")
        return "Player_Default"

def generate_style_summary(player_id: str) -> str:
    """
    Generate style summary from player's recent 20 messages
    This is cached and only done ONCE per conversation
    """
    msgs = list(recent_history.get(player_id, []))
    
    if not msgs or len(msgs) < 3:
        return "Casual gamer. Short messages. Friendly tone."
    
    # Quick analysis
    avg_length = sum(len(m.split()) for m in msgs) / len(msgs)
    has_caps = any(m.isupper() or m[0].isupper() for m in msgs if m)
    has_punctuation = any('!' in m or '?' in m for m in msgs)
    
    style = []
    
    if avg_length < 5:
        style.append("Very brief messages (1-4 words)")
    elif avg_length < 10:
        style.append("Short casual messages (5-9 words)")
    else:
        style.append("Longer descriptive messages")
    
    if has_caps:
        style.append("Uses capitalization")
    if has_punctuation:
        style.append("Uses punctuation")
    
    return ". ".join(style) + "."

def generate_impostor_message(conv: ConversationState) -> Optional[str]:
    """
    Generate impostor reply following the rules:
    1. Style summary (cached)
    2. Global summary
    3. Conversation buffer (priority)
    4. Optional retrieved snippets (if needed)
    """
    
    print(f"\n{'─'*50}")
    print(f"🤖 GENERATING IMPOSTOR REPLY")
    print(f"{'─'*50}")
    
    # Cache style summary if not done yet
    if conv.style_summary is None:
        conv.style_summary = generate_style_summary(conv.disguise_player_id)
        print(f"   📝 Style: {conv.style_summary}")
    
    # Build prompt following rules structure
    game_context = """You are in a survival horror game.
Locations: Pavillion, Church, Mansion, Greenhouse, Sheds
Tasks: collecting mushrooms, collecting wood, fighting aliens, burning mushrooms"""
    
    conversation_text = conv.get_buffer_text()
    
    prompt = f"""{game_context}

STYLE: You are {conv.disguise_player_id}. {conv.style_summary}

RECENT MATCH EVENTS: {global_summary}

CONVERSATION:
{conversation_text}

Respond as {conv.disguise_player_id} (1-2 sentences, natural chat):"""
    
    print(f"   💬 Buffer size: {len(conv.buffer)} messages")
    print(f"   🌍 Global context: {global_summary[:60]}...")
    
    t0 = time.time()
    
    # Get recent messages for style imitation
    recent_msgs = list(recent_history.get(conv.disguise_player_id, []))
    
    reply = generate_npc_reply(
        player_text=prompt,
        round_id="r1",
        imitate_player_id=conv.disguise_player_id,
        recent_msgs=recent_msgs,
    )
    
    t1 = time.time()
    print(f"   ⏱️ Generated in {t1-t0:.2f}s")
    print(f"   💬 Reply: {reply}")
    print(f"{'─'*50}\n")
    
    return reply

def detect_goodbye(message: str) -> bool:
    goodbye_keywords = ["bye", "goodbye", "see ya", "later", "gotta go", "gtg", "brb", "afk"]
    msg_lower = message.lower()
    return any(keyword in msg_lower for keyword in goodbye_keywords)

# ========== API ENDPOINTS ==========

@app.post("/chat")
def receive_message(
    player_id: str = Body(..., embed=True),
    message: str = Body(..., embed=True),
    group_id: str = Body("solo", embed=True),
):
    """Handle incoming chat message"""
    
    player_id = player_id.strip()
    message = message.strip()
    group_id = group_id.strip() if group_id else "solo"
    timestamp = datetime.now(timezone.utc).isoformat()
    
    print(f"\n{'='*60}")
    print(f"💬 [{player_id}] in {group_id}: {message}")
    
    active_players.add(player_id)
    
    # Check if real player conflicts with impostor
    if impostor.is_active and impostor.disguised_as == player_id:
        print(f"⚠️ Real {player_id} appeared! Impostor compromised.")
        impostor.reset()
        if impostor.target_group_id in active_conversations:
            del active_conversations[impostor.target_group_id]
    
    # Store in database
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
        print(f"⚠️ DB store failed: {e}")
    
    # Update histories
    update_player_history(player_id, message)
    global_message_buffer.append({
        'player_id': player_id,
        'message': message,
        'timestamp': timestamp
    })
    
    # Update global summary every 5 messages
    if len(global_message_buffer) % 5 == 0:
        update_global_summary()
    
    response_data = {
        "player_id": player_id,
        "message": message,
        "timestamp": timestamp,
        "group_id": group_id,
        "impostor_message": None,
    }
    
    # Handle impostor conversation
    if impostor.is_active and impostor.target_group_id == group_id:
        conv = active_conversations.get(group_id)
        
        if not conv:
            # Create new conversation
            conv = ConversationState(group_id, impostor.disguised_as)
            active_conversations[group_id] = conv
            print(f"🆕 Started conversation in {group_id}")
        
        # Add player message to buffer
        conv.add_message(player_id, message, is_impostor=False)
        
        # Check for goodbye
        if detect_goodbye(message):
            conv.goodbye_detected = True
            print(f"👋 Goodbye detected")
        
        # Determine if impostor should respond
        should_respond, reason = conv.should_respond(player_id)
        
        print(f"🤔 Should respond? {should_respond} ({reason})")
        
        if should_respond:
            try:
                impostor_msg = generate_impostor_message(conv)
                
                if impostor_msg:
                    impostor_timestamp = datetime.now(timezone.utc).isoformat()
                    
                    # Store impostor message
                    add_player_message_with_group(
                        text=impostor_msg,
                        player_id=f"impostor_{impostor.disguised_as}",
                        round_id="r1",
                        group_id=group_id,
                        location="Unknown",
                        timestamp=impostor_timestamp,
                    )
                    
                    # Add to buffer
                    conv.add_message(impostor.disguised_as, impostor_msg, is_impostor=True)
                    
                    # Add to global buffer
                    global_message_buffer.append({
                        'player_id': f"impostor_{impostor.disguised_as}",
                        'message': impostor_msg,
                        'timestamp': impostor_timestamp
                    })
                    
                    response_data["impostor_message"] = {
                        "player_id": impostor.disguised_as,  # Display as real player
                        "message": impostor_msg,
                        "timestamp": impostor_timestamp,
                    }
                    
                    print(f"✅ Impostor replied as {impostor.disguised_as}")
            
            except Exception as e:
                print(f"❌ Impostor reply failed: {e}")
                import traceback
                traceback.print_exc()
        
        # Check if conversation should end
        finished, finish_reason = conv.is_finished()
        if finished:
            print(f"🏁 Conversation ended: {finish_reason}")
            del active_conversations[group_id]
            impostor.reset()
    
    print(f"{'='*60}\n")
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
    return {'success': True, 'groups_received': len(current_groups)}

@app.get("/impostor/check_spawn")
def check_impostor_spawn():
    """Check if impostor should spawn"""
    
    # Check if should despawn
    if impostor.is_active and impostor.target_group_id:
        conv = active_conversations.get(impostor.target_group_id)
        if conv:
            finished, reason = conv.is_finished()
            if finished:
                return {
                    'should_spawn': False,
                    'should_despawn': True,
                    'reason': f'Conversation ended: {reason}'
                }
        
        return {
            'should_spawn': False,
            'should_despawn': False,
            'reason': 'Impostor active'
        }
    
    # Check if can spawn
    if len(current_groups) < 2:
        return {
            'should_spawn': False,
            'should_despawn': False,
            'reason': f'Need 2+ groups (have {len(current_groups)})'
        }
    
    if spawn_control.should_spawn_now():
        target_group = choose_target_group()
        
        if target_group:
            disguise_player = choose_impostor_disguise(target_group['group_id'])
            
            spawn_control.record_spawn()
            
            return {
                'should_spawn': True,
                'should_despawn': False,
                'target_group_id': target_group['group_id'],
                'target_group_position': target_group['center_position'],
                'target_group_members': target_group['player_ids'],
                'disguise_as': disguise_player,
                'engagement_rate': 0.5,
                'conversation_duration': 60.0
            }
    
    return {
        'should_spawn': False,
        'should_despawn': False,
        'reason': 'Waiting for spawn interval'
    }

@app.post("/impostor/activate")
def activate_impostor(
    target_player_id: Optional[str] = Body(None),
    target_group_id: Optional[str] = Body(None),
    engagement_rate: float = Body(0.5),
):
    if not target_player_id:
        impostor.disguised_as = choose_impostor_disguise(target_group_id)
    else:
        impostor.disguised_as = target_player_id
    
    if not impostor.disguised_as:
        return {"success": False, "message": "No suitable disguise"}
    
    impostor.is_active = True
    impostor.target_group_id = target_group_id
    
    print(f"✅ Impostor activated as: {impostor.disguised_as} in {target_group_id}")
    
    return {
        "success": True,
        "disguised_as": impostor.disguised_as,
        "target_group_id": target_group_id,
    }

@app.post("/impostor/deactivate")
def deactivate_impostor():
    old_disguise = impostor.disguised_as
    impostor.reset()
    
    # Clear conversation
    if impostor.target_group_id in active_conversations:
        del active_conversations[impostor.target_group_id]
    
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
    global_message_buffer.clear()
    impostor.reset()
    
    print("🗑️ Session reset")
    
    return {"success": True, "message": "Session reset"}

@app.get("/")
def root():
    return {
        "status": "online",
        "message": "Impostor Chat Server - V2.0 (Following Rules)",
        "impostor_active": impostor.is_active,
        "tracked_groups": len(current_groups),
        "version": "2.0 - Smart response logic + conversation buffer"
    }

if __name__ == "__main__":
    print("🚀 Starting Impostor Chat Server V2.0...")
    print("📍 Port: 8000")
    print("\n✅ New Features:")
    print("   • 1-on-1: 100% response rate")
    print("   • 2 players: 70% response rate")
    print("   • 3+ players: 40% response rate")
    print("   • 5s minimum cooldown between impostor messages")
    print("   • Conversation buffer (last 20 messages)")
    print("   • Cached style summary (generated once)")
    print("   • Global match summary")
    print("   • Transparent debugging")
    print("\n✅ Server ready!\n")
    
    uvicorn.run(app, host="0.0.0.0", port=8000, log_level="warning")