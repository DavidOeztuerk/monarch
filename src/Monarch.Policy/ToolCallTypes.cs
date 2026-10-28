using Monarch.Domain;

namespace Monarch.Policy;

/// <summary>Ergebnisart einer Policy-Entscheidung. Rangfolge: Deny vor Ask vor Allow.</summary>
public enum PolicyDecisionKind
{
    /// <summary>Erlaubt.</summary>
    Allow,
    /// <summary>Nachfrage beim Menschen (headless: Verweigerung, ADR-0003).</summary>
    Ask,
    /// <summary>Verweigert.</summary>
    Deny,
}

/// <summary>Namen der Regeln, wie sie in Entscheidungen und im Ledger erscheinen.</summary>
public static class PolicyRules
{
    /// <summary>Schutz von Ledger und Shadow-Definitionen.</summary>
    public const string ProtectPaths = "protect_paths";
    /// <summary>Schutz von Secret-Dateien.</summary>
    public const string ProtectSecrets = "protect_secrets";
    /// <summary>Verbotene Bash-Kommandos.</summary>
    public const string DenyBash = "deny_bash";
    /// <summary>Schutz geschützter Branches und Merge-Wege (ADR-0007).</summary>
    public const string ProtectBranches = "protect_branches";
    /// <summary>Read-only Shadow-Klassen.</summary>
    public const string ShadowReadOnly = "shadow_read_only";
    /// <summary>Hoher Rank in permissiven Modi (ADR-0002).</summary>
    public const string RefuseHighRankInBypassMode = "refuse_high_rank_in_bypass_mode";
    /// <summary>Quest Scope Lock.</summary>
    public const string ScopeLock = "scope_lock";
    /// <summary>Rank-S-Freigabe (ADR-0003).</summary>
    public const string RankSApproval = "rank_s_approval";
    /// <summary>Keine Regel greift.</summary>
    public const string None = "none";
}

/// <summary>Zusatzsignale einer Entscheidung, die der Aufrufer ins Ledger bucht.</summary>
public static class PolicySignals
{
    /// <summary>Schreibversuch außerhalb des Scopes: Hidden Boss, Replan nötig.</summary>
    public const string DoubleDungeon = "DoubleDungeon";
    /// <summary>Fehlender oder unbekannter Permission-Mode, behandelt wie default (ADR-0002).</summary>
    public const string UnknownPermissionMode = "policy.unknown_mode";
}

/// <summary>Entscheidung der Tool-Call-Policy.</summary>
/// <param name="Kind">Allow, Ask oder Deny.</param>
/// <param name="Rule">Auslösende Regel.</param>
/// <param name="Reason">Begründung, die dem Modell angezeigt wird.</param>
public sealed record PolicyDecision(PolicyDecisionKind Kind, string Rule, string Reason)
{
    /// <summary>Zusatzsignale, z. B. <see cref="PolicySignals.DoubleDungeon"/>.</summary>
    public IReadOnlyList<string> Signals { get; init; } = [];

    /// <summary>Erlaubt, keine Regel greift.</summary>
    public static PolicyDecision Allow { get; } = new(PolicyDecisionKind.Allow, PolicyRules.None, "");

    /// <summary>Erzeugt ein Deny.</summary>
    public static PolicyDecision Deny(string rule, string reason) => new(PolicyDecisionKind.Deny, rule, reason);

    /// <summary>Erzeugt ein Ask.</summary>
    public static PolicyDecision Ask(string rule, string reason) => new(PolicyDecisionKind.Ask, rule, reason);
}

/// <summary>Ein Tool-Aufruf, wie ihn der PreToolUse-Hook liefert.</summary>
/// <param name="ToolName">Tool, z. B. Edit, Write, Bash, Read.</param>
/// <param name="FilePath">Zielpfad bei Datei-Tools.</param>
/// <param name="Command">Kommando bei Bash.</param>
/// <param name="AgentType">Shadow-Klasse des aufrufenden Subagents (ADR-0001), sonst null.</param>
/// <param name="AgentId">Id des Subagents.</param>
/// <param name="PermissionMode">Permission-Mode (ADR-0002); null oder unbekannt gilt als default.</param>
/// <param name="Cwd">Arbeitsverzeichnis.</param>
/// <param name="Glob">Glob-Filter bei Grep/Glob (<c>tool_input.glob</c>); bei Grep ist <paramref name="FilePath"/> der Suchpfad (<c>tool_input.path</c>).</param>
public sealed record ToolCall(
    string ToolName, string? FilePath = null, string? Command = null, string? AgentType = null,
    string? AgentId = null, string? PermissionMode = null, string Cwd = "", string? Glob = null);

/// <summary>Die aktive Quest, soweit die Policy sie braucht.</summary>
/// <param name="Id">Kennung.</param>
/// <param name="Rank">Rank.</param>
/// <param name="Scope">Pfad-Scope.</param>
public sealed record ActiveQuest(QuestId Id, Rank Rank, QuestScope Scope);

/// <summary>Konfigurierbare Policy-Optionen (Defaults aus system.yaml).</summary>
public sealed record PolicyOptions
{
    /// <summary>Geschützte Pfade (Ledger, lokale Anker/Snapshots, Shadow-Definitionen, Systemkonfiguration, Quests, Gates, Claude-Settings des Projekts).</summary>
    public IReadOnlyList<string> ProtectPaths { get; init; } =
        [".monarch/ledger/**", ".monarch/local/**", ".monarch/shadows/*/shadow.yaml", ".monarch/system.yaml", ".monarch/quests/**", ".monarch/gates/**", ".claude/settings*.json"];

    /// <summary>Zusätzliche Secret-Globs zu <see cref="DefaultSecretGlobs"/>.</summary>
    public IReadOnlyList<string> ExtraSecretGlobs { get; init; } = [];

    /// <summary>Ausnahmen vom Secret-Schutz.</summary>
    public IReadOnlyList<string> SecretExceptions { get; init; } = ["**/.env.example"];

    /// <summary>Verbotene Bash-Muster (<c>*</c>-Glob gegen jedes Kommando-Segment).</summary>
    public IReadOnlyList<string> DenyBash { get; init; } =
        ["git push --force*", "git reset --hard*", "dotnet ef database drop*", "monarch permit*", "monarch promote*"];

    /// <summary>Shadow-Klassen, die nichts schreiben dürfen.</summary>
    public IReadOnlyList<string> ReadOnlyShadowTypes { get; init; } = ["warden", "scout"];

    /// <summary>Branches, in die kein Akteur pushen oder mergen darf (Vergleich case-insensitiv). Die Konfiguration kann nur ergänzen.</summary>
    public IReadOnlyList<string> ProtectedBranches { get; init; } = ["main", "master"];

    /// <summary>Integrations-Branch: ein direkter Push dorthin fragt nach, gemerged wird über <c>monarch gate merge</c>.</summary>
    public string IntegrationBranch { get; init; } = "develop";

    /// <summary>Schaltet <see cref="PolicyRules.RefuseHighRankInBypassMode"/>.</summary>
    public bool RefuseHighRankInBypassMode { get; init; } = true;

    /// <summary>Schaltet <see cref="PolicyRules.ScopeLock"/>.</summary>
    public bool ScopeLock { get; init; } = true;

    /// <summary>Eingebaute Secret-Globs.</summary>
    public static IReadOnlyList<string> DefaultSecretGlobs { get; } =
        ["**/.env", "**/.env.*", "**/*.key", "**/*.p8", "**/*.pfx", "**/keycloak.env", "**/.netrc", "**/id_rsa*"];
}

/// <summary>Kontext der Policy-Auswertung.</summary>
/// <param name="ProjectRoot">Projektwurzel (absolut).</param>
/// <param name="ActiveQuest">Aktive Quest oder null.</param>
/// <param name="PenaltyZoneActive">True, wenn die Penalty Zone aktiv ist.</param>
/// <param name="Options">Optionen.</param>
public sealed record PolicyContext(string ProjectRoot, ActiveQuest? ActiveQuest, bool PenaltyZoneActive, PolicyOptions Options)
{
    /// <summary>Kontext mit Default-Optionen.</summary>
    public PolicyContext(string projectRoot, ActiveQuest? activeQuest = null)
        : this(projectRoot, activeQuest, false, new PolicyOptions()) { }

    /// <summary>Home-Verzeichnis für die globalen Schutzpfade und die Binary-Erkennung; null: <c>$HOME</c> des Hook-Prozesses.</summary>
    public string? HomeDir { get; init; }

    /// <summary>Wert von <c>PATH</c> für die Auflösung eines nackten <c>monarch</c>; null: <c>$PATH</c> des Hook-Prozesses.</summary>
    public string? PathEnv { get; init; }
}

/// <summary>Modus der Penalty Zone.</summary>
public enum PenaltyMode
{
    /// <summary>Nur warnen.</summary>
    Warn,
    /// <summary>Main und Side verweigern.</summary>
    Block,
}
