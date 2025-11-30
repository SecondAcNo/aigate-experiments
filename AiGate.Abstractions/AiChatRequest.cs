namespace AiGate.Abstractions;

public sealed record AiChatRequest(
    string Profile,
    IReadOnlyList<AiMessage> Messages,
    int? MaxTokens = null,
    double? Temperature = null
);