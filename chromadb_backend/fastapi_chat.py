# fastapi_chat.py

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

# Track recent messages per player for style imitation (stylometry cache)
RECENT_MSG_LIMIT = 20
recent_history: Dict[str, List[str]] = {}

# Track active players in current session
active_players: set[str] = set()

# ---- Conversation / group state on backend ----

class ConversationState:
    """
    Per-group conversation buffer + stylometry cache usage.
    One active conversation per target group.
    """
    def __init__(self, group_id: str, disguise_player_id: str):
        self.group_id = group_id
        self.disguise_player_id = disguise_player_id  # real player being mimicked
        self.buffer: List[Dict] = []  # recent turns in this convo
        self.started_at = time.time()
        self.last_activity = time.time()
        self.goodbye_detected = False
        self.max_messages = 30  # soft cap for conversation length

    def add_message(self, player_id: str, message: str, is_impostor: bool):
        self.buffer.append(
            {
                "player_id": player_id,
                "message": message,
                "is_impostor": is_impostor,
                "timestamp": time.time(),
            }
        )
        self.last_activity = time.time()

    def is_finished(self) -> bool:
        # End if goodbye was detected or buffer too long or idle too long
        if self.goodbye_detected:
            return True
        if len(self.buffer) >= self.max_messages:
            return True
        # Idle timeout, e.g., 90 seconds without any message in this conversation
        if time.time() - self.last_activity > 90:
            return True
        return False


# Map target_group_id -> ConversationState (only active conversations)
active_conversations: Dict[str, ConversationState] = {}

# Cached stylometry summary per real player (computed once per conversation)
stylometry_cache: Dict[Tuple[str, str], str] = {}  # (group_id, disguise_player_id) -> style_summary


# Impostor state
class ImpostorState:
    def __init__(self):
        self.disguised_as: Optional[str] = None          # real player id being mimicked
        self.is_active: bool = False
        self.last_message_time: float = 0
        self.message_cooldown: float = 15.0
        self.conversation_engagement: float = 0.3
        self.target_group_id: Optional[str] = None       # backend’s chosen target group

    def is_disguised_as_active_player(self) -> bool:
        """Check if impostor is disguised as a currently active player."""
        return self.disguised_as in active_players


impostor = ImpostorState()


def _update_recent_history(player_id: str, message: str) -> List[str]:
    """Keep a rolling window of recent messages per player for stylometry."""
    history = recent_history.get(player_id, [])
    history.append(message)
    if len(history) > RECENT_MSG_LIMIT:
        history = history[-RECENT_MSG_LIMIT:]
    recent_history[player_id] = history
    return history


def normalize_player_id(player_id: str) -> str:
    """
    Normalize player IDs to handle variations like:
    'Player 1' vs 'player_1' vs 'Player_1' vs 'p1'
    Returns lowercase with underscores.
    """
    if not player_id:
        return ""

    normalized = player_id.lower().replace(" ", "_")
    if normalized.startswith("player") and len(normalized) > 6 and normalized[6].isdigit():
        normalized = "player_" + normalized[6:]
    return normalized


def choose_impostor_disguise(target_group_id: Optional[str] = None) -> Optional[str]:
    """
    Choose which player to disguise as, according to rules.pdf:

    - Back end knows target_group_id (group Unity wants impostor to visit).
    - To choose disguise: find the group farthest away from target group and
      pick a player from that far group, using stored group info in metadatas. [file:7]
    - Skip currently active players and any impostor IDs. [file:1][file:7]
    """
    try:
        # Fetch recent metadata to reconstruct groups and locations
        results = player_messages.get(limit=200)
        groups: Dict[str, Dict] = {}  # group_id -> {"players": set(), "positions": List[Tuple[float,float,float]]}
        player_groups: Dict[str, str] = {}  # player_id -> most recent group_id

        if results and results.get("metadatas"):
            for meta in results["metadatas"]:
                if not meta or "player_id" not in meta:
                    continue
                pid = meta["player_id"]
                if not pid:
                    continue

                # Skip impostor messages and special IDs
                if pid.startswith("impostor_") or pid.startswith("Player_Shadow") or pid.startswith("Player_Ghost"):
                    continue

                gid = meta.get("group_id", "solo")
                pos = meta.get("position") or meta.get("location")  # optional, depends on your storage

                player_groups[pid] = gid

                if gid not in groups:
                    groups[gid] = {"players": set(), "positions": []}
                groups[gid]["players"].add(pid)
                if isinstance(pos, (list, tuple)) and len(pos) == 3:
                    groups[gid]["positions"].append(tuple(pos))

        if not groups:
            print("🔍 No group data available in player_messages, falling back to any inactive player")
            # Simple fallback: pick any inactive player from history
            all_players = set(player_groups.keys())
            inactive = list(all_players - active_players)
            if inactive:
                chosen = random.choice(inactive)
                print(f"🎭 Fallback disguise: {chosen} (inactive, no group data)")
                return chosen
            return None

        # Compute centroid per group
        def group_centroid(info: Dict) -> Tuple[float, float, float]:
            if not info["positions"]:
                return (0.0, 0.0, 0.0)
            xs = [p[0] for p in info["positions"]]
            ys = [p[1] for p in info["positions"]]
            zs = [p[2] for p in info["positions"]]
            return (sum(xs) / len(xs), sum(ys) / len(ys), sum(zs) / len(zs))

        group_centers: Dict[str, Tuple[float, float, float]] = {
            gid: group_centroid(info) for gid, info in groups.items()
        }

        # If backend was given a target_group_id, use that; otherwise just treat
        # all groups equally and skip distance-based selection.
        if target_group_id and target_group_id in group_centers:
            target_center = group_centers[target_group_id]

            # Find group farthest away from the target group (by centroid distance) [file:7]
            def sqr_distance(a, b):
                dx = a[0] - b[0]
                dy = a[1] - b[1]
                dz = a[2] - b[2]
                return dx * dx + dy * dy + dz * dz

            far_group_id = None
            far_group_dist = -1.0

            for gid, center in group_centers.items():
                if gid == target_group_id:
                    continue
                d2 = sqr_distance(center, target_center)
                if d2 > far_group_dist:
                    far_group_dist = d2
                    far_group_id = gid

            chosen_group_id = far_group_id or target_group_id
            print(f"🎯 Disguise selection: target_group={target_group_id}, far_group={far_group_id}")

            candidate_players = [
                pid for pid, gid in player_groups.items()
                if gid == chosen_group_id and pid not in active_players
            ]
            if not candidate_players and chosen_group_id != target_group_id:
                # Fallback to any non-target group
                candidate_players = [
                    pid for pid, gid in player_groups.items()
                    if gid != target_group_id and pid not in active_players
                ]
        else:
            # No target group known: just pick any inactive player [file:1]
            candidate_players = [
                pid for pid in player_groups.keys()
                if pid not in active_players
            ]

        if candidate_players:
            chosen = random.choice(candidate_players)
            print(f"🎭 Impostor disguising as: {chosen} (from group-based rule)")
            return chosen

        # Fallback: anonymous impostor IDs
        fallback_names = [
            f"Player_Shadow_{random.randint(1000, 9999)}",
            f"Player_Ghost_{random.randint(1000, 9999)}",
            f"Player_Phantom_{random.randint(1000, 9999)}",
        ]
        chosen = random.choice(fallback_names)
        print(f"🎭 No suitable players found, using fallback: {chosen}")
        return chosen

    except Exception as e:
        print(f"❌ Error choosing disguise: {e}")
        return None


def _get_or_create_conversation(group_id: str, disguise_player_id: str) -> ConversationState:
    """
    Ensure there is a ConversationState for this target group/disguise. [file:7]
    """
    conv = active_conversations.get(group_id)
    if conv and conv.disguise_player_id != disguise_player_id:
        # If disguise changed for same group, reset conversation
        print(f"⚠️ Conversation for group {group_id} has different disguise; resetting")
        conv = None

    if not conv:
        conv = ConversationState(group_id=group_id, disguise_player_id=disguise_player_id)
        active_conversations[group_id] = conv
        print(f"💬 New conversation started for group {group_id} as {disguise_player_id}")
    return conv


def _detect_goodbye(message: str) -> bool:
    """
    Simple rule-based goodbye detector. [file:7]
    """
    msg = message.lower()
    goodbye_keywords = ["bye", "gtg", "got to go", "see you", "seeya", "good night", "goodbye"]
    return any(k in msg for k in goodbye_keywords)


def should_impostor_respond(recent_messages_count: int, last_msg_player_id: str) -> bool:
    """
    Decide if impostor should inject itself into conversation.
    - Do not respond if disguised as active player or replying to own message. [file:1]
    - Use engagement rate + extra chance when conversation active. [file:1][file:7]
    """
    if not impostor.is_active or not impostor.disguised_as:
        return False

    if impostor.is_disguised_as_active_player():
        print(f"⚠️ Impostor disguised as active player {impostor.disguised_as}, skipping response")
        return False

    if last_msg_player_id == impostor.disguised_as:
        print("⚠️ Impostor won't respond to its own message")
        return False

    if impostor.disguised_as.lower() in ["string", "null", ""]:
        print("⚠️ Invalid impostor disguise, skipping response")
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
    """
    Build or reuse a stylometry-style summary for disguise_player_id
    within this conversation context. Cache it per (group, player). [file:7]
    """
    key = (group_id, disguise_player_id)
    if key in stylometry_cache:
        return stylometry_cache[key]

    # Use recent_history as stylometry cache (up to 20 messages) [file:7]
    msgs = recent_history.get(disguise_player_id, [])
    if not msgs:
        style_summary = "Short, neutral sentences."
    else:
        # Very lightweight style description; in a real system this could be an LLM call.
        avg_len = sum(len(m) for m in msgs) / len(msgs)
        style_summary = (
            f"Typical style: {len(msgs)} recent messages, "
            f"average length {avg_len:.1f} characters. "
            f"Tone: conversational, similar phrasing to their previous lines."
        )

    stylometry_cache[key] = style_summary
    return style_summary


def generate_impostor_message(context_messages: List[Dict]) -> Optional[str]:
    """
    Generate a message from the impostor pretending to be the disguised player.
    - Uses per-group conversation buffer as primary context. [file:7]
    - Uses per-player recent_history (stylometry) only once per conversation. [file:7]
    - Stores impostor messages with id impostor_<disguise>. [file:7]
    """
    if not impostor.disguised_as or not impostor.target_group_id:
        return None

    disguise_player_id = impostor.disguised_as
    group_id = impostor.target_group_id

    conv = _get_or_create_conversation(group_id, disguise_player_id)

    # Build conversation buffer text (only current conversation messages) [file:7]
    convo_lines = []
    for m in conv.buffer[-20:]:
        speaker = m["player_id"]
        text = m["message"]
        convo_lines.append(f"{speaker}: {text}")

    convo_text = "\n".join(convo_lines)

    # Stylometry-style summary for disguise player
    style_summary = _get_style_summary_for_player(disguise_player_id, group_id)

    # Query additional memory only if needed (e.g. for “earlier/last time”) [file:7]
    memory_context = ""
    try:
        if convo_text:
            mem_results = query_collection(
                player_messages,
                convo_text,
                k=3,
                filters={"player_id": disguise_player_id},
            )
            if mem_results and mem_results.get("documents"):
                memory_context = "\n".join(mem_results["documents"][0])
    except Exception as e:
        print(f"⚠️ Memory query failed: {e}")

    # Compose prompt
    prompt_parts = []
    prompt_parts.append(
        f"You are chatting as {disguise_player_id}. "
        f"Their style: {style_summary}"
    )
    if convo_text:
        prompt_parts.append("Current conversation:\n" + convo_text)
    if memory_context:
        prompt_parts.append("What this player might remember:\n" + memory_context)

    prompt_parts.append(
        "Respond with a natural, in-character line that fits this ongoing conversation."
    )

    full_prompt = "\n\n".join(prompt_parts)

    reply = generate_npc_reply(
        player_text=full_prompt,
        round_id="r1",
        imitate_player_id=disguise_player_id,
        recent_msgs=recent_history.get(disguise_player_id, []),
    )
    return reply


@app.post("/chat")
def receive_message(
    player_id: str = Body(..., embed=True),
    message: str = Body(..., embed=True),
    group_id: str = Body("solo", embed=True),
):
    """
    Receives messages from players, stores them with group info,
    and may inject impostor responses. [file:1][file:7]
    """
    player_id = player_id.strip()
    group_id = group_id.strip() if group_id else "solo"
    timestamp = datetime.now(timezone.utc).isoformat()

    print(f"\n💬 Player {player_id} in group '{group_id}' at {timestamp}: {message}")

    active_players.add(player_id)

    # If the real player appears while impostor is disguised as them, drop disguise
    if impostor.is_active and impostor.disguised_as == player_id:
        print(f"⚠️ Real {player_id} is active! Impostor disguise compromised.")
        impostor.disguised_as = None
        impostor.is_active = False
        impostor.target_group_id = None

    # Store player message WITH group info and (optionally) position in metadata [file:7]
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
        print("⚠️ Using legacy message storage without group info")
    except Exception as e:
        print(f"⚠️ Failed to store message in Chroma: {e}")

    # Update per-player stylometry cache
    _update_recent_history(player_id, message)

    # Update per-group conversation buffer if impostor is targeting this group [file:7]
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

    # Decide whether impostor should respond to this message
    if (
        impostor.is_active
        and impostor.target_group_id == group_id  # only talk in target group [file:7]
        and should_impostor_respond(len(recent_history), player_id)
    ):
        # Build context from this conversation’s buffer (already done in generate_impostor_message)
        try:
            impostor_msg = generate_impostor_message(context_messages=[])
            if impostor_msg:
                impostor_timestamp = datetime.now(timezone.utc).isoformat()
                impostor_player_id = f"impostor_{impostor.disguised_as}"

                # Store impostor message WITH group info using separate id [file:7]
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

                # Update conversation buffer & stylometry cache
                conv = _get_or_create_conversation(group_id, impostor.disguised_as)
                conv.add_message(impostor_player_id, impostor_msg, is_impostor=True)
                _update_recent_history(impostor.disguised_as, impostor_msg)
                impostor.last_message_time = time.time()

                response_data["impostor_message"] = {
                    "player_id": impostor_player_id,
                    "message": impostor_msg,
                    "timestamp": impostor_timestamp,
                }

                print(f"🎭 Impostor as {impostor.disguised_as} ({impostor_player_id}): {impostor_msg}")

                # Check if conversation should end (then backend tells Unity to despawn/leave) [file:7]
                if conv.is_finished():
                    print(f"👋 Conversation for group {group_id} finished. Impostor should leave.")
                    # Clear backend state; Unity can call /impostor/deactivate based on its own logic
                    del active_conversations[group_id]
                    impostor.is_active = False
                    impostor.disguised_as = None
                    impostor.target_group_id = None
        except Exception as e:
            print(f"❌ Impostor message generation failed: {e}")

    return response_data


@app.post("/impostor/activate")
def activate_impostor(
    target_player_id: Optional[str] = None,
    target_group_id: Optional[str] = None,
    engagement_rate: float = 0.3,
):
    """
    Activate the impostor AI.
    - Unity spawner chooses a target group and sends target_group_id. [file:5][file:7]
    - Backend chooses a disguise using group-based rules (far group). [file:7]
    - Impostor will only talk in that target_group_id until conversation ends. [file:7]
    """
    if target_player_id and target_player_id.lower() in ["string", "null", ""]:
        target_player_id = None
    if target_group_id and target_group_id.lower() in ["string", "null", ""]:
        target_group_id = None

    if target_player_id and target_player_id in active_players:
        return {
            "success": False,
            "message": f"{target_player_id} is currently active, cannot disguise as them",
            "active_players": list(active_players),
        }

    if target_player_id:
        impostor.disguised_as = target_player_id
        print(f"🎭 Manual disguise selected: {target_player_id}")
    else:
        impostor.disguised_as = choose_impostor_disguise(target_group_id)

    if not impostor.disguised_as:
        return {
            "success": False,
            "message": "Could not find suitable player to disguise as",
        }

    if impostor.disguised_as in active_players:
        print(f"⚠️ Selected disguise {impostor.disguised_as} is active! Aborting.")
        impostor.disguised_as = None
        return {
            "success": False,
            "message": "Cannot activate: chosen identity is currently active",
            "active_players": list(active_players),
        }

    impostor.is_active = True
    impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
    impostor.last_message_time = time.time()
    impostor.target_group_id = target_group_id  # backend tracks which group to talk in [file:7]

    # Reset/clear any existing conversation for this target group
    if target_group_id and target_group_id in active_conversations:
        del active_conversations[target_group_id]

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


@app.post("/impostor/deactivate")
def deactivate_impostor():
    """Deactivate the impostor AI and clear conversation state for its target group. [file:5][file:7]"""
    group_id = impostor.target_group_id
    impostor.is_active = False
    old_disguise = impostor.disguised_as
    impostor.disguised_as = None
    if group_id and group_id in active_conversations:
        del active_conversations[group_id]
    impostor.target_group_id = None

    print(f"🛑 Impostor deactivated (was disguised as: {old_disguise})")
    return {
        "success": True,
        "message": f"Impostor deactivated (was {old_disguise})",
    }


@app.get("/impostor/status")
def impostor_status():
    """Get current impostor status, including target group and conversation info."""
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
        "target_group_id": impostor.target_group_id,
        "active_conversations": list(active_conversations.keys()),
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
    return {"success": True, "updated": updated}


@app.get("/players/active")
def get_active_players():
    """Get list of currently active players."""
    return {"active_players": list(active_players), "count": len(active_players)}


@app.post("/session/reset")
def reset_session():
    """Reset the current session (clear active players, history, and conversations)."""
    active_players.clear()
    recent_history.clear()
    active_conversations.clear()
    stylometry_cache.clear()
    impostor.is_active = False
    impostor.disguised_as = None
    impostor.target_group_id = None
    return {
        "success": True,
        "message": "Session reset complete (active players, history, and conversations cleared)",
    }


@app.post("/database/clear")
def clear_database():
    """
    DANGER: Clears ALL stored messages and memories from ChromaDB.
    """
    try:
        player_messages.delete(where={})
        npc_memory.delete(where={})
        active_players.clear()
        recent_history.clear()
        active_conversations.clear()
        stylometry_cache.clear()
        impostor.is_active = False
        impostor.disguised_as = None
        impostor.target_group_id = None
        print("🗑️ Database cleared: All messages and memories deleted")
        return {
            "success": True,
            "message": "All ChromaDB data cleared successfully",
            "collections_cleared": ["player_messages", "npc_memory"],
        }
    except Exception as e:
        print(f"❌ Error clearing database: {e}")
        return {"success": False, "message": f"Failed to clear database: {str(e)}"}


@app.get("/database/inspect")
def inspect_database():
    """Debug endpoint to see what player IDs are stored in ChromaDB."""
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
    """Health check endpoint."""
    return {
        "status": "online",
        "message": "Impostor Chat Server is running",
        "impostor_active": impostor.is_active,
        "target_group_id": impostor.target_group_id,
        "ollama_connection": "Check if Ollama is running on port 11434",
    }


if __name__ == "__main__":
    print("🚀 Starting Impostor Chat Server...")
    print("📍 Server URL: http://0.0.0.0:8000")
    print("🔧 API Endpoints:")
    print("  POST /chat")
    print("  POST /impostor/activate")
    print("  POST /impostor/deactivate")
    print("  GET  /impostor/status")
    print("  POST /impostor/settings")
    print("  GET  /players/active")
    print("  POST /session/reset")
    print("  POST /database/clear")
    print("  GET  /database/inspect")
    print("  GET  /")
    uvicorn.run(app, host="0.0.0.0", port=8000)
