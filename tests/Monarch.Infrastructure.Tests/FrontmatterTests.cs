using System.Text;
using Monarch.Infrastructure.Boards.Frontmatter;

namespace Monarch.Infrastructure.Tests;

public class FrontmatterTests
{
    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), new UTF8Encoding(false));

    public static TheoryData<string> Fixtures() => new()
    {
        "Q-0003-wt-ports-loopback.md",
        "Q-0008-wt-abmelden-ueberall.md",
        "Q-0040-cp-lizenz-token-hygiene.md",
        "G-0001-wt-interne-vertrauensgrenze.md",
        "G-0012-cp-lizenz.md",
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Real_backlog_files_roundtrip(string name)
    {
        var text = Fixture(name);
        var doc = FrontmatterDocument.Parse(text);
        var written = doc.Write();
        Assert.Equal(doc, FrontmatterDocument.Parse(written));
        Assert.Equal(written, FrontmatterDocument.Parse(written).Write());
        // body is preserved byte for byte
        var closing = text.IndexOf("\n---\n", 3, StringComparison.Ordinal) + 5;
        Assert.Equal(Encoding.UTF8.GetBytes(text[closing..]), Encoding.UTF8.GetBytes(doc.Body));
        Assert.EndsWith(doc.Body, written, StringComparison.Ordinal);
    }

    [Fact]
    public void Quest_fixture_values_are_read()
    {
        var root = FrontmatterDocument.Parse(Fixture("Q-0003-wt-ports-loopback.md")).Root;
        Assert.Equal("Q-0003", root.GetString("id"));
        Assert.Equal(["infra", "auth"], root.GetStrings("risk_tags"));
        Assert.Equal(3, root.GetStrings("dod").Count);
        Assert.StartsWith("Dienstports 8001", root.GetStrings("dod")[0], StringComparison.Ordinal);
        var scope = Assert.IsType<YamlMap>(root.Get("scope"));
        Assert.Equal("workertransfer", scope.GetString("repo"));
        Assert.Equal(4, scope.GetStrings("include").Count);
        Assert.Empty(scope.GetStrings("exclude"));
        var estimate = Assert.IsType<YamlMap>(root.Get("estimate"));
        Assert.True(estimate.Flow);
        Assert.Equal("8", estimate.GetString("session_pct"));
        Assert.Equal(YamlQuote.Double, Assert.IsType<YamlScalar>(root.Get("boss")).Quote);
    }

    [Fact]
    public void Quoted_and_special_items_are_read()
    {
        var root = FrontmatterDocument.Parse(Fixture("Q-0008-wt-abmelden-ueberall.md")).Root;
        Assert.Equal(["Q-0016"], root.GetStrings("blocked_by"));
        Assert.StartsWith("Abmelden beendet alle Sitzungen", root.GetStrings("dod")[0], StringComparison.Ordinal);
        Assert.Contains("(RevokeSubjectBefore)", root.GetStrings("dod")[0], StringComparison.Ordinal);
        var gate = FrontmatterDocument.Parse(Fixture("G-0012-cp-lizenz.md")).Root;
        Assert.Equal(["NoeliaControlPlane", "workertransfer"], gate.GetStrings("repo"));
        Assert.Equal(["Q-0040", "Q-0038", "Q-0039"], gate.GetStrings("quests"));
    }

    [Fact]
    public void All_supported_shapes_parse()
    {
        var doc = FrontmatterDocument.Parse("---\na: b\nq: 'it''s'\nd: \"a\\\"b\\\\n\"\nl: [x, \"y, z\", 'w']\nm: { k: v, k2: [1, 2] }\nn:\n  repo: r\n  include: [\"a\"]\nlist:\n  - one\n  - \"two: 2\"\nempty: []\n---\n# T\nbody");
        var r = doc.Root;
        Assert.Equal("b", r.GetString("a"));
        Assert.Equal("it's", r.GetString("q"));
        Assert.Equal("a\"b\\n", r.GetString("d"));
        Assert.Equal(["x", "y, z", "w"], r.GetStrings("l"));
        var m = Assert.IsType<YamlMap>(r.Get("m"));
        Assert.Equal(["1", "2"], Assert.IsType<YamlList>(m.Get("k2")).Items.Cast<YamlScalar>().Select(s => s.Text));
        Assert.Equal(["one", "two: 2"], r.GetStrings("list"));
        Assert.Empty(r.GetStrings("empty"));
        Assert.Equal("# T\nbody", doc.Body);
        Assert.Equal(doc, FrontmatterDocument.Parse(doc.Write()));
    }

    [Fact]
    public void Body_without_trailing_newline_and_crlf_is_kept()
    {
        const string body = "line1\r\nline2 ä ö\r\n\r\nend";
        var doc = FrontmatterDocument.Parse("---\nid: X\n---\n" + body);
        Assert.Equal(body, doc.Body);
        Assert.EndsWith(body, doc.Write(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("no frontmatter")]
    [InlineData("---\nid: X\nno end")]
    [InlineData("---\nl: [a, b\n---\n")]
    [InlineData("---\nm: { a: 1\n---\n")]
    [InlineData("---\ns: \"open\n---\n")]
    public void Malformed_input_throws(string text) => Assert.Throws<FormatException>(() => FrontmatterDocument.Parse(text));

    [Fact]
    public void With_sets_keeps_position()
    {
        var root = FrontmatterDocument.Parse("---\na: 1\nb: 2\n---\n").Root.With("a", new YamlScalar("9")).With("c", new YamlScalar("3"));
        Assert.Equal(["a", "b", "c"], root.Entries.Select(e => e.Key));
        Assert.Equal("9", root.GetString("a"));
    }
}

public class YamlCommentTests
{
    [Fact]
    public void Comments_outside_quotes_are_stripped_and_inside_quotes_kept()
    {
        var m = Monarch.Infrastructure.Boards.Frontmatter.YamlDocument.Parse("a: b # c\n# full\nq: \"x # y\"  # z\nl: [1, 2] # t\nu: it's#ok\n");
        Assert.Equal("b", m.GetString("a"));
        Assert.Equal("x # y", m.GetString("q"));
        Assert.Equal(["1", "2"], m.GetStrings("l"));
        Assert.Equal("it's#ok", m.GetString("u"));
    }
}
