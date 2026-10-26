import antigravity

def query_obsidian_vault(query: str) -> str:
    """Searches the Obsidian vault for architectural rules and documentation."""
    agent = antigravity.Agent(config="phi-knowledge-scout")
    return agent.execute(prompt=query)