using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>
/// One piece of saved state: it writes and reads its own top-level keys of the save. To save something new, add a
/// part and one line in <see cref="SaveSerializer.Parts"/>, plus a migration step when old saves need it.
/// </summary>
public interface ISavePart
{
	void Write(JsonObject root);

	/// <summary>Reads into the fresh state the part was built over. Throws when the data is unusable.</summary>
	void Read(JsonObject root, LoadContext ctx);
}
