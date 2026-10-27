using System.Formats.Tar;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Spectre.Console;
using Udpi30.Core.Assets;
using Udpi30.Core.Yaml;

namespace Udpi30.Core.Generators;

/// <summary>
/// Generates new deployment projects from templates (built-in, local path,
/// <c>github:owner/repo</c>, or URL tar.gz). Mirrors <c>generators/templates.py</c>;
/// template files use <c>${{ var }}</c> substitution.
/// </summary>
public static partial class TemplateEngine
{
    private static readonly HttpClient Http = new();

    private static readonly HashSet<string> RenderableExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".yml", ".yaml", ".py", ".md", ".txt", ".json", ".sql", ".kql" };

    [GeneratedRegex(@"\$\{\{\s*([^}]+?)\s*\}\}")]
    private static partial Regex VariablePattern();

    private static string TemplatesDir => AssetLocator.TemplatesRoot;

    /// <summary>List available built-in templates with their metadata.</summary>
    public static List<Dictionary<string, object?>> ListTemplates()
    {
        var templates = new List<Dictionary<string, object?>>();
        if (!Directory.Exists(TemplatesDir))
        {
            return templates;
        }

        foreach (var dir in Directory.GetDirectories(TemplatesDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var meta = new Dictionary<string, object?> { ["name"] = Path.GetFileName(dir) };
            var metaFile = Path.Combine(dir, "template.yml");
            if (File.Exists(metaFile))
            {
                var parsed = YamlFactory.CreateGenericDeserializer().Deserialize<object?>(File.ReadAllText(metaFile));
                if (parsed is IDictionary<object, object> d)
                {
                    foreach (var kv in d)
                    {
                        meta[kv.Key?.ToString() ?? ""] = kv.Value;
                    }
                }
            }
            templates.Add(meta);
        }
        return templates;
    }

    /// <summary>Initialize a new project from a template. Returns the path to the created udp.yml.</summary>
    public static string InitFromTemplate(string templateName, string outputDir,
        Dictionary<string, string>? variables = null, IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        variables ??= [];

        if (templateName.StartsWith("http://") || templateName.StartsWith("https://"))
        {
            return InitFromUrl(templateName, outputDir, console);
        }
        if (templateName.StartsWith("github:"))
        {
            var repo = templateName["github:".Length..];
            return InitFromTemplate($"https://github.com/{repo}/archive/refs/heads/main.tar.gz", outputDir, variables, console);
        }

        var templateDir = Path.Combine(TemplatesDir, templateName);
        if (!Directory.Exists(templateDir))
        {
            templateDir = templateName;
            if (!Directory.Exists(templateDir))
            {
                var available = string.Join(", ", ListTemplates().Select(t => t["name"]?.ToString()));
                throw new ArgumentException(
                    $"Template '{templateName}' not found.\n" +
                    $"Available templates: {(string.IsNullOrEmpty(available) ? "none" : available)}\n" +
                    "Install templates or provide a path to a custom template directory.");
            }
        }

        var meta = new Dictionary<string, object?>();
        var metaFile = Path.Combine(templateDir, "template.yml");
        if (File.Exists(metaFile))
        {
            if (YamlFactory.CreateGenericDeserializer().Deserialize<object?>(File.ReadAllText(metaFile)) is IDictionary<object, object> d)
            {
                foreach (var kv in d)
                {
                    meta[kv.Key?.ToString() ?? ""] = kv.Value;
                }
            }
        }

        console.MarkupLine($"Initializing from template: [bold]{Markup.Escape(meta.GetValueOrDefault("name")?.ToString() ?? templateName)}[/]");
        if (meta.GetValueOrDefault("description") is { } desc)
        {
            console.MarkupLine($"  {Markup.Escape(desc.ToString() ?? "")}");
        }

        // Default variables from template metadata.
        if (meta.GetValueOrDefault("variables") is IDictionary<object, object> defaults)
        {
            foreach (var kv in defaults)
            {
                var key = kv.Key?.ToString() ?? "";
                if (!variables.ContainsKey(key))
                {
                    variables[key] = kv.Value is IDictionary<object, object> info
                        ? info.TryGetValue("default", out var dv) ? dv?.ToString() ?? "" : ""
                        : kv.Value?.ToString() ?? "";
                }
            }
        }

        Directory.CreateDirectory(outputDir);

        var filesCreated = 0;
        foreach (var src in Directory.GetFiles(templateDir, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (Path.GetFileName(src) == "template.yml")
            {
                continue;
            }
            var relative = Path.GetRelativePath(templateDir, src);
            var dest = Path.Combine(outputDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            if (RenderableExtensions.Contains(Path.GetExtension(src)))
            {
                var content = Render(File.ReadAllText(src), variables);
                File.WriteAllText(dest, content);
            }
            else
            {
                File.Copy(src, dest, overwrite: true);
            }
            filesCreated++;
        }

        console.MarkupLine($"  Created {filesCreated} files in {Markup.Escape(outputDir)}");
        console.WriteLine();
        console.MarkupLine("[bold green]Project initialized.[/]");
        console.WriteLine();
        console.MarkupLine("Next steps:");
        console.MarkupLine($"  cd {Markup.Escape(outputDir)}");
        console.MarkupLine("  # Edit udp.yml to match your environment");
        console.MarkupLine("  udpi30 validate");
        console.MarkupLine("  udpi30 plan");
        console.MarkupLine("  udpi30 deploy");

        return Path.Combine(outputDir, "udp.yml");
    }

    private static string Render(string content, IReadOnlyDictionary<string, string> variables) =>
        VariablePattern().Replace(content, m =>
        {
            var key = m.Groups[1].Value.Trim();
            return variables.GetValueOrDefault(key, "");
        });

    private static string InitFromUrl(string url, string outputDir, IAnsiConsole console)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "udp-tmpl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var bytes = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            var archive = Path.Combine(tmp, "template.tar.gz");
            File.WriteAllBytes(archive, bytes);

            var extractDir = Path.Combine(tmp, "extract");
            Directory.CreateDirectory(extractDir);
            using (var fs = File.OpenRead(archive))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                TarFile.ExtractToDirectory(gz, extractDir, overwriteFiles: true);
            }

            var metaFile = Directory.GetFiles(extractDir, "template.yml", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new ArgumentException("No template.yml found in downloaded archive");
            var templateDir = Path.GetDirectoryName(metaFile)!;

            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
            CopyDirectory(templateDir, outputDir);
            console.MarkupLine($"[green]Created project from URL template at {Markup.Escape(outputDir)}[/]");
            return Path.Combine(outputDir, "udp.yml");
        }
        finally
        {
            try
            {
                Directory.Delete(tmp, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
