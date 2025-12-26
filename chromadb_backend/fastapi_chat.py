# fastapi_chat.py - COMPLETE VERSION WITH ALL FIXES
# Changes:
# 1. Active players tracked via Unity sync (not message sending)
# 2. Impostor CAN disguise as active players
# 3. Choose player BEFORE spawning
# 4. Default gamer persona for players without chat history
# 5. Conversation ends → despawn signal
# 6. Target group only (ignores other groups)
# 7. NEW: Only spawn impostor if there are at least 2 groups

from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
from datetime import datetime, timezone
import random
import time
from typing import Optional, Dict, List, Tuple

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

# ========== GLOBAL STATE ==========

RECENT_MSG_LIMIT = 20
recent_history: Dict[str, List[str]] = {}

# CHANGED: Active players now tracked via Unity sync (players currently in game)
active_players: set[str] = set()

current_groups: Dict[str, Dict] = {}
last_group_update_time: float = 0.0

# ========== CONVERSATION LIFECYCLE ==========

class ConversationState:
    def __init__(self, group_id: str, disguise_player_id: str):
        self.group_id = group_id
        self.disguise_player_id = disguise_player_id
        self.buffer: List[Dict] = []
        self.started_at = time.time()
        self.last_activity = time.time()
        self.goodbye_detected = False
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

    def reset(self):
        self.disguised_as = None
        self.is_active = False
        self.target_group_id = None
        self.has_sent_goodbye = False

impostor = ImpostorState()

# ========== IMPOSTOR SPAWN CONTROL ==========

class ImpostorSpawnControl:
    def __init__(self):
        self.spawn_interval: float = 30.0
        self.last_spawn_time: float = 0.0
        self.min_group_size: int = 1
        self.min_groups_required: int = 2  # NEW: Need at least 2 groups
        self.max_spawn_distance: float = 100.0

    def should_spawn_now(self) -> bool:
        if impostor.is_active:
            return False

        elapsed = time.time() - self.last_spawn_time
        if elapsed < self.spawn_interval:
            return False

        # NEW: Check if we have at least 2 groups
        if len(current_groups) < self.min_groups_required:
            return False

        valid_groups = [
            g for g in current_groups.values()
            if g.get('size', 0) >= self.min_group_size
        ]

        # Need at least 2 valid groups (one to target, one to disguise from)
        return len(valid_groups) >= self.min_groups_required

    def record_spawn(self):
        self.last_spawn_time = time.time()

spawn_control = ImpostorSpawnControl()

# ========== HELPER FUNCTIONS ==========

def _update_recent_history(player_id: str, message: str) -> List[str]:
    history = recent_history.get(player_id, [])
    history.append(message)
    if len(history) > RECENT_MSG_LIMIT:
        history = history[-RECENT_MSG_LIMIT:]
    recent_history[player_id] = history
    return history

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
    if pid_str.isdigit():  # Reject pure numbers like "1", "2"
        return False
    if pid_str.lower() in ["string", "null", "none", "0", ""]:
        return False
    if pid_str.startswith("impostor_"):
        return False
    return True

def choose_target_group() -> Optional[Dict]:
    """Choose target group for impostor per rules.pdf"""
    if not current_groups:
        return None

    valid_groups = [
        g for g in current_groups.values()
        if g['size'] >= spawn_control.min_group_size
    ]

    if not valid_groups:
        return None

    origin = [0, 0, 0]

    def score_group(g):
        pos = g['center_position']
        distance = ((pos[0] - origin[0])**2 + (pos[1] - origin[1])**2 + (pos[2] - origin[2])**2) ** 0.5
        size_penalty = g['size']
        return distance / size_penalty

    scored_groups = sorted(valid_groups, key=score_group, reverse=True)
    return scored_groups[0]

def choose_impostor_disguise(target_group_id: Optional[str] = None) -> Optional[str]:
    """
    Choose which player to disguise as from OTHER groups.
    CAN select active players (removed active_players check).
    Load chat history if exists, otherwise use default persona.
    """
    try:
        # Get target group members (normalized)
        target_members = set()
        if target_group_id and target_group_id in current_groups:
            raw_members = current_groups[target_group_id].get('player_ids', [])
            target_members = {normalize_player_id(pid) for pid in raw_members if is_valid_player_id(pid)}
            print(f"🎯 Target group '{target_group_id}' has members: {raw_members}")
            print(f"   Normalized: {target_members}")
        
        # Get players from OTHER groups (CAN be active players now)
        all_groups = {}
        for gid, gdata in current_groups.items():
            if gid != target_group_id:
                members = gdata.get('player_ids', [])
                valid_members = [pid for pid in members if is_valid_player_id(pid)]
                if valid_members:
                    all_groups[gid] = valid_members
        
        print(f"📋 Available groups for disguise: {list(all_groups.keys())}")
        
        # Select from OTHER groups (REMOVED active_players check)
        candidate_players = []
        for gid, members in all_groups.items():
            for pid in members:
                normalized_pid = normalize_player_id(pid)
                
                # Skip if in target group (compare normalized)
                if normalized_pid in target_members:
                    print(f"   ⏭️ Skipping {pid} (in target group)")
                    continue
                
                # REMOVED: Skip if currently active - impostor CAN disguise as active players
                
                candidate_players.append(pid)
                print(f"   ✅ {pid} is valid candidate (from group {gid})")
        
        # Choose one player from candidates
        if candidate_players:
            chosen = random.choice(candidate_players)
            print(f"🎭 Impostor will disguise as: {chosen} (from different group)")
            
            # Try to load chat history from database for stylometry
            try:
                results = player_messages.get(
                    limit=50,
                    where={"player_id": chosen}
                )
                
                if results and results.get("documents") and len(results["documents"]) > 0:
                    message_count = len(results["documents"])
                    print(f"   📚 Found {message_count} messages from {chosen} in database")
                    
                    # Store in recent_history for stylometry
                    if chosen not in recent_history:
                        recent_history[chosen] = []
                    for doc in results["documents"]:
                        if doc not in recent_history[chosen]:
                            recent_history[chosen].append(doc)
                    recent_history[chosen] = recent_history[chosen][-20:]
                else:
                    print(f"   📚 No chat history for {chosen}, will use default gamer persona")
            except Exception as e:
                print(f"   ⚠️ Could not fetch chat history for {chosen}: {e}")
            
            return chosen
        
        print(f"⚠️ No valid players in other groups, trying fallbacks...")
        
        # Fallback: Default player
        print(f"❌ No suitable players found!")
        print(f"💡 Using default: Player_Default")
        return "Player_Default"
        
    except Exception as e:
        print(f"❌ Error choosing disguise: {e}")
        import traceback
        traceback.print_exc()
        return "Player_Default"

def _get_or_create_conversation(group_id: str, disguise_player_id: str) -> ConversationState:
    conv = active_conversations.get(group_id)
    if conv and conv.disguise_player_id != disguise_player_id:
        print(f"⚠️ Conversation for group {group_id} has different disguise; resetting")
        conv = None

    if not conv:
        conv = ConversationState(group_id=group_id, disguise_player_id=disguise_player_id)
        active_conversations[group_id] = conv
        print(f"💬 New conversation started for group {group_id} as {disguise_player_id}")

    return conv

def _detect_goodbye(message: str) -> bool:
    msg = message.lower()
    goodbye_keywords = ["bye", "gtg", "got to go", "gotta go", "see you", "seeya", "good night", "goodbye", "later", "heading out"]
    return any(k in msg for k in goodbye_keywords)

def should_impostor_respond(recent_messages_count: int, last_msg_player_id: str) -> bool:
    if not impostor.is_active or not impostor.disguised_as:
        return False

    if last_msg_player_id == impostor.disguised_as:
        print("⚠️ Impostor won't respond to its own message")
        return False

    if impostor.disguised_as.lower() in ["string", "null", ""]:
        print("⚠️ Invalid impostor disguise, skipping")
        return False

    time_since_last = time.time() - impostor.last_message_time
    if time_since_last < impostor.message_cooldown:
        return False

    base_chance = impostor.conversation_engagement
    if recent_messages_count > 2:
        base_chance += 0.2

    should_respond = random.random() < base_chance
    if should_respond:
        print(f"🎲 Impostor decided to respond (chance was {base_chance:.1%})")

    return should_respond

def _get_style_summary_for_player(disguise_player_id: str, group_id: str) -> str:
    key = (group_id, disguise_player_id)
    if key in stylometry_cache:
        return stylometry_cache[key]

    msgs = recent_history.get(disguise_player_id, [])
    if not msgs or len(msgs) == 0:
        # DEFAULT GAMER PERSONA - works for all players without chat history
        style_summary = (
            "Default gamer persona: Casual, friendly, uses common gaming slang. "
            "Short messages (5-15 words). Curious about surroundings. "
            "Examples: 'hey what's up', 'anyone seen anything cool?', 'lol nice'"
        )
        print(f"   💬 Using default gamer persona for {disguise_player_id}")
    else:
        # Has chat history - use their actual style
        avg_len = sum(len(m) for m in msgs) / len(msgs)
        style_summary = (
            f"Typical style: {len(msgs)} recent messages, "
            f"average length {avg_len:.1f} characters. "
            f"Tone: conversational, similar phrasing to their previous lines."
        )

    stylometry_cache[key] = style_summary
    return style_summary

def generate_impostor_message(context_messages: List[Dict]) -> Optional[str]:
    """Generate impostor message with default persona if no chat history"""
    if not impostor.disguised_as or not impostor.target_group_id:
        return None

    disguise_player_id = impostor.disguised_as
    group_id = impostor.target_group_id
    conv = _get_or_create_conversation(group_id, disguise_player_id)

    # Build conversation buffer
    convo_lines = []
    for m in conv.buffer[-20:]:
        speaker = m["player_id"]
        text = m["message"]
        convo_lines.append(f"{speaker}: {text}")
    convo_text = "\n".join(convo_lines)

    # Get style summary (includes default persona logic)
    style_summary = _get_style_summary_for_player(disguise_player_id, group_id)

    # Compose prompt
    prompt_parts = []
    prompt_parts.append(
        f"You are chatting as {disguise_player_id}. "
        f"Their style: {style_summary}"
    )
    if convo_text:
        prompt_parts.append("Current conversation:\n" + convo_text)
    
    prompt_parts.append(
        "Respond with a natural, in-character line (5-15 words) that fits this conversation."
    )

    full_prompt = "\n\n".join(prompt_parts)
    reply = generate_npc_reply(
        player_text=full_prompt,
        round_id="r1",
        imitate_player_id=disguise_player_id,
        recent_msgs=recent_history.get(disguise_player_id, []),
    )

    return reply

def generate_impostor_goodbye() -> str:
    goodbye_templates = [
        "I have to go, see you later",
        "Gotta head out, catch you guys later",
        "I need to check on something, brb",
        "Got things to do, see ya",
        "Alright I'm out, good luck",
        "Time for me to go, later",
    ]
    return random.choice(goodbye_templates)

# ========== API ENDPOINTS ==========

@app.post("/chat")
def receive_message(
    player_id: str = Body(..., embed=True),
    message: str = Body(..., embed=True),
    group_id: str = Body("solo", embed=True),
):
    player_id = player_id.strip()
    group_id = group_id.strip() if group_id else "solo"
    timestamp = datetime.now(timezone.utc).isoformat()

    print(f"\n💬 Player {player_id} in group '{group_id}': {message}")

    # Store player message
    try:
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
        add_player_message(
            text=message,
            player_id=player_id,
            round_id="r1",
            location="Unknown",
            timestamp=timestamp,
        )
    except Exception as e:
        print(f"⚠️ Failed to store message: {e}")

    _update_recent_history(player_id, message)

    # Update conversation buffer ONLY if impostor is targeting THIS group
    if impostor.is_active and impostor.target_group_id == group_id and impostor.disguised_as:
        conv = _get_or_create_conversation(group_id, impostor.disguised_as)
        conv.add_message(player_id, message, is_impostor=False)

        if _detect_goodbye(message):
            conv.goodbye_detected = True
            print(f"👋 Goodbye detected in group {group_id} from {player_id}")

    response_data = {
        "player_id": player_id,
        "message": message,
        "timestamp": timestamp,
        "group_id": group_id,
        "impostor_message": None,
        "conversation_ended": False,
    }

    # Check if impostor should respond (ONLY to target group)
    if (
        impostor.is_active
        and impostor.target_group_id == group_id  # Only respond to target group
        and should_impostor_respond(len(recent_history), player_id)
        and not impostor.has_sent_goodbye
    ):
        try:
            conv = _get_or_create_conversation(group_id, impostor.disguised_as)

            # Check if conversation ending
            if conv.is_finished():
                impostor_msg = generate_impostor_goodbye()
                impostor.has_sent_goodbye = True
                print(f"👋 Conversation ending, impostor sending goodbye: {impostor_msg}")
            else:
                impostor_msg = generate_impostor_message(context_messages=[])

            if impostor_msg:
                impostor_timestamp = datetime.now(timezone.utc).isoformat()
                
                # Store as the disguised player (not "impostor_X")
                impostor_player_id = impostor.disguised_as

                try:
                    from chromatesting import add_player_message_with_group
                    add_player_message_with_group(
                        text=impostor_msg,
                        player_id=impostor_player_id,
                        round_id="r1",
                        group_id=group_id,
                        location="Unknown",
                        timestamp=impostor_timestamp,
                    )
                except ImportError:
                    add_player_message(
                        text=impostor_msg,
                        player_id=impostor_player_id,
                        round_id="r1",
                        location="Unknown",
                        timestamp=impostor_timestamp,
                    )

                add_npc_memory(
                    impostor_msg,
                    "impostor_said",
                    round_id="r1",
                    timestamp=impostor_timestamp,
                )

                conv.add_message(impostor_player_id, impostor_msg, is_impostor=True)
                _update_recent_history(impostor.disguised_as, impostor_msg)
                impostor.last_message_time = time.time()

                # Return as disguised player
                response_data["impostor_message"] = {
                    "player_id": impostor.disguised_as,
                    "message": impostor_msg,
                    "timestamp": impostor_timestamp,
                }

                print(f"🎭 Impostor as {impostor.disguised_as}: {impostor_msg}")

                # Check if conversation ended
                if conv.is_finished():
                    print(f"💬 Conversation finished after {conv.get_duration():.1f}s")
                    print(f"   Total: {conv.message_count} msgs (impostor: {conv.impostor_message_count})")

                    response_data["conversation_ended"] = True
                    response_data["conversation_stats"] = {
                        "duration": conv.get_duration(),
                        "total_messages": conv.message_count,
                        "impostor_messages": conv.impostor_message_count,
                    }

                    # Clean up conversation state
                    del active_conversations[group_id]
                    if (group_id, impostor.disguised_as) in stylometry_cache:
                        del stylometry_cache[(group_id, impostor.disguised_as)]

                    # Backend deactivates LLM (Unity will despawn impostor)
                    impostor.reset()

        except Exception as e:
            print(f"❌ Impostor message generation failed: {e}")
            import traceback
            traceback.print_exc()

    return response_data

@app.post("/groups/sync")
def sync_groups(groups: List[Dict] = Body(...), timestamp: str = Body(None)):
    """
    Receive group updates from Unity.
    NOW ALSO tracks active players from all groups.
    """
    global current_groups, last_group_update_time, active_players

    current_groups.clear()
    new_active_players = set()

    for group_data in groups:
        group_id = group_data.get('group_id')
        if group_id:
            player_ids = group_data.get('player_ids', [])
            current_groups[group_id] = {
                'group_id': group_id,
                'player_ids': player_ids,
                'center_position': group_data.get('center_position', [0, 0, 0]),
                'size': group_data.get('size', 0)
            }
            
            # Add all players to active_players set
            for pid in player_ids:
                if is_valid_player_id(pid):
                    new_active_players.add(pid)

    # Update active players
    active_players = new_active_players
    last_group_update_time = time.time()

    print(f"\n[GroupSync] 📡 Received {len(current_groups)} groups from Unity")
    for gid, gdata in current_groups.items():
        print(f"  • {gid}: {gdata['size']} players at {gdata['center_position']}")
    print(f"  👥 Active players: {active_players}")

    return {
        'success': True,
        'groups_received': len(current_groups),
        'active_players': list(active_players),
        'timestamp': timestamp
    }

@app.get("/impostor/check_spawn")
def check_impostor_spawn():
    """
    Unity polls this regularly.
    Backend chooses player BEFORE telling Unity to spawn.
    Also signals when to despawn (conversation ended).
    NEW: Only spawns if there are at least 2 groups.
    """
    # Check if conversation ended (should despawn)
    if impostor.is_active and impostor.target_group_id:
        conv = active_conversations.get(impostor.target_group_id)
        if conv and conv.is_finished():
            print(f"\n[SpawnControl] 🛑 Conversation finished, telling Unity to despawn")
            return {
                'should_spawn': False,
                'should_despawn': True,
                'reason': 'conversation_ended',
                'conversation_duration': conv.get_duration()
            }

    # Check if impostor should spawn
    if spawn_control.should_spawn_now():
        # NEW: Verify we have at least 2 groups
        if len(current_groups) < spawn_control.min_groups_required:
            if time.frameCount % 60 == 0:  # Log occasionally
                print(f"\n[SpawnControl] ⏸️ Not enough groups ({len(current_groups)}/{spawn_control.min_groups_required})")
            return {
                'should_spawn': False,
                'should_despawn': False,
                'impostor_active': impostor.is_active,
                'reason': f'need_at_least_{spawn_control.min_groups_required}_groups',
                'current_groups': len(current_groups)
            }

        target_group = choose_target_group()

        if target_group:
            print(f"\n[SpawnControl] 🎯 Selecting impostor disguise...")
            print(f"  Total groups: {len(current_groups)}")
            print(f"  Target group: {target_group['group_id']}")
            print(f"  Group size: {target_group['size']}")
            print(f"  Group location: {target_group['center_position']}")

            # CHOOSE PLAYER FIRST (before spawning)
            disguise_player = choose_impostor_disguise(target_group['group_id'])
            
            print(f"  ✅ Selected disguise: {disguise_player}")
            print(f"  ➡️ Now telling Unity to spawn impostor...")

            spawn_control.record_spawn()

            return {
                'should_spawn': True,
                'should_despawn': False,
                'target_group_id': target_group['group_id'],
                'target_group_position': target_group['center_position'],
                'target_group_members': target_group['player_ids'],
                'disguise_as': disguise_player,
                'engagement_rate': 0.4
            }

    return {
        'should_spawn': False,
        'should_despawn': False,
        'impostor_active': impostor.is_active,
        'current_groups': len(current_groups),
        'next_spawn_in': max(0, spawn_control.spawn_interval - (time.time() - spawn_control.last_spawn_time))
    }

@app.post("/impostor/activate")
def activate_impostor(
    target_player_id: Optional[str] = Body(None),
    target_group_id: Optional[str] = Body(None),
    engagement_rate: float = Body(0.3),
):
    """Called by Unity AFTER impostor spawns to activate backend LLM"""
    if target_player_id and target_player_id.lower() in ["string", "null", ""]:
        target_player_id = None

    if target_group_id and target_group_id.lower() in ["string", "null", ""]:
        target_group_id = None

    if not target_player_id:
        impostor.disguised_as = choose_impostor_disguise(target_group_id)
    else:
        impostor.disguised_as = target_player_id

    if not impostor.disguised_as:
        return {
            "success": False,
            "message": "Could not find suitable player to disguise as",
        }

    # Activate impostor backend
    impostor.is_active = True
    impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
    impostor.last_message_time = time.time()
    impostor.target_group_id = target_group_id
    impostor.has_sent_goodbye = False

    if target_group_id and target_group_id in active_conversations:
        del active_conversations[target_group_id]

    print(f"✅ Impostor activated!")
    print(f"  Disguised as: {impostor.disguised_as}")
    print(f"  Target group: {target_group_id or 'any'}")

    target_group_members = []
    if target_group_id and target_group_id in current_groups:
        target_group_members = current_groups[target_group_id]['player_ids']

    return {
        "success": True,
        "disguised_as": impostor.disguised_as,
        "target_group_id": target_group_id,
        "target_group_members": target_group_members,
        "engagement_rate": impostor.conversation_engagement,
    }

@app.post("/impostor/deactivate")
def deactivate_impostor():
    """Called by Unity after impostor despawns"""
    group_id = impostor.target_group_id
    old_disguise = impostor.disguised_as

    if group_id and group_id in active_conversations:
        del active_conversations[group_id]

    if group_id and old_disguise:
        key = (group_id, old_disguise)
        if key in stylometry_cache:
            del stylometry_cache[key]

    # Deactivate LLM
    impostor.reset()

    print(f"🛑 Impostor deactivated (was disguised as: {old_disguise})")

    return {
        "success": True,
        "message": f"Impostor deactivated (was {old_disguise})",
    }

@app.get("/impostor/status")
def impostor_status():
    status = {
        "is_active": impostor.is_active,
        "disguised_as": impostor.disguised_as,
        "engagement_rate": impostor.conversation_engagement,
        "cooldown_remaining": max(
            0, impostor.message_cooldown - (time.time() - impostor.last_message_time)
        ),
        "active_players": list(active_players),
        "available_disguises": list(set(recent_history.keys()) - active_players),
        "target_group_id": impostor.target_group_id,
        "active_conversations": list(active_conversations.keys()),
        "has_sent_goodbye": impostor.has_sent_goodbye,
        "current_groups": len(current_groups),
        "min_groups_required": spawn_control.min_groups_required,
    }

    if impostor.target_group_id and impostor.target_group_id in active_conversations:
        conv = active_conversations[impostor.target_group_id]
        status["conversation_stats"] = {
            "duration": conv.get_duration(),
            "message_count": conv.message_count,
            "impostor_message_count": conv.impostor_message_count,
            "goodbye_detected": conv.goodbye_detected,
        }

    return status

@app.get("/groups/status")
def get_groups_status():
    return {
        'total_groups': len(current_groups),
        'groups': list(current_groups.values()),
        'active_players': list(active_players),
        'last_update': last_group_update_time,
        'time_since_update': time.time() - last_group_update_time if last_group_update_time > 0 else None
    }

@app.post("/impostor/settings")
def update_impostor_settings(
    message_cooldown: Optional[float] = None,
    engagement_rate: Optional[float] = None,
):
    updated = {}

    if message_cooldown is not None:
        impostor.message_cooldown = max(5.0, message_cooldown)
        updated["message_cooldown"] = impostor.message_cooldown

    if engagement_rate is not None:
        impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
        updated["engagement_rate"] = impostor.conversation_engagement

    return {"success": True, "updated": updated}

@app.get("/players/active")
def get_active_players():
    return {"active_players": list(active_players), "count": len(active_players)}

@app.post("/session/reset")
def reset_session():
    active_players.clear()
    recent_history.clear()
    active_conversations.clear()
    stylometry_cache.clear()
    impostor.reset()

    return {
        "success": True,
        "message": "Session reset complete",
    }

@app.post("/database/clear")
def clear_database():
    try:
        from chromatesting import client
        
        try:
            client.delete_collection("player_messages")
            print("🗑️ Deleted player_messages collection")
        except Exception as e:
            print(f"⚠️ Could not delete player_messages: {e}")
        
        try:
            client.delete_collection("npc_memory")
            print("🗑️ Deleted npc_memory collection")
        except Exception as e:
            print(f"⚠️ Could not delete npc_memory: {e}")
        
        try:
            from chromatesting import player_messages, npc_memory
            print("✅ Collections recreated")
        except:
            pass
        
        active_players.clear()
        recent_history.clear()
        active_conversations.clear()
        stylometry_cache.clear()
        impostor.reset()
        
        print("🗑️ Database cleared")
        
        return {
            "success": True,
            "message": "All ChromaDB data cleared successfully",
            "collections_cleared": ["player_messages", "npc_memory"],
        }
    except Exception as e:
        print(f"❌ Error clearing database: {e}")
        import traceback
        traceback.print_exc()
        return {"success": False, "message": f"Failed to clear database: {str(e)}"}

@app.get("/database/inspect")
def inspect_database():
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
            "active_conversations": list(active_conversations.keys()),
        }
    except Exception as e:
        return {"error": str(e)}

@app.get("/")
def root():
    return {
        "status": "online",
        "message": "Impostor Chat Server",
        "impostor_active": impostor.is_active,
        "disguised_as": impostor.disguised_as,
        "target_group_id": impostor.target_group_id,
        "active_conversations": list(active_conversations.keys()),
        "tracked_groups": len(current_groups),
        "active_players": list(active_players),
        "min_groups_required": spawn_control.min_groups_required,
    }

if __name__ == "__main__":
    print("🚀 Starting Impostor Chat Server...")
    print("📍 Server URL: http://0.0.0.0:8000")
    print(f"⚙️  Min groups required for impostor: {spawn_control.min_groups_required}")
    print("\n✅ Server ready!\n")
    
    # Reduced log spam - only warnings and errors
    uvicorn.run(app, host="0.0.0.0", port=8000, log_level="warning")
