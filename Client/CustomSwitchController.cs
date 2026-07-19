using System.Linq;
using EFT.Interactive;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class CustomSwitchController : MonoBehaviour
    {
        public InteractiveObject Data;
        public WorldInteractiveObject Wio;

        private EDoorState _previousState;

        private void Start()
        {
            if (Wio != null)
            {
                _previousState = Wio.DoorState;
                SetTargets(_previousState == EDoorState.Open);
            }
        }

        private void Update()
        {
            if (Wio == null)
                return;

            if (Wio.DoorState != _previousState)
            {
                _previousState = Wio.DoorState;
                bool on = _previousState == EDoorState.Open;
                SetTargets(on);
                Plugin.Log.LogInfo($"Switch '{Data?.name}' toggled to {(on ? "On" : "Off")}.");
            }
        }

        private void SetTargets(bool on)
        {
            var lightSpawner = RuntimeLightZoneSpawner.Instance;
            if (lightSpawner != null && Data != null)
            {
                foreach (var name in Data.linkedLightZoneNames ?? Enumerable.Empty<string>())
                    lightSpawner.SetLightState(name, on);
            }

            var extractSpawner = RuntimeExtractZoneSpawner.Instance;
            if (extractSpawner != null && Data != null)
            {
                foreach (var name in Data.linkedExtractNames ?? Enumerable.Empty<string>())
                    extractSpawner.SetExtractEnabled(name, on);
            }
        }
    }
}
