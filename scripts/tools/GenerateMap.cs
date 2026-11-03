using System.IO;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Dev command: builds the graybox scene of a map layout (raid map standard s1).
/// <c>godot --headless --path . -s res://scripts/tools/GenerateMap.cs -- border_strip</c> reads
/// data/maps/border_strip.layout.json, prints the validator report and writes
/// scenes/maps/generated/border_strip_graybox.tscn. Optional second and third arguments override the input and output paths.
/// </summary>
public partial class GenerateMap : SceneTree
{
	public override void _Initialize()
	{
		var args = OS.GetCmdlineUserArgs();
		if (args.Length == 0)
		{
			GD.PrintErr("usage: -- <map id> [layout path] [output path]");
			Quit(2);
			return;
		}
		var input = args.Length > 1 ? args[1] : ProjectSettings.GlobalizePath($"res://data/maps/{args[0]}.layout.json");
		var output = args.Length > 2 ? args[2] : ProjectSettings.GlobalizePath($"res://scenes/maps/generated/{args[0]}_graybox.tscn");
		try
		{
			var layout = MapLayout.Parse(File.ReadAllText(input));
			var report = MapValidator.Validate(layout);
			foreach (var issue in report.Issues)
				GD.Print($"VALIDATOR {issue.Rule}: {issue.Message}");
			var graybox = GrayboxBuilder.Build(layout);
			Directory.CreateDirectory(Path.GetDirectoryName(output)!);
			File.WriteAllText(output, GrayboxTscn.Write(graybox));
			GD.Print($"GRAYBOX {layout.Id}: {graybox.Boxes.Count} boxes, {graybox.Markers.Count} markers, {report.Issues.Count} validator issues -> {output}");
			Quit(0);
		}
		catch (LayoutException e)
		{
			GD.PrintErr(e.Message);
			Quit(1);
		}
	}
}
