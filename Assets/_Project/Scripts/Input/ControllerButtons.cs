using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// The story actions on Quest Touch controllers (and the Meta XR Simulator's), for when the player isn't using
    /// hand tracking: without this the prompts that say "E" or "1 / 2" could only be answered from a keyboard.
    /// Interact = right trigger or A; Skip = B or Y; dialogue choice [A] = left trigger or X, [B] = right trigger or A.
    /// Only real controllers count (a tracked hand's pinch can show up as a "trigger", and pinching is for grabbing).
    /// Read through <see cref="GameInput"/>; sampled once per frame.
    /// </summary>
    static class ControllerButtons
    {
        enum Button { LeftTrigger, RightTrigger, A, B, X, Y }
        const int Count = 6;

        static readonly Dictionary<GameAction, Button[]> Map = new Dictionary<GameAction, Button[]>
        {
            { GameAction.Interact, new[] { Button.RightTrigger, Button.A } },
            { GameAction.Skip, new[] { Button.B, Button.Y } },
            { GameAction.ChoiceA, new[] { Button.LeftTrigger, Button.X } },
            { GameAction.ChoiceB, new[] { Button.RightTrigger, Button.A } },
        };

        static readonly bool[] now = new bool[Count], before = new bool[Count];
        static int sampled = -1;

        public static string Label(GameAction action) => action switch
        {
            GameAction.Interact => "trigger / A",
            GameAction.Skip => "B / Y",
            GameAction.ChoiceA => "left trigger / X",
            GameAction.ChoiceB => "right trigger / A",
            _ => null,
        };

        public static bool Pressed(GameAction action) => Any(action, (n, b) => n && !b);
        public static bool Held(GameAction action) => Any(action, (n, b) => n);
        public static bool Released(GameAction action) => Any(action, (n, b) => !n && b);

        static bool Any(GameAction action, System.Func<bool, bool, bool> test)
        {
            if (!Map.TryGetValue(action, out Button[] buttons)) return false;
            Sample();
            foreach (Button b in buttons)
                if (test(now[(int)b], before[(int)b])) return true;
            return false;
        }

        static void Sample()
        {
            if (sampled == Time.frameCount) return;
            sampled = Time.frameCount;
            System.Array.Copy(now, before, Count);
            for (int i = 0; i < Count; i++) now[i] = false;
            if (!XRSettings.isDeviceActive) return;
            Read(XRNode.LeftHand, Button.LeftTrigger, Button.X, Button.Y);
            Read(XRNode.RightHand, Button.RightTrigger, Button.A, Button.B);
        }

        static void Read(XRNode node, Button trigger, Button primary, Button secondary)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;
            if ((device.characteristics & InputDeviceCharacteristics.Controller) == 0) return;
            if ((device.characteristics & InputDeviceCharacteristics.HandTracking) != 0) return;
            string name = device.name ?? "";
            if (name.IndexOf("hand", System.StringComparison.OrdinalIgnoreCase) >= 0) return; // hand-tracking "aim" devices
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool t)) now[(int)trigger] = t;
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool p)) now[(int)primary] = p;
            if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool s)) now[(int)secondary] = s;
        }
    }
}
