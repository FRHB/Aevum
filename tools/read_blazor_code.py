import antigravity

def read_blazor_code(filepaths: list) -> str:
    """Reads specific C# or .razor files and returns their structural outline."""
    agent = antigravity.Agent(config="qwen-code-reader")
    return agent.execute(prompt=f"Analyze these files: {filepaths}")