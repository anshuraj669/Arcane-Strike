using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcaneStrike.HandInput
{
    /// <summary>Everything the player can do, by name. Scripts ask <see cref="GameInput"/> about these, never about keys.</summary>
    public enum GameAction
    {
        // Moving
        MoveForward, MoveBack, MoveLeft, MoveRight, TurnLeft, TurnRight, Look, Jump, ToggleSmoothMove,
        // The story
        Interact, Skip, PointGrab, Throw, RaisePalms, ChooseTrain, ChooseOnward, ChoiceA, ChoiceB,
        // Powers
        Firebolt, Aegis, Seal, VoltBurst, Lumen, Sunwheel, SparkBlade, Skyhammer, Grab,
        // Bare hands and dodging
        Punch, Swipe, DodgeLeft, DodgeRight, Duck,
        // The sword (the Rift)
        SwordSlash, SwordThrust, SwordOverhead, SwordWhirlwind, SwordGuard, SwordCharge, SwordRising, SwordCleave, SwordLunge,
        // Settings and testing
        NextAppearance, NextChapter, PreviousChapter, ToggleRoomMesh, CalibrateWebcam, ReplaceSet,
        DebugKillAll, DebugSkipToEnd, ShowControls,
        // (added later: kept at the end so saved values don't shift)
        WalkToTarget,
    }

    /// <summary>
    /// The one place the game's controls are decided. For now every action is on the keyboard (and mouse); each
    /// script asks <see cref="Pressed"/> / <see cref="Held"/> for an action, not for a key.
    /// To move an action to a hand gesture later, have the gesture code call <see cref="Press"/> (a one-off, like a
    /// key press) or <see cref="SetHeld"/> (while the pose is held) for that action: every script that listens to the
    /// action then responds to the gesture too, and the key keeps working alongside. <see cref="Rebind"/> changes a key.
    /// F1 shows the list in the game (<see cref="ControlsHelp"/>).
    /// </summary>
    public static class GameInput
    {
        /// <summary>Off: the keyboard and mouse do nothing (gestures and code still can).</summary>
        public static bool KeyboardEnabled = true;

        enum Mouse { None, Left, Right }

        struct Binding
        {
            public Key key;
            public Mouse mouse;
            public string group, label;
        }

        static readonly Dictionary<GameAction, Binding> Bindings = new Dictionary<GameAction, Binding>();
        static readonly Dictionary<GameAction, int> pressedFrame = new Dictionary<GameAction, int>();
        static readonly Dictionary<GameAction, int> releasedFrame = new Dictionary<GameAction, int>();
        static readonly HashSet<GameAction> held = new HashSet<GameAction>();

        // Every play starts with nothing held (in the editor statics outlive a play with domain reload off), and a new
        // scene starts with no gesture still "holding" an action whose script went away with the old scene.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            KeyboardEnabled = true;
            ReplayPressed = ReplayHeld = ReplayReleased = null;
            ReplayMouse = null;
            held.Clear();
            pressedFrame.Clear();
            releasedFrame.Clear();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Loaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += Loaded;
        }

        static void Loaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single) held.Clear();
        }

        static GameInput()
        {
            Bind(GameAction.MoveForward, Key.W, "Move", "Walk forward");
            Bind(GameAction.MoveBack, Key.S, "Move", "Walk back");
            Bind(GameAction.MoveLeft, Key.A, "Move", "Step left");
            Bind(GameAction.MoveRight, Key.D, "Move", "Step right");
            Bind(GameAction.TurnLeft, Key.Z, "Move", "Turn left (30°)");
            Bind(GameAction.TurnRight, Key.X, "Move", "Turn right (30°)");
            BindMouse(GameAction.Look, Mouse.Right, "Move", "Look around (hold, move the mouse)");
            Bind(GameAction.Jump, Key.Space, "Move", "Jump");
            Bind(GameAction.ToggleSmoothMove, Key.L, "Move", "Smooth walking on/off");
            Bind(GameAction.WalkToTarget, Key.M, "Move", "Walk to what you look at, stop an arm's length away (M again stops). Hands: point the left index finger at it");

            Bind(GameAction.Interact, Key.E, "Story", "Interact / do the next story step");
            Bind(GameAction.Skip, Key.N, "Story", "Skip the line / the step");
            Bind(GameAction.PointGrab, Key.T, "Story", "Grab what you look at / drop it");
            Bind(GameAction.Throw, Key.Y, "Story", "Throw what you hold");
            Bind(GameAction.RaisePalms, Key.P, "Story", "Raise both palms to your face (hold)");
            Bind(GameAction.ChooseTrain, Key.Enter, "Story", "Cave: choose TRAIN WITH EMBER");
            Bind(GameAction.ChooseOnward, Key.Backspace, "Story", "Cave: choose ONWARD (also stops the practice)");
            Bind(GameAction.ChoiceA, Key.Digit1, "Story", "Dialogue: choose [A] (when a choice is shown)");
            Bind(GameAction.ChoiceB, Key.Digit2, "Story", "Dialogue: choose [B] (when a choice is shown)");

            Bind(GameAction.Firebolt, Key.Digit1, "Powers", "Firebolt");
            Bind(GameAction.Aegis, Key.Digit2, "Powers", "Aegis shield (hold)");
            Bind(GameAction.Seal, Key.Digit3, "Powers", "Seal");
            Bind(GameAction.VoltBurst, Key.Digit4, "Powers", "Volt Burst");
            Bind(GameAction.Lumen, Key.Digit5, "Powers", "Lumen");
            Bind(GameAction.Sunwheel, Key.Digit6, "Powers", "Sunwheel");
            Bind(GameAction.SparkBlade, Key.Digit7, "Powers", "Spark Blade (hold)");
            Bind(GameAction.Skyhammer, Key.Digit8, "Powers", "Skyhammer");
            Bind(GameAction.Grab, Key.G, "Powers", "Grab power (hold)");

            Bind(GameAction.Punch, Key.J, "Hands", "Punch");
            Bind(GameAction.Swipe, Key.K, "Hands", "Swipe");
            Bind(GameAction.DodgeLeft, Key.LeftArrow, "Hands", "Dodge left");
            Bind(GameAction.DodgeRight, Key.RightArrow, "Hands", "Dodge right");
            Bind(GameAction.Duck, Key.DownArrow, "Hands", "Duck");

            BindMouse(GameAction.SwordSlash, Mouse.Left, "Sword", "Slash (Jyoti Wave when charged)");
            Bind(GameAction.SwordThrust, Key.F, "Sword", "Thrust");
            Bind(GameAction.SwordOverhead, Key.Q, "Sword", "Overhead smash");
            Bind(GameAction.SwordWhirlwind, Key.V, "Sword", "Whirlwind");
            Bind(GameAction.SwordGuard, Key.LeftShift, "Sword", "Guard / parry (hold)");
            Bind(GameAction.SwordCharge, Key.R, "Sword", "Charge the blade (hold)");
            Bind(GameAction.SwordRising, Key.B, "Sword", "Rising slash (breaks a guard, staggers)");
            Bind(GameAction.SwordCleave, Key.U, "Sword", "Cleave (a wide sweep through everything in front)");
            Bind(GameAction.SwordLunge, Key.I, "Sword", "Lunge (dash in with a thrust)");

            Bind(GameAction.NextAppearance, Key.O, "Other", "Next outfit colours");
            Bind(GameAction.NextChapter, Key.RightBracket, "Other", "Next chapter (powers)");
            Bind(GameAction.PreviousChapter, Key.LeftBracket, "Other", "Previous chapter (powers)");
            Bind(GameAction.ToggleRoomMesh, Key.F5, "Other", "Show/hide the room mesh");
            Bind(GameAction.CalibrateWebcam, Key.C, "Other", "Calibrate webcam hands");
            Bind(GameAction.ReplaceSet, Key.H, "Other", "Re-place the attic set in front of you");
            Bind(GameAction.DebugKillAll, Key.F9, "Other", "Rift: destroy every shadow");
            Bind(GameAction.DebugSkipToEnd, Key.F10, "Other", "Rift: jump to the ending");
            Bind(GameAction.ShowControls, Key.F1, "Other", "Show/hide these controls");
        }

        static void Bind(GameAction action, Key key, string group, string label) =>
            Bindings[action] = new Binding { key = key, group = group, label = label };

        static void BindMouse(GameAction action, Mouse button, string group, string label) =>
            Bindings[action] = new Binding { mouse = button, group = group, label = label };

        /// <summary>Moves an action to another key.</summary>
        public static void Rebind(GameAction action, Key key)
        {
            Bindings.TryGetValue(action, out Binding b);
            b.key = key;
            b.mouse = Mouse.None;
            Bindings[action] = b;
        }

        // ---------------------------------------------------------------- reading

        // ---------------------------------------------------------------- replaying a recorded demo take

        /// <summary>Set while a recorded take replays (Demo/DemoDirector): the keys, controller buttons and mouse
        /// come from the recording instead of the real ones. Gestures (Press / SetHeld) still work as usual.</summary>
        public static System.Func<GameAction, bool> ReplayPressed, ReplayHeld, ReplayReleased;
        public static System.Func<Vector2> ReplayMouse;
        public static bool Replaying => ReplayHeld != null;

        /// <summary>The real keyboard / mouse / controller only (no gestures, no replay): what a take records.</summary>
        public static bool RawPressed(GameAction action) =>
            KeyState(action, k => k.wasPressedThisFrame, m => m.wasPressedThisFrame) || ControllerButtons.Pressed(action);
        public static bool RawHeld(GameAction action) =>
            KeyState(action, k => k.isPressed, m => m.isPressed) || ControllerButtons.Held(action);
        public static bool RawReleased(GameAction action) =>
            KeyState(action, k => k.wasReleasedThisFrame, m => m.wasReleasedThisFrame) || ControllerButtons.Released(action);
        public static Vector2 RawMouseDelta() =>
            KeyboardEnabled && UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.delta.ReadValue() : Vector2.zero;

        /// <summary>Started this frame (key went down, or <see cref="Press"/> / <see cref="SetHeld"/> this frame).</summary>
        public static bool Pressed(GameAction action) =>
            (Replaying ? ReplayPressed(action) : RawPressed(action))
            || pressedFrame.TryGetValue(action, out int frame) && frame == Time.frameCount;

        /// <summary>Held down right now (key, or a held gesture).</summary>
        public static bool Held(GameAction action) =>
            (Replaying ? ReplayHeld(action) : RawHeld(action)) || held.Contains(action);

        /// <summary>Let go this frame.</summary>
        public static bool Released(GameAction action) =>
            (Replaying ? ReplayReleased(action) : RawReleased(action))
            || releasedFrame.TryGetValue(action, out int frame) && frame == Time.frameCount;

        /// <summary>WASD as a stick: x right, y forward (zero when nothing is held).</summary>
        public static Vector2 MoveAxis()
        {
            var v = new Vector2((Held(GameAction.MoveRight) ? 1 : 0) - (Held(GameAction.MoveLeft) ? 1 : 0),
                                (Held(GameAction.MoveForward) ? 1 : 0) - (Held(GameAction.MoveBack) ? 1 : 0));
            return v.sqrMagnitude > 0f ? v.normalized : v;
        }

        /// <summary>Mouse movement this frame (pixels).</summary>
        public static Vector2 MouseDelta() => Replaying ? (ReplayMouse != null ? ReplayMouse() : Vector2.zero) : RawMouseDelta();

        // ---------------------------------------------------------------- driving actions from gestures or code

        /// <summary>A one-off, like tapping the key (for a gesture that has just happened).</summary>
        public static void Press(GameAction action) => pressedFrame[action] = Time.frameCount;

        /// <summary>Held while a pose is held (Pressed fires on the frame it starts).</summary>
        public static void SetHeld(GameAction action, bool down)
        {
            if (down && held.Add(action)) Press(action);
            else if (!down && held.Remove(action)) releasedFrame[action] = Time.frameCount;
        }

        // ---------------------------------------------------------------- describing

        /// <summary>The key (or mouse button) for an action, e.g. "Space".</summary>
        public static string KeyName(GameAction action)
        {
            if (!Bindings.TryGetValue(action, out Binding b)) return "-";
            string pad = ControllerButtons.Label(action);
            if (pad != null) return KeyNameOnly(b) + "  (" + pad + ")";
            return KeyNameOnly(b);
        }

        static string KeyNameOnly(Binding b)
        {
            if (b.mouse == Mouse.Left) return "Left click";
            if (b.mouse == Mouse.Right) return "Right mouse";
            return b.key switch
            {
                Key.Digit1 => "1", Key.Digit2 => "2", Key.Digit3 => "3", Key.Digit4 => "4",
                Key.Digit5 => "5", Key.Digit6 => "6", Key.Digit7 => "7", Key.Digit8 => "8",
                Key.LeftArrow => "←", Key.RightArrow => "→", Key.DownArrow => "↓", Key.UpArrow => "↑",
                Key.LeftBracket => "[", Key.RightBracket => "]", Key.LeftShift => "Shift",
                _ => b.key.ToString(),
            };
        }

        /// <summary>Every action as (group, key, what it does), in order.</summary>
        public static IEnumerable<(string group, string key, string label)> List()
        {
            foreach (var pair in Bindings) yield return (pair.Value.group, KeyName(pair.Key), pair.Value.label);
        }

        static bool KeyState(GameAction action, System.Func<UnityEngine.InputSystem.Controls.KeyControl, bool> key,
                             System.Func<UnityEngine.InputSystem.Controls.ButtonControl, bool> mouse)
        {
            if (!KeyboardEnabled || !Bindings.TryGetValue(action, out Binding b)) return false;
            if (b.mouse != Mouse.None)
            {
                var m = UnityEngine.InputSystem.Mouse.current;
                if (m == null) return false;
                return mouse(b.mouse == Mouse.Left ? m.leftButton : m.rightButton);
            }
            var kb = Keyboard.current;
            return kb != null && b.key != Key.None && key(kb[b.key]);
        }
    }
}
