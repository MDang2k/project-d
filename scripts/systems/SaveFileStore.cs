using System;
using System.IO;

namespace ProjectD;

public enum SaveSource { None, Main, Backup }

/// <summary>
/// Reads and writes the save file safely: a crash while writing never leaves a half-written
/// save, the previous save is kept as <c>.bak</c>, and a file that cannot be parsed is renamed
/// aside, never deleted. Plain System.IO so it is testable without the engine.
/// </summary>
public sealed class SaveFileStore
{
	private readonly string _path;

	public SaveFileStore(string path) => _path = path;

	public string BackupPath => _path + ".bak";

	public void Write(string json)
	{
		var dir = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
		var temp = _path + ".tmp";
		File.WriteAllText(temp, json);
		if (File.Exists(_path))
			File.Copy(_path, BackupPath, true);
		File.Move(temp, _path, true);
	}

	/// <param name="parse">Throws when the text is not a usable save (corrupt, too new).</param>
	public (T? Value, SaveSource Source) Load<T>(Func<string, T> parse) where T : class
	{
		foreach (var (file, source, tag) in new[] { (_path, SaveSource.Main, "main"), (BackupPath, SaveSource.Backup, "bak") })
		{
			if (!File.Exists(file))
				continue;
			try
			{
				return (parse(File.ReadAllText(file)), source);
			}
			catch (Exception)
			{
				Quarantine(file, tag);
			}
		}
		return (null, SaveSource.None);
	}

	private void Quarantine(string file, string tag)
	{
		var dir = Path.GetDirectoryName(_path) ?? "";
		var name = Path.GetFileNameWithoutExtension(_path);
		var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
		File.Move(file, Path.Combine(dir, $"{name}.corrupt-{stamp}-{tag}.json"), true);
	}
}
