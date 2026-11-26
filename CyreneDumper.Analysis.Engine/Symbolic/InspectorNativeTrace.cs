using CyreneDumper.Analysis.Engine.Image;
using CyreneDumper.Msil.Resolver.Input;

namespace CyreneDumper.Analysis.Engine.Symbolic;

public sealed class InspectorNativeTrace(NativeImage image, InspectorReferences references)
{
    public static NativeTrace Create(NativeImage image, InspectorReferences references, params ulong[] allocators)
        => new(image, new InspectorNativeTrace(image, references).Resolve, allocators);

    private NativeValue? Resolve(ulong slot)
    {
        if (references.Literals.TryGetValue(slot, out string? literal)) return new("literal", Text: literal);
        if (references.Methods.TryGetValue(slot, out ulong method))
            return new("method", unchecked((long)image.NormalizeAddress(method)), references.MethodSignatures.GetValueOrDefault(slot));
        return references.Types.TryGetValue(slot, out string? type) ? new("type", Text: type) : null;
    }
}
