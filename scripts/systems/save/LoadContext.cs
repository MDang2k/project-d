using System;
using System.Collections.Generic;

namespace ProjectD;

/// <summary>What one load shares between its save parts.</summary>
public sealed class LoadContext
{
	public LoadContext(Func<string, ItemSpec?> lookup) => Lookup = lookup;

	public Func<string, ItemSpec?> Lookup { get; }

	public List<string> Warnings { get; } = new();

	/// <summary>Parts that no longer fit their slot after a spec change; the serializer moves them to the stash.</summary>
	public List<ItemInstance> Detached { get; } = new();
}
