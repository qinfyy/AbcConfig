using System.CommandLine;
using Abc.Config.Storage.Vfs;
using AbcDumper.Contracts.Model;
using AbcDumper.Contracts.Model.Loader;

namespace AbcDumper.ResourceExport.Cli.Commands;

internal abstract class ConfigCommand : Command
{
	protected Option<string> Game { get; } = new("--game") { Required = true, Description = "客户端安装目录。" };
	protected Option<string> Contracts { get; } = new("--contracts") { Required = true, Description = "包含 contracts.json 的分析合同目录。" };

	protected ConfigCommand(string name, string description) : base(name, description)
	{
		Options.Add(Game);
		Options.Add(Contracts);
	}

	protected (ConfigPackage Package, ConfigContract Contract) Open(ParseResult result)
	{
		string game = result.GetValue(Game)!;
		ConfigContract contract = ConfigContractLoader.Load(result.GetValue(Contracts)!);
		string data = Path.Combine(game, "NexusAnima_Data");
		string update = Path.Combine(data, "PersistentAssets", "Bin", contract.ChangeList);
		string fallback = Path.Combine(data, "StreamingAssets", "Bin");
		ConfigPackage package = ConfigPackage.Open(update, fallback, contract.ManifestSeed);
		contract.ValidateClient(package.AppVersion, package.ChangeList);
		return (package, contract);
	}
}
