using System.CommandLine;
using AbcDumper.Bin;
using AbcDumper.Contracts.Model.Loader;
using AnimeSDK.Common.IO;

namespace AbcDumper.ResourceExport.Cli.Commands;

internal sealed class ConfigExtractCommand : ConfigCommand
{
	private readonly Option<string> _type = new("--type") { Required = true, Description = "类型 ID，支持十进制或 0x 前缀。" };
	private readonly Option<string> _record = new("--record") { Required = true, Description = "记录 ID，支持十进制或 0x 前缀。" };
	private readonly Option<string> _output = new("--out") { Required = true, Description = "空输出目录，保存单条记录的原始字节。" };

	public ConfigExtractCommand() : base("config-extract", "按类型与记录 ID 提取原始 Config 字节。")
	{
		Options.Add(_type);
		Options.Add(_record);
		Options.Add(_output);
		SetAction(Execute);
	}

	private int Execute(ParseResult result)
	{
		uint type = ParseId(result.GetValue(_type)!);
		uint id = ParseId(result.GetValue(_record)!);
		var (package, _) = Open(result);
		ConfigRecord record = package.Find(type, id);
		ExportDirectory output = new(result.GetValue(_output)!, result.GetValue(Game)!, result.GetValue(Contracts)!);
		string file = $"{type:X8}-{id}.bin";
		using Stream data = package.GetResource(record).OpenRead();
		output.Write(file, data.CopyTo);
		Console.WriteLine($"分片 {record.ShardIndex}，偏移 {record.Offset}，长度 {record.Length}：{output.Resolve(file)}");
		return 0;
	}

	private static uint ParseId(string value) => ConfigContractLoader.TryParseId(value, out uint id)
		? id : throw new ArgumentException($"无效的 32 位无符号 ID：{value}。");
}
