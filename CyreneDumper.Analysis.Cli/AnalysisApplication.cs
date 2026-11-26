using System.CommandLine;
using System.Text.Json;
using AbcDumper.Contracts.Model;
using AbcDumper.Contracts.Model.Loader;
using AnimeSDK.Common.IO;
using CyreneDumper.Analysis.Cli.Config;
using CyreneDumper.Analysis.Engine.Client;
using CyreneDumper.Msil.Resolver.Input;

namespace CyreneDumper.Analysis.Cli;

public sealed class AnalysisApplication
{
    private const string DefaultDumpDirectory = "DUMP";
    private readonly RootCommand _root = new("分析 Nexus Anima x64 客户端与 SDK 元数据。");

    public AnalysisApplication()
    {
        Command command = new("all", "恢复全部 Config、RawConfig 和 TextMap 的解码合同。");
        command.Aliases.Add("config");
        Option<string> game = new("--game") { Required = true, Description = "客户端安装目录。" };
        Option<string> build = new("--build") { Required = true, Description = "Config 资源的 ChangeList。" };
        Option<string?> dump = new("--dump") { Description = $"SDK Dump 目录或 il2cpp.json，默认使用客户端下的 {DefaultDumpDirectory}。" };
        Option<string> output = new("--out") { Required = true, Description = "空的合同输出目录。" };
        command.Options.Add(game);
        command.Options.Add(build);
        command.Options.Add(dump);
        command.Options.Add(output);
        command.SetAction(result => Execute(result.GetValue(game)!, result.GetValue(build)!, result.GetValue(dump), result.GetValue(output)!));
        _root.Subcommands.Add(command);
    }

    public async Task<int> Run(string[] args)
    {
        try
        {
            return await _root.Parse(args.Length == 0 ? ["--help"] : args)
                .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or JsonException or ArgumentException or FormatException or BadImageFormatException or NotSupportedException
            or OverflowException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int Execute(string gameDirectory, string build, string? dumpPath, string outputDirectory)
    {
        string game = Path.GetFullPath(gameDirectory);
        if (build.Length == 0 || !build.All(char.IsAsciiDigit))
            throw new ArgumentException("--build 必须是数字 ChangeList。");

        string update = Path.Combine(game, "NexusAnima_Data", "PersistentAssets", "Bin", build);
        if (!File.Exists(Path.Combine(update, "ConfigLoadManifest.bin")))
            throw new FileNotFoundException("找不到 Config 资源包：" + update);

        string binary = Path.Combine(game, "GameAssembly.dll");
        if (!File.Exists(binary))
            throw new FileNotFoundException("找不到客户端 GameAssembly.dll：" + binary);

        string dump = Path.GetFullPath(dumpPath ?? Path.Combine(game, DefaultDumpDirectory));
        string inspectorFile = MetadataDump.ResolveInspectorFile(dump);
        ExportDirectory output = new(outputDirectory, Directory.Exists(dump) ? [game, dump] : [game]);
        using ClientAnalysisSession session = new(binary, inspectorFile);
        ConfigContract contract = new ConfigAnalyzer(session).Analyze(build);

        output.Write(ConfigContractLoader.FileName, stream => ConfigContractWriter.Write(stream, contract));
        Console.WriteLine($"{contract.Types.Count} 个 Config 类型，{contract.RawCodecs.Count} 个原生布局，"
            + $"{contract.TextMap?.Languages.Count ?? 0} 种 TextMap 语言：{output.Resolve(ConfigContractLoader.FileName)}");
        return 0;
    }
}
