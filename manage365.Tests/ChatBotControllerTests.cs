using System.Net;
using System.Text;
using System.Text.Json;
using manage365.Routes.API.ChatBot;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace manage365.Tests;

public sealed class ChatBotControllerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Chat_ReturnsBadRequest_WhenMessageIsEmpty(string emptyMessage)
    {
        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler());
        var controller = new ChatBotController(factory, NullLogger<ChatBotController>.Instance);

        var result = await controller.Chat(new ChatRequest(emptyMessage), TestContext.Current.CancellationToken);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("empty_message", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Chat_ForwardsRequest_AndReturnsContent_WhenUpstreamSucceeds()
    {
        var handler = new FakeHttpMessageHandler((req) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Contains("chat", req.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"success\":true,\"reply\":\"Hello Employee!\"}",
                    Encoding.UTF8,
                    "application/json"
                )
            };
        });

        var factory = new FakeHttpClientFactory(handler);
        var controller = new ChatBotController(factory, NullLogger<ChatBotController>.Instance);

        var result = await controller.Chat(new ChatRequest("Hello"), TestContext.Current.CancellationToken);

        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", contentResult.ContentType);
        Assert.Contains("Hello Employee!", contentResult.Content);
    }

    [Fact]
    public async Task Health_ReturnsUpstreamJson()
    {
        var handler = new FakeHttpMessageHandler((req) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("health", req.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"status\":\"online\",\"service\":\"HR Assistant AI\"}",
                    Encoding.UTF8,
                    "application/json"
                )
            };
        });

        var factory = new FakeHttpClientFactory(handler);
        var controller = new ChatBotController(factory, NullLogger<ChatBotController>.Instance);

        var result = await controller.Health(TestContext.Current.CancellationToken);

        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Contains("online", contentResult.Content);
    }

    [Fact]
    public async Task Templates_ReturnsUpstreamJson()
    {
        var handler = new FakeHttpMessageHandler((req) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("templates", req.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"success\":true,\"templates\":[{\"code\":\"BM-01\"}]}",
                    Encoding.UTF8,
                    "application/json"
                )
            };
        });

        var factory = new FakeHttpClientFactory(handler);
        var controller = new ChatBotController(factory, NullLogger<ChatBotController>.Instance);

        var result = await controller.Templates(TestContext.Current.CancellationToken);

        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Contains("BM-01", contentResult.Content);
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler)
            {
                BaseAddress = new Uri("https://manage365.click/api/")
            };
        }
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = responder?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(response);
        }
    }
}
