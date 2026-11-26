using CyreneDumper.Analysis.Engine.Image;
using CyreneDumper.Msil.Resolver.Input;

namespace CyreneDumper.Analysis.Engine.Client;

public sealed class ClientAnalysisSession : IDisposable
{
    private readonly Lazy<NativeImage> _image;
    private readonly Lazy<InspectorReferences> _references;
    private bool _disposed;

    public NativeImage Image => Read(_image);
    public InspectorReferences References => Read(_references);

    public ClientAnalysisSession(string binary, string inspectorFile)
    {
        _image = new(() => new(binary));
        _references = new(() => InspectorReferences.Read(inspectorFile));
    }

    private T Read<T>(Lazy<T> value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return value.Value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_image.IsValueCreated) _image.Value.Dispose();
    }
}
