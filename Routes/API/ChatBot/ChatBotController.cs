using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.ChatBot;

[ApiController]
[Tags("ChatBot")]
[Route("api/chatbot")]
public sealed class ChatBotController(
    IHttpClientFactory httpClientFactory,
    ILogger<ChatBotController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost("chat")]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Nội dung câu hỏi không được để trống.",
                Extensions = { ["code"] = "empty_message" }
            });
        }

        try
        {
            var client = httpClientFactory.CreateClient("ChatBot");
            var content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json"
            );

            using var response = await client.PostAsync("chat", content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("ChatBot upstream error {StatusCode}: {Body}", response.StatusCode, responseBody);
                return StatusCode((int)response.StatusCode, responseBody);
            }

            return Content(responseBody, "application/json");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi kết nối tới upstream chatbot API.");
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Không thể kết nối tới dịch vụ ChatBot nhân sự Manage365.",
                Detail = ex.Message,
                Extensions = { ["code"] = "upstream_unavailable" }
            });
        }
    }

    [HttpPost("stream")]
    public async Task Stream([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.ContentType = "application/json";
            await Response.WriteAsync("{\"error\":\"Nội dung câu hỏi không được để trống.\"}", cancellationToken);
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient("ChatBot");
            var content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json"
            );

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "chat/stream")
            {
                Content = content
            };
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var upstreamResponse = await client.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            Response.StatusCode = (int)upstreamResponse.StatusCode;
            Response.ContentType = "text/event-stream; charset=utf-8";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            await upstreamStream.CopyToAsync(Response.Body, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client closed stream connection
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi streaming từ upstream chatbot API.");
            if (!Response.HasStarted)
            {
                Response.StatusCode = StatusCodes.Status502BadGateway;
                Response.ContentType = "text/event-stream; charset=utf-8";
                await Response.WriteAsync($"data: {{\"error\":\"Lỗi kết nối tới máy chủ AI ({ex.Message})\"}}\n\n", cancellationToken);
            }
        }
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("ChatBot");
            using var response = await client.GetAsync("health", cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return Content(content, "application/json");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi kiểm tra sức khỏe upstream chatbot API.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "offline",
                service = "HR Support Assistant AI",
                error = ex.Message
            });
        }
    }

    [HttpGet("templates")]
    public async Task<IActionResult> Templates(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("ChatBot");
            using var response = await client.GetAsync("templates", cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return Content(content, "application/json");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi tải danh sách biểu mẫu từ upstream chatbot API.");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                success = false,
                templates = Array.Empty<object>(),
                error = ex.Message
            });
        }
    }
}
