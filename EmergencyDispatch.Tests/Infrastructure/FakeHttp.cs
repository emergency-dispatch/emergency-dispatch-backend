using System.Net;
using System.Text;

namespace EmergencyDispatch.Tests.Infrastructure;

/// <summary>HttpMessageHandler that answers with a fixed function and records every request.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public static FakeHttpHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public static FakeHttpHandler Throws(Exception ex) => new(_ => throw ex);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return _respond(request);
    }
}

/// <summary>IHttpClientFactory returning clients backed by one handler per name.</summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly Dictionary<string, HttpMessageHandler> _handlers = new();
    private readonly HttpMessageHandler? _default;

    public FakeHttpClientFactory(HttpMessageHandler? defaultHandler = null) => _default = defaultHandler;

    public FakeHttpClientFactory With(string name, HttpMessageHandler handler)
    {
        _handlers[name] = handler;
        return this;
    }

    public HttpClient CreateClient(string name)
    {
        var h = _handlers.TryGetValue(name, out var x) ? x : _default ?? throw new InvalidOperationException($"No handler for '{name}'");
        return new HttpClient(h, disposeHandler: false);
    }
}
