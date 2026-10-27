using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Generators;

namespace Udpi30.Core.Tests;

public class TemplateTests
{
    private static IAnsiConsole Silent() => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(TextWriter.Null),
    });

    [Fact]
    public void ListTemplates_Includes_Builtins()
    {
        var names = TemplateEngine.ListTemplates().Select(t => t["name"]?.ToString()).ToList();
        Assert.Contains("blank", names);
        Assert.Contains("medallion", names);
    }

    [Fact]
    public void Init_Blank_Substitutes_ProjectName_And_Loads()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "udp-tmpl-" + Guid.NewGuid().ToString("N"));
        try
        {
            var udpYml = TemplateEngine.InitFromTemplate("blank", outDir,
                new Dictionary<string, string> { ["project_name"] = "acme-analytics" }, Silent());

            Assert.True(File.Exists(udpYml));
            var text = File.ReadAllText(udpYml);
            Assert.Contains("name: acme-analytics", text);
            Assert.DoesNotContain("${{", text); // all placeholders rendered

            // The generated project must load and validate.
            var deployment = Loader.LoadDeployment(udpYml);
            Assert.Equal("acme-analytics", deployment.Deployment.Name);
            Assert.True(deployment.Resources.Notebooks.ContainsKey("sample_notebook"));
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }
}
