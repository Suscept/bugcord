using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

public partial class SpacesList : ScrollContainer
{
	[Export] public PackedScene spaceUi;
	[Export] public Control spaceContainer;
	[Export] public Bugcord bugcord;

	[Signal] public delegate void OnSpacePickedEventHandler(string guid);
	[Signal] public delegate void OnSpaceInviteEventHandler(string guid);

	private List<Control> renderedSpaces = new List<Control>();
	private SpaceSelector selectedSpace;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void Update(Dictionary spaces){
		foreach (Control renderedSpace in renderedSpaces){
			renderedSpace.QueueFree();
		}

		renderedSpaces.Clear();

		foreach (var (guid, info) in spaces){
			SpaceSelector space = spaceUi.Instantiate<SpaceSelector>();
			space.Initialize((string)guid, (string)((Dictionary)info)["name"]);

			space.OnPicked += OnSpaceSelectorPicked;
			space.OnInvite += OnSpaceSelectorInvite;

			spaceContainer.AddChild(space);

			renderedSpaces.Add(space);
		}
	}

	public void OnSpaceSelectorPicked(string guid, SpaceSelector spaceButton){
		if (selectedSpace != null){
			selectedSpace.SetSelected(false);
		}
		selectedSpace = spaceButton;
		spaceButton.SetSelected(true);
		
		EmitSignal(SignalName.OnSpacePicked, guid);
	}

	public void OnSpaceSelectorInvite(string guid){
		EmitSignal(SignalName.OnSpaceInvite, guid);
	}
}
