using System.CommandLine;
using AbcDumper.Bin;

namespace AbcDumper.ResourceExport.Cli.Commands;

internal sealed class ConfigInspectCommand : ConfigCommand
{
	public ConfigInspectCommand() : base("config-inspect", "解密清单并检查 Config 分片与记录索引。") => SetAction(Execute);

	private int Execute(ParseResult result)
	{
		var (package, _) = Open(result);
		ConfigShardReport report = package.InspectShards();
		Console.WriteLine($"AppVersion={package.AppVersion} ChangeList={package.ChangeList} Records={package.RecordCount}");
		Console.WriteLine($"Shards={package.ShardCount} Primary={report.Primary} Fallback={report.Fallback} Unavailable={report.Problems.Length}");
		foreach (string issue in report.Problems)
			Console.Error.WriteLine(issue);
		return report.Problems.Length == 0 ? 0 : 2;
	}
}
