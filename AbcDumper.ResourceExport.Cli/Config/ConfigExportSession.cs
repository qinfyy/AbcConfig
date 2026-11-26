using System.Text.Json;
using AbcDumper.Bin;
using AbcDumper.Bin.Reading;
using AbcDumper.Contracts.Model;
using AnimeSDK.Common.IO;

namespace AbcDumper.ResourceExport.Cli.Config;

internal sealed class ConfigExportSession(ConfigPackage package, AbcConfigReader reader, TextMapContract textMap, ExportDirectory output)
{
	public const string ReportPath = "contracts/export.json";

	public bool Export(CancellationToken cancellation)
	{
		int finished = 0;
		Dictionary<string, int> counts = new(StringComparer.Ordinal);
		List<ExportFile> files = [];
		List<RecordIssue> issues = [];
		var groups = package.Records.GroupBy(record => record.TypeId).OrderBy(group => group.Key).ToArray();
		var names = ConfigOutputNames.Resolve(groups.Where(group => group.Key != textMap.RecordType)
			.Select(group => (group.Key, reader.Resolve(group.Key))));
		foreach (var group in groups)
		{
			if (group.Key != textMap.RecordType)
			{
				string? name = names.GetValueOrDefault(group.Key);
				WriteRecords(group.Key, name is null ? null : $"Bin/{name}.json", group);
				continue;
			}
			foreach (var records in group.GroupBy(record => record.RecordId / textMap.RecordStride).OrderBy(bucket => bucket.Key))
			{
				if (textMap.Languages.TryGetValue(records.Key, out string? language))
					WriteRecords(group.Key, $"TestMap/TextMap{language}.json", records);
				else
					foreach (ConfigRecord record in records)
						Record(record, "UnmappedLanguage", [new("", "languageBucket", records.Key.ToString())]);
			}
		}
		cancellation.ThrowIfCancellationRequested();
		ConfigShardReport shards = package.InspectShards();
		bool complete = counts.GetValueOrDefault(nameof(ConfigDecodeStatus.Decoded)) == package.RecordCount &&
			shards.Problems.Length == 0 && files.Count(file => file.Path.StartsWith("TestMap/", StringComparison.Ordinal)) == textMap.Languages.Count;
		WriteJson(ReportPath, new
		{
			package.AppVersion, package.ChangeList, Complete = complete, Shards = shards,
			Records = package.RecordCount, Counts = counts, Files = files, Issues = issues,
		});
		Console.WriteLine(string.Join(", ", counts.Select(pair => $"{pair.Key}: {pair.Value}")));
		Console.WriteLine(output.Resolve(ReportPath));
		return complete;

		void WriteRecords(uint typeId, string? path, IEnumerable<ConfigRecord> records)
		{
			List<object> messages = [];
			foreach (ConfigRecord record in records.OrderBy(record => record.RecordId))
			{
				cancellation.ThrowIfCancellationRequested();
				ConfigDecodeResult result = reader.Read(record);
				Record(record, result.Status.ToString(), result.Issues);
				if (result.Data is not null)
					messages.Add(result.Data);
			}
			if (messages.Count == 0)
				return;
			if (path is null)
				throw new InvalidDataException("已解析的 protobuf 类型缺少导出名称。");
			WriteJson(path, messages);
			files.Add(new(path, typeId, messages.Count));
		}

		void Record(ConfigRecord record, string status, DecodeIssue[]? recordIssues)
		{
			cancellation.ThrowIfCancellationRequested();
			counts[status] = counts.GetValueOrDefault(status) + 1;
			if (status != nameof(ConfigDecodeStatus.Decoded))
				issues.Add(new(record.TypeId, record.RecordId, status, recordIssues));
			if (++finished % 10000 == 0 || finished == package.RecordCount)
				Console.WriteLine($"TextMap: {finished}/{package.RecordCount}");
		}
	}

	private void WriteJson<T>(string path, T value) => output.Write(path, stream =>
	{
		JsonSerializer.Serialize(stream, value, ProtoJson.Options);
		stream.WriteByte((byte)'\n');
	});

	private sealed record ExportFile(string Path, uint TypeId, int Records);
	private sealed record RecordIssue(uint TypeId, uint RecordId, string Status, DecodeIssue[]? Issues);
}
