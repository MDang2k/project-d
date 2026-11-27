using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>An insured item waiting for delivery (v4).</summary>
public sealed record InsuranceReturnSave(ItemSave Item, int RaidsRemaining);

/// <summary>Insured items on their way back ("insuranceReturns").</summary>
public sealed class InsuranceReturnsPart(InsuranceQueue queue) : ISavePart
{
	public void Write(JsonObject root)
		=> SaveJson.Set(root, "insuranceReturns", queue.Entries.Select(e => new InsuranceReturnSave(ItemSaves.ToSave(e.Item), e.RaidsRemaining)).ToList());

	public void Read(JsonObject root, LoadContext ctx)
	{
		foreach (var ret in SaveJson.Get<List<InsuranceReturnSave>>(root, "insuranceReturns") ?? new())
		{
			if (ItemSaves.LoadItem(ret?.Item, ctx) is { } returned)
				queue.Add(new PendingReturn(returned, Math.Max(0, ret!.RaidsRemaining)));
		}
	}
}
