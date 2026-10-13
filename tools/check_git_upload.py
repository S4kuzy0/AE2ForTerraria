"""Read the Git index and reject private/generated files or recognizable secrets."""
from pathlib import Path
import re
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
names = subprocess.check_output(["git", "ls-files", "-z"], cwd=root).decode("utf-8").split("\0")
forbidden_dirs = {"backups", "dist", "bin", "obj", "node_modules", "__pycache__", ".local", ".vs", ".idea", ".vscode", "worlds", "players", "modconfigs", "savedata"}
forbidden_suffixes = (".tmod", ".dll", ".pdb", ".exe", ".zip", ".7z", ".log", ".pid", ".map", ".tmap", ".pem", ".key", ".user", ".suo")
secret_patterns = {
    "GitHub token": r"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})\b",
    "private key": r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
    "AWS access key": r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b",
    "OpenAI key": r"\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{40,}\b",
}
errors = []
count = size = 0
for name in filter(None, names):
    path = Path(name)
    lower = name.lower()
    if any(part.lower() in forbidden_dirs for part in path.parts) or lower.endswith(forbidden_suffixes) or re.search(r"\.(?:wld|twld|plr|tplr|bak)(?:\.|$)", lower) or path.name.lower().startswith(".env"):
        errors.append((name, "private or generated file"))
    if path.name.lower() in {"agent.md", "agents.md", "config.json", "client-config.json", "client-preferences.json", "enabled.json", "ae2forterraria-layout.txt"}:
        errors.append((name, "personal configuration"))
    if lower.startswith("ui-preview/") and name != "ui-preview/recipe-browser/FEATURE-PARITY.md":
        errors.append((name, "preview data outside tracked scope"))
    data = subprocess.check_output(["git", "show", ":" + name], cwd=root)
    count += 1
    size += len(data)
    if len(data) > 10_000_000:
        errors.append((name, "unexpected large file"))
    try:
        text = data.decode("utf-8-sig")
    except UnicodeDecodeError:
        continue
    for label, pattern in secret_patterns.items():
        if re.search(pattern, text):
            errors.append((name, label))
if errors:
    for name, reason in errors:
        print(f"REJECT {name}: {reason}")
    sys.exit(1)
print(f"Upload checks passed: {count} tracked files, {size} bytes. No private/generated files or recognized secret formats found.")
