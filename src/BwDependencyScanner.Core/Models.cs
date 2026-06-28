namespace BwDependencyScanner.Core;

public enum BwMessageRole
{
    Unknown,
    Input,
    Output,
    Fault,
    Operation
}

public enum BwVariableKind
{
    Global,
    Shared,
    JobShared
}

public sealed class BwResourceFile
{
    public string Path { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string ResourceKind { get; init; } = "Unknown";
    public string Name { get; init; } = "";
    public List<BwVariableUsage> Variables { get; init; } = new();
    public List<string> ProcessReferences { get; init; } = new();
    public List<string> SharedResourceReferences { get; init; } = new();
    public List<BwMessageReference> MessageReferences { get; init; } = new();
}

public sealed class BwVariableUsage
{
    public string Name { get; init; } = "";
    public BwVariableKind Kind { get; init; }
    public string Evidence { get; init; } = "";
}

public sealed class BwMessageReference
{
    public string Name { get; init; } = "";
    public BwMessageRole Role { get; init; }
    public string Evidence { get; init; } = "";
}

public sealed class BwDependencyEdge
{
    public string SourceFile { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string Target { get; init; } = "";
    public string DependencyType { get; init; } = "";
}

public sealed class BwRepositoryIndex
{
    public string RootPath { get; init; } = "";
    public List<BwResourceFile> Files { get; init; } = new();
    public List<BwDependencyEdge> Edges { get; init; } = new();
}

public sealed class BwMessageDependencyReport
{
    public string MessageName { get; init; } = "";
    public List<BwResourceFile> DirectFiles { get; init; } = new();
    public List<string> TransitiveProcesses { get; init; } = new();
    public List<string> GlobalVariables { get; init; } = new();
    public List<string> SharedVariables { get; init; } = new();
    public List<string> JobSharedVariables { get; init; } = new();
    public List<string> SharedResources { get; init; } = new();
    public List<BwDependencyEdge> TraversedEdges { get; init; } = new();
}
