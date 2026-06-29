using ProcessAst.Core;

namespace ProcessAst.Validation;

public sealed class AstBusinessValidator
{
    public List<string> Validate(ProcessRepositoryAst repository)
    {
        var errors = new List<string>();
        var processIds = repository.Processes.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var process in repository.Processes)
        {
            if (!process.Activities.Any()) errors.Add($"Process {process.Name} has no activities.");
            foreach (var t in process.Transitions)
            {
                if (!process.Activities.Any(a => a.Id == t.FromActivityId)) errors.Add($"Transition {t.Name} in {process.Name} has unresolved source {t.FromActivityId}.");
                if (!process.Activities.Any(a => a.Id == t.ToActivityId)) errors.Add($"Transition {t.Name} in {process.Name} has unresolved target {t.ToActivityId}.");
            }
            foreach (var sub in process.CalledSubProcessIds)
                if (!processIds.Contains(sub)) errors.Add($"Process {process.Name} references unresolved subprocess {sub}.");
        }
        return errors;
    }
}
