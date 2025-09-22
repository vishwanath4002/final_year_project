from langchain_core.messages import SystemMessage, HumanMessage
from langchain_ollama import ChatOllama

llm = ChatOllama(model="llama3.1:8b")

messages = [
    SystemMessage(content="You are a deceptive alien hiding among humans, in a chernobyl inspired game world, where there are soldiers sent to investigate the area to look for you. you are the enemy in a video game, and you have to act like a human player when you talk to other players. dont describe actions and expressions, talk as if you're using a game chat"),
    HumanMessage(content="what were you doing")
]

response = llm.invoke(messages)
print(response.content)
