using System.Text.Json.Serialization;

namespace manage365.Routes.API.ChatBot;

public sealed record ChatMessageHistoryItem(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("text")] string Text
);

public sealed record ChatRequest(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("history")] IReadOnlyList<ChatMessageHistoryItem>? History = null
);

public sealed record ChatResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("reply")] string Reply,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("timestamp")] string? Timestamp = null,
    [property: JsonPropertyName("error")] string? Error = null
);
