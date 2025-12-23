# fastapi_chat.py - FULLY FIXED VERSION

from fastapi import FastAPI, Body
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
from datetime import datetime, timezone
import random
import time
from typing import Optional
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

# Track recent messages per player for style imitation
RECENT_MSG_LIMIT = 20
recent_history: dict[str, list[str]] = {}

# Track active players in current session (NORMALIZED names)
active_players: set[str] = set()

# Track all active groups
active_groups: dict[str, list[str]] = {}  # NORMALIZED group_id -> [player1, player2, ...]


# Impostor state
class ImpostorState:
    def __init__(self):
        self.disguised_as: Optional[str] = None  # Normalized player name
        self.target_group_id: Optional[str] = None  # NORMALIZED group ID
        self.is_active: bool = False
        self.last_message_time: float = 0
        self.message_cooldown: float = 10.0  # Reduced from 15 to 10 seconds
        self.conversation_engagement: float = 0.5  # Increased from 0.4 to 0.5

    def reset(self):
        """Reset impostor state"""
        self.disguised_as = None
        self.target_group_id = None
        self.is_active = False
        self.last_message_time = 0


impostor = ImpostorState()


def normalize_player_name(player_name: str) -> str:
    """
    Normalize player names to EXACTLY match Unity's PlayerIdentity format.
    Unity uses: Player_1, Player_2, Player_3, Player_4
    
    This handles:
    - "Player 1" -> "Player_1"
    - "player_1" -> "Player_1" 
    - "Player1" -> "Player_1"
    - "p1" -> "Player_1"
    """
    if not player_name:
        return ""
    
    # Remove extra whitespace
    name = player_name.strip()
    
    # Handle variations
    name_lower = name.lower().replace(" ", "_")
    
    # Extract number from various formats
    import re
    match = re.search(r'(\d+)', name)
    if match:
        num = match.group(1)
        # Return Unity format: Player_N
        return f"Player_{num}"
    
    # If no number found, return as-is with capitalization
    return name.replace(" ", "_").title()


def normalize_group_id(group_id: str) -> str:
    """
    Normalize group IDs to use normalized player names.
    
    Example:
    - "group_Player 1_Player 2" -> "group_Player_1_Player_2"
    - "solo" -> "solo"
    """
    if not group_id or group_id == "solo":
        return group_id
    
    # Split by underscore, normalize player names, rejoin
    parts = group_id.split("_")
    
    if len(parts) < 2:
        return group_id
    
    # First part is "group", rest are player names
    if parts[0] == "group":
        normalized_players = []
        current_name = ""
        
        for i, part in enumerate(parts[1:], 1):
            # Build player name (handle "Player 1" with space)
            if current_name:
                current_name += " " + part
            else:
                current_name = part
            
            # Check if this could be a complete player name
            if i == len(parts) - 1 or (i < len(parts) - 1 and parts[i+1].lower().startswith("player")):
                # Normalize and add
                normalized = normalize_player_name(current_name)
                if normalized:
                    normalized_players.append(normalized)
                current_name = ""
        
        # Handle last accumulated name
        if current_name:
            normalized = normalize_player_name(current_name)
            if normalized:
                normalized_players.append(normalized)
        
        if normalized_players:
            # Sort for consistency (group_Player_1_Player_2 == group_Player_2_Player_1)
            return "group_" + "_".join(sorted(normalized_players))
    
    return group_id


def update_active_groups(player_id: str, group_id: str):
    """
    Update the active groups tracking.
    Removes player from ALL old groups, adds to new group.
    """
    # Normalize the group ID
    normalized_group = normalize_group_id(group_id)
    
    # Remove player from ALL groups first
    for gid, members in list(active_groups.items()):
        if player_id in members:
            members.remove(player_id)
            # Remove empty groups
            if not members:
                del active_groups[gid]
                print(f"   🗑️ Removed empty group: {gid}")
    
    # Add to new group
    if normalized_group not in active_groups:
        active_groups[normalized_group] = []
    
    if player_id not in active_groups[normalized_group]:
        active_groups[normalized_group].append(player_id)
        print(f"   ➕ Added {player_id} to {normalized_group}")


def get_group_members(group_id: str) -> list[str]:
    """Get list of players in a group"""
    normalized_group = normalize_group_id(group_id)
    return active_groups.get(normalized_group, [])


def get_all_groups_except(exclude_group_id: str) -> dict[str, list[str]]:
    """Get all groups except the specified one"""
    normalized_exclude = normalize_group_id(exclude_group_id)
    return {gid: members for gid, members in active_groups.items() 
            if gid != normalized_exclude and len(members) > 0}  # Only non-empty groups


def choose_impostor_disguise(target_group_id: str) -> Optional[str]:
    """
    Choose which player the impostor should disguise as.
    
    Rules:
    1. Must be from a DIFFERENT group than target_group_id
    2. Must be a currently ACTIVE player (in this session)
    3. Randomly select from available players
    
    Returns: Normalized player name or None
    """
    normalized_target = normalize_group_id(target_group_id)
    
    if normalized_target not in active_groups:
        print(f"❌ Invalid target group: {normalized_target}")
        print(f"   Available groups: {list(active_groups.keys())}")
        return None
    
    target_members = active_groups[normalized_target]
    print(f"🎯 Target group '{normalized_target}' has members: {target_members}")
    
    # Get all OTHER groups
    other_groups = get_all_groups_except(normalized_target)
    
    if not other_groups:
        print("❌ No other groups available for disguise selection")
        print(f"   Total groups: {len(active_groups)}")
        print(f"   Active groups: {active_groups}")
        return None
    
    # Collect all players NOT in target group
    available_disguises = []
    for gid, members in other_groups.items():
        available_disguises.extend(members)
    
    if not available_disguises:
        print("❌ No available players to disguise as")
        return None
    
    # Choose randomly from available players
    chosen = random.choice(available_disguises)
    
    # Find which group they're from (for logging)
    chosen_group = "unknown"
    for gid, members in other_groups.items():
        if chosen in members:
            chosen_group = gid
            break
    
    print(f"🎭 Impostor will disguise as: {chosen} (from group '{chosen_group}')")
    print(f"   Available options were: {available_disguises}")
    
    return chosen


def should_impostor_respond(
    recent_messages_count: int, 
    last_msg_player_id: str,
    message_group_id: str
) -> bool:
    """
    Decide if impostor should inject itself into conversation.
    
    Rules:
    1. Must be active and have a disguise
    2. Must be in the same group as the message
    3. Cooldown must have passed
    4. Random chance based on engagement rate
    """
    if not impostor.is_active or not impostor.disguised_as:
        print(f"   ⏸️ Impostor not responding: active={impostor.is_active}, disguised={impostor.disguised_as}")
        return False
    
    # Normalize both group IDs for comparison
    normalized_message_group = normalize_group_id(message_group_id)
    normalized_target_group = normalize_group_id(impostor.target_group_id)
    
    # Only respond to messages in the target group
    if normalized_message_group != normalized_target_group:
        print(f"   ⏸️ Wrong group: message in '{normalized_message_group}', targeting '{normalized_target_group}'")
        return False
    
    # Don't respond to own messages
    if last_msg_player_id == impostor.disguised_as:
        print(f"   ⏸️ Won't respond to own message")
        return False
    
    # Check cooldown
    time_since_last = time.time() - impostor.last_message_time
    if time_since_last < impostor.message_cooldown:
        remaining = impostor.message_cooldown - time_since_last
        print(f"   ⏸️ Cooldown: {remaining:.1f}s remaining")
        return False
    
    # Higher chance to respond during active conversation
    base_chance = impostor.conversation_engagement
    if recent_messages_count > 2:
        base_chance += 0.2
    
    should_respond = random.random() < base_chance
    if should_respond:
        print(f"   🎲 Impostor decided to respond (chance was {base_chance:.1%})")
    else:
        print(f"   🎲 Impostor skipped response (chance was {base_chance:.1%})")
    
    return should_respond


@app.post("/chat")
def chat_endpoint(
    player_id: str = Body(...),
    message: str = Body(...),
    group_id: str = Body("solo"),
):
    """
    Main chat endpoint - receives player messages with group info.
    """
    start_time = time.time()
    
    # Normalize player name and group ID
    normalized_player = normalize_player_name(player_id)
    normalized_group = normalize_group_id(group_id)
    
    print(f"\n{'='*60}")
    print(f"📨 Message received:")
    print(f"   Player: '{player_id}' -> '{normalized_player}'")
    print(f"   Group: '{group_id}' -> '{normalized_group}'")
    print(f"   Message: '{message}'")
    
    # Track active players and groups
    active_players.add(normalized_player)
    update_active_groups(normalized_player, normalized_group)
    
    # Update recent history
    if normalized_player not in recent_history:
        recent_history[normalized_player] = []
    recent_history[normalized_player].append(message)
    if len(recent_history[normalized_player]) > RECENT_MSG_LIMIT:
        recent_history[normalized_player] = recent_history[normalized_player][-RECENT_MSG_LIMIT:]
    
    # Store in database with normalized names
    try:
        add_player_message_with_group(
            text=message,
            player_id=normalized_player,
            round_id="r1",
            group_id=normalized_group,
            timestamp=datetime.now(timezone.utc).isoformat(),
        )
    except Exception as e:
        print(f"❌ Failed to store message: {e}")
    
    # Debug: Show current state
    print(f"\n📊 Current state:")
    print(f"   Active players: {sorted(active_players)}")
    print(f"   Active groups: {active_groups}")
    print(f"   Impostor active: {impostor.is_active}")
    if impostor.is_active:
        print(f"   Impostor disguised as: {impostor.disguised_as}")
        print(f"   Impostor target group: {impostor.target_group_id}")
    
    # Check if impostor should respond
    impostor_response = None
    if impostor.is_active and impostor.target_group_id:
        print(f"\n🤔 Checking if impostor should respond...")
        
        # Get recent messages from this group's conversation
        group_members = get_group_members(normalized_group)
        recent_count = sum(
            len(recent_history.get(member, []))
            for member in group_members
        )
        
        if should_impostor_respond(recent_count, normalized_player, normalized_group):
            print(f"🤖 Generating impostor response...")
            
            try:
                # Build conversation context
                conversation_buffer = []
                for member in group_members:
                    for msg in recent_history.get(member, [])[-10:]:
                        conversation_buffer.append(f"{member}: {msg}")
                
                # Get style history for the player being imitated
                style_history = recent_history.get(impostor.disguised_as, [])
                
                if not style_history:
                    print(f"⚠️ No style history for {impostor.disguised_as}, using generic style")
                
                # Generate impostor reply
                impostor_message = generate_npc_reply(
                    player_text="\n".join(conversation_buffer[-5:]),  # Last 5 messages
                    round_id="r1",
                    imitate_player_id=impostor.disguised_as,
                    recent_msgs=style_history if style_history else ["hey", "what's up"]
                )
                
                if impostor_message:
                    # Store impostor message in database
                    impostor_id = f"impostor_{impostor.disguised_as}"
                    add_player_message_with_group(
                        text=impostor_message,
                        player_id=impostor_id,
                        round_id="r1",
                        group_id=normalized_group,
                        timestamp=datetime.now(timezone.utc).isoformat(),
                    )
                    
                    impostor_response = {
                        "player_id": impostor.disguised_as,
                        "message": impostor_message,
                        "timestamp": datetime.now(timezone.utc).isoformat(),
                    }
                    
                    impostor.last_message_time = time.time()
                    print(f"✅ Impostor response: '{impostor_message}'")
                else:
                    print(f"⚠️ No impostor message generated")
            
            except Exception as e:
                print(f"❌ Error generating impostor response: {e}")
                import traceback
                traceback.print_exc()
    
    elapsed = time.time() - start_time
    print(f"⏱️ Request processed in {elapsed:.2f}s")
    print(f"{'='*60}\n")
    
    response = {
        "player_id": normalized_player,
        "message": message,
        "timestamp": datetime.now(timezone.utc).isoformat(),
    }
    
    if impostor_response:
        response["impostor_message"] = impostor_response
    
    return response


@app.post("/impostor/activate")
def activate_impostor(
    target_group_id: str = Body(...),
    engagement_rate: float = Body(0.5),
):
    """
    Activate impostor for a specific group.
    Unity will call this when spawning an impostor alien near a group.
    
    target_group_id: The group the impostor is approaching (e.g., "group_Player_1_Player_2")
    engagement_rate: How often impostor responds (0.0 to 1.0)
    """
    print(f"\n{'='*60}")
    print(f"🚀 IMPOSTOR ACTIVATION REQUEST")
    print(f"   Raw target_group_id: '{target_group_id}'")
    
    # Normalize group ID
    normalized_target = normalize_group_id(target_group_id)
    print(f"   Normalized target: '{normalized_target}'")
    
    # Validate group exists
    if normalized_target not in active_groups:
        print(f"❌ Group not found!")
        print(f"   Looking for: '{normalized_target}'")
        print(f"   Available groups: {list(active_groups.keys())}")
        return {
            "success": False,
            "message": f"Group '{normalized_target}' not found",
            "active_groups": list(active_groups.keys()),
            "all_groups_data": active_groups,
        }
    
    # Choose which player to disguise as
    disguise = choose_impostor_disguise(normalized_target)
    
    if not disguise:
        return {
            "success": False,
            "message": "Could not find suitable player to disguise as",
            "active_groups": active_groups,
        }
    
    # Activate impostor
    impostor.disguised_as = disguise
    impostor.target_group_id = normalized_target
    impostor.is_active = True
    impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
    impostor.last_message_time = time.time() - impostor.message_cooldown  # Allow immediate response
    
    target_members = get_group_members(normalized_target)
    
    print(f"✅ IMPOSTOR ACTIVATED")
    print(f"   Disguised as: {impostor.disguised_as}")
    print(f"   Target group: {impostor.target_group_id}")
    print(f"   Group members: {target_members}")
    print(f"   Engagement rate: {impostor.conversation_engagement}")
    print(f"{'='*60}\n")
    
    return {
        "success": True,
        "disguised_as": impostor.disguised_as,
        "target_group_id": impostor.target_group_id,
        "target_group_members": target_members,
        "engagement_rate": impostor.conversation_engagement,
        "all_active_groups": active_groups,
    }


@app.post("/impostor/deactivate")
def deactivate_impostor():
    """Deactivate the impostor AI."""
    old_disguise = impostor.disguised_as
    old_group = impostor.target_group_id
    
    impostor.reset()
    
    print(f"\n🛑 IMPOSTOR DEACTIVATED")
    print(f"   Was disguised as: {old_disguise}")
    print(f"   Was targeting group: {old_group}\n")
    
    return {
        "success": True,
        "message": f"Impostor deactivated (was {old_disguise} in group {old_group})",
    }


@app.get("/impostor/status")
def impostor_status():
    """Get current impostor status."""
    return {
        "is_active": impostor.is_active,
        "disguised_as": impostor.disguised_as,
        "target_group_id": impostor.target_group_id,
        "target_group_members": get_group_members(impostor.target_group_id) if impostor.target_group_id else [],
        "engagement_rate": impostor.conversation_engagement,
        "cooldown_remaining": max(
            0, impostor.message_cooldown - (time.time() - impostor.last_message_time)
        ),
    }


@app.get("/groups/active")
def get_active_groups_endpoint():
    """Get all currently active groups."""
    return {
        "active_groups": active_groups,
        "group_count": len(active_groups),
    }


@app.get("/players/active")
def get_active_players():
    """Get list of currently active players."""
    return {
        "active_players": sorted(list(active_players)),
        "count": len(active_players),
    }


@app.post("/session/reset")
def reset_session():
    """Reset the current session (clear active players and groups)."""
    active_players.clear()
    active_groups.clear()
    recent_history.clear()
    impostor.reset()
    
    print("\n🔄 SESSION RESET\n")
    
    return {
        "success": True,
        "message": "Session reset complete (players, groups, and history cleared)",
    }


@app.post("/database/clear")
def clear_database():
    """
    DANGER: Clears ALL stored messages and memories from ChromaDB.
    This action cannot be undone!
    """
    try:
        player_messages.delete(where={})
        npc_memory.delete(where={})
        
        active_players.clear()
        active_groups.clear()
        recent_history.clear()
        impostor.reset()
        
        print("🗑️ DATABASE CLEARED: All messages and memories deleted\n")
        return {
            "success": True,
            "message": "All ChromaDB data cleared successfully",
            "collections_cleared": ["player_messages", "npc_memory"],
        }
    except Exception as e:
        print(f"❌ Error clearing database: {e}")
        return {
            "success": False,
            "message": f"Failed to clear database: {str(e)}",
        }


@app.get("/database/inspect")
def inspect_database():
    """Debug endpoint to see what's stored in ChromaDB."""
    try:
        results = player_messages.get(limit=200)
        player_ids = set()
        message_count = {}
        group_ids = set()
        
        if results and results.get("metadatas"):
            for meta in results["metadatas"]:
                if meta and "player_id" in meta:
                    pid = meta["player_id"]
                    player_ids.add(pid)
                    message_count[pid] = message_count.get(pid, 0) + 1
                if meta and "group_id" in meta:
                    group_ids.add(meta["group_id"])
        
        return {
            "total_messages": len(results.get("ids", [])),
            "unique_player_ids": sorted(list(player_ids)),
            "unique_group_ids": sorted(list(group_ids)),
            "message_count_per_player": message_count,
            "currently_active_players": sorted(list(active_players)),
            "currently_active_groups": active_groups,
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
        "impostor_disguised_as": impostor.disguised_as,
        "impostor_target_group": impostor.target_group_id,
        "active_players": sorted(list(active_players)),
        "active_groups": active_groups,
    }


if __name__ == "__main__":
    print("🚀 Starting Impostor Chat Server (FULLY FIXED VERSION)...")
    print("📍 Server URL: http://0.0.0.0:8000")
    print("🔧 Key Features:")
    print("  ✅ Normalized player names (Player_1, Player_2, etc.)")
    print("  ✅ Normalized group IDs")
    print("  ✅ Real-time group tracking")
    print("  ✅ Automatic group cleanup")
    print("  ✅ Verbose logging for debugging")
    print("\n🔧 API Endpoints:")
    print("  POST /chat - Send player messages with group info")
    print("  POST /impostor/activate - Activate impostor for a group")
    print("  POST /impostor/deactivate - Deactivate impostor")
    print("  GET /impostor/status - Check impostor status")
    print("  GET /groups/active - List active groups")
    print("  GET /players/active - List active players")
    print("  POST /session/reset - Reset session")
    print("  POST /database/clear - ⚠️ CLEAR ALL DATA")
    print("  GET /database/inspect - 🔍 DEBUG: See stored data")
    print("  GET / - Health check")
    print("\n⚠️ Make sure Ollama is running: ollama serve\n")
    uvicorn.run(app, host="0.0.0.0", port=8000)