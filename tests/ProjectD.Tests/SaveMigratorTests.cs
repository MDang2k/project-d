using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectD.Tests;

public class SaveMigratorTests
{
	/// <summary>Frozen v1 sample: if this stops loading, v1 saves in the wild broke.</summary>
	private const string V1Fixture = """
		{"version":1,"raidInProgress":true,"needsKit":false,
		 "counters":{"raids":4,"extractions":2,"deaths":1,"mia":1},
		 "quickUse":["bandage","morphine",null,"water_canteen"],
		 "character":{"slots":{"Melee":{"id":"combat_knife"}},"pockets":[{"width":1,"height":2,"items":[]}]}}
		""";

	[Fact]
	public void FrozenV1FixtureStillParses()
	{
		var root = SaveMigrator.Default.Migrate(V1Fixture);
		Assert.Equal(SaveSerializer.CurrentVersion, (int)root["version"]!);
		Assert.Equal(GameProfile.StashHeight, (int)root["stash"]!["height"]!);

		var save = SaveSerializer.Parse(V1Fixture, _ => null);
		Assert.Empty(save.Profile.Traders);
		Assert.Null(save.Profile.Side);
		Assert.Empty(save.Profile.Stash.Items);
		Assert.True(save.RaidInProgress);
		Assert.Equal(4, save.Run.Raids);
		Assert.Equal("morphine", save.Run.QuickUse.IdAt(1));
	}

	[Fact]
	public void StepsRunInOrderAndVersionIsStamped()
	{
		var m = new SaveMigrator(3, new Dictionary<int, Action<JsonObject>>
		{
			[1] = o => o["a"] = 1,
			[2] = o => o["b"] = (int)o["a"]! + 1,
		});
		var result = m.Migrate("""{"version":1}""");
		Assert.Equal(3, (int)result["version"]!);
		Assert.Equal(2, (int)result["b"]!);
	}

	[Fact]
	public void NewerThanSupportedThrowsSaveTooNew()
		=> Assert.Throws<SaveTooNewException>(() => new SaveMigrator(1).Migrate("""{"version":2}"""));

	[Fact]
	public void MissingStepOrMissingVersionIsAnError()
	{
		Assert.Throws<JsonException>(() => new SaveMigrator(2).Migrate("""{"version":1}"""));
		Assert.Throws<JsonException>(() => new SaveMigrator(1).Migrate("""{"nope":1}"""));
		Assert.Throws<JsonException>(() => new SaveMigrator(1).Migrate(""));
	}
}
