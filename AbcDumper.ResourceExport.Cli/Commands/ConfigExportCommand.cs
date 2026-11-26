using System.CommandLine;
using AbcDumper.Contracts.Model.Loader;
using AbcDumper.ResourceExport.Cli.Config;
using AnimeSDK.Common.IO;

namespace AbcDumper.ResourceExport.Cli.Commands;

internal sealed class ConfigExportCommand : ConfigCommand
{
	private readonly Option<string> _output = new("--out") { Required = true, Description = "空的 JSON 导出目录。" };

	public ConfigExportCommand() : base("config-export", "按 protobuf schema 导出 Config 和 TextMap JSON。")
	{
		Options.Add(_output);
		SetAction((result, cancellation) => Task.FromResult(Execute(result, cancellation)));
	}

	private int Execute(ParseResult result, CancellationToken cancellation)
	{
		var (package, contract) = Open(result);
		var textMap = contract.TextMap ?? throw new InvalidDataException("合同缺少 TextMap 映射，请重新生成分析合同。");
		using Stream source = File.OpenRead(Path.Combine(result.GetValue(Contracts)!, ConfigContractLoader.FileName));
		ExportDirectory output = new(result.GetValue(_output)!, result.GetValue(Game)!, result.GetValue(Contracts)!);
		output.Write($"contracts/{ConfigContractLoader.FileName}", source.CopyTo);
		return new ConfigExportSession(package, new(contract, package), textMap, output).Export(cancellation) ? 0 : 2;
	}
}
