using System.Text.Json;
using Udpi30.Ontology.Models;

namespace Udpi30.Ontology;

/// <summary>
/// Reads and writes the tool's local ontology file (<c>*.ontology.json</c>) — the
/// editable source of truth that round-trips logical names and descriptions
/// alongside the technical ontology structure.
/// </summary>
public static class OntologyIo
{
    public const string FileExtension = ".ontology.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Save(OntologyDocument doc, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(doc, Options));

    public static string Serialize(OntologyDocument doc) => JsonSerializer.Serialize(doc, Options);

    public static OntologyDocument Load(string path)
    {
        var text = File.ReadAllText(path);
        return JsonSerializer.Deserialize<OntologyDocument>(text, Options)
            ?? throw new InvalidOperationException($"'{path}' did not contain a valid ontology document.");
    }
}
