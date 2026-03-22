using System;
using System.IO;
using System.Threading.Tasks;

namespace CipherVault.Tests;

internal static class TestFileCleanup
{
	public static async Task DeleteWithRetryAsync(string path, int attempts = 10, int delayMs = 50)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		for (int i = 0; i < attempts; i++)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				TryDeleteRelatedFiles(path);
				return;
			}
			catch (IOException)
			{
				if (i < attempts - 1)
				{
					await Task.Delay(delayMs);
					continue;
				}
			}
			catch (UnauthorizedAccessException)
			{
				if (i < attempts - 1)
				{
					await Task.Delay(delayMs);
					continue;
				}
			}
			break;
		}
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			TryDeleteRelatedFiles(path);
		}
		catch
		{
		}
	}

	private static void TryDeleteRelatedFiles(string path)
	{
		string path2 = path + "-wal";
		string path3 = path + "-shm";
		if (File.Exists(path2))
		{
			File.Delete(path2);
		}
		if (File.Exists(path3))
		{
			File.Delete(path3);
		}
	}
}
