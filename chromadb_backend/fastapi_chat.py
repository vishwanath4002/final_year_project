# fastapi_chat.py - REDESIGNED VERSION (Distance-Based Impostor)

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

# Track all groups (no more "active players" tracking)
all_groups: dict[str, list[str]] = {}  # group_id -> [player1, player2, ...]


# Impostor state
class ImpostorState:
    def __init__(self):
        self.disguised_as: Optional[str] = None
        self.target_group_id: Optional[str] = None
        self.is_active: bool = False
        self.last_message_time: float = 0
        self.message_cooldown: float = 8.0
        self.conversation_engagement: float = 0.6

    def reset(self):
        """Reset impostor state"""
        self.disguised_as = None
        self.target_group_id = None
        self.is_active = False
        self.last_message_time = 0


impostor = ImpostorState()


def normalize_player_name(player_name: str) -> str:
    """
    Normalize player names to Unity format: Player_1, Player_2, etc.
    """
    if not player_name:
        return ""
    
    name = player_name.strip()
    
    import re
    match = re.search(r'(\d+)', name)
    if match:
        num = match.group(1)
        return f"Player_{num}"
    
    return name.replace(" ", "_").title()


def normalize_group_id(group_id: str) -> str:
    """
    Normalize group IDs: "group_Player 1_Player 2" -> "group_Player_1_Player_2"
    """
    if not group_id or group_id == "solo":
        return group_id
    
    parts = group_id.split("_")
    
    if len(parts) < 2:
        return group_id
    
    if parts[0] == "group":
        normalized_players = []
        current_name = ""
        
        for i, part in enumerate(parts[1:], 1):
            if current_name:
                current_name += " " + part
            else:
                current_name = part
            
            if i == len(parts) - 1 or (i < len(parts) - 1 and parts[i+1].lower().startswith("player")):
                normalized = normalize_player_name(current_name)
                if normalized:
                    normalized_players.append(normalized)
                current_name = ""
        
        if current_name:
            normalized = normalize_player_name(current_name)
            if normalized:
                normalized_players.append(normalized)
        
        if normalized_players:
            return "group_" + "_".join(sorted(normalized_players))
    
    return group_id


def update_groups(player_id: str, group_id: str):
    """
    Update group membership - simplified version.
    Just tracks which group each player is in currently.
    """
    normalized_group = normalize_group_id(group_id)
    
    # Remove player from all other groups
    for gid, members in list(all_groups.items()):
        if player_id in members and gid != normalized_group:
            members.remove(player_id)
            if not members:
                del all_groups[gid]
                print(f"   🗑️ Removed empty group: {gid}")
    
    # Add to current group
    if normalized_group not in all_groups:
        all_groups[normalized_group] = []
    
    if player_id not in all_groups[normalized_group]:
        all_groups[normalized_group].append(player_id)


def get_group_members(group_id: str) -> list[str]:
    """Get members of a specific group"""
    normalized = normalize_group_id(group_id)
    return all_groups.get(normalized, [])


def get_all_player_names() -> list[str]:
    """Get all unique players across all groups"""
    players = set()
    for members in all_groups.values():
        players.update(members)
    return sorted(list(players))


def choose_impostor_disguise_from_farthest_group(target_group_id: str) -> Optional[str]:
    """
    NEW LOGIC: Choose disguise from the FARTHEST group from target.
    
    Unity will tell us which group is farthest when calling /impostor/activate.
    We just need to pick any player NOT in the target group.
    
    This is simpler and more reliable than tracking "active" players.
    """
    normalized_target = normalize_group_id(target_group_id)
    
    if normalized_target not in all_groups:
        print(f"❌ Target group not found: {normalized_target}")
        return None
    
    target_members = all_groups[normalized_target]
    all_players = get_all_player_names()
    
    # Get players NOT in target group
    available = [p for p in all_players if p not in target_members]
    
    if not available:
        print(f"❌ No players available outside target group")
        print(f"   Target members: {target_members}")
        print(f"   All players: {all_players}")
        return None
    
    chosen = random.choice(available)
    
    # Find which group they're in
    chosen_group = "unknown"
    for gid, members in all_groups.items():
        if chosen in members:
            chosen_group = gid
            break
    
    print(f"🎭 Impostor disguising as: {chosen}")
    print(f"   From group: {chosen_group}")
    print(f"   Available options: {available}")
    
    return chosen


def should_impostor_respond(
    recent_messages_count: int, 
    last_msg_player_id: str,
    message_group_id: str
) -> bool:
    """Decide if impostor should respond"""
    if not impostor.is_active or not impostor.disguised_as:
        return False
    
    normalized_msg_group = normalize_group_id(message_group_id)
    normalized_target = normalize_group_id(impostor.target_group_id)
    
    if normalized_msg_group != normalized_target:
        return False
    
    if last_msg_player_id == impostor.disguised_as:
        return False
    
    time_since_last = time.time() - impostor.last_message_time
    if time_since_last < impostor.message_cooldown:
        return False
    
    base_chance = impostor.conversation_engagement
    if recent_messages_count > 2:
        base_chance = min(0.9, base_chance + 0.2)
    
    return random.random() < base_chance


@app.post("/chat")
def chat_endpoint(
    player_id: str = Body(...),
    message: str = Body(...),
    group_id: str = Body("solo"),
):
    """Main chat endpoint"""
    start_time = time.time()
    
    normalized_player = normalize_player_name(player_id)
    normalized_group = normalize_group_id(group_id)
    
    print(f"\n{'='*60}")
    print(f"📨 Message from {normalized_player} in {normalized_group}")
    print(f"   Content: '{message}'")
    
    # Update groups
    update_groups(normalized_player, normalized_group)
    
    # Update history
    if normalized_player not in recent_history:
        recent_history[normalized_player] = []
    recent_history[normalized_player].append(message)
    if len(recent_history[normalized_player]) > RECENT_MSG_LIMIT:
        recent_history[normalized_player] = recent_history[normalized_player][-RECENT_MSG_LIMIT:]
    
    # Store in database
    try:
        add_player_message_with_group(
            text=message,
            player_id=normalized_player,
            round_id="r1",
            group_id=normalized_group,
            timestamp=datetime.now(timezone.utc).isoformat(),
        )
    except Exception as e:
        print(f"❌ DB error: {e}")
    
    # Show state
    print(f"📊 Groups: {all_groups}")
    print(f"   Impostor: {'✓ Active' if impostor.is_active else '✗ Inactive'}", end="")
    if impostor.is_active:
        print(f" (as {impostor.disguised_as}, targeting {impostor.target_group_id})")
    else:
        print()
    
    # Check impostor response
    impostor_response = None
    if impostor.is_active and impostor.target_group_id:
        group_members = get_group_members(normalized_group)
        recent_count = sum(len(recent_history.get(m, [])) for m in group_members)
        
        if should_impostor_respond(recent_count, normalized_player, normalized_group):
            print(f"🤖 Generating impostor response...")
            
            try:
                # Build conversation
                conversation = []
                for member in group_members:
                    for msg in recent_history.get(member, [])[-10:]:
                        conversation.append(f"{member}: {msg}")
                
                style_history = recent_history.get(impostor.disguised_as, ["hey", "what's up"])
                
                impostor_message = generate_npc_reply(
                    player_text="\n".join(conversation[-5:]),
                    round_id="r1",
                    imitate_player_id=impostor.disguised_as,
                    recent_msgs=style_history
                )
                
                if impostor_message:
                    # Store
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
                    print(f"✅ Response: '{impostor_message}'")
            
            except Exception as e:
                print(f"❌ Generation error: {e}")
    
    elapsed = time.time() - start_time
    print(f"⏱️ {elapsed:.2f}s")
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
    farthest_group_id: Optional[str] = Body(None),  # NEW: Unity tells us farthest group
    engagement_rate: float = Body(0.6),
):
    """
    Activate impostor for a specific group.
    
    NEW APPROACH:
    - Unity determines which group is farthest
    - We just pick any player from that farthest group
    - Simpler and more reliable
    """
    print(f"\n{'='*60}")
    print(f"🚀 IMPOSTOR ACTIVATION")
    
    normalized_target = normalize_group_id(target_group_id)
    print(f"   Target: {normalized_target}")
    
    # Validate target exists
    if normalized_target not in all_groups:
        print(f"❌ Target group not found!")
        print(f"   Available: {list(all_groups.keys())}")
        return {
            "success": False,
            "message": f"Target group not found: {normalized_target}",
            "available_groups": list(all_groups.keys()),
        }
    
    # Choose disguise
    disguise = choose_impostor_disguise_from_farthest_group(normalized_target)
    
    if not disguise:
        return {
            "success": False,
            "message": "Could not find suitable disguise",
            "all_groups": all_groups,
        }
    
    # Activate
    impostor.disguised_as = disguise
    impostor.target_group_id = normalized_target
    impostor.is_active = True
    impostor.conversation_engagement = max(0.1, min(1.0, engagement_rate))
    impostor.last_message_time = time.time() - impostor.message_cooldown
    
    target_members = get_group_members(normalized_target)
    
    print(f"✅ ACTIVATED")
    print(f"   Disguised as: {impostor.disguised_as}")
    print(f"   Target members: {target_members}")
    print(f"   Engagement: {impostor.conversation_engagement:.0%}")
    print(f"{'='*60}\n")
    
    return {
        "success": True,
        "disguised_as": impostor.disguised_as,
        "target_group_id": impostor.target_group_id,
        "target_group_members": target_members,
        "engagement_rate": impostor.conversation_engagement,
    }


@app.post("/impostor/deactivate")
def deactivate_impostor():
    """Deactivate impostor"""
    old = impostor.disguised_as
    impostor.reset()
    print(f"🛑 Impostor deactivated (was {old})\n")
    return {"success": True, "message": f"Deactivated (was {old})"}


@app.get("/impostor/status")
def impostor_status():
    """Get impostor status"""
    return {
        "is_active": impostor.is_active,
        "disguised_as": impostor.disguised_as,
        "target_group_id": impostor.target_group_id,
        "target_group_members": get_group_members(impostor.target_group_id) if impostor.target_group_id else [],
        "engagement_rate": impostor.conversation_engagement,
        "cooldown_remaining": max(0, impostor.message_cooldown - (time.time() - impostor.last_message_time)),
    }


@app.get("/groups")
def get_groups():
    """Get all groups"""
    return {
        "groups": all_groups,
        "group_count": len(all_groups),
        "all_players": get_all_player_names(),
    }


@app.post("/session/reset")
def reset_session():
    """Reset session"""
    all_groups.clear()
    recent_history.clear()
    impostor.reset()
    print("🔄 Session reset\n")
    return {"success": True}


@app.post("/database/clear")
def clear_database():
    """Clear database"""
    try:
        player_messages.delete(where={})
        npc_memory.delete(where={})
        all_groups.clear()
        recent_history.clear()
        impostor.reset()
        print("🗑️ Database cleared\n")
        return {"success": True}
    except Exception as e:
        return {"success": False, "error": str(e)}


@app.get("/")
def root():
    """Health check"""
    return {
        "status": "online",
        "impostor_active": impostor.is_active,
        "groups": all_groups,
        "all_players": get_all_player_names(),
    }


if __name__ == "__main__":
    print("🚀 Impostor Chat Server - REDESIGNED")
    print("📍 http://0.0.0.0:8000")
    print("\n✨ Changes:")
    print("  • No more 'active players' tracking")
    print("  • Simplified group-based logic")
    print("  • Distance calculation done in Unity")
    print("  • Faster responses (8s cooldown, 60% engagement)")
    print("\n🔧 Endpoints:")
    print("  POST /chat")
    print("  POST /impostor/activate")
    print("  GET /impostor/status")
    print("  GET /groups")
    print("  POST /session/reset")
    print("  POST /database/clear")
    print("\n")
    uvicorn.run(app, host="0.0.0.0", port=8000)