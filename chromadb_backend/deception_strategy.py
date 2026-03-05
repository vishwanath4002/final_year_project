# deception_strategy.py - STRATEGIC DECEPTION ENGINE
# Key change: strategy follows a temporal arc (gather → trust → doubt/accuse)
# and generates STRUCTURED INTENTS rather than pre-formed sentences.
# The LLM's only job is to render an intent in the player's style.

from typing import Dict, List, Optional, Tuple
from enum import Enum
import random
from game_context import get_response_templates


class DeceptionMode(Enum):
    GATHER_INFO  = "gather_info"
    BUILD_TRUST  = "build_trust"
    SEED_DOUBT   = "seed_doubt"
    ACCUSE_OTHER = "accuse_other"
    DEFEND_SELF  = "defend_self"
    LIE_LOW      = "lie_low"


# ─────────────────────────────────────────────────────────────────────────────
# Temporal arc thresholds
# Messages 0-4   → GATHER_INFO   (seem new, ask questions, establish cover)
# Messages 5-9   → BUILD_TRUST   (be helpful, blend in)
# Messages 10+   → SEED_DOUBT / ACCUSE_OTHER (start causing chaos)
# Override at any point: DEFEND_SELF if accusation detected
# ─────────────────────────────────────────────────────────────────────────────
ARC = [
    (0,  5,  DeceptionMode.GATHER_INFO),
    (5,  10, DeceptionMode.BUILD_TRUST),
    (10, 99, DeceptionMode.SEED_DOUBT),
]


class DeceptionIntent:
    """
    A structured description of WHAT the impostor wants to say,
    without specifying exact words.  The LLM renders this in the player's style.

    action:   'ask_location' | 'confirm_task' | 'provide_alibi' |
              'accuse' | 'seed_doubt' | 'redirect' | 'casual'
    target:   player name if relevant
    alibi:    a known fact to use as cover (from PlayerProfile.get_facts_as_text)
    detail:   extra hint for the LLM
    """
    def __init__(self, action: str, target: str = None,
                 alibi: str = None, detail: str = None):
        self.action = action
        self.target = target
        self.alibi  = alibi
        self.detail = detail

    def to_prompt_fragment(self) -> str:
        """
        Returns a short directive injected into the LLM prompt.
        Keeps it under ~30 tokens so the small model can follow it.
        Note: players are physically nearby (proximity chat) — no need to ask
        where someone is if they're standing right there.
        """
        if self.action == 'ask_location':
            t = f" {self.target}" if self.target else ""
            return f"Ask where{t} has been or what they were doing earlier."
        if self.action == 'confirm_task':
            return f"Say you were {self.alibi or 'doing a task'} earlier."
        if self.action == 'provide_alibi':
            return f"Defend yourself: say you were {self.alibi or 'doing a task'} just now."
        if self.action == 'accuse':
            return f"Suggest {self.target} seems suspicious to the people nearby."
        if self.action == 'seed_doubt':
            return f"Casually mention you are not sure about where {self.target} was earlier."
        if self.action == 'redirect':
            return f"Deflect suspicion toward {self.target} in front of the group."
        if self.action == 'casual':
            return self.detail or "Say something casual to the people nearby."
        return self.detail or "Say something short and casual."


class DeceptionStrategy:

    def __init__(self, disguised_as: str):
        self.disguised_as       = disguised_as
        self.current_mode       = DeceptionMode.GATHER_INFO
        self.has_been_accused   = False
        self.accusation_count   = 0
        self.message_count      = 0   # incremented by fastapi on each impostor reply
        # How many facts we've actively extracted from player replies mid-convo.
        # GATHER_INFO phase ends early once this hits the threshold — so the
        # impostor actually uses the intel it asked for.
        self.facts_gathered     = 0
        self.facts_threshold    = 3   # leave GATHER_INFO after 3 facts learned

    # ── Mode selection ────────────────────────────────────────────────────────

    def decide_mode(self, suspicion_on_self: float,
                    other_suspicions: List[Tuple[str, float]],
                    trust_scores: Dict[str, float] = None) -> DeceptionMode:
        """
        Priority:
          1. DEFEND_SELF  if accused or suspicion_on_self > 3
          2. Leave GATHER_INFO early if enough facts collected
          3. Leave BUILD_TRUST early if at least one player is genuinely trusting
             (score >= 1.5) — we have credibility to spend
          4. Temporal arc otherwise
        """
        trust_scores = trust_scores or {}

        if self.has_been_accused or suspicion_on_self > 3.0:
            self.current_mode = DeceptionMode.DEFEND_SELF
            return self.current_mode

        for start, end, mode in ARC:
            if start <= self.message_count < end:
                # Early exit from GATHER_INFO once we have enough intel
                if mode == DeceptionMode.GATHER_INFO and self.facts_gathered >= self.facts_threshold:
                    print(f"   📥 Enough facts gathered ({self.facts_gathered}) — advancing to BUILD_TRUST")
                    self.current_mode = DeceptionMode.BUILD_TRUST
                    return self.current_mode
                # Early exit from BUILD_TRUST if credibility established
                if mode == DeceptionMode.BUILD_TRUST and trust_scores:
                    trusted_players = [p for p, s in trust_scores.items() if s >= 1.5]
                    if trusted_players:
                        print(f"   🤝 Trust established with {trusted_players} — advancing to SEED_DOUBT")
                        self.current_mode = DeceptionMode.SEED_DOUBT
                        return self.current_mode
                # Upgrade to ACCUSE_OTHER in the chaos phase if a good target exists
                if mode == DeceptionMode.SEED_DOUBT and other_suspicions:
                    top_score = other_suspicions[0][1]
                    if top_score >= 3.0:
                        self.current_mode = DeceptionMode.ACCUSE_OTHER
                        return self.current_mode
                self.current_mode = mode
                return self.current_mode

        self.current_mode = DeceptionMode.SEED_DOUBT
        return self.current_mode

    # ── Intent generation ─────────────────────────────────────────────────────

    def get_intent(
        self,
        message: str,
        profiles: Dict,
        suspicion_tracker,
        known_facts_text: str = "",
        trust_scores: Dict[str, float] = None,
    ) -> DeceptionIntent:
        """
        Main entry: decide WHAT to say (as a DeceptionIntent).
        The LLM will decide HOW to say it.
        """
        trust_scores = trust_scores or {}

        who_suspects_me   = suspicion_tracker.who_suspects(self.disguised_as)
        suspicion_on_self = sum(s for _, s in who_suspects_me)
        other_suspicions  = suspicion_tracker.get_most_suspected(3)
        other_suspicions  = [(p, s) for p, s in other_suspicions
                             if p != self.disguised_as]

        mode = self.decide_mode(suspicion_on_self, other_suspicions, trust_scores)
        print(f"   🎭 Mode: {mode.value} (msg#{self.message_count}, "
              f"self-suspicion={suspicion_on_self:.1f}, "
              f"trust={trust_scores})")

        # ── DEFEND ────────────────────────────────────────────────────────────
        if mode == DeceptionMode.DEFEND_SELF:
            alibi = known_facts_text or "collecting wood at the sheds"
            return DeceptionIntent('provide_alibi', alibi=alibi)

        # ── GATHER INFO ───────────────────────────────────────────────────────
        if mode == DeceptionMode.GATHER_INFO:
            target = self._pick_target_from_message(message, profiles)
            return DeceptionIntent('ask_location', target=target)

        # ── BUILD TRUST ───────────────────────────────────────────────────────
        if mode == DeceptionMode.BUILD_TRUST:
            alibi = known_facts_text or random.choice([
                "collecting wood", "taking cans to church",
                "burning mushrooms", "shooting aliens",
            ])
            return DeceptionIntent('confirm_task', alibi=alibi)

        # ── SEED DOUBT ────────────────────────────────────────────────────────
        if mode == DeceptionMode.SEED_DOUBT:
            # Prefer targeting skeptics (low/negative trust) — they're already
            # halfway there. Protect trusting players — burning them looks suspicious.
            target = self._pick_skeptic_target(trust_scores, suspicion_tracker)
            if target:
                return DeceptionIntent('seed_doubt', target=target)
            return DeceptionIntent('casual', detail="Say something vague about the game.")

        # ── ACCUSE ────────────────────────────────────────────────────────────
        if mode == DeceptionMode.ACCUSE_OTHER:
            # Same logic — accuse skeptics not trusters
            target = self._pick_skeptic_target(trust_scores, suspicion_tracker)
            if not target and other_suspicions:
                target = other_suspicions[0][0]
            return DeceptionIntent('accuse', target=target or "someone")

        return DeceptionIntent('casual')

    def _pick_skeptic_target(self, trust_scores: Dict[str, float],
                              suspicion_tracker) -> Optional[str]:
        """
        Pick the best target to accuse or seed doubt about.

        Preference order:
          1. Low/negative trust toward impostor (already skeptical — easier to
             redirect their suspicion outward than to silence them)
          2. Not already the top suspect (piling on looks obvious)
          3. Exclude self
        """
        if not trust_scores:
            # Fall back to existing strategic target logic
            target, _ = suspicion_tracker.get_strategic_target([self.disguised_as])
            return target

        # Sort by trust ascending — most skeptical first
        candidates = sorted(
            [(pid, score) for pid, score in trust_scores.items()
             if pid != self.disguised_as],
            key=lambda x: x[1]
        )

        # Don't pile on the top suspect — pick 2nd most skeptical if possible
        most_suspected = [p for p, _ in suspicion_tracker.get_most_suspected(1)]
        for pid, score in candidates:
            if pid not in most_suspected:
                return pid

        # All skeptics are already top suspects — just take the most skeptical
        return candidates[0][0] if candidates else None

    def _pick_target_from_message(self, message: str,
                                   profiles: Dict) -> Optional[str]:
        """Extract a player name from the message, or pick one from profiles."""
        import re
        mentions = re.findall(r'[Pp]layer\s*\d+', message)
        if mentions:
            return mentions[0]
        known = [p for p in profiles.keys() if p != self.disguised_as]
        return random.choice(known) if known else None

    def should_respond_to_message(self, message: str,
                                   context: Dict) -> Tuple[bool, str]:
        """Quick check before generating a full intent."""
        msg_lower = message.lower()
        # Always respond to direct accusations
        if self.disguised_as.lower() in msg_lower and any(
            w in msg_lower for w in ['sus','suspicious','lying','impostor','you']
        ):
            return True, "defend"
        # Always respond to direct questions
        if self.disguised_as.lower() in msg_lower and '?' in message:
            return True, "answer_question"
        # 30% opportunistic
        if random.random() < 0.3:
            return True, "opportunistic"
        return False, "stay_quiet"