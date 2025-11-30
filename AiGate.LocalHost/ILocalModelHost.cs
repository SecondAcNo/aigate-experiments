namespace AiGate.LocalHost;

public interface ILocalModelHost : IAsyncDisposable
{
    Task EnsureStartedAsync(CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
}
