# player_profiling.py - PLAYER BEHAVIOR & STATEMENT TRACKING

from dataclasses import dataclass, field
from typing import List, Dict, Optional
from datetime import datetime
from collections import deque
import json

@dataclass
class PlayerStatement:
    """A statement made by a player"""
    timestamp: str
    location: str
    statement: str
    category: str  # 'location', 'action', 'observation', 'accusation', 'defense'
    mentioned_players: List[str] = field(default_factory=list)
    mentioned_locations: List[str] = field(default_factory=list)

@dataclass
class PlayerProfile:
    """Complete profile of a player's behavior and statements"""
    player_id: str
    
    # Communication style
    avg_message_length: float = 5.0
    uses_caps: bool = False
    uses_punctuation: bool = True
    common_phrases: List[str] = field(default_factory=list)
    
    # Behavioral data
    statements: deque = field(default_factory=lambda: deque(maxlen=50))  # Last 50 statements
    locations_visited: Dict[str, int] = field(default_factory=dict)  # location -> visit count
    actions_mentioned: Dict[str, int] = field(default_factory=dict)  # action -> count
    players_mentioned: Dict[str, int] = field(default_factory=dict)  # player -> mention count
    
    # Verifiable facts (things we KNOW are true)
    verified_locations: List[tuple] = field(default_factory=list)  # (location, timestamp)
    verified_actions: List[tuple] = field(default_factory=list)  # (action, timestamp)
    
    # Suspicion data
    accused_by: Dict[str, int] = field(default_factory=dict)  # accuser -> accusation count
    accused_others: Dict[str, int] = field(default_factory=dict)  # accused -> accusation count
    
    def add_statement(self, statement: str, location: str, category: str, 
                     mentioned_players: List[str] = None, 
                     mentioned_locations: List[str] = None):
        """Add a new statement to the profile"""
        stmt = PlayerStatement(
            timestamp=datetime.utcnow().isoformat(),
            location=location,
            statement=statement,
            category=category,
            mentioned_players=mentioned_players or [],
            mentioned_locations=mentioned_locations or []
        )
        self.statements.append(stmt)
        
        # Update location visits
        if location and location != "Unknown":
            self.locations_visited[location] = self.locations_visited.get(location, 0) + 1
        
        # Update mentioned players
        for player in (mentioned_players or []):
            self.players_mentioned[player] = self.players_mentioned.get(player, 0) + 1
    
    def get_recent_statements(self, n: int = 10, category: Optional[str] = None) -> List[PlayerStatement]:
        """Get recent statements, optionally filtered by category"""
        if category:
            return [s for s in list(self.statements)[-n:] if s.category == category]
        return list(self.statements)[-n:]
    
    def get_alibi(self, time_range: tuple = None) -> List[PlayerStatement]:
        """Get statements that could serve as alibi (locations, actions, witnesses)"""
        return [s for s in self.statements if s.category in ['location', 'action', 'observation']]
    
    def update_style(self, message: str):
        """Update communication style based on message"""
        words = message.split()
        
        # Update average message length
        current_len = len(words)
        total_statements = len(self.statements)
        if total_statements > 0:
            self.avg_message_length = (self.avg_message_length * total_statements + current_len) / (total_statements + 1)
        
        # Check for caps and punctuation
        if any(word.isupper() for word in words):
            self.uses_caps = True
        if any(char in message for char in '!?.'):
            self.uses_punctuation = True
    
    def to_dict(self) -> dict:
        """Serialize profile for storage"""
        return {
            'player_id': self.player_id,
            'avg_message_length': self.avg_message_length,
            'uses_caps': self.uses_caps,
            'uses_punctuation': self.uses_punctuation,
            'statements': [
                {
                    'timestamp': s.timestamp,
                    'location': s.location,
                    'statement': s.statement,
                    'category': s.category,
                    'mentioned_players': s.mentioned_players,
                    'mentioned_locations': s.mentioned_locations
                }
                for s in list(self.statements)[-20:]  # Last 20 only
            ],
            'locations_visited': self.locations_visited,
            'actions_mentioned': self.actions_mentioned,
            'players_mentioned': self.players_mentioned,
            'accused_by': self.accused_by,
            'accused_others': self.accused_others
        }


class PlayerProfileManager:
    """Manages all player profiles"""
    
    def __init__(self):
        self.profiles: Dict[str, PlayerProfile] = {}
    
    def get_or_create_profile(self, player_id: str) -> PlayerProfile:
        """Get existing profile or create new one"""
        if player_id not in self.profiles:
            self.profiles[player_id] = PlayerProfile(player_id=player_id)
        return self.profiles[player_id]
    
    def analyze_message(self, player_id: str, message: str, location: str = "Unknown") -> PlayerStatement:
        """
        Analyze message and extract structured information
        
        Categories:
        - location: Player mentions where they are/were
        - action: Player describes what they did
        - observation: Player saw something/someone
        - accusation: Player accuses another player
        - defense: Player defends themselves
        """
        profile = self.get_or_create_profile(player_id)
        
        msg_lower = message.lower()
        
        # Detect category
        category = "general"
        mentioned_players = []
        mentioned_locations = []
        
        # Location keywords
        LOCATIONS = ["pavillion", "church", "mansion", "greenhouse", "sheds"]
        for loc in LOCATIONS:
            if loc.lower() in msg_lower:
                mentioned_locations.append(loc)
                category = "location"
        
        # Action keywords
        if any(word in msg_lower for word in ["collected", "collecting", "found", "fighting", "burned", "went to"]):
            category = "action"
        
        # Observation keywords
        if any(word in msg_lower for word in ["saw", "seen", "noticed", "spotted", "watched"]):
            category = "observation"
        
        # Accusation keywords
        if any(word in msg_lower for word in ["suspicious", "lying", "liar", "impostor", "alien", "not them"]):
            category = "accusation"
        
        # Defense keywords
        if any(word in msg_lower for word in ["wasn't me", "i didn't", "i was at", "i have proof", "i swear"]):
            category = "defense"
        
        # Extract mentioned players (simple heuristic - look for "Player N" patterns)
        import re
        player_mentions = re.findall(r'Player\s*\d+', message, re.IGNORECASE)
        mentioned_players = list(set(player_mentions))
        
        # Add statement
        profile.add_statement(
            statement=message,
            location=location,
            category=category,
            mentioned_players=mentioned_players,
            mentioned_locations=mentioned_locations
        )
        
        # Update style
        profile.update_style(message)
        
        return profile.statements[-1]
    
    def get_player_summary(self, player_id: str) -> str:
        """Get text summary of player's profile"""
        if player_id not in self.profiles:
            return f"{player_id}: No data"
        
        profile = self.profiles[player_id]
        
        parts = [f"Profile of {player_id}:"]
        
        # Style
        parts.append(f"  Style: {'Brief' if profile.avg_message_length < 5 else 'Detailed'} messages")
        
        # Recent locations
        if profile.locations_visited:
            top_locs = sorted(profile.locations_visited.items(), key=lambda x: x[1], reverse=True)[:3]
            parts.append(f"  Locations: {', '.join([f'{loc}({count})' for loc, count in top_locs])}")
        
        # Recent actions
        if profile.actions_mentioned:
            top_actions = sorted(profile.actions_mentioned.items(), key=lambda x: x[1], reverse=True)[:3]
            parts.append(f"  Actions: {', '.join([f'{act}({count})' for act, count in top_actions])}")
        
        # Suspicion
        if profile.accused_by:
            accusers = ', '.join(profile.accused_by.keys())
            parts.append(f"  Accused by: {accusers}")
        
        if profile.accused_others:
            accused = ', '.join(profile.accused_others.keys())
            parts.append(f"  Accused: {accused}")
        
        return "\n".join(parts)
    
    def get_all_profiles_summary(self) -> str:
        """Get summary of all players"""
        if not self.profiles:
            return "No player data"
        
        summaries = [self.get_player_summary(pid) for pid in self.profiles.keys()]
        return "\n\n".join(summaries)