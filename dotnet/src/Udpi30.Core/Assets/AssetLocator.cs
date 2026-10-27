namespace Udpi30.Core.Assets;

/// <summary>
/// Locates the static assets shipped alongside the assembly — the JSON schema
/// (<c>udp.schema.json</c>) and the built-in project templates. These are copied
/// next to the assembly via <c>CopyToOutputDirectory</c> and, for a packed
/// <c>dotnet tool</c>, extracted into the tool store; in both cases they live
/// under <see cref="System.AppContext.BaseDirectory"/>.
/// </summary>
public static class AssetLocator
{
    /// <summary>Root directory containing the <c>Assets</c> folder.</summary>
    public static string AssetsRoot { get; } =
        Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>Absolute path to the bundled <c>udp.schema.json</c>.</summary>
    public static string SchemaPath => Path.Combine(AssetsRoot, "udp.schema.json");

    /// <summary>Absolute path to the built-in templates directory.</summary>
    public static string TemplatesRoot => Path.Combine(AssetsRoot, "templates");

    /// <summary>Reads the bundled JSON schema text.</summary>
    public static string ReadSchema()
    {
        if (!File.Exists(SchemaPath))
        {
            throw new FileNotFoundException(
                $"Bundled schema not found at '{SchemaPath}'. The build may not have copied Assets to the output directory.");
        }
        return File.ReadAllText(SchemaPath);
    }
}
