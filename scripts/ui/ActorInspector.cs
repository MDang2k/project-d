using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Dev panel (F5): the stats of the AI the player is looking at, each with its base and the modifiers that changed
/// it. F6 and F7 switch the veteran and rookie sets on that AI to see a modifier act live. Debug builds only.
/// </summary>
public partial class ActorInspector : CanvasLayer
{
	private const float MaxDistance = 60f;
	private const float ConeDot = 0.866f; // 30 degrees

	private Label _text = null!;

	public override void _Ready()
	{
		_text = GetNode<Label>("Text");
		Visible = false;
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (!OS.IsDebugBuild() || @event is not InputEventKey { Pressed: true, Echo: false } key)
			return;
		switch (key.Keycode)
		{
			case Key.F5:
				Visible = !Visible;
				break;
			case Key.F6 when Visible:
				Toggle("veteran");
				break;
			case Key.F7 when Visible:
				Toggle("rookie");
				break;
		}
	}

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		_text.Text = Target() is { Actor: { } actor }
			? string.Join("\n", ActorInspectorText.Lines(actor)) + "\n\nF6 veteran  F7 rookie (toggle on this AI)"
			: "F5 stat inspector: look at an AI within 60 m";
	}

	private void Toggle(string set)
	{
		if (Target()?.Actor is not { } actor || !ActorDb.Catalog.SetNames.Contains(set))
			return;
		if (actor.Stats.Active().Any(a => a.Modifier.Source == set))
			actor.Stats.Remove(set);
		else
			actor.Stats.AddRange(ActorDb.Catalog.Set(set));
	}

	/// <summary>Nearest AI inside a narrow cone around where the camera looks.</summary>
	private AiHuman? Target()
	{
		var cam = GetViewport().GetCamera3D();
		if (cam is null)
			return null;
		var forward = -cam.GlobalTransform.Basis.Z;
		return GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>()
			.Where(a => a.Actor is not null)
			.Select(a => (ai: a, to: a.GlobalPosition + Vector3.Up * 1.2f - cam.GlobalPosition))
			.Where(x => x.to.Length() <= MaxDistance && x.to.Normalized().Dot(forward) >= ConeDot)
			.OrderBy(x => x.to.Length())
			.Select(x => x.ai)
			.FirstOrDefault();
	}
}
