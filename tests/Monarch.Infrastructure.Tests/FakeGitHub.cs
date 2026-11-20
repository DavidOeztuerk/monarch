using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Monarch.Infrastructure.Tests;

/// <summary>In-memory stand-in for the subset of the GitHub REST API the board uses. No network.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    internal sealed class Milestone { public int Number; public string Title = ""; public string? Description; public string State = "open"; }

    internal sealed class Issue { public int Number; public string Title = ""; public string Body = ""; public string State = "open"; public List<string> Labels = []; public int? Milestone; }

    public List<Milestone> Milestones { get; } = [];

    public HashSet<string> Labels { get; } = [];

    public List<Issue> Issues { get; } = [];

    public List<(string Method, string Path, string? Body, string? Auth)> Requests { get; } = [];

    public int FailWith { get; set; }

    public IEnumerable<(string Method, string Path, string? Body, string? Auth)> Writes => Requests.Where(r => r.Method != "GET");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var auth = request.Headers.Authorization?.ToString();
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add((request.Method.Method, path + request.RequestUri.Query, body, auth));
        if (FailWith != 0)
        {
            return new HttpResponseMessage((HttpStatusCode)FailWith) { Content = new StringContent("{\"message\":\"boom\"}") };
        }

        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        var parts = path.Trim('/').Split('/');
        var json = body is null ? null : JsonNode.Parse(body)!.AsObject();
        var kind = parts[3];
        var id = parts.Length > 4 ? int.Parse(parts[4]) : (int?)null;
        return (request.Method.Method, kind) switch
        {
            ("GET", "milestones") => Page(query, Milestones.Select(m => (JsonNode)M(m))),
            ("GET", "labels") => Page(query, Labels.Select(l => (JsonNode)new JsonObject { ["name"] = l })),
            ("GET", "issues") => Page(query, Issues.Where(i => query["labels"] is null || i.Labels.Contains(query["labels"]!)).Select(i => (JsonNode)I(i))),
            ("POST", "milestones") => Created(M(AddMilestone(json!))),
            ("PATCH", "milestones") => Ok(M(PatchMilestone(id!.Value, json!))),
            ("POST", "labels") => Created(Label(json!)),
            ("POST", "issues") => Created(I(AddIssue(json!))),
            ("PATCH", "issues") => Ok(I(PatchIssue(id!.Value, json!))),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage Ok(JsonNode n) => new(HttpStatusCode.OK) { Content = new StringContent(n.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Created(JsonNode n) => new(HttpStatusCode.Created) { Content = new StringContent(n.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Page(System.Collections.Specialized.NameValueCollection q, IEnumerable<JsonNode> all)
    {
        var per = int.Parse(q["per_page"] ?? "30");
        var page = int.Parse(q["page"] ?? "1");
        var arr = new JsonArray();
        foreach (var n in all.Skip((page - 1) * per).Take(per))
        {
            arr.Add(n);
        }

        return Ok(arr);
    }

    private static JsonObject M(Milestone m) => new() { ["number"] = m.Number, ["title"] = m.Title, ["description"] = m.Description, ["state"] = m.State };

    private JsonObject I(Issue i)
    {
        var ms = i.Milestone is { } n ? M(Milestones.Single(m => m.Number == n)) : null;
        var labels = new JsonArray();
        foreach (var l in i.Labels)
        {
            labels.Add(new JsonObject { ["name"] = l });
        }

        return new JsonObject { ["number"] = i.Number, ["title"] = i.Title, ["body"] = i.Body, ["state"] = i.State, ["labels"] = labels, ["milestone"] = ms };
    }

    private JsonObject Label(JsonObject j)
    {
        Labels.Add(j["name"]!.GetValue<string>());
        return j;
    }

    private Milestone AddMilestone(JsonObject j)
    {
        var m = new Milestone { Number = Milestones.Count + 1, Title = j["title"]!.GetValue<string>(), Description = j["description"]?.GetValue<string>() };
        Milestones.Add(m);
        return m;
    }

    private Milestone PatchMilestone(int n, JsonObject j)
    {
        var m = Milestones.Single(x => x.Number == n);
        m.Title = j["title"]?.GetValue<string>() ?? m.Title;
        m.Description = j["description"]?.GetValue<string>() ?? m.Description;
        m.State = j["state"]?.GetValue<string>() ?? m.State;
        return m;
    }

    private Issue AddIssue(JsonObject j)
    {
        var i = new Issue { Number = Issues.Count + 1 };
        Apply(i, j);
        Issues.Add(i);
        return i;
    }

    private Issue PatchIssue(int n, JsonObject j)
    {
        var i = Issues.Single(x => x.Number == n);
        Apply(i, j);
        return i;
    }

    private static void Apply(Issue i, JsonObject j)
    {
        i.Title = j["title"]?.GetValue<string>() ?? i.Title;
        i.Body = j["body"]?.GetValue<string>() ?? i.Body;
        i.State = j["state"]?.GetValue<string>() ?? i.State;
        i.Milestone = j["milestone"]?.GetValue<int>() ?? i.Milestone;
        if (j["labels"] is JsonArray a)
        {
            i.Labels = [.. a.Select(x => x!.GetValue<string>())];
        }
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Uris { get; } = [];

    public List<string?> Auth { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uris.Add(request.RequestUri!.PathAndQuery);
        Auth.Add(request.Headers.Authorization?.ToString());
        return Task.FromResult(respond(request));
    }
}
