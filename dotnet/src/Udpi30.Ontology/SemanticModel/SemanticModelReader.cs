using System.Text;
using System.Text.Json.Nodes;
using Udpi30.Core.Providers;

namespace Udpi30.Ontology.SemanticModel;

/// <summary>Identifies a semantic model in a workspace.</summary>
public sealed record SemanticModelRef(string WorkspaceId, string WorkspaceName, string ItemId, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Reads a Fabric semantic model's definition through the Fabric REST API and
/// parses its TMDL parts into a <see cref="SemanticModelSchema"/>. Uses the public
/// <see cref="FabricClient.Request"/> / <see cref="FabricClient.WaitForOperation"/>
/// surface so it can request the TMDL format explicitly and follow the
/// long-running <c>getDefinition</c> operation.
/// </summary>
public static class SemanticModelReader
{
    /// <summary>List the semantic models in a workspace.</summary>
    public static List<SemanticModelRef> ListSemanticModels(FabricClient client, string workspaceId, string workspaceName)
    {
        var models = new List<SemanticModelRef>();
        foreach (var node in client.ListItems(workspaceId, "SemanticModel"))
        {
            var id = node["id"]?.GetValue<string>();
            var name = node["displayName"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
            {
                models.Add(new SemanticModelRef(workspaceId, workspaceName, id, name));
            }
        }
        return models.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Fetch and parse the schema (tables, columns, relationships) of a semantic model.</summary>
    public static SemanticModelSchema ReadSchema(FabricClient client, string workspaceId, string itemId)
    {
        var result = client.Request(
            "POST",
            $"/workspaces/{workspaceId}/semanticModels/{itemId}/getDefinition",
            queryParams: new Dictionary<string, string> { ["format"] = "TMDL" });

        var opUrl = result?["operation_url"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(opUrl))
        {
            result = client.WaitForOperationResult(opUrl);
        }

        var parts = (result?["definition"]?["parts"] ?? result?["parts"]) as JsonArray
            ?? throw new InvalidOperationException(
                "The semantic model returned no definition parts. Confirm the item is a semantic model and you have read access.");

        var tmdlTexts = new List<string>();
        foreach (var partNode in parts)
        {
            if (partNode is not JsonObject part)
            {
                continue;
            }
            var path = part["path"]?.GetValue<string>() ?? "";
            if (!path.EndsWith(".tmdl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var payload = part["payload"]?.GetValue<string>();
            if (string.IsNullOrEmpty(payload))
            {
                continue;
            }
            tmdlTexts.Add(DecodePayload(payload, part["payloadType"]?.GetValue<string>()));
        }

        if (tmdlTexts.Count == 0)
        {
            throw new InvalidOperationException(
                "The semantic model definition contained no TMDL parts to parse.");
        }

        return TmdlParser.Parse(tmdlTexts);
    }

    private static string DecodePayload(string payload, string? payloadType)
    {
        // Fabric returns definition parts as InlineBase64 by default.
        if (string.Equals(payloadType, "InlineBase64", StringComparison.OrdinalIgnoreCase) || payloadType is null)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        }
        return payload;
    }
}
