namespace AbcDumper.Contracts.Model;

public sealed record ConfigContract(string AppVersion, string ChangeList, byte[] ManifestSeed, IReadOnlyDictionary<uint, string> Types,
	IReadOnlyDictionary<uint, string> RawCodecs, TextMapContract? TextMap = null)
{
	public void ValidateClient(string appVersion, string changeList)
	{
		if (AppVersion != appVersion || ChangeList != changeList)
			throw new InvalidDataException($"Config 合同版本 {AppVersion}/{ChangeList} 与资源包 {appVersion}/{changeList} 不符。");
	}
}
