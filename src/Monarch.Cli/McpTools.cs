using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;
using Monarch.Infrastructure.Boards.Frontmatter;
using Monarch.Mcp;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary><c>monarch mcp</c>: the stdio MCP server with exactly six phase-0 tools. There is deliberately no verify, clear, promote or permit tool.</summary>
internal static class McpTools
{
    private const string ServerVersion = "0.1.0";
    private const int MaxKnowledgeBytes = 1_000_000;

    /// <summary>Serves MCP on the environment's streams.</summary>
    public static int Run(CliEnv env)
    {
        TimeSpan? timeout = int.TryParse(env.GetVar("MONARCH_MCP_ROOTS_TIMEOUT_MS"), System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms) : null;
        var options = new McpServerOptions(env.Err, env.GetVar("MONARCH_MCP_ROOTS_LOG"), timeout);
        return new McpServer(Build(env), "monarch", ServerVersion, options).Run(env.In, env.Out);
    }

    private const string ProjectProperty = "\"project\":{\"type\":\"string\",\"description\":\"Absolute path of an awakened project root at or below the client roots. Without it the project is resolved from the roots, then the start directory.\"}";

    private static string Schema(string schema)
    {
        const string marker = "\"properties\":{";
        var at = schema.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return schema[..at] + ProjectProperty + (schema[at] == '}' ? string.Empty : ",") + schema[at..];
    }

    /// <summary>The six tools.</summary>
    public static IReadOnlyList<McpTool> Build(CliEnv env) =>
    [
        new("system_status", "Read-only project status (active quest, quest counts, XP, mana, guard, ledger integrity); same data as 'monarch status --json'.",
            Schema("""{"type":"object","properties":{},"additionalProperties":false}"""), true,
            (a, c) => InProject(env, c, "system_status", true, true, a, (e, _, _) => Capture(e, x => StatusCommand.Run(x, true)))),
        new("gate_plan", "Validate and store a strategist plan: one gate plus its quests as files, with GateOpened/QuestProposed ledger events. Ranks are raised to the minimum implied by risk-tag paths; quests beyond Proposed are never overwritten.",
            Schema("""{"type":"object","required":["gate","quests"],"properties":{"gate":{"type":"object","required":["id","title","rank"],"properties":{"id":{"type":"string","pattern":"^G-\\d{4,}$"},"title":{"type":"string"},"rank":{"enum":["E","D","C","B","A","S"]},"kind":{"enum":["standard","red","instance","jobchange"]}}},"quests":{"type":"array","items":{"type":"object","required":["id","title","kind","rank","scope","estimate","dod"],"properties":{"id":{"type":"string","pattern":"^Q-\\d{4,}$"},"title":{"type":"string"},"kind":{"enum":["main","side","daily","penalty","emergency","hidden"]},"rank":{"enum":["E","D","C","B","A","S"]},"scope":{"type":"object","required":["include"],"properties":{"include":{"type":"array","items":{"type":"string"}},"exclude":{"type":"array","items":{"type":"string"}}}},"estimate":{"type":"object","required":["session_pct","weekly_pct"],"properties":{"session_pct":{"type":"number"},"weekly_pct":{"type":"number"}}},"dod":{"type":"array","items":{"type":"string"}},"boss":{"type":"string"}}}}}}"""),
            false, (a, c) => InProject(env, c, "gate_plan", false, true, a, (e, p, s) => GatePlan.Apply(e, a, p!, s))),
        new("quest_transition", "Move a quest forward: accept, start or submit. Verify and clear are not MCP tools; they run from forge evidence via the CLI.",
            Schema("""{"type":"object","required":["id","action"],"properties":{"id":{"type":"string"},"action":{"enum":["accept","start","submit"]}}}"""), false,
            (a, c) => InProject(env, c, "quest_transition", false, true, a, (e, _, _) => Transition(e, a))),
        new("evidence_sync", "Pull checks and merge state of linked pull requests from GitHub and verify/clear quests accordingly. Only forge data counts.",
            Schema("""{"type":"object","properties":{"quest":{"type":"string","description":"Quest id; omit for all linked quests."}}}"""), false,
            (a, c) => InProject(env, c, "evidence_sync", false, true, a, (e, _, _) => Capture(e, x => EvidenceCommands.Sync(x, Opt(a, "quest"))))),
        new("mana_forecast", "Ask the Mana-Guard whether estimated steps fit: decision, remaining headroom per window and the limit source (plan default or adaptive). Works without a project (uncalibrated, plan defaults).",
            Schema("""{"type":"object","required":["estimates"],"properties":{"estimates":{"type":"array","minItems":1,"items":{"type":"object","required":["session_pct","weekly_pct"],"properties":{"session_pct":{"type":"number"},"weekly_pct":{"type":"number"}}}}}}"""), true,
            (a, c) => InProject(env, c, "mana_forecast", true, false, a, (e, p, _) => Forecast(e, a, p))),
        new("knowledge_write", "Write a file below .monarch/knowledge/ (relative path only; no '..', absolute paths or symlink escapes) and log KnowledgeWritten.",
            Schema("""{"type":"object","required":["path","content"],"properties":{"path":{"type":"string"},"content":{"type":"string"}}}"""), false,
            (a, c) => InProject(env, c, "knowledge_write", false, true, a, (e, p, s) => KnowledgeWrite(e, a, p!, s))),
    ];

    /// <summary>Optional string argument.</summary>
    internal static string? Opt(JsonElement a, string name) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>
    /// Resolves the project of this call (ADR-0013), runs <paramref name="body"/> with an environment whose cwd is the resolved project, logs
    /// <c>McpProjectResolved</c> for writing tools before any effect and prefixes the answer with <c>project: &lt;root&gt; (source: …)</c>.
    /// </summary>
    private static McpToolResult InProject(CliEnv env, McpCallContext ctx, string tool, bool readOnly, bool required, JsonElement args, Func<CliEnv, ProjectPaths?, string, McpToolResult> body)
    {
        string? parameter = null;
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("project", out var p) && p.ValueKind != JsonValueKind.Null)
        {
            parameter = p.ValueKind == JsonValueKind.String ? p.GetString() : string.Empty;
        }

        var session = env.GetVar("CLAUDE_CODE_SESSION_ID") is { } sid && !string.IsNullOrWhiteSpace(sid) ? ProjectLedger.Sanitize(sid.Trim()) : "mcp-" + ctx.ServerId;
        var res = ProjectResolver.Resolve(env, parameter, ctx.Roots);
        if (res.Paths is null)
        {
            if (!required && res.Failure == ProjectFailure.NotFound)
            {
                return Prefixed("project: null", body(env, null, session));
            }

            env.Err.WriteLine($"mcp: {tool}: {res.Error}");
            return new McpToolResult("project: null\nerror: " + res.Error, true);
        }

        var paths = res.Paths;
        if (!readOnly)
        {
            var ledger = new ProjectLedger(paths, env.Clock);
            ledger.Append(session, "mcp", "McpProjectResolved", ("tool", tool), ("project", paths.Root), ("source", res.Source), ("roots_count", res.RootsCount),
                ("cwd_project", res.CwdProject), ("session", session));
        }

        return Prefixed($"project: {paths.Root} (source: {res.Source})", body(env with { Cwd = paths.Root }, paths, session));
    }

    private static McpToolResult Prefixed(string header, McpToolResult r) => new(r.Text.Length == 0 ? header : header + "\n" + r.Text, r.IsError);

    private static McpToolResult Capture(CliEnv env, Func<CliEnv, int> run)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = run(env with { Out = o, Err = e });
        return new McpToolResult((o.ToString() + e.ToString()).Trim(), code != 0);
    }

    private static McpToolResult Transition(CliEnv env, JsonElement a)
    {
        var id = Opt(a, "id");
        var action = Opt(a, "action");
        if (id is null || action is null)
        {
            return new McpToolResult("error: 'id' and 'action' are required.", true);
        }

        if (action is not ("accept" or "start" or "submit"))
        {
            return new McpToolResult($"error: action '{action}' is not available over MCP; only accept, start and submit are. verify and clear are not MCP tools: they run from forge evidence via the CLI.", true);
        }

        return Capture(env, e => QuestCommands.Transition(e, action, id));
    }

    private static McpToolResult Forecast(CliEnv env, JsonElement a, ProjectPaths? project)
    {
        // Without a project the forecast is global: the snapshot lives under ~/.monarch, the factor is the start factor and the limits are the plan
        // defaults. A directory that does not exist stands in for the project, so no per-project file is read or written.
        var paths = project ?? new ProjectPaths(Path.Combine(Path.GetTempPath(), "monarch-no-project-" + Guid.NewGuid().ToString("N")));

        if (!a.TryGetProperty("estimates", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            return new McpToolResult("error: 'estimates' must be a non-empty array of {session_pct, weekly_pct}.", true);
        }

        double s = 0, w = 0;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("session_pct", out var sp) || !sp.TryGetDouble(out var sv)
                || !item.TryGetProperty("weekly_pct", out var wp) || !wp.TryGetDouble(out var wv) || sv < 0 || wv < 0)
            {
                return new McpToolResult("error: every estimate needs non-negative numbers session_pct and weekly_pct.", true);
            }

            s += sv;
            w += wv;
        }

        return new McpToolResult(ManaCommands.Forecast(env, paths, s, w));
    }

    private static McpToolResult KnowledgeWrite(CliEnv env, JsonElement a, ProjectPaths paths, string session)
    {
        var rel = Opt(a, "path");
        var content = Opt(a, "content");
        if (string.IsNullOrWhiteSpace(rel) || content is null)
        {
            return new McpToolResult("error: 'path' and 'content' are required.", true);
        }

        var bytes = Encoding.UTF8.GetByteCount(content);
        if (bytes > MaxKnowledgeBytes)
        {
            return new McpToolResult($"error: content is {bytes} bytes; the limit is {MaxKnowledgeBytes}.", true);
        }

        var segments = rel.Split('/');
        if (rel.StartsWith('/') || rel.Contains('\\', StringComparison.Ordinal) || rel.Contains(':', StringComparison.Ordinal) || Path.IsPathRooted(rel)
            || segments.Any(s => s.Length == 0 || s is "." or ".." || s.Contains('\0', StringComparison.Ordinal)))
        {
            return new McpToolResult("error: path must be relative, '/'-separated, without '..', '.', empty segments, drive letters or backslashes.", true);
        }

        var dir = Path.Combine(paths.MonarchDir, "knowledge");
        var chain = new List<string> { paths.MonarchDir, dir };
        var cur = dir;
        foreach (var s in segments)
        {
            cur = Path.Combine(cur, s);
            chain.Add(cur);
        }

        foreach (var p in chain)
        {
            FileSystemInfo info = Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p);
            if (!info.Exists)
            {
                break;
            }

            if (info.LinkTarget is not null)
            {
                return new McpToolResult($"error: '{Path.GetRelativePath(paths.MonarchDir, p)}' is a symbolic link; writes through links are refused.", true);
            }
        }

        var full = Path.GetFullPath(Path.Combine(dir, rel));
        if (!full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return new McpToolResult("error: path escapes .monarch/knowledge/.", true);
        }

        Files.WriteAtomic(full, content);
        var ledger = new ProjectLedger(paths, env.Clock);
        var clean = string.Join('/', segments);
        ledger.Append(session, "mcp", "KnowledgeWritten", ("path", clean), ("bytes", (long)bytes), ("sha256", ManaSupport.Sha(content)));
        return new McpToolResult($"wrote .monarch/knowledge/{clean} ({bytes} bytes)");
    }
}

/// <summary>The <c>gate_plan</c> tool.</summary>
internal static partial class GatePlan
{
    /// <summary>Validates and stores the plan; nothing is written unless the whole plan is valid.</summary>
    public static McpToolResult Apply(CliEnv env, JsonElement a, ProjectPaths paths, string session)
    {
        if (!a.TryGetProperty("gate", out var g) || g.ValueKind != JsonValueKind.Object)
        {
            return new McpToolResult("error: 'gate' object is required.", true);
        }

        var errors = new List<string>();
        var gateId = McpTools.Opt(g, "id") ?? string.Empty;
        if (!GateIdPattern().IsMatch(gateId))
        {
            errors.Add($"gate.id '{gateId}' must look like G-0001.");
        }

        var gateTitle = McpTools.Opt(g, "title");
        if (string.IsNullOrWhiteSpace(gateTitle))
        {
            errors.Add("gate.title is required.");
        }

        var gateRank = ParseEnum<Rank>(McpTools.Opt(g, "rank"), "gate.rank", errors);
        var gateKind = (McpTools.Opt(g, "kind") ?? "standard").ToLowerInvariant();
        if (gateKind is not ("standard" or "red" or "instance" or "jobchange"))
        {
            errors.Add($"gate.kind '{gateKind}' must be standard, red, instance or jobchange.");
        }

        var config = SystemConfigReader.Load(paths.Root);
        var board = new LocalFileBoard(paths.Root);
        var existing = board.ListQuestsAsync().GetAwaiter().GetResult();
        var planned = new List<Planned>();
        if (!a.TryGetProperty("quests", out var qs) || qs.ValueKind != JsonValueKind.Array)
        {
            errors.Add("'quests' array is required.");
        }
        else
        {
            foreach (var q in qs.EnumerateArray())
            {
                var id = McpTools.Opt(q, "id") ?? string.Empty;
                var tag = id.Length > 0 ? id : "quest";
                if (!QuestIdPattern().IsMatch(id))
                {
                    errors.Add($"quest id '{id}' must look like Q-0001.");
                }

                if (planned.Any(p => p.Quest.Id.Value == id))
                {
                    errors.Add($"{tag}: duplicate id in plan.");
                }

                var title = McpTools.Opt(q, "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    errors.Add($"{tag}: title is required.");
                }

                var kind = ParseEnum<QuestKind>(McpTools.Opt(q, "kind"), $"{tag}.kind", errors);
                var rank = ParseEnum<Rank>(McpTools.Opt(q, "rank"), $"{tag}.rank", errors);
                var include = Strings(q, "scope", "include");
                var exclude = Strings(q, "scope", "exclude");
                if (include.Count == 0)
                {
                    errors.Add($"{tag}: scope.include needs at least one glob.");
                }

                var dod = q.TryGetProperty("dod", out var d) && d.ValueKind == JsonValueKind.Array
                    ? d.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length > 0).ToList() : [];
                if (dod.Count == 0)
                {
                    errors.Add($"{tag}: dod needs at least one item.");
                }

                double sp = -1, wp = -1;
                if (!q.TryGetProperty("estimate", out var est) || est.ValueKind != JsonValueKind.Object
                    || !est.TryGetProperty("session_pct", out var spe) || !spe.TryGetDouble(out sp)
                    || !est.TryGetProperty("weekly_pct", out var wpe) || !wpe.TryGetDouble(out wp) || sp < 0 || wp < 0)
                {
                    errors.Add($"{tag}: estimate needs non-negative session_pct and weekly_pct.");
                }

                if (existing.FirstOrDefault(x => x.Id.Value == id) is { } old && old.State != QuestState.Proposed)
                {
                    errors.Add($"{tag}: quest exists in state {QuestCommands.StateName(old.State)}; only Proposed quests can be replaced.");
                }

                var requested = rank;
                var implied = RankPolicy.Assess(new ChangeFacts(FileCount: 1, LogicChange: false), config.RiskTags, include);
                string? reason = null;
                if (implied.Rank > rank)
                {
                    reason = $"rank raised {rank}->{implied.Rank}: {string.Join("; ", implied.Reasons)}";
                    rank = implied.Rank;
                }

                planned.Add(new Planned(
                    new BoardQuest(new QuestId(id), new GateId(gateId), title ?? string.Empty, kind, rank, QuestState.Proposed, dod, new QuestScope(include, exclude)),
                    requested, reason, McpTools.Opt(q, "boss"), new ManaCost(sp, wp, 0m)));
            }
        }

        if (errors.Count > 0)
        {
            return new McpToolResult("error: plan rejected, nothing written:\n- " + string.Join("\n- ", errors), true);
        }

        var oldGate = board.ListGatesAsync().GetAwaiter().GetResult().FirstOrDefault(x => x.Id.Value == gateId);
        board.UpsertGateAsync(new BoardGate(new GateId(gateId), gateTitle!, gateRank, oldGate?.State ?? "proposed", [.. planned.Select(p => p.Quest.Id)], null)).GetAwaiter().GetResult();
        var gateFile = Directory.EnumerateFiles(Path.Combine(paths.MonarchDir, "gates"), gateId + "-*.md").First();
        BoardFiles.Edit(gateFile, r => r.With("type", new YamlScalar(gateKind)));

        var ledger = new ProjectLedger(paths, env.Clock);
        if (oldGate is null)
        {
            ledger.Append(session, "mcp", "GateOpened", ("gate", gateId), ("title", gateTitle), ("rank", gateRank.ToString()), ("kind", gateKind));
        }

        foreach (var p in planned)
        {
            var revised = existing.Any(x => x.Id == p.Quest.Id);
            board.UpsertQuestAsync(p.Quest).GetAwaiter().GetResult();
            BoardFiles.Edit(board.FindQuestFile(p.Quest.Id.Value)!, r =>
            {
                r = r.With("estimate", new YamlMap(
                    [
                        new("session_pct", new YamlScalar(p.Estimate.SessionPct.ToString("0.##", CultureInfo.InvariantCulture))),
                        new("weekly_pct", new YamlScalar(p.Estimate.WeeklyPct.ToString("0.##", CultureInfo.InvariantCulture))),
                        new("gold_usd", new YamlScalar("0")),
                    ], true));
                return p.Boss is null ? r : r.With("boss", new YamlScalar(p.Boss, YamlQuote.Double));
            });
            ledger.Append(session, "mcp", "QuestProposed", ("quest", p.Quest.Id.Value), ("gate", gateId), ("rank", p.Quest.Rank.ToString()),
                ("kind", p.Quest.Kind.ToString().ToLowerInvariant()), ("revised", revised));
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("gate", gateId);
            w.WriteStartArray("quests");
            foreach (var p in planned)
            {
                w.WriteStartObject();
                w.WriteString("id", p.Quest.Id.Value);
                w.WriteString("rank", p.Quest.Rank.ToString());
                w.WriteBoolean("raised", p.Reason is not null);
                if (p.Reason is not null)
                {
                    w.WriteString("requestedRank", p.Requested.ToString());
                    w.WriteString("reason", p.Reason);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return new McpToolResult(Encoding.UTF8.GetString(ms.ToArray()));
    }

    private static T ParseEnum<T>(string? text, string field, List<string> errors)
        where T : struct, Enum
    {
        if (text is not null && Enum.TryParse<T>(text, true, out var v) && Enum.IsDefined(v))
        {
            return v;
        }

        errors.Add($"{field} '{text}' is not one of {string.Join("/", Enum.GetNames<T>())}.");
        return default;
    }

    private static List<string> Strings(JsonElement q, string obj, string name) =>
        q.TryGetProperty(obj, out var o) && o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var l) && l.ValueKind == JsonValueKind.Array
            ? [.. l.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length > 0)]
            : [];

    [GeneratedRegex(@"^G-\d{4,}$")]
    private static partial Regex GateIdPattern();

    [GeneratedRegex(@"^Q-\d{4,}$")]
    private static partial Regex QuestIdPattern();

    private sealed record Planned(BoardQuest Quest, Rank Requested, string? Reason, string? Boss, ManaCost Estimate);
}
