using System.CommandLine;
using System.Security.Cryptography;
using System.Text.Json;
using AbcDumper.ResourceExport.Cli.Commands;
using ProtoBuf;

namespace AbcDumper.ResourceExport.Cli;

internal sealed class ResourceExportApplication
{
	public async Task<int> Run(string[] args)
	{
		RootCommand root = new("使用 protobuf schema 离线读取 NexusAnima Config 和 TextMap。");
		root.Subcommands.Add(new ConfigExportCommand());
		root.Subcommands.Add(new ConfigInspectCommand());
		root.Subcommands.Add(new ConfigExtractCommand());
		try
		{
			return await root.Parse(args.Length == 0 ? ["--help"] : args)
				.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
		}
		catch (OperationCanceledException)
		{
			Console.Error.WriteLine("操作已取消。");
			return 130;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
			ArgumentException or KeyNotFoundException or InvalidOperationException or JsonException or CryptographicException or ProtoException)
		{
			Console.Error.WriteLine(error.Message);
			return 1;
		}
	}
}
