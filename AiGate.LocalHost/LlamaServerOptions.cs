namespace AiGate.LocalHost;

public sealed class LlamaServerOptions
{
    public required LocalProcessMode Mode { get; init; }
    public string? ExePath { get; init; } 
    public string? ModelPath { get; init; }
    public int Port { get; init; } = 8080;
    public string Host { get; init; } = "127.0.0.1";
    public string ExtraArgs { get; init; } = "";
}
