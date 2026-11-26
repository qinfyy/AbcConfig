using System;
using System.IO;
using System.Linq;

namespace AnimeSDK.Common.IO;

public static class SafePath
{
	public static string CombineUnder(string directory, string relativePath)
	{
		ArgumentException.ThrowIfNullOrEmpty(directory, "directory");
		ArgumentException.ThrowIfNullOrEmpty(relativePath, "relativePath");
		string normalized = relativePath.Replace('\\', '/');
		if (Path.IsPathRooted(normalized) || normalized.Contains(':') || normalized.Split('/').Any(delegate(string text)
		{
			switch (text)
			{
			default:
				if (text.Length != 0)
				{
					break;
				}
				goto case "..";
			case "..":
			case ".":
				return true;
			case null:
				break;
			}
			return false;
		}))
		{
			throw new InvalidDataException("Invalid relative resource path: " + relativePath);
		}
		string[] array = normalized.Split('/');
		foreach (string part in array)
		{
			if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || (OperatingSystem.IsWindows() && (part.EndsWith('.') || part.EndsWith(' '))))
			{
				throw new InvalidDataException("Invalid output file name: " + part);
			}
		}
		string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
		string path = Path.GetFullPath(Path.Combine(root, normalized));
		StringComparison comparison = (OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		if (!path.StartsWith(root, comparison))
		{
			throw new InvalidDataException("Resource path escapes its directory: " + relativePath);
		}
		return path;
	}

	public static void RequireNoReparsePoints(string path)
	{
		for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
		{
			try
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != FileAttributes.None)
				{
					throw new InvalidDataException("Output path contains a reparse point: " + current);
				}
			}
			catch (FileNotFoundException)
			{
			}
			catch (DirectoryNotFoundException)
			{
			}
		}
	}
}
