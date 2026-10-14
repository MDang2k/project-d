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
		var file = SaveSerializer.Parse(V1Fixture);
		Assert.Equal(1, file.Version);
		Assert.True(file.RaidInProgress);
		Assert.Equal(4, file.Counters.Raids);
		Assert.Equal("morphine", file.QuickUse[1]);
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
