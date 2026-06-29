using System.Text.Json;

namespace ProcessAst.Migration;

public sealed class MigrationRule
{
    public string SourceActivityType { get; set; } = "";
    public string CanonicalCapability { get; set; } = "";
    public string TargetConstruct { get; set; } = "";
    public string Hint { get; set; } = "";
}

public sealed class MigrationRuleSet
{
    public List<MigrationRule> Rules { get; set; } = new();

    public static MigrationRuleSet Load(string path)
    {
        var text = File.ReadAllText(path);
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Deserialize<MigrationRuleSet>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new MigrationRuleSet();

        var result = new MigrationRuleSet();
        MigrationRule? current = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            if (line.StartsWith("-"))
            {
                current = new MigrationRule();
                result.Rules.Add(current);
                line = line.TrimStart('-').Trim();
                if (line.Contains(':')) Apply(current, line);
                continue;
            }
            if (current != null && line.Contains(':')) Apply(current, line);
        }
        return result;
    }

    private static void Apply(MigrationRule rule, string line)
    {
        var idx = line.IndexOf(':');
        if (idx < 0) return;
        var key = line[..idx].Trim();
        var value = line[(idx + 1)..].Trim().Trim('"');
        switch (key)
        {
            case "SourceActivityType": rule.SourceActivityType = value; break;
            case "CanonicalCapability": rule.CanonicalCapability = value; break;
            case "TargetConstruct": rule.TargetConstruct = value; break;
            case "Hint": rule.Hint = value; break;
        }
    }
}
