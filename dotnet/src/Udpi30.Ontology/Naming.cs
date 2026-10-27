using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Udpi30.Ontology;

/// <summary>
/// Turns physical semantic-model identifiers (e.g. <c>dim_customer</c>,
/// <c>FactSalesOrderDetail</c>, <c>cust_id</c>) into human-friendly logical names
/// and sanitized Fabric-safe technical names, plus generates descriptions. This is
/// the "create logical names and descriptions" behavior the tool is built around.
/// </summary>
public static partial class Naming
{
    // Dimensional-modeling prefixes that add no business meaning to a logical name.
    private static readonly string[] StripPrefixes =
        ["dim_", "fact_", "dim", "fact", "d_", "f_", "tbl_", "tb_", "vw_", "v_", "stg_", "ref_"];

    [GeneratedRegex(@"[A-Za-z][a-z0-9]*|[A-Z]+(?![a-z])|[0-9]+", RegexOptions.Compiled)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"[^A-Za-z0-9_-]", RegexOptions.Compiled)]
    private static partial Regex NonNameChars();

    /// <summary>
    /// Split a raw identifier into title-cased words: "FactSalesOrder" → "Sales Order"
    /// (the <c>Fact</c> prefix is dropped), "dim_customer" → "Customer",
    /// "unit_price_usd" → "Unit Price Usd".
    /// </summary>
    public static string Humanize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw ?? "";
        }

        var working = raw.Trim();
        foreach (var prefix in StripPrefixes)
        {
            if (working.Length > prefix.Length
                && working.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                working = working[prefix.Length..];
                break;
            }
        }

        var words = WordRegex().Matches(working)
            .Select(m => m.Value)
            .Where(w => w.Length > 0)
            .Select(TitleWord)
            .ToList();

        var result = string.Join(" ", words).Trim();
        return result.Length == 0 ? TitleWord(raw.Trim()) : result;
    }

    /// <summary>
    /// Produce a Fabric-safe technical name matching <c>^[A-Za-z][A-Za-z0-9_-]{0,127}$</c>
    /// from a logical name: PascalCase with non-conforming characters removed.
    /// </summary>
    public static string ToTechnicalName(string logicalOrRaw, string fallback = "Item")
    {
        var pascal = string.Concat(Humanize(logicalOrRaw).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        pascal = NonNameChars().Replace(pascal, "");

        if (pascal.Length == 0)
        {
            pascal = fallback;
        }
        // Must start with a letter.
        if (!char.IsLetter(pascal[0]))
        {
            pascal = "X" + pascal;
        }
        if (pascal.Length > 128)
        {
            pascal = pascal[..128];
        }
        return pascal;
    }

    private static string TitleWord(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }
        // Keep all-caps acronyms (ID, USD, SKU) as-is.
        if (word.Length <= 4 && word.All(char.IsUpper))
        {
            return word;
        }
        return char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..].ToLowerInvariant();
    }

    // -- description generation ----------------------------------------------

    public static string DescribeEntity(string logicalName, string sourceTable, int propertyCount) =>
        $"The {logicalName} entity, derived from semantic-model table '{sourceTable}'. " +
        $"Represents a {logicalName.ToLowerInvariant()} with {propertyCount} attribute(s).";

    public static string DescribeProperty(string logicalName, string valueType, string sourceColumn) =>
        $"{logicalName} ({valueType}) — mapped from column '{sourceColumn}'.";

    public static string DescribeRelationship(string sourceLogical, string targetLogical) =>
        $"Associates each {sourceLogical} with its related {targetLogical}.";

    public static string DescribeOntology(string modelName, int entityCount, int relationshipCount) =>
        $"Ontology generated from the '{modelName}' semantic model: " +
        $"{entityCount} entity type(s) and {relationshipCount} relationship type(s). " +
        "Logical names and descriptions were generated automatically by the UDPI30 Ontology Builder.";
}

/// <summary>
/// Generates unique positive 64-bit IDs for ontology entity types, properties, and
/// relationship types, as required by the Fabric ontology definition format.
/// </summary>
public sealed class IdGenerator
{
    private readonly Random _random;
    private readonly HashSet<long> _used = [];

    public IdGenerator(int? seed = null) => _random = seed is { } s ? new Random(s) : new Random();

    /// <summary>Seed the generator with IDs already present (e.g. from a loaded file).</summary>
    public void Reserve(IEnumerable<string> existing)
    {
        foreach (var id in existing)
        {
            if (long.TryParse(id, out var v))
            {
                _used.Add(v);
            }
        }
    }

    /// <summary>Next unique positive 64-bit ID, as a string (13–16 digits).</summary>
    public string Next()
    {
        long value;
        do
        {
            // 1_000_000_000_000 .. ~9_000_000_000_000_000 — comfortably positive Int64.
            value = (long)(_random.NextDouble() * 9_000_000_000_000_000L) + 1_000_000_000_000L;
        }
        while (!_used.Add(value));
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
