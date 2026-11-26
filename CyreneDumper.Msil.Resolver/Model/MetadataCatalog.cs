using CyreneDumper.Msil.Resolver.Input;

namespace CyreneDumper.Msil.Resolver.Model;

public readonly record struct MetadataIdentity(string Module, uint Token);
public sealed record CatalogMethod(ManagedType Owner, ManagedMethod Method)
{
    public MetadataIdentity Identity => new(Owner.Module, Method.Token);
}

public sealed class MetadataCatalog
{
    private readonly ILookup<string, ManagedType> _names;
    private readonly ILookup<MetadataIdentity, ManagedType> _identities;
    private readonly ILookup<ulong, CatalogMethod> _addresses;

    public IReadOnlyList<ManagedType> Types { get; }
    public InspectorReferences References { get; }

    private MetadataCatalog(ManagedType[] types, InspectorReferences references)
    {
        Types = Array.AsReadOnly(types);
        References = references;
        _names = types.ToLookup(type => Normalize(type.Name), StringComparer.Ordinal);
        _identities = types.ToLookup(type => new MetadataIdentity(type.Module, type.Token));
        _addresses = types.SelectMany(type => type.Methods.Where(method => method.Address.HasValue)
            .Select(method => new CatalogMethod(type, method))).ToLookup(entry => entry.Method.Address!.Value);
    }

    public static MetadataCatalog Load(MetadataDump dump)
    {
        using ManagedCatalog assemblies = dump.OpenAssemblies();
        return new(assemblies.Read().ToArray(), dump.ReadReferences());
    }

    public IEnumerable<ManagedType> FindTypes(string name) => _names[Normalize(name)];
    public IEnumerable<ManagedType> FindTypes(MetadataIdentity identity) => _identities[identity];
    public IEnumerable<CatalogMethod> FindMethods(ulong address) => _addresses[address];

    public Dictionary<string, ManagedType> UniqueTypes() => _names.Where(group => group.Count() == 1)
        .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

    internal static string Normalize(string name) => name.Replace('/', '+');
}
