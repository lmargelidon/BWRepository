using System.Text.Json;
using BwDependencyScanner.Core;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: BwDependencyScanner.Cli <bw-root-folder> <message-name> [folder-filter] [csv-output]");
    return 1;
}

var root = args[0];
var message = args[1];
var folderFilter = args.Length > 2 ? args[2] : null;
var csvOutput = args.Length > 3 ? args[3] : null;

if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"Directory not found: {root}");
    return 2;
}

var scanner = new BwRepositoryScanner();
var report = scanner.BuildMessageReport(root, message, folderFilter);

if (!string.IsNullOrWhiteSpace(csvOutput))
{
    File.WriteAllText(csvOutput, scanner.ExportCsv(report));
}

Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true
}));

return 0;
