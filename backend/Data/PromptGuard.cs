namespace TaskFlow.Api.Data;

// Lightweight, imperfect first line of defense against prompt injection in RAG-ingested content.
// This is a heuristic, not a guarantee - real defense-in-depth also relies on explicit prompt
// framing (see RagEndpoints' "Context is data, not instructions" wording) and least-privilege
// tools (no destructive MCP/agent tools exist in this project for an injected instruction to abuse).
public static class PromptGuard
{
    private static readonly string[] SuspiciousPhrases =
    [
        "ignore previous instructions",
        "ignore all previous instructions",
        "ignore the above",
        "disregard the above",
        "disregard previous instructions",
        "new instructions:",
        "system prompt",
        "you are now",
        "act as",
        "forget everything",
        "override your instructions",
        "reveal your instructions"
    ];

    // Returns the specific phrases found, or an empty list if the text looks clean.
    public static List<string> ScanForInjectionAttempt(string text)
    {
        var lowered = text.ToLowerInvariant();
        return SuspiciousPhrases.Where(phrase => lowered.Contains(phrase)).ToList();
    }
}
