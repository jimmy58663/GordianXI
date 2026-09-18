import json
import re
import sys

REFERENCE_DIRS = [
    r"C:\Users\jimmy\OneDrive\Documents\Git\LSBserver",
    r"C:\Users\jimmy\OneDrive\Documents\Git\xiloader",
    r"C:\Users\jimmy\OneDrive\Documents\Git\xi-model-viewer",
]

GIT_MUTATING_SUBCOMMANDS = (
    r"commit|push|pull|add|rm|mv|reset|merge|rebase|cherry-pick|revert|restore|"
    r"clean|apply|checkout|branch|stash|submodule|tag|gc|init|fetch"
)

MUTATING_PATTERNS = [
    r"\brm\b",
    r"\bdel\b",
    r"\berase\b",
    r"\bmv\b",
    r"\bmove\b",
    r"\bmkdir\b",
    r"\brmdir\b",
    r"\btouch\b",
    r">>?\s*\S",
    r"\bdotnet\s+(clean|publish|pack|nuget\s+push)\b",
    r"\bnpm\s+(install|uninstall|ci)\b",
    r"\bpip\s+install\b",
]

def looks_like_mutating_git(command):
    if not re.search(r"\bgit\b", command, re.IGNORECASE):
        return False
    if re.search(rf"\b({GIT_MUTATING_SUBCOMMANDS})\b", command, re.IGNORECASE):
        return True
    return False


def normalize(text):
    return text.replace("\\", "/").lower()


def main():
    try:
        data = json.load(sys.stdin)
    except Exception:
        return

    command = data.get("tool_input", {}).get("command", "")
    if not command:
        return

    norm_cmd = normalize(command)
    hit_dir = next((d for d in REFERENCE_DIRS if normalize(d) in norm_cmd), None)
    if not hit_dir:
        return

    is_mutating = looks_like_mutating_git(command) or any(
        re.search(pattern, command, re.IGNORECASE) for pattern in MUTATING_PATTERNS
    )
    if is_mutating:
        print(json.dumps({
            "hookSpecificOutput": {
                "hookEventName": "PreToolUse",
                "permissionDecision": "deny",
                "permissionDecisionReason": (
                    f"Reference workspace '{hit_dir}' is strictly read-only per AGENTS.md "
                    "(view/search/schema-inspection only). Mutating command blocked."
                ),
            }
        }))


if __name__ == "__main__":
    main()
