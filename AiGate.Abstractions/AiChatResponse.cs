namespace AiGate.Abstractions;

public sealed record AiChatResponse(
    string Content,
    string Model,
    AiChatUsage? Usage,
    string? ProviderRequestId = null
);
