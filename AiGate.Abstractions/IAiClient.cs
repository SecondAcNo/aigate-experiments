namespace AiGate.Abstractions;

public interface IAiClient
{
    Task<AiChatResponse> ChatAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default
    );
}
