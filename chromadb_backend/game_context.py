# game_context.py - GAME WORLD CONTEXT & RULES

GAME_WORLD_RULES = """**GAME WORLD - Chernobyl Survival (Alien Impostor)**

**Setting**: Multiplayer survival in alien-infested Chernobyl Exclusion Zone. Proximity chat only.

**VALID LOCATIONS** (ONLY these exist):
- Sheds (wood spawns here)
- Barns (wood spawns here)
- Greenhouse (mushrooms spawn here)
- Church (food can drop-off point)
- Pavilion (meeting/safe area)

**VALID ITEMS** (hold ONE at a time):
- Woods/logs (collect from sheds/barns, then burn)
- Mushrooms (collect from greenhouse, then burn)
- Food cans (collect and deliver to church)

**VALID ACTIONS**:
- Collecting wood from sheds/barns
- Burning wood (at any location)
- Collecting mushrooms from greenhouse
- Burning mushrooms
- Bringing food cans to church
- Walking/running between locations
- Shooting aliens with gun (LIMITED ammo - mention being low/reloading)
- Talking to nearby players (proximity chat)
- Accusing/defending/gossiping with players

**COMBAT RULES**:
- Everyone has gun with LIMITED ammo (mention reload/conserve)
- Shoot aliens when they attack
- Players can shoot each other (PvP/friendly fire)
- If YOU get hit: flee and stop talking

**THINGS THAT DO NOT EXIST** (NEVER mention):
- Day/night cycles, time of day
- Crouching, stealth, hiding
- Knives, melee weapons, swords
- Multiple item inventory slots
- Caves, underground areas, tunnels
- NPCs, researchers, scientists
- Loot boxes, item spawns (other than wood/mushrooms/cans)
- Reactor, radiation zones (beyond general Chernobyl)
- Animals (only aliens exist)
- Upgrades, perks, skills"""

IMPOSTOR_STRATEGY_GUIDE = """**IMPOSTOR BEHAVIOR RULES**:

**Communication Style**:
- Keep messages SHORT (1-2 sentences max)
- Sound casual like a real player ("sup", "nah", "fr", "sus")
- Match the disguised player's style
- Don't over-explain or sound robotic

**What to Talk About**:
- Tasks: "collecting wood", "burning mushrooms", "taking cans to church"
- Locations: "at the sheds", "heading to church", "saw someone at pavilion"
- Ammo: "running low on ammo", "need to reload soon", "shot some aliens"
- Other players: "anyone seen [player]?", "where's everyone at?"

**Valid Alibis** (use these when accused):
- "I was collecting wood at the sheds"
- "I was bringing food cans to the church"
- "I was burning mushrooms"
- "I was shooting aliens near [location]"
- "My ammo's low, I was just gathering supplies"

**NEVER Say**:
- Anything about game mechanics not in the rules
- "I was hiding" / "I was crouching"
- "I have a knife" / "I found loot"
- "It's daytime/nighttime"
- Made-up locations or items"""

def get_response_templates(mode: str) -> list:
    """
    Get appropriate response templates for each strategy mode
    
    Args:
        mode: 'gather_info', 'seed_doubt', 'defend_self', 'accuse_other', 'build_trust', 'casual'
    """
    templates = {
        'gather_info': [
            "Where's everyone at?",
            "Anyone collecting wood?",
            "What's everyone doing?",
            "See any aliens?",
            "Anyone burning mushrooms?",
            "Who's got the food cans?",
            "How's everyone's ammo?",
            "Anyone at the church?",
        ],
        
        'seed_doubt': [
            "Idk, seems kinda sus",
            "[player] acting weird tbh",
            "Did anyone see [player] at [location]?",
            "Where was [player] again?",
            "Not sure about [player]",
            "[player] been real quiet",
        ],
        
        'defend_self': [
            "I was at the sheds collecting wood",
            "I was taking cans to church",
            "I was burning mushrooms",
            "My ammo's low, just gathering supplies",
            "I was shooting aliens near [location]",
            "Wasn't me, I was at [location]",
            "Why would I do that?",
            "Where's your proof?",
        ],
        
        'accuse_other': [
            "[player] seems sus",
            "[player] wasn't where they said",
            "I think it's [player]",
            "Why's [player] so quiet?",
            "[player] acting weird",
        ],
        
        'build_trust': [
            "Need help with anything?",
            "Let's work together",
            "I got some wood at the sheds",
            "Heading to church with cans",
            "Just shot some aliens",
            "Low on ammo but I'm good",
        ],
        
        'casual': [
            "Hey",
            "What's up",
            "Yeah",
            "Nah",
            "Cool",
            "Alright",
            "Got it",
            "Same",
        ]
    }
    
    return templates.get(mode, templates['casual'])

def get_game_context_prompt(disguise_name: str, style_summary: str, strategy_mode: str) -> str:
    """
    Generate the core game context prompt for the LLM
    
    This goes at the START of every LLM prompt to keep responses in-context
    """
    return f"""You are {disguise_name} in a Chernobyl survival game with aliens.

**GAME RULES** (follow STRICTLY):
- Locations: Sheds, Barns, Greenhouse, Church, Pavilion (ONLY these)
- Items: Wood (from sheds/barns), Mushrooms (from greenhouse), Food cans (take to church)
- Hold ONE item at a time
- Everyone has gun with LIMITED ammo
- Shoot aliens, can shoot other players too

**YOUR STYLE**: {style_summary}

**NEVER mention**: day/night, crouching, knives, inventory, caves, NPCs, loot, reactor, animals, upgrades
**NEVER use**: emojis, emoticons, or any special symbols. Plain text only.

**RESPOND**: 1-2 short casual sentences as {disguise_name}. Sound like a real player. Plain English only."""

def validate_response(response: str) -> tuple[bool, str]:
    """
    Validate that a response follows game rules
    
    Returns: (is_valid, error_message)
    """
    response_lower = response.lower()
    
    # Check for forbidden concepts
    forbidden = {
        'day': 'time cycles',
        'night': 'time cycles',
        'crouch': 'crouching',
        'hide': 'hiding mechanic',
        'knife': 'melee weapons',
        'sword': 'melee weapons',
        'cave': 'underground areas',
        'tunnel': 'underground areas',
        'researcher': 'NPCs',
        'scientist': 'NPCs',
        'loot': 'loot boxes',
        'reactor': 'specific zones',
        'radiation': 'radiation mechanics',
        'inventory': 'inventory system',
        'upgrade': 'upgrade system',
        'skill': 'skill system',
        'level': 'leveling system',
    }
    
    for word, category in forbidden.items():
        if word in response_lower:
            return False, f"Mentioned forbidden concept: {category}"
    
    # Response seems valid
    return True, ""

def get_contextual_facts(conversation_buffer: list, profiles: dict) -> str:
    """
    Extract recent contextual facts from conversation for the impostor to reference
    
    Returns a string summary of what's been discussed
    """
    if not conversation_buffer:
        return "Conversation just started."
    
    # Get last 3-5 messages
    recent = conversation_buffer[-5:]
    
    facts = []
    
    for msg in recent:
        msg_lower = msg.lower()
        
        # Extract locations mentioned
        locations = ['sheds', 'barns', 'greenhouse', 'church', 'pavilion']
        for loc in locations:
            if loc in msg_lower:
                facts.append(f"Mentioned {loc}")
        
        # Extract actions mentioned
        if 'wood' in msg_lower or 'log' in msg_lower:
            facts.append("Discussed collecting wood")
        if 'mushroom' in msg_lower:
            facts.append("Discussed mushrooms")
        if 'food' in msg_lower or 'can' in msg_lower:
            facts.append("Discussed food cans")
        if 'alien' in msg_lower:
            facts.append("Talked about aliens")
        if 'ammo' in msg_lower:
            facts.append("Mentioned ammo")
    
    if not facts:
        return "General conversation."
    
    # Deduplicate and join
    facts = list(set(facts))
    return "; ".join(facts[:3])  # Max 3 facts