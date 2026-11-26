using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;

namespace ShkoOnline.EarlyBound.Tool.Dataverse;

/// <summary>Read access to the Dataverse Web API; the seam that lets the downloader be tested without a server.</summary>
internal interface IDataverseApi
{
    /// <summary>GETs a URL relative to the Web API root and returns the JSON body.</summary>
    Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken cancellationToken);
}

/// <summary>A request the Web API answered with an error.</summary>
internal sealed class DataverseException : Exception
{
    public DataverseException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>Calls the Dataverse Web API with a bearer token, honouring the service's throttling.</summary>
internal sealed class DataverseApi : IDataverseApi, IDisposable
{
    public const string ApiVersion = "v9.2";

    private const int MaxAttempts = 6;

    private readonly HttpClient _http;
    private readonly TokenCredential _credential;
    private readonly TokenRequestContext _tokenRequest;

    public DataverseApi(Uri environment, TokenCredential credential, HttpMessageHandler? handler = null)
    {
        Environment = Normalize(environment);
        _credential = credential;
        _tokenRequest = new TokenRequestContext(new[] { Scope(Environment) });
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(Environment, $"api/data/{ApiVersion}/");
        _http.Timeout = TimeSpan.FromMinutes(5);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _http.DefaultRequestHeaders.Add("OData-Version", "4.0");

        // Metadata is cached per server; ask for the copy that reflects the latest customisations.
        _http.DefaultRequestHeaders.Add("Consistency", "Strong");
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ShkoOnline.EarlyBound", ToolInfo.Version));
    }

    /// <summary>The environment's root URL, e.g. <c>https://contoso.crm4.dynamics.com/</c>.</summary>
    public Uri Environment { get; }

    /// <summary>The OAuth scope that grants access to an environment.</summary>
    public static string Scope(Uri environment) => Normalize(environment).GetLeftPart(UriPartial.Authority) + "/.default";

    /// <summary>Reduces whatever URL the user pasted (an app URL, an API URL) to the environment root.</summary>
    public static Uri Normalize(Uri environment) => new(environment.GetLeftPart(UriPartial.Authority) + "/");

    public async Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var token = await _credential.GetTokenAsync(_tokenRequest, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(Backoff(attempt, null), cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                var transient = response.StatusCode == HttpStatusCode.TooManyRequests
                    || response.StatusCode == HttpStatusCode.ServiceUnavailable
                    || response.StatusCode == HttpStatusCode.BadGateway
                    || response.StatusCode == HttpStatusCode.GatewayTimeout;
                if (transient && attempt < MaxAttempts)
                {
                    await Task.Delay(Backoff(attempt, response.Headers.RetryAfter), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new DataverseException(response.StatusCode, Describe(response.StatusCode, body));
            }
        }
    }

    private static TimeSpan Backoff(int attempt, RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta < TimeSpan.FromMinutes(2) ? delta : TimeSpan.FromMinutes(2);
        }

        return TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
    }

    private static string Describe(HttpStatusCode status, string body)
    {
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var text))
            {
                message = text.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON: an HTML error page from a proxy, or nothing at all.
        }

        var hint = status switch
        {
            HttpStatusCode.Unauthorized => " The sign-in was not accepted by this environment. Check the URL and that you signed in with an account of its tenant.",
            HttpStatusCode.Forbidden => " The signed-in user has no access to this environment, or lacks permission to read its metadata.",
            _ => string.Empty,
        };

        return $"Dataverse answered {(int)status} {status}" + (string.IsNullOrWhiteSpace(message) ? "." : ": " + message) + hint;
    }

    public void Dispose() => _http.Dispose();
}
