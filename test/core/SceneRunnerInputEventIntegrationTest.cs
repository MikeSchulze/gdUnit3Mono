using System;
using System.Threading.Tasks;

namespace GdUnit3.Tests
{
    using System.Linq;
    using Godot;
    using static Assertions;

    [TestSuite]
    class SceneRunnerInputEventIntegrationTest
    {
#nullable disable
        private ISceneRunner SceneRunner;
#nullable enable

        [Before]
        public void Setup()
        {
        }

        [BeforeTest]
        public void BeforeTest()
        {
            SceneRunner = ISceneRunner.Load("res://test/core/resources/scenes/TestSceneGDScript.tscn", true);
            AssertInitalMouseState();
            AssertInitalKeyState();
            // we need to maximize the view, a minimized view cannot handle mouse events see (https://github.com/godotengine/godot/issues/73461)
            SceneRunner.MoveWindowToForeground();
        }

        private void AssertInitalMouseState()
        {
            foreach (ButtonList button in Enum.GetValues(typeof(ButtonList)))
            {
                AssertThat(Input.IsMouseButtonPressed((int)(int)button))
                    .OverrideFailureMessage($"Expect ButtonList {button} is not 'IsMouseButtonPressed'")
                    .IsFalse();
            }
            AssertThat((long)Input.GetMouseButtonMask()).IsEqual(0L);
        }

        private void AssertInitalKeyState()
        {
            foreach (KeyList key in Enum.GetValues(typeof(KeyList)))
            {
                AssertThat(Input.IsKeyPressed((int)key))
                    .OverrideFailureMessage($"Expect key {key} is not 'IsKeyPressed'")
                    .IsFalse();
                AssertThat(Input.IsPhysicalKeyPressed((int)key))
                    .OverrideFailureMessage($"Expect key {key} is not 'IsPhysicalKeyPressed'")
                    .IsFalse();
            }
        }

        private Vector2 ActualMousePos() => SceneRunner.Scene().GetViewport().GetMousePosition();


        [TestCase]
        public void ToMouseButtonMask()
        {
            AssertThat(GdUnit3.Core.SceneRunner.toMouseButtonMask(ButtonList.Left)).IsEqual((int)ButtonList.MaskLeft);
            AssertThat(GdUnit3.Core.SceneRunner.toMouseButtonMask(ButtonList.Middle)).IsEqual((int)ButtonList.MaskMiddle);
            AssertThat(GdUnit3.Core.SceneRunner.toMouseButtonMask(ButtonList.Right)).IsEqual((int)ButtonList.MaskRight);
            AssertThat(GdUnit3.Core.SceneRunner.toMouseButtonMask(ButtonList.WheelUp)).IsEqual(8);
            AssertThat(GdUnit3.Core.SceneRunner.toMouseButtonMask(ButtonList.WheelDown)).IsEqual(16);
            AssertThat(GdUnit3.Core.SceneRunner.toMouseButtonMask(ButtonList.WheelLeft)).IsEqual(32);
            AssertThat(GdUnit3.Core.SceneRunner.toMouseButtonMask(ButtonList.WheelRight)).IsEqual(64);
            AssertThat(GdUnit3.Core.SceneRunner.toMouseButtonMask(ButtonList.Xbutton1)).IsEqual((int)ButtonList.MaskXbutton1);
            AssertThat(GdUnit3.Core.SceneRunner.toMouseButtonMask(ButtonList.Xbutton2)).IsEqual((int)ButtonList.MaskXbutton2);
        }

        [TestCase]
        public async Task ResetToInitalStateOnRelease()
        {
            // move mouse out of button range to avoid scene button interactons
            SceneRunner.SetMousePos(new Vector2(400, 400));
            // simulate mouse buttons and key press but we never released it
            SceneRunner.SimulateMouseButtonPress(ButtonList.Left);
            SceneRunner.SimulateMouseButtonPress(ButtonList.Right);
            SceneRunner.SimulateMouseButtonPress(ButtonList.Middle);
            SceneRunner.SimulateKeyPress(KeyList.Key0);
            SceneRunner.SimulateKeyPress(KeyList.X);
            await SceneRunner.AwaitIdleFrame();

            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Left)).IsTrue();
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Right)).IsTrue();
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Middle)).IsTrue();
            AssertThat(Input.IsKeyPressed((int)KeyList.Key0)).IsTrue();
            AssertThat(Input.IsPhysicalKeyPressed((int)KeyList.Key0)).IsTrue();
            AssertThat(Input.IsKeyPressed((int)KeyList.X)).IsTrue();
            AssertThat(Input.IsPhysicalKeyPressed((int)KeyList.X)).IsTrue();

            // unreference the scene SceneRunner to enforce reset to initial Input state
            SceneRunner.Dispose();
            await SceneRunner.AwaitIdleFrame();

            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Left)).IsFalse();
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Right)).IsFalse();
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Middle)).IsFalse();
            AssertThat(Input.IsKeyPressed((int)KeyList.Key0)).IsFalse();
            AssertThat(Input.IsPhysicalKeyPressed((int)KeyList.Key0)).IsFalse();
            AssertThat(Input.IsKeyPressed((int)KeyList.X)).IsFalse();
            AssertThat(Input.IsPhysicalKeyPressed((int)KeyList.X)).IsFalse();
        }

        [TestCase]
        public async Task SimulateKeyPress()
        {
            KeyList[] keys = { KeyList.A, KeyList.D, KeyList.X, KeyList.Key0 };

            foreach (KeyList key in keys)
            {
                SceneRunner.SimulateKeyPress(key);
                await SceneRunner.AwaitIdleFrame();

                var eventKey = new InputEventKey();
                eventKey.Scancode = (uint)key;
                eventKey.PhysicalScancode = (uint)key;
                eventKey.Pressed = true;
                //Verify(_scene_spy, 1)._input(eventKey);
                AssertThat(Input.IsKeyPressed((int)key)).IsTrue();
            }

            AssertThat(Input.IsKeyPressed((int)KeyList.A)).IsTrue();
            AssertThat(Input.IsKeyPressed((int)KeyList.D)).IsTrue();
            AssertThat(Input.IsKeyPressed((int)KeyList.X)).IsTrue();
            AssertThat(Input.IsKeyPressed((int)KeyList.Key0)).IsTrue();

            AssertThat(Input.IsKeyPressed((int)KeyList.B)).IsFalse();
            AssertThat(Input.IsKeyPressed((int)KeyList.G)).IsFalse();
            AssertThat(Input.IsKeyPressed((int)KeyList.Z)).IsFalse();
            AssertThat(Input.IsKeyPressed((int)KeyList.Key1)).IsFalse();
        }

        [TestCase]
        public async Task SmulateKeyPressWithModifiers()
        {
            // press shift key + A
            SceneRunner
                .SimulateKeyPress(KeyList.Shift)
                .SimulateKeyPress(KeyList.A);
            await SceneRunner.AwaitIdleFrame();

            // results in two events, first is the shift key is press
            var eventKey = new InputEventKey();
            eventKey.Scancode = (int)KeyList.Shift;
            eventKey.PhysicalScancode = (int)KeyList.Shift;
            eventKey.Pressed = true;
            eventKey.Shift = true;
            //verify(_scene_spy, 1)._input(mouseEvent)

            // second is the comnbination of current press shift and key A
            eventKey = new InputEventKey();
            eventKey.Scancode = (int)KeyList.A;
            eventKey.PhysicalScancode = (int)KeyList.A;
            eventKey.Pressed = true;
            eventKey.Shift = true;
            //verify(_scene_spy, 1)._input(mouseEvent)
            AssertThat(Input.IsKeyPressed((int)KeyList.Shift)).IsTrue();
            AssertThat(Input.IsKeyPressed((int)KeyList.A)).IsTrue();
        }

        [TestCase]
        public async Task SimulateManyKeysPress()
        {
            //press and hold keys W and Z
            SceneRunner
                .SimulateKeyPress(KeyList.W)
                .SimulateKeyPress(KeyList.Z);
            await SceneRunner.AwaitIdleFrame();

            AssertThat(Input.IsKeyPressed((int)KeyList.W)).IsTrue();
            AssertThat(Input.IsPhysicalKeyPressed((int)KeyList.W)).IsTrue();
            AssertThat(Input.IsKeyPressed((int)KeyList.Z)).IsTrue();
            AssertThat(Input.IsPhysicalKeyPressed((int)KeyList.Z)).IsTrue();

            // now release key w
            SceneRunner.SimulateKeyRelease(KeyList.W);
            await SceneRunner.AwaitIdleFrame();

            AssertThat(Input.IsKeyPressed((int)KeyList.W)).IsFalse();
            AssertThat(Input.IsPhysicalKeyPressed((int)KeyList.W)).IsFalse();
            AssertThat(Input.IsKeyPressed((int)KeyList.Z)).IsTrue();
            AssertThat(Input.IsPhysicalKeyPressed((int)KeyList.Z)).IsTrue(); ;
        }

        [TestCase]
        public async Task SimulateSetMousePos()
        {
            // save current global mouse pos
            var gmp = SceneRunner.GetGlobalMousePosition();
            // set mouse to pos 100, 100
            SceneRunner.SetMousePos(new Vector2(100, 100));
            await SceneRunner.SimulateFrames(10);

            AssertThat(ActualMousePos()).IsEqual(new Vector2(100, 100));

            var mouseEvent = new InputEventMouseMotion();
            mouseEvent.Position = new Vector2(100, 100);
            mouseEvent.GlobalPosition = gmp;
            //verify(_scene_spy, 1)._input(mouseEvent)

            // set mouse to pos 800, 400
            gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SetMousePos(new Vector2(800, 400));
            await SceneRunner.SimulateFrames(10);

            AssertThat(ActualMousePos()).IsEqual(new Vector2(800, 400));

            mouseEvent = new InputEventMouseMotion();
            mouseEvent.Position = new Vector2(800, 400);
            mouseEvent.GlobalPosition = gmp;
            //verify(_scene_spy, 1)._input(mouseEvent)

            // and again back to 100,100
            gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SetMousePos(new Vector2(100, 100));
            await SceneRunner.SimulateFrames(10);

            AssertThat(ActualMousePos()).IsEqual(new Vector2(100, 100));

            mouseEvent = new InputEventMouseMotion();
            mouseEvent.Position = new Vector2(100, 100);
            mouseEvent.GlobalPosition = gmp;
            //verify(_scene_spy, 1)._input(mouseEvent)
        }

        [TestCase]
        public async Task SimulateSetMousePosWithModifiers()
        {
            var isAlt = false;
            var isControl = false;
            var isShift = false;

            KeyList[] modifiers = { KeyList.Shift, KeyList.Control, KeyList.Alt };
            ButtonList[] buttons = { ButtonList.Left, ButtonList.Middle, ButtonList.Right };

            foreach (KeyList modifier in modifiers)
            {
                isAlt = isAlt || KeyList.Alt == modifier;
                isControl = isControl || KeyList.Control == modifier;
                isShift = isShift || KeyList.Shift == modifier;

                foreach (ButtonList mouse_button in buttons)
                {
                    // simulate press shift, set mouse pos and final press mouse button
                    var gmp = SceneRunner.GetGlobalMousePosition();

                    SceneRunner.SimulateKeyPress(modifier);
                    SceneRunner.SetMousePos(Vector2.Zero);
                    SceneRunner.SimulateMouseButtonPress(mouse_button);
                    await SceneRunner.SimulateFrames(10);

                    var mouseEvent = new InputEventMouseButton();
                    mouseEvent.Position = Vector2.Zero;
                    mouseEvent.GlobalPosition = gmp;
                    mouseEvent.Alt = isAlt;
                    mouseEvent.Control = isControl;
                    mouseEvent.Shift = isShift;
                    mouseEvent.Pressed = true;
                    mouseEvent.ButtonIndex = (int)mouse_button;
                    mouseEvent.ButtonMask = GdUnit3.Core.SceneRunner.toMouseButtonMask(mouse_button);
                    //verify(_scene_spy, 1)._input(mouseEvent)
                    AssertThat(ActualMousePos()).IsEqual(Vector2.Zero);
                    AssertThat(Input.IsMouseButtonPressed((int)mouse_button)).IsTrue();
                    AssertThat(Input.GetMouseButtonMask()).IsEqual(mouseEvent.ButtonMask);

                    // finally release it
                    SceneRunner.SimulateMouseButtonRelease(mouse_button);
                    await SceneRunner.AwaitIdleFrame();
                    AssertThat(Input.IsMouseButtonPressed((int)mouse_button)).IsFalse();
                    AssertThat(Input.GetMouseButtonMask()).IsEqual(0);
                }
            }
        }

        [TestCase]
        public async Task SimulateMouseMove()
        {
            var gmp = SceneRunner.GetGlobalMousePosition();

            SceneRunner.SimulateMouseMove(new Vector2(400, 100));
            await SceneRunner.SimulateFrames(10);

            AssertThat(ActualMousePos()).IsEqual(new Vector2(400, 100));
            var mouseEvent = new InputEventMouseMotion();
            mouseEvent.Position = new Vector2(400, 100);
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Relative = new Vector2(400, 100) - new Vector2(10, 10);
            //verify(_scene_spy, 1)._input(mouseEvent)

            // move mouse to next pos
            gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SimulateMouseMove(new Vector2(55, 42));
            await SceneRunner.SimulateFrames(10);

            AssertThat(ActualMousePos()).IsEqual(new Vector2(55, 42));
            mouseEvent = new InputEventMouseMotion();
            mouseEvent.Position = new Vector2(55, 42);
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Relative = new Vector2(55, 42) - new Vector2(400, 100);
            //verify(_scene_spy, 1)._input(mouseEvent)
        }

        [TestCase(Timeout = 4000)]
        public async Task SimulateMouseMoveRelative()
        {
            SceneRunner.SimulateMouseMove(new Vector2(10, 10));
            await SceneRunner.SimulateFrames(10);
            // initial pos
            AssertThat(ActualMousePos()).IsEqual(new Vector2(10, 10));

            await SceneRunner.SimulateMouseMoveRelative(new Vector2(900, 400), new Vector2(.2f, 1));
            // final pos
            AssertThat(ActualMousePos()).IsEqual(new Vector2(910, 410));
        }

        [TestCase]
        public async Task SimulateMouseButtonPressLeft()
        {
            // simulate mouse button press and hold
            var gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SimulateMouseButtonPress(ButtonList.Left);
            await SceneRunner.AwaitIdleFrame();

            var mouseEvent = new InputEventMouseButton();
            mouseEvent.Position = Vector2.Zero;
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Pressed = true;
            mouseEvent.Doubleclick = false;
            mouseEvent.ButtonIndex = (int)ButtonList.Left;
            mouseEvent.ButtonMask = (int)ButtonList.MaskLeft;
            //verify(_scene_spy, 1)._input(mouseEvent)
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Left)).IsTrue();
            AssertThat(Input.GetMouseButtonMask()).IsEqual((int)ButtonList.MaskLeft);
        }

        [TestCase]
        public async Task SimulateMouseButtonPressLeftDoubleclick()
        {
            // simulate mouse button press double_click
            var gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SimulateMouseButtonPress(ButtonList.Left, true);
            await SceneRunner.AwaitIdleFrame();

            var mouseEvent = new InputEventMouseButton();
            mouseEvent.Position = Vector2.Zero;
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Pressed = true;
            mouseEvent.Doubleclick = true;
            mouseEvent.ButtonIndex = (int)ButtonList.Left;
            mouseEvent.ButtonMask = (int)ButtonList.MaskLeft;
            //verify(_scene_spy, 1)._input(mouseEvent)
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Left)).IsTrue();
            AssertThat(Input.GetMouseButtonMask()).IsEqual((int)ButtonList.MaskLeft);
        }

        [TestCase]
        public async Task SimulateMouseButtonPressRight()
        {
            // simulate mouse button press and hold
            var gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SimulateMouseButtonPress(ButtonList.Right);
            await SceneRunner.AwaitIdleFrame();

            var mouseEvent = new InputEventMouseButton();
            mouseEvent.Position = Vector2.Zero;
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Pressed = true;
            mouseEvent.Doubleclick = false;
            mouseEvent.ButtonIndex = (int)ButtonList.Right;
            mouseEvent.ButtonMask = (int)ButtonList.MaskRight;
            //verify(_scene_spy, 1)._input(mouseEvent)
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Right)).IsTrue();
            AssertThat(Input.GetMouseButtonMask()).IsEqual((int)ButtonList.MaskRight);
        }

        [TestCase]
        public async Task SimulateMouseButtonPressRightDoubleclick()
        {
            // simulate mouse button press double_click
            var gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SimulateMouseButtonPress(ButtonList.Right, true);
            await SceneRunner.AwaitIdleFrame();

            var mouseEvent = new InputEventMouseButton();
            mouseEvent.Position = Vector2.Zero;
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Pressed = true;
            mouseEvent.Doubleclick = true;
            mouseEvent.ButtonIndex = (int)ButtonList.Right;
            mouseEvent.ButtonMask = (int)ButtonList.MaskRight;
            //verify(_scene_spy, 1)._input(mouseEvent)
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Right)).IsTrue();
            AssertThat(Input.GetMouseButtonMask()).IsEqual((int)ButtonList.MaskRight);
        }

        [TestCase]
        public async Task SimulateMouseButtonPressLeftAndRight()
        {
            // simulate mouse button press left+right
            var gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SimulateMouseButtonPress(ButtonList.Left);
            SceneRunner.SimulateMouseButtonPress(ButtonList.Right);
            await SceneRunner.AwaitIdleFrame();


            // results in two events, first is left mouse button
            var mouseEvent = new InputEventMouseButton();
            mouseEvent.Position = Vector2.Zero;
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Pressed = true;
            mouseEvent.ButtonIndex = (int)ButtonList.Left;
            mouseEvent.ButtonMask = (int)ButtonList.MaskLeft;
            //verify(_scene_spy, 1)._input(mouseEvent)

            // second is left+right and combined mask
            mouseEvent = new InputEventMouseButton();
            mouseEvent.Position = Vector2.Zero;
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Pressed = true;
            mouseEvent.ButtonIndex = (int)ButtonList.Right;
            mouseEvent.ButtonMask = (int)ButtonList.MaskLeft | (int)ButtonList.MaskRight;
            //verify(_scene_spy, 1)._input(mouseEvent)
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Left)).IsTrue();
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Right)).IsTrue();
            AssertThat(Input.GetMouseButtonMask()).IsEqual((int)ButtonList.MaskLeft | (int)ButtonList.MaskRight);
        }

        [TestCase]
        public async Task SimulateMouseButtonPressLeftAndRightAndRelease()
        {
            // simulate mouse button press left+right
            var gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SimulateMouseButtonPress(ButtonList.Left);
            SceneRunner.SimulateMouseButtonPress(ButtonList.Right);
            await SceneRunner.AwaitIdleFrame();

            // will results into two events
            // first for left mouse button
            var mouseEvent = new InputEventMouseButton();
            mouseEvent.Position = Vector2.Zero;
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Pressed = true;
            mouseEvent.ButtonIndex = (int)ButtonList.Left;
            mouseEvent.ButtonMask = (int)ButtonList.MaskLeft;
            //verify(_scene_spy, 1)._input(mouseEvent)

            // second is left+right and combined mask
            mouseEvent = new InputEventMouseButton();
            mouseEvent.Position = Vector2.Zero;
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Pressed = true;
            mouseEvent.ButtonIndex = (int)ButtonList.Right;
            mouseEvent.ButtonMask = (int)ButtonList.MaskLeft | (int)ButtonList.MaskRight;
            //verify(_scene_spy, 1)._input(mouseEvent)
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Left)).IsTrue();
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Right)).IsTrue();
            AssertThat(Input.GetMouseButtonMask()).IsEqual((int)ButtonList.MaskLeft | (int)ButtonList.MaskRight);

            // now release the right button
            gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SimulateMouseButtonPressed(ButtonList.Right);
            await SceneRunner.AwaitIdleFrame();
            // will result in right button press false but stay with mask for left pressed
            mouseEvent = new InputEventMouseButton();
            mouseEvent.Position = Vector2.Zero;
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Pressed = false;
            mouseEvent.ButtonIndex = (int)ButtonList.Right;
            mouseEvent.ButtonMask = (int)ButtonList.MaskLeft;
            //verify(_scene_spy, 1)._input(mouseEvent)
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Left)).IsTrue();
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Right)).IsFalse();
            AssertThat(Input.GetMouseButtonMask()).IsEqual((int)ButtonList.MaskLeft);

            // finally relase left button
            gmp = SceneRunner.GetGlobalMousePosition();
            SceneRunner.SimulateMouseButtonPressed(ButtonList.Left);
            await SceneRunner.AwaitIdleFrame();
            // will result in right button press false but stay with mask for left pressed
            mouseEvent = new InputEventMouseButton();
            mouseEvent.Position = Vector2.Zero;
            mouseEvent.GlobalPosition = gmp;
            mouseEvent.Pressed = false;
            mouseEvent.ButtonIndex = (int)ButtonList.Left;
            mouseEvent.ButtonMask = 0;
            //verify(_scene_spy, 1)._input(mouseEvent)
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Left)).IsFalse();
            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Right)).IsFalse();
            AssertThat(Input.GetMouseButtonMask()).IsEqual(0);
        }

        [TestCase]
        public async Task SimulateMouseButtonPressed()
        {
            ButtonList[] buttons = { ButtonList.Left, ButtonList.Middle, ButtonList.Right };
            foreach (var mouse_button in buttons)
            {
                // simulate mouse button press and release
                var gmp = SceneRunner.GetGlobalMousePosition();
                SceneRunner.SimulateMouseButtonPressed(mouse_button);
                await SceneRunner.AwaitIdleFrame();

                // it genrates two events, first for press and second as released
                var mouseEvent = new InputEventMouseButton();
                mouseEvent.Position = Vector2.Zero;
                mouseEvent.GlobalPosition = gmp;
                mouseEvent.Pressed = true;
                mouseEvent.ButtonIndex = (int)mouse_button;
                mouseEvent.ButtonMask = GdUnit3.Core.SceneRunner.toMouseButtonMask(mouse_button);
                //verify(_scene_spy, 1)._input(mouseEvent)


                mouseEvent = new InputEventMouseButton();
                mouseEvent.Position = Vector2.Zero;
                mouseEvent.GlobalPosition = gmp;
                mouseEvent.Pressed = false;
                mouseEvent.ButtonIndex = (int)mouse_button;
                mouseEvent.ButtonMask = 0;
                //verify(_scene_spy, 1)._input(mouseEvent)
                AssertThat(Input.IsMouseButtonPressed((int)mouse_button)).IsFalse();
                AssertThat(Input.GetMouseButtonMask()).IsEqual(0);
                //verify(_scene_spy, 2)._input(any_class(InputEventMouseButton))
                //reset(_scene_spy)
            }
        }

        [TestCase]
        public async Task SimulateMouseButtonPressedDoubleclick()
        {
            ButtonList[] buttons = { ButtonList.Left, ButtonList.Middle, ButtonList.Right };
            foreach (var mouse_button in buttons)
            {
                // simulate mouse button press and release by double_click
                var gmp = SceneRunner.GetGlobalMousePosition();
                SceneRunner.SimulateMouseButtonPressed(mouse_button, true);
                await SceneRunner.AwaitIdleFrame();

                // it genrates two events, first for press and second as released
                var mouseEvent = new InputEventMouseButton();
                mouseEvent.Position = Vector2.Zero;
                mouseEvent.GlobalPosition = gmp;
                mouseEvent.Pressed = true;
                mouseEvent.Doubleclick = true;
                mouseEvent.ButtonIndex = (int)mouse_button;
                mouseEvent.ButtonMask = GdUnit3.Core.SceneRunner.toMouseButtonMask(mouse_button);
                //verify(_scene_spy, 1)._input(mouseEvent)

                mouseEvent = new InputEventMouseButton();
                mouseEvent.Position = Vector2.Zero;
                mouseEvent.GlobalPosition = gmp;
                mouseEvent.Pressed = false;
                mouseEvent.Doubleclick = false;
                mouseEvent.ButtonIndex = (int)mouse_button;
                mouseEvent.ButtonMask = 0;
                //verify(_scene_spy, 1)._input(mouseEvent)

                AssertThat(Input.IsMouseButtonPressed((int)mouse_button)).IsFalse();
                AssertThat(Input.GetMouseButtonMask()).IsEqual(0);
                //verify(_scene_spy, 2)._input(any_class(InputEventMouseButton))
                //reset(_scene_spy)
            }
        }

        [TestCase]
        public async Task SimulateMouseButtonPressAndRelease()
        {
            ButtonList[] buttons = { ButtonList.Left, ButtonList.Middle, ButtonList.Right };
            foreach (var mouse_button in buttons)
            {
                var gmp = SceneRunner.GetGlobalMousePosition();
                // simulate mouse button press and release
                SceneRunner.SimulateMouseButtonPress(mouse_button);
                await SceneRunner.AwaitIdleFrame();

                var mouseEvent = new InputEventMouseButton();
                mouseEvent.Position = Vector2.Zero;
                mouseEvent.GlobalPosition = gmp;
                mouseEvent.Pressed = true;
                mouseEvent.ButtonIndex = (int)mouse_button;
                mouseEvent.ButtonMask = GdUnit3.Core.SceneRunner.toMouseButtonMask(mouse_button);
                //verify(_scene_spy, 1)._input(mouseEvent)
                AssertThat(Input.IsMouseButtonPressed((int)mouse_button)).IsTrue();
                AssertThat(Input.GetMouseButtonMask()).IsEqual(mouseEvent.ButtonMask);

                // now simulate mouse button release
                gmp = SceneRunner.GetGlobalMousePosition();
                SceneRunner.SimulateMouseButtonRelease(mouse_button);
                await SceneRunner.AwaitIdleFrame();

                mouseEvent = new InputEventMouseButton();
                mouseEvent.Position = Vector2.Zero;
                mouseEvent.GlobalPosition = gmp;
                mouseEvent.Pressed = false;
                mouseEvent.ButtonIndex = (int)mouse_button;
                mouseEvent.ButtonMask = 0;
                //verify(_scene_spy, 1)._input(mouseEvent)
                AssertThat(Input.IsMouseButtonPressed((int)mouse_button)).IsFalse();
                AssertThat(Input.GetMouseButtonMask()).IsEqual(mouseEvent.ButtonMask);
            }
        }

        [TestCase]
        public async Task MouseDragAndDrop()
        {
            var DragAndDropSceneRunner = ISceneRunner.Load("res://test/core/resources/scenes/DragAndDrop/DragAndDropTestScene.tscn", true);
            //var spy_scene = spy("res://addons/GdUnit3/test/core/resources/scenes/drag_and_drop/DragAndDropTestScene.tscn")
            //var runner := scene_runner(spy_scene)

            var scene = DragAndDropSceneRunner.Scene();
            TextureRect slot_left = scene.GetNode<TextureRect>(new NodePath($"/root/DragAndDropScene/left/TextureRect"));
            TextureRect slot_right = scene.GetNode<TextureRect>(new NodePath($"/root/DragAndDropScene/right/TextureRect"));

            var save_mouse_pos = DragAndDropSceneRunner.GetMousePosition();
            // set inital mouse pos over the left slot
            var mouse_pos = slot_left.RectGlobalPosition + new Vector2(10, 10);

            DragAndDropSceneRunner.SetMousePos(mouse_pos);
            await DragAndDropSceneRunner.AwaitMillis(1000);
            await SceneRunner.AwaitIdleFrame();

            var mouseEvent = new InputEventMouseMotion();
            mouseEvent.Position = mouse_pos;
            mouseEvent.GlobalPosition = save_mouse_pos;
            //verify(spy_scene, 1)._gui_input(mouseEvent)

            DragAndDropSceneRunner.SimulateMouseButtonPress(ButtonList.Left);
            await SceneRunner.AwaitIdleFrame();

            AssertThat(Input.IsMouseButtonPressed((int)(int)ButtonList.Left)).IsTrue();

            //# start drag&drop to left pannel
            foreach (var i in Enumerable.Range(0, 20))
            {
                DragAndDropSceneRunner.SimulateMouseMove(mouse_pos + new Vector2(i * .5f * i, 0));
                await DragAndDropSceneRunner.AwaitMillis(40);
            }
            DragAndDropSceneRunner.SimulateMouseButtonRelease(ButtonList.Left);
            await SceneRunner.AwaitIdleFrame();

            AssertThat(slot_right.Texture).IsEqual(slot_left.Texture);
        }
    }
}