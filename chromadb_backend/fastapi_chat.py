# fastapi_chat.py - V4.0 STRATEGIC DECEPTIVE IMPOSTOR
# Complete integration with player profiling, suspicion tracking, and deception strategy

from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
from typing import Dict, List, Optional, Tuple
import time
import random
from datetime import datetime, timezone
from collections import deque
import uvicorn

# Import our new modules
from player_profiling import PlayerProfileManager, PlayerProfile
from suspicion_tracker import SuspicionTracker
from deception_strategy import DeceptionStrategy, DeceptionMode

# Import existing modules
from chromatesting import (
    generate_npc_reply_fast,
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

# Player profiling system
profile_manager = PlayerProfileManager()

# Suspicion tracking system
suspicion_tracker = SuspicionTracker()

# Per-player recent messages (for LLM style imitation)
recent_history: Dict[str, deque] = {}

# All active players
active_players: set[str] = set()

# Current groups from Unity
current_groups: Dict[str, Dict] = {}
last_group_update_time: float = 0.0

# Global summary for match events
global_message_buffer: deque = deque(maxlen=40)
global_summary: str = "Match just started. Players are exploring."

# ========== CONVERSATION STATE ==========

class ConversationState:
    """Enhanced conversation state with strategic intelligence"""
    
    def __init__(self, group_id: str, disguise_player_id: str):
        self.group_id = group_id
        self.disguise_player_id = disguise_player_id
        
        # Conversation buffer (last 20 turns)
        self.buffer: deque = deque(maxlen=20)
        
        # Style summary (cached)
        self.style_summary: Optional[str] = None
        
        # ✅ NEW: Deception strategy engine
        self.strategy = DeceptionStrategy(disguise_player_id)
        
        # Timing
        self.started_at: float = time.time()
        self.last_activity: float = time.time()
        self.last_impostor_response: float = 0
        
        # Conversation state
        self.goodbye_detected: bool = False
        self.message_count = 0
        self.impostor_message_count = 0
        self.max_messages = 30
        self.idle_timeout = 90
        
        # ✅ NEW: Track conversation facts
        self.facts_learned = 0
        self.questions_asked = 0
        self.accusations_made = 0
        
    def add_message(self, player_id: str, message: str, is_impostor: bool):
        """Add message and analyze it"""
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
        
        # ✅ NEW: Analyze message for strategic info
        msg_lower = message.lower()
        
        # Track facts learned
        if any(word in msg_lower for word in ['was at', 'went to', 'saw', 'found']):
            self.facts_learned += 1
        
        # Track accusations
        if any(word in msg_lower for word in ['suspicious', 'lying', 'impostor']):
            if not is_impostor:  # Player accused someone
                self.accusations_made += 1
                
                # Update suspicion tracker
                if self.disguise_player_id.lower() in msg_lower:
                    # We're being accused!
                    suspicion_tracker.add_accusation(
                        accuser=player_id,
                        accused=self.disguise_player_id,
                        reason=message[:50],
                        weight=3.0
                    )
                    self.strategy.has_been_accused = True
                    self.strategy.accusation_count += 1
    
    def should_respond(self, last_msg_player_id: str) -> Tuple[bool, str]:
        """Determine if impostor should respond - STRATEGIC VERSION"""
        
        # Don't respond to ourselves
        if last_msg_player_id == self.disguise_player_id:
            return False, "self"
        
        # Check cooldown
        time_since_last = time.time() - self.last_impostor_response
        if self.last_impostor_response > 0 and time_since_last < 5.0:
            return False, f"cooldown {time_since_last:.1f}s"
        
        # Count humans in conversation
        human_players = set()
        for msg in self.buffer:
            if not msg['is_impostor']:
                human_players.add(msg['player_id'])
        
        num_humans = len(human_players)
        
        # ✅ NEW: Strategic override - ALWAYS respond if accused
        if self.strategy.has_been_accused:
            return True, "MUST DEFEND (accused!)"
        
        # ✅ NEW: Check if we should respond strategically
        last_message = list(self.buffer)[-1]['message'] if self.buffer else ""
        should_respond, response_type = self.strategy.should_respond_to_message(
            last_message,
            {'num_humans': num_humans, 'facts_learned': self.facts_learned}
        )
        
        if response_type == "defend":
            return True, "defending accusation"
        elif response_type == "answer_question":
            return True, "answering question"
        elif response_type == "seed_doubt":
            return True, "seeding doubt (strategic)"
        
        # Original logic for non-strategic responses
        if num_humans == 1:
            return True, "1-on-1 (100%)"
        elif num_humans == 2:
            if random.random() < 0.7:
                return True, "2 players (70%)"
            else:
                return False, "2 players (skip)"
        else:
            if random.random() < 0.4:
                return True, f"{num_humans} players (40%)"
            else:
                return False, f"{num_humans} players (skip)"
    
    def is_finished(self) -> Tuple[bool, str]:
        """Check if conversation should end"""
        if self.goodbye_detected:
            return True, "goodbye"
        if self.message_count >= self.max_messages:
            return True, f"max messages"
        
        idle_time = time.time() - self.last_activity
        if idle_time > self.idle_timeout:
            return True, f"idle {idle_time:.0f}s"
        if self.impostor_message_count >= 10:
            return True, f"impostor done"
        
        return False, "active"
    
    def get_buffer_text(self) -> str:
        if not self.buffer:
            return "(conversation just started)"
        lines = []
        for msg in self.buffer:
            lines.append(f"{msg['player_id']}: {msg['message']}")
        return "\n".join(lines)
    
    def get_conversation_summary(self) -> str:
        """Generate conversation summary"""
        players = set(msg['player_id'] for msg in self.buffer)
        
        summary_parts = [
            f"📊 CONVERSATION SUMMARY",
            f"   Duration: {time.time() - self.started_at:.0f}s",
            f"   Messages: {self.message_count} ({self.impostor_message_count} impostor)",
            f"   Participants: {', '.join(players)}",
            f"   Facts learned: {self.facts_learned}",
            f"   Questions asked: {self.questions_asked}",
            f"   Accusations made: {self.accusations_made}",
        ]
        
        return "\n".join(summary_parts)

# Active conversations
active_conversations: Dict[str, ConversationState] = {}

# ========== IMPOSTOR STATE ==========

class ImpostorState:
    def __init__(self):
        self.disguised_as: Optional[str] = None
        self.is_active: bool = False
        self.target_group_id: Optional[str] = None
        # Track ALL previously visited groups so the impostor cycles through them
        self.visited_group_ids: list = []

    def reset(self):
        # Record the group we just left before clearing state
        if self.target_group_id and self.target_group_id not in self.visited_group_ids:
            self.visited_group_ids.append(self.target_group_id)
        # Keep history capped at last 10 groups (avoids memory growing forever)
        if len(self.visited_group_ids) > 10:
            self.visited_group_ids = self.visited_group_ids[-10:]
        self.disguised_as = None
        self.is_active = False
        self.target_group_id = None

    @property
    def last_visited_group_id(self) -> Optional[str]:
        """Backward-compatible property — returns most recently visited group."""
        return self.visited_group_ids[-1] if self.visited_group_ids else None

impostor = ImpostorState()

class ImpostorSpawnControl:
    def __init__(self):
        self.spawn_interval: float = 60.0  # 1 minute minimum between despawn and next spawn
        self.last_despawn_time: float = 0.0  # track despawn time, not spawn time
        self.min_group_size: int = 1

    def should_spawn_now(self) -> bool:
        if impostor.is_active:
            return False
        # Enforce delay from last DESPAWN, not last spawn
        if self.last_despawn_time > 0:
            elapsed = time.time() - self.last_despawn_time
            if elapsed < self.spawn_interval:
                remaining = self.spawn_interval - elapsed
                return False
        valid_groups = [g for g in current_groups.values() if g.get('size', 0) >= self.min_group_size]
        return len(valid_groups) > 0

    def record_despawn(self):
        """Call this when the impostor despawns to start the cooldown timer."""
        self.last_despawn_time = time.time()

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
    """Update per-player recent message history"""
    if player_id not in recent_history:
        recent_history[player_id] = deque(maxlen=20)
    recent_history[player_id].append(message)

def update_global_summary():
    """Update global summary every 5 messages using an LLM rewrite step."""
    global global_summary

    if len(global_message_buffer) < 5:
        return

    recent_msgs = list(global_message_buffer)[-20:]

    # Build a plain transcript for the LLM to summarise
    transcript_lines = []
    for msg in recent_msgs:
        pid = msg.get('player_id', '')
        text = msg.get('message', '')
        if pid.startswith('impostor_'):
            continue          # hide the impostor's identity
        transcript_lines.append(f"{pid}: {text}")

    transcript = "\n".join(transcript_lines)

    prompt = f"""You are summarising a multiplayer Chernobyl survival game chat for an AI NPC.

Recent player messages:
{transcript}

Write ONE concise sentence (max 20 words) describing what the players are doing and where they are.
Only mention locations and actions from the game: Sheds, Barns, Greenhouse, Church, Pavilion, collecting wood/mushrooms, taking cans, shooting aliens.
Do NOT mention day/night, knives, or anything outside the game rules.
Reply with ONLY the summary sentence."""

    try:
        from langchain_ollama import ChatOllama
        _summary_llm = ChatOllama(
            model="llama3.2:3b",
            temperature=0.3,
            base_url="http://127.0.0.1:11434",
            num_ctx=256,
            num_predict=40,
        )
        response = _summary_llm.invoke(prompt)
        candidate = (response.content or "").strip().split("\n")[0].strip()
        if len(candidate) > 10:
            global_summary = candidate
            print(f"   📝 Global summary updated: {global_summary}")
        else:
            raise ValueError("Empty summary")
    except Exception as e:
        print(f"   ⚠️ Summary LLM failed ({e}), falling back to keyword summary")
        # Keyword fallback (original logic, kept as safety net)
        players_mentioned: set = set()
        locations_mentioned: set = set()
        VALID_LOCATIONS = ["Church", "Greenhouse", "Sheds", "Barns", "Pavilion"]
        for msg in recent_msgs:
            pid = msg.get('player_id', '')
            text = msg.get('message', '').lower()
            if not pid.startswith('impostor_'):
                players_mentioned.add(pid)
            for loc in VALID_LOCATIONS:
                if loc.lower() in text:
                    locations_mentioned.add(loc)
        parts = []
        if players_mentioned:
            parts.append(f"Players active: {', '.join(list(players_mentioned)[:3])}")
        if locations_mentioned:
            parts.append(f"Locations mentioned: {', '.join(list(locations_mentioned)[:2])}")
        global_summary = ". ".join(parts) if parts else "Players exploring the area."

def choose_target_group() -> Optional[Dict]:
    """
    Choose the target group for the impostor using a composite score:
        score = distance_from_all_others - size_penalty
    This implements the rule: "farthest group, with priority for smaller groups."
    Bug 3 fix: never pick the same group the impostor most recently visited.
    """
    if len(current_groups) < 2:
        return None

    # Exclude all previously visited groups so the impostor cycles through different ones
    valid_groups = [
        g for g in current_groups.values()
        if g['size'] >= spawn_control.min_group_size
        and g['group_id'] not in impostor.visited_group_ids
    ]

    if not valid_groups:
        # All groups have been visited — reset history and start a new cycle,
        # but still avoid the most recent one if possible
        print("   ♻️  All groups visited — resetting group history for new cycle")
        impostor.visited_group_ids.clear()
        valid_groups = [
            g for g in current_groups.values()
            if g['size'] >= spawn_control.min_group_size
            and g['group_id'] != impostor.last_visited_group_id
        ]
    if not valid_groups:
        valid_groups = [
            g for g in current_groups.values()
            if g['size'] >= spawn_control.min_group_size
        ]
    if not valid_groups:
        return None

    def group_score(g: Dict) -> float:
        pos = g.get('center_position', [0, 0, 0])
        # Sum of distances to all OTHER groups (higher = more isolated / farther away)
        total_dist = 0.0
        for other in current_groups.values():
            if other['group_id'] == g['group_id']:
                continue
            other_pos = other.get('center_position', [0, 0, 0])
            dist = ((pos[0] - other_pos[0]) ** 2 + (pos[2] - other_pos[2]) ** 2) ** 0.5
            total_dist += dist
        # Bug 2 fix: smaller groups are preferred — subtract a size penalty
        # Each extra player above 1 costs 5 units of distance-score
        size_penalty = (g['size'] - 1) * 5.0
        return total_dist - size_penalty

    return max(valid_groups, key=group_score)

def choose_impostor_disguise(target_group_id: Optional[str] = None) -> Optional[str]:
    """Choose player from farthest group"""
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
        
        candidate_players_with_distance.sort(key=lambda x: x['distance'], reverse=True)
        chosen_data = candidate_players_with_distance[0]
        
        return chosen_data['player_id']
        
    except Exception as e:
        print(f"❌ Error choosing disguise: {e}")
        return "Player_Default"

def generate_style_summary(player_id: str) -> str:
    """
    Generate a real LLM-based style summary from the player's last 20 messages.
    Bug 5 fix: delegates to stylometric.summarize_player_style() instead of
    returning a word-count heuristic string.
    Falls back to a safe default if the player has fewer than 3 messages.
    """
    msgs = list(recent_history.get(player_id, []))

    if not msgs or len(msgs) < 3:
        return "Casual gamer, short responses, friendly tone."

    try:
        from stylometric import summarize_player_style
        summary = summarize_player_style(player_id, msgs)
        print(f"   🖊️ Style for {player_id}: {summary[:80]}...")
        return summary
    except Exception as e:
        print(f"   ⚠️ Style LLM failed ({e}), using word-count fallback")
        avg_length = sum(len(m.split()) for m in msgs) / len(msgs)
        if avg_length < 4:
            return "Very short messages, minimal punctuation, blunt tone."
        elif avg_length < 8:
            return "Brief casual chat, informal, uses short sentences."
        else:
            return "Longer detailed messages, explains actions clearly."

def generate_impostor_message(conv: ConversationState) -> Optional[str]:
    """
    ✅ NEW: Generate STRATEGIC impostor reply
    
    Uses deception strategy to decide what to say
    """
    
    print(f"\n{'─'*50}")
    print(f"🤖 GENERATING STRATEGIC REPLY")
    print(f"{'─'*50}")
    
    # Cache style summary
    if conv.style_summary is None:
        conv.style_summary = generate_style_summary(conv.disguise_player_id)
    
    conversation_text = conv.get_buffer_text()
    
    # ✅ NEW: Get all player profiles for strategic decision-making
    profiles = {
        pid: profile.to_dict()
        for pid, profile in profile_manager.profiles.items()
    }
    
    # Get recent messages
    recent_msgs = list(recent_history.get(conv.disguise_player_id, []))
    
    # Get last message from conversation
    last_message = list(conv.buffer)[-1]['message'] if conv.buffer else ""
    conversation_history = [msg['message'] for msg in list(conv.buffer)[-10:]]
    
    print(f"   Style: {conv.style_summary}")
    print(f"   Buffer: {len(conv.buffer)} messages")
    print(f"   Facts: {conv.facts_learned}")
    print(f"   Mode: {conv.strategy.current_mode.value}")
    
    # ✅ NEW: Use deception strategy to generate response
    try:
        strategic_response = conv.strategy.get_response_strategy(
            message=last_message,
            profiles=profiles,
            suspicion_tracker=suspicion_tracker,
            conversation_history=conversation_history
        )

        if strategic_response:
            print(f"   🎭 Strategic: {strategic_response[:60]}...")
        else:
            print(f"   🎭 No strategic response — using casual generation")

        t0 = time.time()

        # Bug 1 fix: pass strategic_response AND strategy_mode into the LLM call
        # so the prompt becomes "Say this but in your natural style: <strategic_response>"
        reply = generate_npc_reply_fast(
            disguise_name=conv.disguise_player_id,
            style_summary=conv.style_summary,
            global_context=global_summary,
            conversation=conversation_text,
            recent_msgs=recent_msgs,
            strategy_mode=conv.strategy.current_mode.value,
            strategic_response=strategic_response,
        )

        t1 = time.time()
        print(f"   ⏱️ {t1-t0:.2f}s")
        print(f"   💬 {reply}")
    
    except Exception as e:
        print(f"   ❌ Strategy failed: {e}")
        import traceback
        traceback.print_exc()
        reply = "Not sure."
    
    print(f"{'─'*50}\n")
    
    return reply

def detect_goodbye(message: str) -> bool:
    goodbye_keywords = ["bye", "goodbye", "see ya", "later", "gotta go", "gtg", "brb", "afk"]
    return any(keyword in message.lower() for keyword in goodbye_keywords)

# ========== API ENDPOINTS ==========

@app.post("/chat")
def receive_message(
    player_id: str = Body(..., embed=True),
    message: str = Body(..., embed=True),
    group_id: str = Body("solo", embed=True),
):
    """Enhanced chat endpoint with strategic analysis"""
    
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
    
    # ✅ NEW: Analyze message and update player profile
    statement = profile_manager.analyze_message(player_id, message, location=group_id)
    print(f"   📝 Category: {statement.category}")
    if statement.mentioned_players:
        print(f"   👥 Mentioned: {', '.join(statement.mentioned_players)}")
    
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
            conv = ConversationState(group_id, impostor.disguised_as)
            active_conversations[group_id] = conv
            print(f"🆕 Conversation started")
        
        # Add message to buffer
        conv.add_message(player_id, message, is_impostor=False)
        
        # Check for goodbye
        if detect_goodbye(message):
            conv.goodbye_detected = True
            print(f"👋 Goodbye detected")
        
        # Determine if impostor should respond
        should_respond, reason = conv.should_respond(player_id)
        
        print(f"🤔 Respond? {should_respond} ({reason})")
        
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
                    
                    global_message_buffer.append({
                        'player_id': f"impostor_{impostor.disguised_as}",
                        'message': impostor_msg,
                        'timestamp': impostor_timestamp
                    })
                    
                    response_data["impostor_message"] = {
                        "player_id": impostor.disguised_as,
                        "message": impostor_msg,
                        "timestamp": impostor_timestamp,
                    }
                    
                    print(f"✅ Replied as {impostor.disguised_as}")
            
            except Exception as e:
                print(f"❌ Reply failed: {e}")
                import traceback
                traceback.print_exc()
        
        # Check if conversation should end
        finished, finish_reason = conv.is_finished()
        if finished:
            print(f"\n{conv.get_conversation_summary()}")
            print(f"\n{suspicion_tracker.get_suspicion_summary()}")
            print(f"🏁 Ended: {finish_reason}\n")
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
        'reason': 'Waiting'
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
    old_group = impostor.target_group_id
    impostor.reset()
    spawn_control.record_despawn()

    if old_group in active_conversations:
        del active_conversations[old_group]

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

@app.get("/profiles")
def get_profiles():
    """Get all player profiles"""
    return {
        "profiles": {
            pid: profile.to_dict()
            for pid, profile in profile_manager.profiles.items()
        }
    }

@app.get("/suspicions")
def get_suspicions():
    """Get suspicion summary"""
    return {
        "summary": suspicion_tracker.get_suspicion_summary(),
        "most_suspected": suspicion_tracker.get_most_suspected(5)
    }

@app.post("/session/reset")
def reset_session():
    active_players.clear()
    recent_history.clear()
    active_conversations.clear()
    global_message_buffer.clear()
    impostor.reset()
    profile_manager.profiles.clear()
    suspicion_tracker.suspicion_matrix.clear()
    
    print("🗑️ Session reset")
    
    return {"success": True, "message": "Session reset"}

@app.get("/")
def root():
    return {
        "status": "online",
        "message": "Impostor Chat Server - V4.0 STRATEGIC DECEPTION",
        "impostor_active": impostor.is_active,
        "tracked_groups": len(current_groups),
        "tracked_players": len(profile_manager.profiles),
        "version": "4.0 - Strategic deceptive impostor with profiling & suspicion tracking"
    }

if __name__ == "__main__":
    print("🚀 Impostor Chat Server V4.0 - STRATEGIC DECEPTION")
    print("📍 Port: 8000")
    print("\n✅ NEW FEATURES:")
    print("   [✓] Player profiling (tracks behavior, statements, locations)")
    print("   [✓] Suspicion tracking (scores who suspects whom)")
    print("   [✓] Deception strategy engine (decides when to lie/accuse/defend)")
    print("   [✓] Strategic question asking (gathers intel)")
    print("   [✓] Smart accusation targeting (picks best targets)")
    print("   [✓] Defense generation (uses alibis from memory)")
    print("   [✓] Doubt seeding (plants subtle suspicion)")
    print("\n✅ API ENDPOINTS:")
    print("   GET  /profiles     - View all player profiles")
    print("   GET  /suspicions   - View suspicion matrix")
    print("\n✅ Server ready!\n")
    
    uvicorn.run(app, host="0.0.0.0", port=8000, log_level="warning")