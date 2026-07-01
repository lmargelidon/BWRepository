using ProcessAst.Core;
namespace ProcessAst.Bw;
public sealed class BwRepositoryParser
{
    public ProcessRepositoryAst ParseRepository(string rootFolder)
        => new() { Name = "Repository", Kind = ProcessNodeKind.Repository, Source = new SourceLocation { FilePath = rootFolder } };
}
