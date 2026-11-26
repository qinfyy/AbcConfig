namespace AnimeSDK.Common.IO;

public sealed class BinaryResourceSources(params IBinaryResourceSource[] sources) : IBinaryResourceSource
{
	public bool TryGetResource(string logicalPath, out BinaryResource? resource)
	{
		IBinaryResourceSource[] array = sources;
		foreach (IBinaryResourceSource source in array)
		{
			if (source.TryGetResource(logicalPath, out resource))
			{
				return true;
			}
		}
		resource = null;
		return false;
	}
}
