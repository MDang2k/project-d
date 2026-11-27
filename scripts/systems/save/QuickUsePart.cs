using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>Quick-use bindings by slot ("quickUse", required).</summary>
public sealed class QuickUsePart(QuickUseBar bar) : ISavePart
{
	public void Write(JsonObject root)
		=> SaveJson.Set(root, "quickUse", Enumerable.Range(0, QuickUseBar.SlotCount).Select(bar.IdAt).ToList());

	public void Read(JsonObject root, LoadContext ctx)
	{
		var ids = SaveJson.Get<List<string?>>(root, "quickUse") ?? throw new JsonException("save is missing quickUse");
		for (var i = 0; i < QuickUseBar.SlotCount; i++)
		{
			var id = i < ids.Count ? ids[i] : null;
			if (id is null)
				bar.Clear(i);
			else
				bar.Bind(i, id);
		}
	}
}
