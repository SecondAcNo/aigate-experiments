namespace AiGate.Core;

public sealed class AiProfileProcessConfig
{
    public string? Mode { get; init; }

    public string? ExePath { get; init; }

    public string? ModelPath { get; init; }

    public int? Port { get; init; }

    public string? ExtraArgs { get; init; }
}
