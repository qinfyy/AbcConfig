namespace AbcDumper.Proto;

public static class SchemaSource
{
	public static Stream Open() => typeof(SchemaSource).Assembly.GetManifestResourceStream("AbcDumper.Proto.dump.proto")
		?? throw new InvalidOperationException("The embedded Config schema is missing.");
}
