# deception_strategy.py - STRATEGIC DECEPTION ENGINE (GAME-CONTEXTUAL)

from typing import Dict, List, Optional, Tuple
from enum import Enum
import random
from game_context import get_response_templates

class DeceptionMode(Enum):
    """Different modes of deceptive behavior"""
    GATHER_INFO = "gather_info"
    SEED_DOUBT = "seed_doubt"
    DEFEND_SELF = "defend_self"
    ACCUSE_OTHER = "accuse_other"
    BUILD_TRUST = "build_trust"
    LIE_LOW = "lie_low"


class DeceptionStrategy:
    """Strategic deception with game-contextual responses"""
    
    def __init__(self, disguised_as: str):
        self.disguised_as = disguised_as
        self.current_mode = DeceptionMode.GATHER_INFO
        self.has_been_accused = False
        self.accusation_count = 0
        self.last_mode_change = 0
        self.aggression_level = 0.3
        self.info_threshold = 5
        
    def decide_mode(self, context: Dict) -> Tuple[DeceptionMode, str]:
        """Decide what mode to operate in based on context"""
        has_been_accused = context.get('has_been_accused', False)
        suspicion_on_self = context.get('suspicion_on_self', 0.0)
        available_info = context.get('available_info', 0)
        other_suspicions = context.get('other_suspicions', [])
        
        if has_been_accused or suspicion_on_self > 3.0:
            self.current_mode = DeceptionMode.DEFEND_SELF
            return self.current_mode, "being accused - must defend"
        
        if available_info < self.info_threshold:
            self.current_mode = DeceptionMode.GATHER_INFO
            return self.current_mode, f"low info ({available_info} facts) - gathering intel"
        
        if not other_suspicions or max([s[1] for s in other_suspicions]) < 2.0:
            self.current_mode = DeceptionMode.SEED_DOUBT
            return self.current_mode, "weak suspicions - seeding doubt"
        
        if other_suspicions and max([s[1] for s in other_suspicions]) >= 3.0:
            self.current_mode = DeceptionMode.ACCUSE_OTHER
            return self.current_mode, "strong suspicion exists - joining accusation"
        
        self.current_mode = DeceptionMode.BUILD_TRUST
        return self.current_mode, "no immediate threats - building trust"
    
    def generate_defense(self, accusation: str, player_profile: Dict, conversation_history: List[str]) -> str:
        """Generate game-contextual defense"""
        # Use template responses that fit the game
        defenses = [
            "I was collecting wood at the sheds",
            "I was taking cans to church",
            "I was burning mushrooms",
            "My ammo's low, just getting supplies",
            "Wasn't me, I was at the sheds",
            "Where's your proof?",
        ]
        return random.choice(defenses)
    
    def generate_accusation(self, target: str, profiles: Dict, suspicion_tracker) -> Tuple[str, str]:
        """Generate game-contextual accusation"""
        accusations = [
            f"{target} seems sus",
            f"{target} acting weird",
            f"I think it's {target}",
            f"{target} been real quiet",
        ]
        return random.choice(accusations), "suspicion"
    
    def generate_doubt_seed(self, target: str, profiles: Dict, facts: List[str]) -> str:
        """Generate subtle game-contextual doubt"""
        doubts = [
            f"Did anyone see {target} at the sheds?",
            f"Where was {target} again?",
            f"{target} acting kinda sus tbh",
            f"Not sure about {target}",
        ]
        return random.choice(doubts)
    
    def generate_question(self, target: Optional[str], context: str) -> str:
        """Generate game-contextual questions"""
        if target:
            questions = [
                f"{target}, where you at?",
                f"{target}, you collecting wood?",
                f"Did you see aliens, {target}?",
                f"{target}, you got ammo?",
            ]
        else:
            questions = [
                "Where's everyone at?",
                "Anyone see aliens?",
                "What's everyone doing?",
                "Anyone burning mushrooms?",
                "Who's collecting wood?",
            ]
        return random.choice(questions)
    
    def should_respond_to_message(self, message: str, context: Dict) -> Tuple[bool, str]:
        """
        Decide if and how to respond to a message
        
        Returns: (should_respond, response_type)
        """
        msg_lower = message.lower()
        
        # Always respond to direct accusations
        if any(word in msg_lower for word in ['suspicious', 'lying', 'impostor', 'alien']):
            if self.disguised_as.lower() in msg_lower:
                return True, "defend"
        
        # Respond to questions directed at us
        if self.disguised_as.lower() in msg_lower and '?' in message:
            return True, "answer_question"
        
        # Opportunistically seed doubt (30% chance)
        if random.random() < 0.3:
            return True, "seed_doubt"
        
        # Otherwise, stay quiet
        return False, "stay_quiet"
    
    def get_response_strategy(self, message: str, profiles: Dict, suspicion_tracker, conversation_history: List[str]) -> str:
        """Main entry: Generate strategic response using game context"""
        msg_lower = message.lower()
        
        who_suspects_me = suspicion_tracker.who_suspects(self.disguised_as)
        my_suspicion = sum([score for _, score in who_suspects_me])
        
        available_facts = sum([len(p.get('statements', [])) for p in profiles.values()])
        other_suspicions = suspicion_tracker.get_most_suspected(3)
        
        context = {
            'has_been_accused': len(who_suspects_me) > 0,
            'suspicion_on_self': my_suspicion,
            'available_info': available_facts,
            'other_suspicions': other_suspicions
        }
        
        mode, reason = self.decide_mode(context)
        print(f"   🎭 Strategy mode: {mode.value} ({reason})")
        
        if mode == DeceptionMode.DEFEND_SELF:
            my_profile = profiles.get(self.disguised_as, {})
            return self.generate_defense(message, my_profile, conversation_history)
        
        elif mode == DeceptionMode.GATHER_INFO:
            return self.generate_question(None, message)
        
        elif mode == DeceptionMode.SEED_DOUBT:
            target, _ = suspicion_tracker.get_strategic_target([self.disguised_as])
            if target:
                return self.generate_doubt_seed(target, profiles, [])
            else:
                return "Not sure what's going on..."
        
        elif mode == DeceptionMode.ACCUSE_OTHER:
            if other_suspicions:
                target = other_suspicions[0][0]
                accusation, reasoning = self.generate_accusation(target, profiles, suspicion_tracker)
                print(f"   🎯 Accusing {target}: {reasoning}")
                return accusation
            else:
                return "Something feels off..."
        
        elif mode == DeceptionMode.BUILD_TRUST:
            trust_responses = get_response_templates('build_trust')
            return random.choice(trust_responses)
        
        else:
            return None