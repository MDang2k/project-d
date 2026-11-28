using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectD;

public sealed record CountersSave(int Raids, int Extractions, int Deaths, int Mia);

/// <summary>Raid counters ("counters", required).</summary>
public sealed class CountersPart(RunProgress run) : ISavePart
{
	public void Write(JsonObject root)
		=> SaveJson.Set(root, "counters", new CountersSave(run.Raids, run.Extractions, run.Deaths, run.Mia));

	public void Read(JsonObject root, LoadContext ctx)
	{
		var c = SaveJson.Get<CountersSave>(root, "counters") ?? throw new JsonException("save is missing counters");
		run.Restore(run.Inventory, c.Raids, c.Extractions, c.Deaths, c.Mia);
	}
}
