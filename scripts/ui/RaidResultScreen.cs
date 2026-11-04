using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace ProjectD;

/// <summary>
/// End-of-raid screen (GDD s15): extracted, killed or MIA, what was lost and what was kept, run
/// totals, and a button for the next raid. Built in code; pauses the game while shown.
/// </summary>
public partial class RaidResultScreen : CanvasLayer
{
	public event Action? NewRaidRequested;

	public static RaidResultScreen Show(Node parent, RaidOutcome outcome, float elapsedS,
		IReadOnlyList<ItemInstance> lost, IReadOnlyList<ItemInstance> kept, RunProgress run,
		InsuranceRaidResult? insurance = null, SkillBook? skills = null, NemesisKillResult? nemesis = null,
		IReadOnlyList<string>? knowledge = null)
	{
		var screen = new RaidResultScreen { Layer = 100, ProcessMode = Node.ProcessModeEnum.Always };
		screen.Build(outcome, elapsedS, lost, kept, run, insurance, skills, nemesis, knowledge);
		parent.AddChild(screen);
		parent.GetTree().Paused = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		return screen;
	}

	/// <summary>Display name of a reputation target: a faction or a trader.</summary>
	public static string RepName(string target)
		=> ReputationSummary.Factions.Contains(target) ? GameText.Get("faction." + target) : GameText.Get("trader." + target);

	private static string Names(IEnumerable<ItemInstance> items, int max)
	{
		var groups = items.GroupBy(i => i.Spec.NameKey)
			.Select(g => (Name: GameText.Get(g.Key), Count: g.Sum(i => Math.Max(1, i.Count))))
			.OrderBy(g => g.Name)
			.ToList();
		var sb = new StringBuilder();
		foreach (var (name, count) in groups.Take(max))
			sb.AppendLine(count > 1 ? $"  {name} x{count}" : $"  {name}");
		if (groups.Count > max)
			sb.AppendLine($"  ... and {groups.Count - max} more kinds");
		return sb.Length == 0 ? "  nothing\n" : sb.ToString();
	}

	private void Build(RaidOutcome outcome, float elapsedS, IReadOnlyList<ItemInstance> lost,
		IReadOnlyList<ItemInstance> kept, RunProgress run, InsuranceRaidResult? insurance, SkillBook? skills, NemesisKillResult? nemesis, IReadOnlyList<string>? knowledge)
	{
		var key = outcome switch
		{
			RaidOutcome.Extracted => "raid.extracted",
			RaidOutcome.Killed => "raid.killed",
			_ => "raid.mia",
		};
		var color = outcome == RaidOutcome.Extracted ? new Color(0.45f, 0.9f, 0.5f) : new Color(0.95f, 0.4f, 0.35f);

		var shade = new ColorRect { Color = new Color(0f, 0f, 0f, 0.82f) };
		shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(shade);

		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(center);
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(560f, 0f) };
		box.AddThemeConstantOverride("separation", 10);
		center.AddChild(box);

		var title = new Label { Text = GameText.Get(key), HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 44);
		title.AddThemeColorOverride("font_color", color);
		box.AddChild(title);
		box.AddChild(new Label
		{
			Text = GameText.Get(key + ".detail"),
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		});

		if (outcome == RaidOutcome.Killed && nemesis is not null)
		{
			var killer = new Label
			{
				Text = NemesisSummary.KilledBy(nemesis.Nemesis, k => GameText.Get(k)),
				HorizontalAlignment = HorizontalAlignment.Center,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				Modulate = new Color(1f, 0.85f, 0.4f),
			};
			box.AddChild(killer);
		}

		var minutes = (int)(elapsedS / 60f);
		var summary = new StringBuilder();
		summary.AppendLine($"Raid time {minutes}:{(int)elapsedS % 60:00}");
		if (outcome == RaidOutcome.Extracted)
		{
			summary.AppendLine($"\nCarried out ({kept.Count} items):");
			summary.Append(Names(kept, 14));
		}
		else
		{
			summary.AppendLine($"\nLost ({lost.Count} items):");
			summary.Append(Names(lost, 10));
			summary.AppendLine("\nKept in the secure container:");
			summary.Append(Names(kept, 8));
		}
		if (outcome != RaidOutcome.Extracted && insurance is not null)
		{
			summary.AppendLine(insurance.Returned.Count > 0
				? "\nInsured items returning after your next raid:"
				: "\nNo insured items returned.");
			if (insurance.Returned.Count > 0)
				summary.Append(Names(insurance.Returned, 6));
		}
		if (skills is not null && SkillSummary.RaidLines(skills, id => GameText.Get("skill." + id)) is { Count: > 0 } trained)
		{
			summary.AppendLine("\n" + GameText.Get("raid.skills"));
			foreach (var line in trained)
				summary.AppendLine("  " + line);
		}
		if (knowledge is { Count: > 0 })
		{
			summary.AppendLine("\n" + GameText.Get("knowledge.raid.title"));
			foreach (var line in knowledge)
				summary.AppendLine("  " + line);
		}
		if (ReputationSummary.RaidLines(RaidStanding.Changes, RepName) is { Count: > 0 } reputation)
		{
			summary.AppendLine("\n" + GameText.Get("rep.raid.title"));
			foreach (var line in reputation)
				summary.AppendLine("  " + line);
		}
		summary.AppendLine($"\nRun: {run.Raids} raids, {run.Extractions} extracted, {run.Deaths} killed, {run.Mia} MIA");
		box.AddChild(new Label { Text = summary.ToString() });

		var button = new Button { Text = GameText.Get("raid.new"), CustomMinimumSize = new Vector2(0f, 48f) };
		button.Pressed += () =>
		{
			GetTree().Paused = false;
			NewRaidRequested?.Invoke();
		};
		box.AddChild(button);
		button.CallDeferred(Control.MethodName.GrabFocus);
	}
}
