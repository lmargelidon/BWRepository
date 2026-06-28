using System.Text.Json;
using ProcessAst.Bw;
using ProcessAst.Core;
using ProcessAst.Export;
using ProcessAst.Migration;
using ProcessAst.Validation;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage:\n  processast parse-bw <root-folder> <output-json>\n  processast validate-ast <root-folder> <output-json>\n  processast export-canonical <root-folder> <output-json>\n  processast export-logicapps <root-folder> <output-folder> [rules.json|rules.yml]\n  processast migration-rules <rules-file> <output-json>");
    return 1;
}

var cmd = args[0].ToLowerInvariant();
if (cmd == "migration-rules")
{
    var rules = MigrationRuleSet.Load(args[1]);
    File.WriteAllText(args[2], JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

var root = args[1];
var output = args[2];
var repo = new BwRepositoryParser().ParseRepository(root);

switch (cmd)
{
    case "parse-bw":
        File.WriteAllText(output, JsonSerializer.Serialize(repo, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    case "validate-ast":
        File.WriteAllText(output, JsonSerializer.Serialize(new AstBusinessValidator().Validate(repo), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    case "export-canonical":
        File.WriteAllText(output, new CanonicalExporter().Export(repo));
        return 0;
    case "export-logicapps":
        Directory.CreateDirectory(output);
        MigrationRuleSet? ruleSet = args.Length > 3 ? MigrationRuleSet.Load(args[3]) : null;
        var exporter = new LogicAppsExporter();
        foreach (var process in repo.Processes.Where(p => !p.IsSubProcess))
        {
            var path = Path.Combine(output, process.Name + ".workflow.json");
            File.WriteAllText(path, exporter.ExportWorkflow(process, ruleSet));
        }
        return 0;
    default:
        return 2;
}
