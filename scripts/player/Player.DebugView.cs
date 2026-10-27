using Godot;

namespace ProjectD;

public enum DebugCameraView { FirstPerson, Front, Side, Back }

/// <summary>
/// Dev: outside cameras to look at the player's own character (front, right side, back). The camera is a child
/// of the player, so it turns with the view and keeps facing the character; controls stay first person.
/// </summary>
public partial class Player
{
	private DebugCameraView _debugView;
	private Camera3D? _debugCam;

	public DebugCameraView DebugView
	{
		get => _debugView;
		set
		{
			_debugView = value;
			_body.ShowHead(value != DebugCameraView.FirstPerson);
			if (value == DebugCameraView.FirstPerson)
			{
				_debugCam?.QueueFree();
				_debugCam = null;
				_camera.Current = true;
				return;
			}
			if (_debugCam is null)
			{
				_debugCam = new Camera3D { Name = "DebugViewCam", Fov = 45f, Near = 0.05f };
				AddChild(_debugCam);
			}
			var at = value switch
			{
				DebugCameraView.Front => new Vector3(0f, 1.5f, -4.4f),
				DebugCameraView.Side => new Vector3(4.4f, 1.5f, 0f),
				_ => new Vector3(0f, 1.7f, 4.6f),
			};
			_debugCam.LookAtFromPosition(ToGlobal(at), ToGlobal(new Vector3(0f, 0.95f, 0f)), Vector3.Up);
			_debugCam.Current = true;
		}
	}

	/// <summary>First person, front, side, back, then first person again.</summary>
	public void CycleDebugView() => DebugView = (DebugCameraView)(((int)_debugView + 1) % 4);
}
