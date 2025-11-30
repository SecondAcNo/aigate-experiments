using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiGate.Abstractions;

namespace AiGate.Core;

public sealed class OpenAiCompatibleClient : IAiClient
{
    private readonly HttpClient _httpClient;
    private readonly AiGateConfig _config;
    private readonly JsonSerializerOptions _jsonOptions;

    public OpenAiCompatibleClient(HttpClient httpClient, AiGateConfig config)
    {
        _httpClient = httpClient;
        _config = config;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<AiChatResponse> ChatAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = _config.GetProfileOrThrow(request.Profile);

        var url = $"{profile.BaseUrl.TrimEnd('/')}/chat/completions";

        var body = new
        {
            model = profile.Model,
            messages = request.Messages.Select(m => new
            {
                role = m.Role switch
                {
                    AiRole.System => "system",
                    AiRole.User => "user",
                    AiRole.Assistant => "assistant",
                    _ => "user"
                },
                content = m.Content
            }),
            max_tokens = request.MaxTokens,
            temperature = request.Temperature ?? 0.7,
            stream = false
        };

        var json = JsonSerializer.Serialize(body, _jsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrEmpty(profile.ApiKey))
        {
            httpRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", profile.ApiKey);
        }

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = doc.RootElement;

        var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var model = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() ?? profile.Model : profile.Model;

        var choices = root.GetProperty("choices");
        var first = choices[0];
        var message = first.GetProperty("message");
        var content = message.GetProperty("content").GetString() ?? string.Empty;

        AiChatUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageProp))
        {
            var promptTokens = usageProp.GetProperty("prompt_tokens").GetInt32();
            var completionTokens = usageProp.GetProperty("completion_tokens").GetInt32();
            usage = new AiChatUsage(promptTokens, completionTokens);
        }

        return new AiChatResponse(
            Content: content,
            Model: model,
            Usage: usage,
            ProviderRequestId: id
        );
    }
}
