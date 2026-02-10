# suspicion_tracker.py - TRACK SUSPICION BETWEEN PLAYERS

from typing import Dict, List, Tuple
from collections import defaultdict
import time

class SuspicionTracker:
    """
    Tracks suspicion levels between players
    
    Scoring system:
    - Direct accusation: +3 points
    - Indirect suspicion ("seems weird"): +1 point
    - Defense of someone: -2 points (reduces suspicion)
    - Alibi provided: -1 point
    """
    
    def __init__(self):
        # suspicion_matrix[accuser][accused] = score
        self.suspicion_matrix: Dict[str, Dict[str, float]] = defaultdict(lambda: defaultdict(float))
        
        # Track reasons for suspicion
        self.suspicion_reasons: Dict[Tuple[str, str], List[str]] = defaultdict(list)
        
        # Track when suspicions were last updated
        self.last_update: Dict[Tuple[str, str], float] = {}
    
    def add_accusation(self, accuser: str, accused: str, reason: str = "", weight: float = 3.0):
        """Add a direct accusation"""
        self.suspicion_matrix[accuser][accused] += weight
        self.suspicion_reasons[(accuser, accused)].append(f"Accused: {reason}")
        self.last_update[(accuser, accused)] = time.time()
        
        print(f"   📊 Suspicion: {accuser} → {accused} (+{weight:.1f}) = {self.suspicion_matrix[accuser][accused]:.1f}")
    
    def add_suspicion(self, accuser: str, accused: str, reason: str = "", weight: float = 1.0):
        """Add indirect suspicion"""
        self.suspicion_matrix[accuser][accused] += weight
        self.suspicion_reasons[(accuser, accused)].append(f"Suspicious: {reason}")
        self.last_update[(accuser, accused)] = time.time()
    
    def add_defense(self, defender: str, defended: str, reason: str = "", weight: float = -2.0):
        """Reduce suspicion when someone defends another"""
        self.suspicion_matrix[defender][defended] += weight  # Negative weight
        self.suspicion_reasons[(defender, defended)].append(f"Defended: {reason}")
        self.last_update[(defender, defended)] = time.time()
    
    def get_most_suspected(self, limit: int = 3) -> List[Tuple[str, float]]:
        """
        Get the most suspected players overall
        
        Returns: List of (player_id, total_suspicion_score)
        """
        total_suspicion = defaultdict(float)
        
        for accuser, accused_dict in self.suspicion_matrix.items():
            for accused, score in accused_dict.items():
                if score > 0:  # Only count positive suspicion
                    total_suspicion[accused] += score
        
        # Sort by suspicion score
        sorted_suspects = sorted(total_suspicion.items(), key=lambda x: x[1], reverse=True)
        return sorted_suspects[:limit]
    
    def get_least_suspected(self, limit: int = 3) -> List[Tuple[str, float]]:
        """Get players with lowest suspicion (good targets to accuse)"""
        total_suspicion = defaultdict(float)
        
        for accuser, accused_dict in self.suspicion_matrix.items():
            for accused, score in accused_dict.items():
                total_suspicion[accused] += score
        
        # Sort by suspicion score (lowest first)
        sorted_suspects = sorted(total_suspicion.items(), key=lambda x: x[1])
        return sorted_suspects[:limit]
    
    def who_suspects(self, player_id: str) -> List[Tuple[str, float]]:
        """Get list of who suspects this player"""
        suspectors = []
        
        for accuser, accused_dict in self.suspicion_matrix.items():
            if player_id in accused_dict and accused_dict[player_id] > 0:
                suspectors.append((accuser, accused_dict[player_id]))
        
        return sorted(suspectors, key=lambda x: x[1], reverse=True)
    
    def get_suspicion_score(self, accuser: str, accused: str) -> float:
        """Get specific suspicion score"""
        return self.suspicion_matrix[accuser].get(accused, 0.0)
    
    def get_suspicion_summary(self) -> str:
        """Get human-readable summary of all suspicions"""
        if not self.suspicion_matrix:
            return "No suspicions recorded"
        
        lines = ["📊 SUSPICION SUMMARY:"]
        
        most_suspected = self.get_most_suspected(5)
        if most_suspected:
            lines.append("\n🎯 Most Suspected:")
            for player, score in most_suspected:
                suspectors = self.who_suspects(player)
                suspector_names = ', '.join([f"{name}({s:.1f})" for name, s in suspectors])
                lines.append(f"   • {player}: {score:.1f} points (by: {suspector_names})")
        
        return "\n".join(lines)
    
    def get_strategic_target(self, exclude_players: List[str] = None) -> Tuple[str, str]:
        """
        Get strategic accusation target for impostor
        
        Strategy:
        1. Pick someone who is NOT already heavily suspected (avoid bandwagon)
        2. Pick someone who hasn't accused many others (seems quiet/innocent)
        3. Prefer players with some existing suspicion (easier to build on)
        
        Returns: (target_player, reason)
        """
        exclude_players = exclude_players or []
        
        # Calculate scores for each potential target
        target_scores = {}
        
        for player in self.get_all_players():
            if player in exclude_players:
                continue
            
            # Get total suspicion on this player
            total_susp = sum(
                score for accused_dict in self.suspicion_matrix.values()
                for accused, score in accused_dict.items()
                if accused == player and score > 0
            )
            
            # Get how many times this player accused others (accusers are risky targets)
            accusations_made = sum(
                1 for accused, score in self.suspicion_matrix.get(player, {}).items()
                if score > 0
            )
            
            # Scoring:
            # - Low existing suspicion (0-2): Good target (+3)
            # - Medium suspicion (2-5): Okay target (+2)
            # - High suspicion (>5): Avoid (-5)
            # - Low accusations made: Safe target (+2)
            # - High accusations made: Risky target (-3)
            
            score = 0
            
            if total_susp < 2:
                score += 3
                reason = "appears innocent"
            elif total_susp < 5:
                score += 2
                reason = "some suspicion already"
            else:
                score -= 5
                reason = "already heavily suspected"
            
            if accusations_made < 2:
                score += 2
            else:
                score -= 3
                reason += ", but very vocal"
            
            target_scores[player] = (score, reason)
        
        if not target_scores:
            return None, "no valid targets"
        
        # Pick best target
        best_target = max(target_scores.items(), key=lambda x: x[1][0])
        return best_target[0], best_target[1][1]
    
    def get_all_players(self) -> List[str]:
        """Get list of all players tracked"""
        players = set()
        
        for accuser in self.suspicion_matrix.keys():
            players.add(accuser)
        
        for accused_dict in self.suspicion_matrix.values():
            for accused in accused_dict.keys():
                players.add(accused)
        
        return list(players)
    
    def decay_suspicion(self, decay_rate: float = 0.1):
        """
        Decay old suspicions over time
        Suspicions older than 5 minutes lose strength
        """
        current_time = time.time()
        
        for (accuser, accused), last_time in list(self.last_update.items()):
            age = current_time - last_time
            
            if age > 300:  # 5 minutes
                old_score = self.suspicion_matrix[accuser][accused]
                self.suspicion_matrix[accuser][accused] *= (1 - decay_rate)
                
                if abs(self.suspicion_matrix[accuser][accused]) < 0.1:
                    del self.suspicion_matrix[accuser][accused]
                    print(f"   🕐 Suspicion decayed: {accuser} → {accused} (was {old_score:.1f})")