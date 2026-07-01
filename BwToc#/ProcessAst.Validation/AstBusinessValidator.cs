using ProcessAst.Core;
namespace ProcessAst.Validation;
public sealed class AstBusinessValidator { public object Validate(ProcessRepositoryAst repo) => new { isValid = true, processCount = repo.Processes.Count, resourceCount = repo.Resources.Count }; }
