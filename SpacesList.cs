using Godot;
using System;
using System.Collections.Generic;

public partial class SpacesList : ScrollContainer
{
	[Export] public PackedScene spaceUiPrefab;
	[Export] public Control spaceContainer;

	[Signal] public delegate void OnSpacePickedEventHandler(string guid);
	[Signal] public delegate void OnSpaceInviteEventHandler(string guid);

	private List<Control> renderedSpaces = new List<Control>();
	private SpaceSelector selectedSpace;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void Update(Dictionary<string, Dictionary<string, string>> spaces){
		foreach (Control renderedSpace in renderedSpaces){
			renderedSpace.QueueFree();
		}

		renderedSpaces.Clear();
		string selectedSpaceId = "";
		if (selectedSpace != null){
			selectedSpaceId = selectedSpace.spaceGuid;
		}
		selectedSpace = null;

		foreach (KeyValuePair<string, Dictionary<string, string>> space in spaces){
			SpaceSelector spaceUi = spaceUiPrefab.Instantiate<SpaceSelector>();
			spaceUi.Initialize(space.Key, space.Value["name"]);

			spaceUi.OnPicked += OnSpaceSelectorPicked;
			spaceUi.OnInvite += OnSpaceSelectorInvite;

			spaceContainer.AddChild(spaceUi);

			renderedSpaces.Add(spaceUi);

			// This was the space we had selected. So re-display it as selected
			if (space.Key == selectedSpaceId){
				selectedSpace = spaceUi;
				selectedSpace.SetSelected(true);
			}
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
