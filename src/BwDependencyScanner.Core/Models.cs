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

public enum BwBackendKind
{
    Unknown,
    Http,
    Soap,
    Jdbc,
    Jms,
    Rv,
    File,
    Ftp,
    Sftp
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
    public List<BwBackendConnection> BackendConnections { get; init; } = new();
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

public sealed class BwResolvedValue
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string SourceType { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string Evidence { get; init; } = "";
}

public sealed class BwBackendConnection
{
    public BwBackendKind Kind { get; init; }
    public string SourceType { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string ResourceRef { get; init; } = "";
    public string ActivityName { get; init; } = "";
    public string Url { get; init; } = "";
    public string Host { get; init; } = "";
    public string Port { get; init; } = "";
    public string PathOrOperation { get; init; } = "";
    public string Username { get; init; } = "";
    public string PasswordReference { get; init; } = "";
    public string DriverOrFactory { get; init; } = "";
    public string Destination { get; init; } = "";
    public List<string> VariableReferences { get; init; } = new();
    public List<BwResolvedValue> ResolvedValues { get; init; } = new();
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
    public List<BwBackendConnection> BackendConnections { get; init; } = new();
}
