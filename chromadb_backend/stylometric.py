# stylometry_summary.py
from langchain_ollama import ChatOllama

# Initialize the LLM once
llm = ChatOllama(model="llama3.1:8b", temperature=0.7)

def summarize_player_style(player_id: str, messages: list[str]) -> str:
    """
    Summarizes the writing style of a player from a small set of messages.
    Returns a short text description suitable for NPC imitation.
    """
    if not messages:
        return "neutral, casual game chat style"

    # Build the prompt for the LLM
    prompt = f"""
You are analyzing chat messages from a player in a multiplayer game.
Summarize their style in 2-3 sentences for NPC imitation:
- How they write messages
- Sentence length
- Use of slang, abbreviations, or emojis
- Tone (formal, casual, sarcastic, etc.)

Messages:
{chr(10).join(messages)}

Player style summary:
"""

    # Call the LLM
    response = llm.invoke(prompt)
    summary = response.content.strip()
    return summary
