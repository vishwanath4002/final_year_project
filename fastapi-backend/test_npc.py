# test_npc.py
import requests
url = "http://127.0.0.1:8000/npc/reply"
payload = {
    "player_text": "Where were you last round?",
    "round_id": "r1",
    "imitate_player_id": "p1",
    "recent_msgs": ["hlo.. ik i was der ok ..", "brb, gonna check Church"]
}
r = requests.post(url, json=payload, timeout=10)
print(r.status_code, r.text)
