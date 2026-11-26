namespace CyreneDumper.Msil.Resolver.Input;

public sealed class MetadataDump
{
    public string DirectoryPath { get; }
    public string AssembliesDirectory { get; }
    public string InspectorFile { get; }

    public MetadataDump(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        AssembliesDirectory = Path.Combine(DirectoryPath, "DummyDll");
        if (!Directory.Exists(AssembliesDirectory))
            throw new DirectoryNotFoundException("找不到当批 DummyDll 目录：" + AssembliesDirectory);

        InspectorFile = ResolveInspectorFile(DirectoryPath);
    }

    public static string ResolveInspectorFile(string path)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path))
            return path;
        string nested = Path.Combine(path, "Script_IDA", "il2cpp.json");
        string flat = Path.Combine(path, "il2cpp.json");
        bool hasNested = File.Exists(nested), hasFlat = File.Exists(flat);
        if (hasNested == hasFlat)
            throw new InvalidDataException($"Expected one il2cpp.json under {path}; specify the index file directly.");
        return hasNested ? nested : flat;
    }

    internal ManagedCatalog OpenAssemblies() => new(AssembliesDirectory);
    internal InspectorReferences ReadReferences() => InspectorReferences.Read(InspectorFile);
}
