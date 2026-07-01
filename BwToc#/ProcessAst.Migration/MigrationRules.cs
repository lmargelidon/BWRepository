namespace ProcessAst.Migration;
public sealed class MigrationRuleSet { public List<MigrationRule> Rules { get; } = new(); public static MigrationRuleSet Load(string path) => new(); }
public sealed class MigrationRule { public string SourceActivityType { get; set; } = ""; public string CanonicalCapability { get; set; } = ""; public string TargetConstruct { get; set; } = ""; public string Hint { get; set; } = ""; }
