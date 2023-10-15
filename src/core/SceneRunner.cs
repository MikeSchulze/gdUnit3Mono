using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace GdUnit3
{
    using Asserts;
    using Executions;
    using Godot;
    using static Assertions;

    public static class GdUnitAwaiter
    {
        public static async Task WithTimeout(this Task task, int timeoutMillis)
        {
            var lineNumber = GetWithTimeoutLineNumber();
            var wrapperTask = Task.Run(async () => await task);
            using var token = new CancellationTokenSource();
            var completedTask = await Task.WhenAny(wrapperTask, Task.Delay(timeoutMillis, token.Token));
            if (completedTask != wrapperTask)
                throw new ExecutionTimeoutException($"Assertion: Timed out after {timeoutMillis}ms.", lineNumber);
            token.Cancel();
            await task;
        }

        public static async Task<T> WithTimeout<T>(this Task<T> task, int timeoutMillis)
        {
            var lineNumber = GetWithTimeoutLineNumber();
            var wrapperTask = Task.Run(async () => await task);
            using var token = new CancellationTokenSource();
            var completedTask = await Task.WhenAny(wrapperTask, Task.Delay(timeoutMillis, token.Token));
            if (completedTask != wrapperTask)
                throw new ExecutionTimeoutException($"Assertion: Timed out after {timeoutMillis}ms.", lineNumber);
            token.Cancel();
            return await task;
        }

        private static int GetWithTimeoutLineNumber()
        {
            StackTrace saveStackTrace = new StackTrace(true);
            return saveStackTrace.FrameCount > 4 ? saveStackTrace.GetFrame(4).GetFileLineNumber() : -1;
        }

        public sealed class GodotMethodAwaiter<V>
        {
            private string MethodName { get; }
            private Node Instance { get; }
            private object[] Args { get; }

            public GodotMethodAwaiter(Node instance, string methodName, params object[] args)
            {
                Instance = instance;
                MethodName = methodName;
                Args = args;
                if (!Instance.HasMethod(methodName))
                    throw new MissingMethodException($"The method '{methodName}' not exist on loaded scene.");
            }

            public async Task IsEqual(V expected) =>
                await Task.Run(async () => await IsReturnValue((current) => Comparable.IsEqual(current, expected).Valid));

            public async Task IsNull() =>
                await Task.Run(async () => await IsReturnValue((current) => current == null));

            public async Task IsNotNull() =>
                await Task.Run(async () => await IsReturnValue((current) => current != null));

            private delegate bool Comperator(object current);
            private async Task IsReturnValue(Comperator comperator)
            {
                while (true)
                {
                    var current = Instance.Call(MethodName, Args);
                    if (current is GDScriptFunctionState)
                    {
                        object[] result = await Instance.ToSignal(current as GDScriptFunctionState, "completed");
                        current = result[0];
                    }
                    if (comperator(current))
                        return;
                }
            }
        }

        public static async Task AwaitSignal(this Godot.Node node, string signal, params object[]? expectedArgs)
        {
            while (true)
            {
                object[] signalArgs = await Engine.GetMainLoop().ToSignal(node, signal);
                if (expectedArgs?.Length == 0 || signalArgs.SequenceEqual(expectedArgs))
                    return;
            }
        }
    }
}

namespace GdUnit3.Core
{
    using Godot;
    using Executions;
    using System.Collections.Generic;

    internal sealed class SceneRunner : GdUnit3.ISceneRunner
    {
        private SceneTree SceneTree { get; set; }
        private Node CurrentScene { get; set; }
        private bool Verbose { get; set; }
        private bool SceneAutoFree { get; set; }
        private Vector2 CurrentMousePos { get; set; }
        private double TimeFactor { get; set; }
        private int SavedIterationsPerSecond { get; set; }

        private InputEvent? LastInputEvent { get; set; }
        private ICollection<KeyList> KeyOnPress = new HashSet<KeyList>();
        private ICollection<ButtonList> MouseButtonOnPress = new HashSet<ButtonList>();

        public SceneRunner(string resourcePath, bool autoFree = false, bool verbose = false)
        {
            Verbose = verbose;
            SceneAutoFree = autoFree;
            ExecutionContext.RegisterDisposable(this);
            SceneTree = (SceneTree)Godot.Engine.GetMainLoop();
            CurrentScene = ((PackedScene)Godot.ResourceLoader.Load(resourcePath)).Instance();
            SceneTree.Root.AddChild(CurrentScene);
            CurrentMousePos = default;
            SavedIterationsPerSecond = (int)ProjectSettings.GetSetting("physics/common/physics_fps");
            SetTimeFactor(1.0);
        }

        private void ResetInputToDefault()
        {
            // reset all mouse button to inital state if need
            foreach (ButtonList button in MouseButtonOnPress.ToList())
            {
                if (Input.IsMouseButtonPressed((int)button))
                    SimulateMouseButtonRelease(button);
            }
            MouseButtonOnPress.Clear();

            foreach (KeyList key in KeyOnPress.ToList())
            {
                if (Input.IsKeyPressed((int)key))
                    SimulateKeyRelease(key);
            }
            KeyOnPress.Clear();
            Input.FlushBufferedEvents();
        }


        /// <summary>
        /// copy over current active modifiers
        /// </summary>
        /// <param name="inputEvent"></param>
        private void ApplyInputModifiers(InputEventWithModifiers inputEvent)
        {
            if (LastInputEvent is InputEventWithModifiers lastInputEvent)
            {
                inputEvent.Meta = inputEvent.Meta || lastInputEvent.Meta;
                inputEvent.Alt = inputEvent.Alt || lastInputEvent.Alt;
                inputEvent.Shift = inputEvent.Shift || lastInputEvent.Shift;
                inputEvent.Control = inputEvent.Control || lastInputEvent.Control;
            }
        }

        /// <summary>
        /// copy over current active mouse mask and combine with curren mask
        /// </summary>
        /// <param name="inputEvent"></param>
        private void ApplyInputMouseMask(InputEvent inputEvent)
        {
            // first apply last mask
            if (LastInputEvent is InputEventMouse lastInputEvent && inputEvent is InputEventMouse ie)
                ie.ButtonMask |= lastInputEvent.ButtonMask;
            if (inputEvent is InputEventMouseButton inputEventMouseButton)
            {
                ButtonList button = (ButtonList)Enum.ToObject(typeof(ButtonList), inputEventMouseButton.ButtonIndex);
                int mask = toMouseButtonMask(button);
                if (inputEventMouseButton.IsPressed())
                    inputEventMouseButton.ButtonMask |= mask;
                else
                    inputEventMouseButton.ButtonMask ^= mask;
            }
        }

        internal static int toMouseButtonMask(ButtonList button)
        {
            int button_mask = 1 << ((int)button - 1);
            return button_mask;
        }


        /// <summary>
        /// copy over last mouse position if need
        /// </summary>
        /// <param name="inputEvent"></param>
        private void ApplyInputMousePosition(InputEvent inputEvent)
        {
            if (LastInputEvent is InputEventMouse lastInputEvent && inputEvent is InputEventMouseButton ie)
                ie.Position = lastInputEvent.Position;
        }

        /// <summary>
        /// for handling read https://docs.godotengine.org/en/stable/tutorials/inputs/inputevent.html?highlight=inputevent#how-does-it-work
        /// </summary>
        /// <param name="inputEvent"></param>
        /// <returns></returns>
        private ISceneRunner HandleInputEvent(InputEvent inputEvent)
        {
            if (inputEvent is InputEventMouse ie)
                Input.WarpMousePosition(ie.Position);
            Input.ParseInputEvent(inputEvent);
            Input.FlushBufferedEvents();

            if (Godot.Object.IsInstanceValid(CurrentScene))
            {
                Print($"	process event {CurrentScene} ({SceneName()}) <- {inputEvent.AsText()}");
                if (CurrentScene.HasMethod("_gui_input"))
                    CurrentScene.Call("_gui_input", inputEvent);
                if (CurrentScene.HasMethod("_unhandled_input"))
                    CurrentScene.Call("_unhandled_input", inputEvent);
                CurrentScene.GetViewport().SetInputAsHandled();
            }
            // save last input event needs to be merged with next InputEventMouseButton
            LastInputEvent = inputEvent;
            return this;
        }

        public ISceneRunner SimulateKeyPress(KeyList keyCode, bool shiftPressed = false, bool controlPressed = false)
        {
            PrintCurrentFocus();
            var inputEvent = new InputEventKey();
            inputEvent.Pressed = true;
            inputEvent.Scancode = ((uint)keyCode);
            inputEvent.PhysicalScancode = ((uint)keyCode);
            inputEvent.Alt = keyCode == KeyList.Alt;
            inputEvent.Shift = shiftPressed || keyCode == KeyList.Shift;
            inputEvent.Control = controlPressed || keyCode == KeyList.Control;
            ApplyInputModifiers(inputEvent);
            KeyOnPress.Add(keyCode);
            return HandleInputEvent(inputEvent);
        }

        public ISceneRunner SimulateKeyPressed(KeyList keyCode, bool shift = false, bool control = false)
        {
            SimulateKeyPress(keyCode, shift, control);
            SimulateKeyRelease(keyCode, shift, control);
            return this;
        }

        public ISceneRunner SimulateKeyRelease(KeyList keyCode, bool shiftPressed = false, bool controlPressed = false)
        {
            PrintCurrentFocus();
            var inputEvent = new InputEventKey();
            inputEvent.Pressed = false;
            inputEvent.Scancode = ((uint)keyCode);
            inputEvent.PhysicalScancode = ((uint)keyCode);
            inputEvent.Alt = keyCode == KeyList.Alt;
            inputEvent.Shift = shiftPressed || keyCode == KeyList.Shift;
            inputEvent.Control = controlPressed || keyCode == KeyList.Control;
            ApplyInputModifiers(inputEvent);
            KeyOnPress.Remove(keyCode);
            return HandleInputEvent(inputEvent);
        }

        public ISceneRunner SimulateMouseButtonPressed(ButtonList buttonIndex, bool doubleClick = false)
        {
            SimulateMouseButtonPress(buttonIndex, doubleClick);
            SimulateMouseButtonRelease(buttonIndex);
            return this;
        }

        public ISceneRunner SimulateMouseButtonPress(ButtonList buttonIndex, bool doubleClick = false)
        {
            PrintCurrentFocus();
            var inputEvent = new InputEventMouseButton();
            inputEvent.ButtonIndex = (int)buttonIndex;
            inputEvent.Pressed = true;
            inputEvent.Doubleclick = doubleClick;

            MouseButtonOnPress.Add(buttonIndex);
            ApplyInputMousePosition(inputEvent);
            ApplyInputMouseMask(inputEvent);
            ApplyInputModifiers(inputEvent);
            return HandleInputEvent(inputEvent);
        }

        public ISceneRunner SimulateMouseButtonRelease(ButtonList buttonIndex)
        {
            var inputEvent = new InputEventMouseButton();
            inputEvent.ButtonIndex = (int)buttonIndex;
            inputEvent.Pressed = false;

            MouseButtonOnPress.Remove(buttonIndex);
            ApplyInputMousePosition(inputEvent);
            ApplyInputMouseMask(inputEvent);
            ApplyInputModifiers(inputEvent);
            return HandleInputEvent(inputEvent);
        }

        public ISceneRunner SetMousePos(Vector2 position)
        {
            var inputEvent = new InputEventMouseMotion();
            inputEvent.Position = position;
            inputEvent.GlobalPosition = GetGlobalMousePosition();
            ApplyInputModifiers(inputEvent);
            return HandleInputEvent(inputEvent);
        }

        public Vector2 GetMousePosition()
        {
            if (LastInputEvent is InputEventMouse me)
                return me.Position;
            return CurrentScene.GetViewport().GetMousePosition();
        }

        public Vector2 GetGlobalMousePosition() =>
            SceneTree.Root.GetMousePosition();

        public ISceneRunner SimulateMouseMove(Vector2 position)
        {
            var inputEvent = new InputEventMouseMotion();
            inputEvent.Position = position;
            inputEvent.Relative = position - GetMousePosition();
            ApplyInputMouseMask(inputEvent);
            ApplyInputModifiers(inputEvent);
            return HandleInputEvent(inputEvent);
        }

        public async Task SimulateMouseMoveRelative(Vector2 relative, Vector2 speed = default)
        {
            if (LastInputEvent is InputEventMouse lastInputEvent)
            {
                var current_pos = lastInputEvent.Position;
                var final_pos = current_pos + relative;
                double delta_milli = speed.x * 0.1;
                var t = 0.0;

                while (!current_pos.IsEqualApprox(final_pos))
                {
                    t += delta_milli * speed.x;
                    SimulateMouseMove(current_pos);
                    await AwaitMillis((uint)(delta_milli * 1000));
                    current_pos = current_pos.LinearInterpolate(final_pos, (float)t);
                }
                SimulateMouseMove(final_pos);
                await SimulateFrames(10);
            }
        }

        public GdUnit3.ISceneRunner SetTimeFactor(double timeFactor = 1.0)
        {
            TimeFactor = Math.Min(9.0, timeFactor);
            ActivateTimeFactor();

            Print("set time factor: {0}", TimeFactor);
            Print("set physics iterations_per_second: {0}", SavedIterationsPerSecond * TimeFactor);
            return this;
        }

        public async Task SimulateFrames(uint frames, uint deltaPeerFrame)
        {
            for (int frame = 0; frame < frames; frame++)
                await AwaitMillis(deltaPeerFrame);
        }

        public async Task SimulateFrames(uint frames)
        {
            var timeShiftFrames = Math.Max(1, frames / TimeFactor);
            for (int frame = 0; frame < timeShiftFrames; frame++)
                await SceneTree.ToSignal(SceneTree, "idle_frame");
        }

        private void ActivateTimeFactor()
        {
            Engine.TimeScale = (float)TimeFactor;
            Engine.IterationsPerSecond = (int)(SavedIterationsPerSecond * TimeFactor);
        }

        private void DeactivateTimeFactor()
        {
            Engine.TimeScale = 1;
            Engine.IterationsPerSecond = SavedIterationsPerSecond;
        }

        private void Print(string message, params object[] args)
        {
            if (Verbose)
                Console.WriteLine(String.Format(message, args));
        }

        private void PrintCurrentFocus()
        {
            if (!Verbose)
                return;
            var focusedNode = (CurrentScene as Control)?.GetFocusOwner();

            if (focusedNode != null)
                Console.WriteLine("	focus on {0}", focusedNode);
            else
                Console.WriteLine("	no focus set");
        }

        private string SceneName()
        {
            Script? sceneScript = (Script?)CurrentScene.GetScript();

            if (!(sceneScript is Script))
                return CurrentScene.Name;
            if (!CurrentScene.Name.BeginsWith("@"))
                return CurrentScene.Name;

            return sceneScript.ResourceName.BaseName();
        }

        public Node Scene() => CurrentScene;

        public GdUnitAwaiter.GodotMethodAwaiter<V> AwaitMethod<V>(string methodName) =>
            new GdUnitAwaiter.GodotMethodAwaiter<V>(CurrentScene, methodName);

        public async Task AwaitIdleFrame() =>
            await SceneTree.ToSignal(SceneTree, "idle_frame");

        public async Task AwaitMillis(uint timeMillis)
        {
            using (var tokenSource = new CancellationTokenSource())
            {
                await Task.Delay(System.TimeSpan.FromMilliseconds(timeMillis), tokenSource.Token);
            }
        }

        public async Task AwaitSignal(string signal, params object[] args) =>
            await GdUnitAwaiter.AwaitSignal(CurrentScene, signal, args);

        public object Invoke(string name, params object[] args)
        {
            if (!CurrentScene.HasMethod(name))
                throw new MissingMethodException($"The method '{name}' not exist on loaded scene.");
            return CurrentScene.Call(name, args);
        }

        public T GetProperty<T>(string name)
        {
            var property = CurrentScene.Get(name);
            if (property != null)
            {
                return (T)property;
            }
            throw new MissingFieldException($"The property '{name}' not exist on loaded scene.");
        }

        public Node FindNode(string name, bool recursive = true) => CurrentScene.FindNode(name, recursive, false);

        public void MoveWindowToForeground()
        {
            OS.WindowMaximized = true;
            OS.WindowMinimized = false;
            OS.CenterWindow();
            OS.MoveWindowToForeground();
        }

        internal bool disposed = false;
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            DeactivateTimeFactor();
            ResetInputToDefault();
            OS.WindowMaximized = false;
            OS.WindowMinimized = true;
            SceneTree.Root.RemoveChild(CurrentScene);
            if (CurrentScene != null)
            {
                //SceneTree.Root.RemoveChild(CurrentScene);
                if (SceneAutoFree)
                    CurrentScene.Free();
            }
            disposed = true;
            // we hide the scene/main window after runner is finished 
            OS.WindowMaximized = false;
            OS.WindowMinimized = true;
        }

    }
}
