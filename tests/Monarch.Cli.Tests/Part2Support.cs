using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Monarch.Infrastructure.Tests;

namespace Monarch.Cli.Tests;

/// <summary>Fake GitHub: pulls and check runs on top of the board fake. No network.</summary>
internal sealed partial class FakeForge : HttpMessageHandler
{
    public const string Sha = "abc123def456abc123def456abc123def456abcd";

    public FakeGitHub Board { get; } = new();

    public Dictionary<int, (string Head, bool Merged)> Pulls { get; } = [];

    public Dictionary<string, string> Checks { get; } = [];

    public List<string> Paths { get; } = [];

    /// <summary>Base branch per PR (default <c>develop</c>).</summary>
    public Dictionary<int, string> Bases { get; } = [];

    /// <summary>Committer date of every commit (default before the test clock).</summary>
    public string CommitDate { get; set; } = "2026-10-27T11:00:00Z";

    /// <summary>When set, <c>PUT …/merge</c> fails with this status.</summary>
    public int MergeFailsWith { get; set; }

    /// <summary>When set, the PR base changes to this branch while the merge happens (simulates a retarget).</summary>
    public string? BaseAfterMerge { get; set; }

    /// <summary>Request bodies of merge calls.</summary>
    public List<string> MergeBodies { get; } = [];

    public const string MergeSha = "m3rge0000000000000000000000000000000000";

    public static string Run(int id, string name, string conclusion, string status = "completed") =>
        $$"""{"id":{{id}},"name":"{{name}}","head_sha":"{{Sha}}","status":"{{status}}","conclusion":{{(conclusion == "" ? "null" : $"\"{conclusion}\"")}},"completed_at":{{(status == "completed" ? "\"2026-10-27T09:05:00Z\"" : "null")}},"html_url":"https://github.com/o/r/runs/{{id}}"}""";

    public void SetChecks(params string[] runs) => Checks[Sha] = $$"""{"total_count":{{runs.Length}},"check_runs":[{{string.Join(",", runs)}}]}""";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Add(request.Method.Method + " " + path);
        if (request.Method == HttpMethod.Put && PullMergePath().Match(path) is { Success: true } mm)
        {
            var n = int.Parse(mm.Groups[1].Value);
            MergeBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            if (MergeFailsWith != 0 || !Pulls.TryGetValue(n, out var pr))
            {
                return new HttpResponseMessage(MergeFailsWith != 0 ? (HttpStatusCode)MergeFailsWith : HttpStatusCode.NotFound) { Content = new StringContent("""{"message":"Head branch was modified"}""") };
            }

            Pulls[n] = (pr.Head, true);
            if (BaseAfterMerge is not null) Bases[n] = BaseAfterMerge;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"sha":"{{MergeSha}}","merged":true,"message":"Pull Request successfully merged"}""", Encoding.UTF8, "application/json") };
        }

        var cm = CommitPath().Match(path);
        if (cm.Success)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"sha\":\"" + cm.Groups[1].Value + "\",\"commit\":{\"committer\":{\"date\":\"" + CommitDate + "\"},\"author\":{\"date\":\"" + CommitDate + "\"}}}", Encoding.UTF8, "application/json") };
        }

        var m = PullPath().Match(path);
        if (m.Success)
        {
            var n = int.Parse(m.Groups[1].Value);
            if (!Pulls.TryGetValue(n, out var pull))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var json = $$$"""{"number":{{{n}}},"merged":{{{(pull.Merged ? "true" : "false")}}},"merged_at":"2026-10-27T11:00:00Z","merge_commit_sha":{{{(pull.Merged ? "\"m3rge0000000000000000000000000000000000\"" : "null")}}},"html_url":"https://github.com/o/r/pull/{{{n}}}","state":"{{{(pull.Merged ? "closed" : "open")}}}","head":{"sha":"{{{pull.Head}}}"},"base":{"ref":"{{{Bases.GetValueOrDefault(n, "develop")}}}"}}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        var c = ChecksPath().Match(path);
        if (c.Success)
        {
            var body = Checks.TryGetValue(c.Groups[1].Value, out var j) ? j : """{"total_count":0,"check_runs":[]}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        return await new HttpMessageInvoker(Board).SendAsync(request, cancellationToken);
    }

    [GeneratedRegex(@"/repos/o/r/pulls/(\d+)$")]
    private static partial Regex PullPath();

    [GeneratedRegex(@"/repos/o/r/pulls/(\d+)/merge$")]
    private static partial Regex PullMergePath();

    [GeneratedRegex(@"/repos/o/r/commits/([^/]+)$")]
    private static partial Regex CommitPath();

    [GeneratedRegex(@"/repos/o/r/commits/([^/]+)/check-runs$")]
    private static partial Regex ChecksPath();
}

internal static class Seed
{
    public const string ForgeYaml = "boards:\n  github:\n    owner: o\n    repo: r\n";

    public static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures", "backlog");

    public static string Quest(TempProject p, string id, string state = "proposed", string gate = "G-0001", string rank = "C", string kind = "main", string extra = "", string include = "src/**")
    {
        var path = Path.Combine(p.Root, ".monarch", "quests", $"{id}-t.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"---\nid: {id}\ngate: {gate}\nkind: {kind}\nrank: {rank}\nstate: {state}\ndod:\n  - first item\n  - second item\nscope:\n  include: [\"{include}\"]\n  exclude: []\nestimate: {{ session_pct: 5, weekly_pct: 1, gold_usd: 0 }}\n{extra}---\n# Title {id}\n\n## Definition of Done\n- [ ] first item\n- [x] second item\n");
        return path;
    }

    public static string Gate(TempProject p, string id = "G-0001", string quests = "[Q-0001]", string rank = "C")
    {
        var path = Path.Combine(p.Root, ".monarch", "gates", $"{id}-g.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"---\nid: {id}\ntype: standard\ntitle: Gate {id}\nrank: {rank}\nstate: proposed\nquests: {quests}\nboss: \"boss\"\n---\n# Gate {id}\n");
        return path;
    }

    public static void Mana(TempProject p, double five, double seven, DateTimeOffset? fiveReset = null, DateTimeOffset? sevenReset = null)
    {
        var f = (fiveReset ?? TempProject.Now.AddHours(2)).ToUnixTimeSeconds();
        var s = (sevenReset ?? TempProject.Now.AddDays(3)).ToUnixTimeSeconds();
        var now = TempProject.Now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(p.Home, "mana.json"),
            FormattableString.Invariant($$$"""{"schema":1,"written_at":"{{{now}}}","rate_limits_at":"{{{now}}}","rate_limits":{"five_hour":{"used_percentage":{{{five}}},"resets_at":{{{f}}}},"seven_day":{"used_percentage":{{{seven}}},"resets_at":{{{s}}}}}""") + "}");
    }

    public static string State(string questFile) => System.Text.RegularExpressions.Regex.Match(File.ReadAllText(questFile), @"^state: (\S+)", System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value;
}
