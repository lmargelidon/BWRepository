using System.Text.Json;
using ProcessAst.Core;
namespace ProcessAst.Export;
public sealed class CanonicalExporter { public string Export(ProcessRepositoryAst repository) => JsonSerializer.Serialize(repository, new JsonSerializerOptions { WriteIndented = true }); }
