namespace AiGate.Abstractions;

public sealed record AiChatUsage(
    int PromptTokens,
    int CompletionTokens
);