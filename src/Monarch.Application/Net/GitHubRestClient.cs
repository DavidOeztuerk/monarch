using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

namespace Monarch.Application.Net;

/// <summary>Minimal AOT-safe GitHub REST client: source-generated JSON only, token only in the request header.</summary>
public sealed class GitHubRestClient
{
    private readonly HttpClient _http;
    private readonly GitHubTokenProvider _tokens;

    /// <summary>Initializes the client.</summary>
    /// <param name="http">HTTP client with the API base address.</param>
    /// <param name="tokens">Token source.</param>
    public GitHubRestClient(HttpClient http, GitHubTokenProvider tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    /// <summary>Sends a GET and deserializes the response.</summary>
    /// <typeparam name="T">Response type.</typeparam>
    /// <param name="path">Path and query relative to the API base.</param>
    /// <param name="info">Source-generated type info of the response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    public Task<T> GetAsync<T>(string path, JsonTypeInfo<T> info, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, null, info, cancellationToken);

    /// <summary>Sends a request with a JSON body and deserializes the response.</summary>
    /// <typeparam name="TBody">Body type.</typeparam>
    /// <typeparam name="T">Response type.</typeparam>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Path and query relative to the API base.</param>
    /// <param name="body">The body.</param>
    /// <param name="bodyInfo">Source-generated type info of the body.</param>
    /// <param name="info">Source-generated type info of the response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    public Task<T> SendJsonAsync<TBody, T>(HttpMethod method, string path, TBody body, JsonTypeInfo<TBody> bodyInfo, JsonTypeInfo<T> info, CancellationToken cancellationToken) =>
        SendAsync(method, path, JsonContent.Create(body, bodyInfo), info, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content, JsonTypeInfo<T> info, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("monarch", "0"));
        if (_tokens.GetToken() is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // The body is deliberately not echoed: error payloads can quote request data.
            throw new HttpRequestException($"GitHub {method.Method} /{path.Split('?')[0]} failed with {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync(info, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("GitHub returned an empty body.");
    }
}
