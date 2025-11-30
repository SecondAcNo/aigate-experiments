namespace AiGate.Abstractions;

public sealed record AiMessage(
    AiRole Role,
    string Content
)
{
    public static AiMessage System(string content) => new(AiRole.System, content);
    public static AiMessage User(string content) => new(AiRole.User, content);
    public static AiMessage Assistant(string content) => new(AiRole.Assistant, content);
}
