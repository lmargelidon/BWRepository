using System.Text.Json;
using ProcessAst.Bw;
using ProcessAst.Export;
using ProcessAst.Migration;
using ProcessAst.Validation;

if (args.Length < 1)
{
    Console.Error.WriteLine(
        "Usage:\n" +
        " processast parse-bw <root-folder> <output.json>\n" +
        " processast validate-ast <root-folder> <output.json>\n" +
        " processast export-canonical <root-folder> <output.json>\n" +
        " processast export-bw-csharp <root-folder> <output-folder> [rules.json|rules.yml]\n" +
        " processast migration-rules <rules-file> <output.json>\n" +
        " processast export-message-closure <root-folder> <message-name> <output.json> [configuration-message-affaire.xml]\n" +
        " processast export-message-bw-csharp <root-folder> <message-name> <output-folder> [rules.json|rules.yml] [configuration-message-affaire.xml]\n" +
        " processast export-message-restfacades <root-folder> <message-name> <output-folder> [configuration-message-affaire.xml]"
    );
    return 1;
}

var cmd = args[0].ToLowerInvariant();
if (cmd == "migration-rules")
{
    if (args.Length < 3) return 1;
    var rules = MigrationRuleSet.Load(args[1]);
    File.WriteAllText(args[2], JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length < 3) return 1;
var parser = new BwRepositoryParser();

switch (cmd)
{
    case "parse-bw":
    {
        var repo = parser.ParseRepository(args[1]);
        File.WriteAllText(args[2], JsonSerializer.Serialize(repo, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    case "validate-ast":
    {
        var repo = parser.ParseRepository(args[1]);
        File.WriteAllText(args[2], JsonSerializer.Serialize(new AstBusinessValidator().Validate(repo), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    case "export-canonical":
    {
        var repo = parser.ParseRepository(args[1]);
        File.WriteAllText(args[2], new CanonicalExporter().Export(repo));
        return 0;
    }

    case "export-bw-csharp":
    {
        var repo = parser.ParseRepository(args[1]);
        Directory.CreateDirectory(args[2]);
        MigrationRuleSet? ruleSet = args.Length > 3 ? MigrationRuleSet.Load(args[3]) : null;
        var exporter = new BwProcessCSharpExporter();

        foreach (var process in repo.Processes.Where(p => !p.IsSubProcess))
        {
            var path = Path.Combine(args[2], process.Name + ".g.cs");
            File.WriteAllText(path, exporter.ExportProcess(process, ruleSet));
        }

        return 0;
    }

    case "export-message-closure":
    {
        if (args.Length < 4) return 1;
        var repo = parser.ParseRepository(args[1]);
        var configurationXmlPath = args.Length > 4 ? args[4] : null;
        var closure = new MessageClosureBuilder().Build(repo, args[2], configurationXmlPath);
        File.WriteAllText(args[3], new MessageClosureExporter().Export(closure));
        return 0;
    }

    case "export-message-bw-csharp":
    {
        if (args.Length < 4) return 1;
        var repo = parser.ParseRepository(args[1]);
        var configurationXmlPath = args.Length > 5 ? args[5] : null;
        var closure = new MessageClosureBuilder().Build(repo, args[2], configurationXmlPath);
        Directory.CreateDirectory(args[3]);
        MigrationRuleSet? ruleSet = args.Length > 4 ? MigrationRuleSet.Load(args[4]) : null;
        var exporter = new BwProcessCSharpExporter();

        foreach (var process in closure.Processes.Where(p => !p.IsSubProcess))
        {
            var path = Path.Combine(args[3], process.Name + ".g.cs");
            File.WriteAllText(path, exporter.ExportProcess(
                process,
                closure.MessageName,
                closure.GlobalVariables,
                closure.Resources,
                closure.IntegrationEndpoints,
                ruleSet));
        }

        File.WriteAllText(
            Path.Combine(args[3], "migration-manifest.json"),
            new MessageClosureExporter().Export(closure));

        return 0;
    }

    case "export-message-restfacades":
    {
        if (args.Length < 4) return 1;
        var repo = parser.ParseRepository(args[1]);
        var configurationXmlPath = args.Length > 4 ? args[4] : null;
        var closure = new MessageClosureBuilder().Build(repo, args[2], configurationXmlPath);
        Directory.CreateDirectory(args[3]);
        var exporter = new RestFacadeExporter();

        File.WriteAllText(Path.Combine(args[3], "rest-facades.json"), exporter.ExportEndpoints(closure.IntegrationEndpoints));
        File.WriteAllText(Path.Combine(args[3], "GeneratedFacadeController.cs"), exporter.ExportControllerScaffold(closure.IntegrationEndpoints));
        File.WriteAllText(Path.Combine(args[3], "restfacade-manifest.json"), new MessageClosureExporter().Export(closure));
        return 0;
    }

    default:
        return 2;
}
