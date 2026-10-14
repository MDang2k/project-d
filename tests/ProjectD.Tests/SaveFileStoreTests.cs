using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public sealed class SaveFileStoreTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "pd_save_" + Guid.NewGuid().ToString("N"));
	private readonly SaveFileStore _store;

	public SaveFileStoreTests() => _store = new SaveFileStore(Path.Combine(_dir, "save.json"));

	public void Dispose()
	{
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, true);
	}

	private static string Parse(string text) => text.StartsWith("ok") ? text : throw new FormatException("bad");

	private string[] Files() => Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(x => x).ToArray()!;

	[Fact]
	public void MissingFilesGiveNone()
	{
		var (value, source) = _store.Load(Parse);
		Assert.Null(value);
		Assert.Equal(SaveSource.None, source);
	}

	[Fact]
	public void WriteThenLoadAndSecondWriteKeepsBackup()
	{
		_store.Write("ok-1");
		_store.Write("ok-2");
		Assert.Equal(new[] { "save.json", "save.json.bak" }, Files());
		Assert.Equal("ok-1", File.ReadAllText(_store.BackupPath));
		var (value, source) = _store.Load(Parse);
		Assert.Equal("ok-2", value);
		Assert.Equal(SaveSource.Main, source);
	}

	[Fact]
	public void CorruptMainFallsBackToBackupAndIsQuarantinedNotDeleted()
	{
		_store.Write("ok-1");
		_store.Write("ok-2");
		File.WriteAllText(Path.Combine(_dir, "save.json"), "garbage");
		var (value, source) = _store.Load(Parse);
		Assert.Equal("ok-1", value);
		Assert.Equal(SaveSource.Backup, source);
		Assert.Contains(Files(), f => f.StartsWith("save.corrupt-") && f.EndsWith("-main.json"));
		Assert.False(File.Exists(Path.Combine(_dir, "save.json")));
	}

	[Fact]
	public void BothUnreadableGiveNoneAndKeepBothFiles()
	{
		Directory.CreateDirectory(_dir);
		File.WriteAllText(Path.Combine(_dir, "save.json"), "");
		File.WriteAllText(Path.Combine(_dir, "save.json.bak"), "garbage");
		var (value, source) = _store.Load(Parse);
		Assert.Null(value);
		Assert.Equal(SaveSource.None, source);
		Assert.Equal(2, Files().Count(f => f.StartsWith("save.corrupt-")));
	}

	[Fact]
	public void TryWriteReturnsFalseInsteadOfThrowingWhenThePathIsUnwritable()
	{
		Directory.CreateDirectory(_dir);
		var blocker = Path.Combine(_dir, "blocker");
		File.WriteAllText(blocker, "x");
		var store = new SaveFileStore(Path.Combine(blocker, "save.json"));
		Assert.False(store.TryWrite("ok", out var error));
		Assert.False(string.IsNullOrEmpty(error));
	}

	[Fact]
	public void TooNewSaveIsNotQuarantinedAndSurfaces()
	{
		_store.Write("ok-1");
		Assert.Throws<SaveTooNewException>(() => _store.Load<string>(_ => throw new SaveTooNewException(2, 1)));
		Assert.Equal(new[] { "save.json" }, Files());
	}

	[Fact]
	public void QuarantineIsReportedToTheCaller()
	{
		_store.Write("ok-1");
		_store.Write("ok-2");
		File.WriteAllText(Path.Combine(_dir, "save.json"), "garbage");
		string? reported = null;
		_store.Load(Parse, (file, e) => reported = Path.GetFileName(file) + ":" + e.Message);
		Assert.Equal("save.json:bad", reported);
	}
}
