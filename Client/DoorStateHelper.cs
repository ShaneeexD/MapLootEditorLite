using System;
using EFT.Interactive;

namespace MapLootEditorLite.Client
{
    // Shared lock-state handling for doors. EFT locks a door by setting WorldInteractiveObject.DoorState = Locked
    // (this is what WorldInteractiveObject.Lock() does); KeyId only decides which key can unlock it. No separate state is invented.
    internal static class DoorStateHelper
    {
        // Pack values: "Locked", "Shut" (unlocked, closed), "Open". Anything else (including empty) is not a valid explicit state.
        public static bool TryParse(string value, out EDoorState state)
        {
            state = EDoorState.Shut;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            if (string.Equals(value, "Locked", StringComparison.OrdinalIgnoreCase)) { state = EDoorState.Locked; return true; }
            if (string.Equals(value, "Shut", StringComparison.OrdinalIgnoreCase)) { state = EDoorState.Shut; return true; }
            if (string.Equals(value, "Open", StringComparison.OrdinalIgnoreCase)) { state = EDoorState.Open; return true; }
            return false;
        }

        // Mirrors EFT's own sequence: state, initial/fallback state (so a later interaction falls back correctly),
        // then the matching angle. Must run after the door's OnEnable (which snapshots InitialDoorState).
        public static void Apply(WorldInteractiveObject wio, EDoorState state)
        {
            wio.DoorState = state;
            wio.InitialDoorState = state;
            wio.FallbackState = state;
            wio.CurrentAngle = wio.GetAngle(state);
        }
    }
}
