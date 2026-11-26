using System.Globalization;
using Mono.Cecil;

namespace CyreneDumper.Msil.Resolver.Input;

public sealed record ManagedField(string Name, string Type, int? Offset, string? Constant, bool IsStatic = false,
    int? StorageTag = null, string? StorageBase = null, bool IsThreadStatic = false);
public sealed record ManagedMethod(string Name, string Returns, string[] Parameters, uint Token, ulong? Address, bool IsStatic = false);
public sealed record ManagedProperty(string Name, string Type, uint? Getter, ulong? Address);
public sealed record ManagedType(string Module, string Name, string? Base, uint Token, ManagedField[] Fields,
    ManagedProperty[] Properties, ManagedMethod[] Methods, string[]? Interfaces = null, bool IsInterface = false, string[]? GenericParameters = null);

public sealed class ManagedCatalog : IDisposable
{
    private readonly string _directory;
    private readonly DefaultAssemblyResolver _resolver = new();
    private readonly Dictionary<string, AssemblyDefinition> _assemblies = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    internal ManagedCatalog(string assembliesDirectory)
    {
        _directory = Path.GetFullPath(assembliesDirectory);
        _resolver.AddSearchDirectory(_directory);
    }

    public IEnumerable<ManagedType> Read(string? module = null, string? typeFilter = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string[] files = module is null ? Directory.GetFiles(_directory, "*.dll").Order(StringComparer.Ordinal).ToArray() : [Path.Combine(_directory, Path.GetFileName(module))];
        if (files.Length == 0) throw new FileNotFoundException("没有找到 DummyDll 程序集。", _directory);
        foreach (string file in files)
        {
            if (!_assemblies.TryGetValue(file, out AssemblyDefinition? assembly))
            {
                assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters
                {
                    AssemblyResolver = _resolver, ReadingMode = ReadingMode.Deferred, ReadSymbols = false
                });
                _assemblies.Add(file, assembly);
            }
            foreach (TypeDefinition type in Flatten(assembly.MainModule.Types))
            {
                if (type.Name == "<Module>" || typeFilter is not null && !type.FullName.Contains(typeFilter, StringComparison.Ordinal)) continue;
                ManagedMethod[] methods = type.Methods.Select(method => new ManagedMethod(method.Name, method.ReturnType.FullName,
                    method.Parameters.Select(parameter => parameter.ParameterType.FullName).ToArray(), method.MetadataToken.ToUInt32(), Address(method), method.IsStatic)).ToArray();
                yield return new(Path.GetFileName(file), type.FullName, type.BaseType?.FullName, type.MetadataToken.ToUInt32(),
                    type.Fields.Select(field => new ManagedField(field.Name, field.FieldType.FullName, FieldOffset(field),
                        field.HasConstant ? Convert.ToString(field.Constant, CultureInfo.InvariantCulture) : null, field.IsStatic,
                        StaticStorageTag(field), AttributeValue(field, "StaticFieldOffsetAttribute", "StorageBase") as string,
                        AttributeValue(field, "StaticFieldOffsetAttribute", "ThreadStatic") is true)).ToArray(),
                    type.Properties.Select(property => new ManagedProperty(property.Name, property.PropertyType.FullName,
                        property.GetMethod?.MetadataToken.ToUInt32(), property.GetMethod is null ? null : Address(property.GetMethod))).ToArray(), methods,
                    type.Interfaces.Select(item => item.InterfaceType.FullName).ToArray(), type.IsInterface, type.GenericParameters.Select(parameter => parameter.Name).ToArray());
            }
        }
    }

    private IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition child in Flatten(type.NestedTypes)) yield return child;
        }
    }

    private ulong? AttributeNumber(ICustomAttributeProvider source, string attributeName, params string[] members)
    {
        foreach (CustomAttribute attribute in source.CustomAttributes)
        {
            if (attribute.AttributeType.Name != attributeName) continue;
            foreach (string member in members)
            {
                object? value = attribute.Fields.Concat(attribute.Properties).Where(item => item.Name == member).Select(item => item.Argument.Value).FirstOrDefault();
                if (value is not string text) continue;
                text = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
                if (ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong number)) return number;
            }
        }
        return null;
    }

    private ulong? Address(MethodDefinition method) => AttributeNumber(method, "AddressAttribute", "VA", "RVA");
    private int? FieldOffset(FieldDefinition field) => (field.IsStatic ? AttributeNumber(field, "StaticFieldOffsetAttribute", "Offset")
        ?? AttributeNumber(field, "FieldOffsetAttribute", "Offset") : AttributeNumber(field, "FieldOffsetAttribute", "Offset")) is ulong value ? checked((int)value) : null;

    private int? StaticStorageTag(FieldDefinition field)
    {
        object? value = AttributeValue(field, "StaticFieldOffsetAttribute", "StorageTag");
        if (value is null) return null;
        string text = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? int.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(text, CultureInfo.InvariantCulture);
    }

    private object? AttributeValue(ICustomAttributeProvider source, string name, string member) => source.CustomAttributes
        .Where(attribute => attribute.AttributeType.Name == name).SelectMany(attribute => attribute.Fields.Concat(attribute.Properties))
        .Where(argument => argument.Name == member).Select(argument => argument.Argument.Value).SingleOrDefault();

    public void Dispose()
    {
        if (_disposed) return;
        foreach (AssemblyDefinition assembly in _assemblies.Values) assembly.Dispose();
        _resolver.Dispose();
        _disposed = true;
    }
}
