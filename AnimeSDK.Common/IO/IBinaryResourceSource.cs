namespace AnimeSDK.Common.IO;

public interface IBinaryResourceSource
{
	bool TryGetResource(string logicalPath, out BinaryResource? resource);
}
