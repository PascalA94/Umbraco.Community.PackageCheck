using System.Text.Json;
using PackageCheck.Core;
using PackageCheck.Core.Model;
using PackageCheck.Inputs;

namespace PackageCheck.Inputs.Tests;

public class RepoShapeTests
{
    public static TheoryData<string> Shapes() => new(Enumerable.Range(1, 10).Select(i => $"shape-{i:00}").ToArray());

    [Theory, MemberData(nameof(Shapes))]
    public async Task Shape_MatchesExpected(string shape)
    {
        var root = Fixtures.Path(shape);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "expected.json")));
        var e = expected.RootElement;
        var transitive = e.TryGetProperty("transitive", out var t) && t.GetBoolean();
        var options = new InputOptions(transitive, UseMsBuild: false, StopAt: root);

        if (e.TryGetProperty("error", out var error))
        {
            Assert.Equal("NoUmbracoProject", error.GetString());
            await Assert.ThrowsAsync<NoUmbracoProjectException>(() => new ProjectReader().ReadAsync(root, options, CancellationToken.None));
            return;
        }

        var input = await new ProjectReader().ReadAsync(root, options, CancellationToken.None);

        Assert.Equal(e.GetProperty("transitiveStatus").GetString(), input.Transitive.ToString());
        Assert.Equal(e.GetProperty("siteUmbracoVersion").GetString(), input.SiteUmbracoVersion?.ToNormalizedString());
        Assert.Equal(e.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()), input.Warnings);

        var expectedProjects = e.GetProperty("projects").EnumerateArray().ToList();
        Assert.Equal(expectedProjects.Select(p => p.GetProperty("name").GetString()),
            input.Projects.Select(p => Path.GetFileNameWithoutExtension(p.Path)));
        for (var i = 0; i < expectedProjects.Count; i++)
        {
            var want = expectedProjects[i].GetProperty("packages").EnumerateArray()
                .Select(p => $"{p.GetProperty("id").GetString()}|{p.GetProperty("version").GetString()}|{p.GetProperty("direct").GetBoolean()}|{p.GetProperty("unresolved").GetBoolean()}");
            var got = input.Projects[i].Packages
                .Select(p => $"{p.Id}|{p.Version?.ToNormalizedString()}|{p.Direct}|{p.UnresolvedReason is not null}");
            Assert.Equal(want, got);
        }
    }
}
