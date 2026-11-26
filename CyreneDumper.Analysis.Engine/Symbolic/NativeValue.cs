namespace CyreneDumper.Analysis.Engine.Symbolic;

public sealed record NativeValue(string Kind, long Number = 0, string? Text = null, NativeValue? Base = null, NativeValue? Index = null, int Scale = 0, int Width = 0)
{
    public static NativeValue Argument(int index) => new("arg", index);
    public static NativeValue Constant(long value) => new("constant", value);
    public NativeValue At(long offset) => Kind == "stack" ? new("stack", Number + offset)
        : Kind == "address" && Base is not null ? Base.At(checked(Number + offset)) : new("address", offset, Base: this);
    public NativeValue Load() => new("load", Base: this);
}

public sealed record NativeCall(ulong Site, ulong? Target, NativeValue?[] Arguments);
public sealed record NativeAccess(ulong Site, NativeValue Address, NativeValue? Value, int Size);
public sealed record NativeTraceResult(ulong Entry, NativeCall[] Calls, NativeAccess[] Loads, NativeAccess[] Stores,
    NativeValue? ReturnValue = null, NativeValue? FloatReturnValue = null);
