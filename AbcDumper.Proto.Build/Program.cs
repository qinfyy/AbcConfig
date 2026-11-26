using System.Text;
using Google.Protobuf.Reflection;
using ProtoBuf;
using ProtoBuf.Reflection;

namespace AbcDumper.Proto.Build;

internal sealed class AbcCodeGenerator : CSharpCodeGenerator
{
	// abc.xor_mask in abc_options.proto.
	private const int XorMaskOption = 51001;

	protected override void WriteField(GeneratorContext context, FieldDescriptorProto field, ref object state, OneOfStub[] oneOfs)
	{
		if (field.Options is IExtensible options && Extensible.TryGetValue(options, XorMaskOption, out uint mask))
			context.WriteLine($"[global::AbcDumper.Bin.Serialization.Attributes.AbcProtoXor(Mask = 0x{mask:X})]");
		base.WriteField(context, field, ref state, oneOfs);
	}
}

internal static class Program
{
	private static int Main(string[] args)
	{
		if (args.Length != 4)
		{
			Console.Error.WriteLine("Usage: AbcDumper.Proto.Build <dump.proto> <NameTranslation.txt> <abc_options.proto> <output.cs>");
			return 1;
		}
		string dump = args[0], names = args[1], options = args[2], output = args[3];
		try
		{
			var schema = new FileDescriptorSet();
			using StreamReader imports = File.OpenText(options);
			using StringReader source = new(ProtoSource.Read(dump, names));
			schema.Add(Path.GetFileName(options), includeInOutput: false, imports);
			schema.Add(Path.GetFileName(dump), includeInOutput: true, source);
			schema.Process();
			var errors = schema.GetErrors().Where(error => error.IsError).ToArray();
			foreach (var error in errors)
				Console.Error.WriteLine(error);
			if (errors.Length != 0)
				return 1;

			var generated = new AbcCodeGenerator().Generate(schema,
				options: new() { ["names"] = "original", ["repeatedaslist"] = "true" }).Single();
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
			File.WriteAllText(output, generated.Text, new UTF8Encoding(false));
			return 0;
		}
		catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			Console.Error.WriteLine(error.Message);
			return 1;
		}
	}
}
