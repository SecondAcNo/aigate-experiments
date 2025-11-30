namespace AiGate.Core;

public sealed class AiGateConfig
{
    public required List<AiProfileConfig> Profiles { get; init; }

    public AiProfileConfig GetProfileOrThrow(string name) =>
        Profiles.FirstOrDefault(p => p.Name == name)
        ?? throw new InvalidOperationException($"Unknown AI profile: '{name}'");
}
