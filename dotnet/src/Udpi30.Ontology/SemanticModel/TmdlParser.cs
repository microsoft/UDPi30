namespace Udpi30.Ontology.SemanticModel;

/// <summary>A table parsed from a semantic model's TMDL definition.</summary>
public sealed class SmTable
{
    public string Name { get; set; } = "";
    public List<SmColumn> Columns { get; init; } = [];
}

/// <summary>A column parsed from a TMDL table.</summary>
public sealed class SmColumn
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "string";
    public bool IsTimeRelated => DataType is "dateTime";
}

/// <summary>A relationship parsed from a semantic model's TMDL definition.</summary>
public sealed class SmRelationship
{
    public string FromTable { get; set; } = "";
    public string FromColumn { get; set; } = "";
    public string ToTable { get; set; } = "";
    public string ToColumn { get; set; } = "";
}

/// <summary>The subset of a semantic model the ontology builder cares about.</summary>
public sealed class SemanticModelSchema
{
    public List<SmTable> Tables { get; init; } = [];
    public List<SmRelationship> Relationships { get; init; } = [];
}

/// <summary>
/// A focused, line-based parser for Tabular Model Definition Language (TMDL). It
/// extracts only what an ontology needs — tables, their columns and data types,
/// and relationships — and ignores measures, partitions, expressions, hierarchies,
/// and annotations. TMDL nests blocks by tab indentation; names containing special
/// characters are single-quoted with doubled inner quotes.
/// </summary>
public static class TmdlParser
{
    private static readonly string[] BlockResetKeywords =
        ["measure", "partition", "hierarchy", "calculationGroup", "annotation", "changedProperty"];

    /// <summary>
    /// Parse a collection of TMDL part texts (one per file, e.g. each
    /// <c>definition/tables/*.tmdl</c> plus <c>relationships.tmdl</c>) into a schema.
    /// </summary>
    public static SemanticModelSchema Parse(IEnumerable<string> tmdlParts)
    {
        var schema = new SemanticModelSchema();
        foreach (var text in tmdlParts)
        {
            ParseInto(text, schema);
        }
        return schema;
    }

    private static void ParseInto(string text, SemanticModelSchema schema)
    {
        SmTable? table = null;
        SmColumn? column = null;
        SmRelationship? relationship = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r', ' ', '\t');
            if (line.Length == 0 || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart(' ', '\t').Length;
            var content = line.TrimStart(' ', '\t');
            var (keyword, rest) = SplitFirst(content);

            switch (keyword)
            {
                case "table" when indent == 0:
                    table = new SmTable { Name = Unquote(TakeName(rest)) };
                    schema.Tables.Add(table);
                    column = null;
                    relationship = null;
                    break;

                case "relationship" when indent == 0:
                    relationship = new SmRelationship();
                    schema.Relationships.Add(relationship);
                    table = null;
                    column = null;
                    break;

                case "column" when table is not null:
                    column = new SmColumn { Name = Unquote(TakeName(rest)) };
                    table.Columns.Add(column);
                    break;

                case "dataType:" when column is not null:
                    column.DataType = rest.Trim();
                    break;

                case "fromColumn:" when relationship is not null:
                    (relationship.FromTable, relationship.FromColumn) = ParseQualifiedRef(rest);
                    break;

                case "toColumn:" when relationship is not null:
                    (relationship.ToTable, relationship.ToColumn) = ParseQualifiedRef(rest);
                    break;

                default:
                    // Entering a measure/partition/etc. block leaves the column context,
                    // so stray indented properties aren't misread as column data types.
                    if (BlockResetKeywords.Contains(keyword))
                    {
                        column = null;
                    }
                    break;
            }
        }
    }

    // -- helpers -------------------------------------------------------------

    private static (string keyword, string rest) SplitFirst(string s)
    {
        var idx = s.IndexOf(' ');
        return idx < 0 ? (s, "") : (s[..idx], s[(idx + 1)..]);
    }

    /// <summary>The declared name in a "table NAME" / "column NAME [= expr]" line.</summary>
    private static string TakeName(string rest)
    {
        rest = rest.Trim();
        if (rest.StartsWith('\''))
        {
            var (name, _) = ReadQuoted(rest);
            return name;
        }
        // Unquoted: name runs to the first space or '=' (calculated column/table).
        var stop = rest.Length;
        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i] is ' ' or '=')
            {
                stop = i;
                break;
            }
        }
        return rest[..stop].Trim();
    }

    /// <summary>Parse a "Table.Column" reference, honoring single-quoted segments.</summary>
    private static (string table, string column) ParseQualifiedRef(string s)
    {
        s = s.Trim();
        string table;
        var remainder = s;

        if (s.StartsWith('\''))
        {
            (table, remainder) = ReadQuoted(s);
        }
        else
        {
            var dot = s.IndexOf('.');
            if (dot < 0)
            {
                return (Unquote(s), "");
            }
            table = s[..dot];
            remainder = s[dot..];
        }

        remainder = remainder.TrimStart();
        if (remainder.StartsWith('.'))
        {
            remainder = remainder[1..];
        }
        var columnPart = remainder.StartsWith('\'') ? ReadQuoted(remainder).value : remainder;
        return (Unquote(table), Unquote(columnPart.Trim()));
    }

    /// <summary>Read a single-quoted token (with '' escaping) from the start of <paramref name="s"/>.</summary>
    private static (string value, string remainder) ReadQuoted(string s)
    {
        var sb = new System.Text.StringBuilder();
        var i = 1; // skip opening quote
        while (i < s.Length)
        {
            if (s[i] == '\'')
            {
                if (i + 1 < s.Length && s[i + 1] == '\'')
                {
                    sb.Append('\'');
                    i += 2;
                    continue;
                }
                i++; // closing quote
                break;
            }
            sb.Append(s[i]);
            i++;
        }
        return (sb.ToString(), i < s.Length ? s[i..] : "");
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s.StartsWith('\'') && s.EndsWith('\''))
        {
            return s[1..^1].Replace("''", "'");
        }
        return s;
    }
}
