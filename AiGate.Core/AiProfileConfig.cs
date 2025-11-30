namespace AiGate.Core;

public sealed class AiProfileConfig
{
    public required string Name { get; init; }
    public required string BaseUrl { get; init; }
    public required string Model { get; init; }

    public AiProfileProcessConfig? Process { get; init; }
    public string? ApiKey { get; init; }                
}
