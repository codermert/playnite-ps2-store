using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace BDSoftPS2Store
{
    public enum PadButton
    {
        Up,
        Down,
        Left,
        Right,
        A,
        B,
        X,
        Y,
        LB,
        RB
    }

    // Polls XInput controllers while the store window is active and raises button presses
    // (with key-repeat for directions).
    public class Gamepad : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState14(int userIndex, out XInputState state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState910(int userIndex, out XInputState state);

        private const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008;
        private const ushort ShoulderL = 0x0100, ShoulderR = 0x0200;
        private const ushort ButtonA = 0x1000, ButtonB = 0x2000, ButtonX = 0x4000, ButtonY = 0x8000;
        private const int StickDeadZone = 16000;
        private static readonly TimeSpan FirstRepeat = TimeSpan.FromMilliseconds(380);
        private static readonly TimeSpan NextRepeat = TimeSpan.FromMilliseconds(110);

        private readonly DispatcherTimer timer;
        private readonly Action<PadButton> onPress;
        private readonly Func<bool> isActive;
        private readonly bool[] down = new bool[10];
        private readonly DateTime[] nextRepeat = new DateTime[10];
        private bool useLegacyDll;
        private bool unavailable;

        public Gamepad(Dispatcher dispatcher, Func<bool> isActive, Action<PadButton> onPress)
        {
            this.isActive = isActive;
            this.onPress = onPress;
            timer = new DispatcherTimer(DispatcherPriority.Input, dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += Poll;
        }

        public void Start()
        {
            for (int i = 0; i < down.Length; i++)
            {
                down[i] = true; // ignore buttons that are already held when the store opens
            }
            timer.Start();
        }

        public void Stop()
        {
            timer.Stop();
        }

        public void Dispose()
        {
            timer.Stop();
        }

        private bool TryGetState(out XInputGamepad pad)
        {
            pad = new XInputGamepad();
            if (unavailable)
            {
                return false;
            }
            for (int index = 0; index < 4; index++)
            {
                XInputState state;
                int result;
                try
                {
                    result = useLegacyDll ? XInputGetState910(index, out state) : XInputGetState14(index, out state);
                }
                catch (DllNotFoundException)
                {
                    if (useLegacyDll)
                    {
                        unavailable = true;
                        return false;
                    }
                    useLegacyDll = true;
                    index--;
                    continue;
                }
                if (result == 0)
                {
                    pad = state.Gamepad;
                    return true;
                }
            }
            return false;
        }

        private void Poll(object sender, EventArgs e)
        {
            XInputGamepad pad;
            if (!isActive() || !TryGetState(out pad))
            {
                return;
            }

            ushort b = pad.Buttons;
            Handle(PadButton.Up, (b & DPadUp) != 0 || pad.ThumbLY > StickDeadZone, true);
            Handle(PadButton.Down, (b & DPadDown) != 0 || pad.ThumbLY < -StickDeadZone, true);
            Handle(PadButton.Left, (b & DPadLeft) != 0 || pad.ThumbLX < -StickDeadZone, true);
            Handle(PadButton.Right, (b & DPadRight) != 0 || pad.ThumbLX > StickDeadZone, true);
            Handle(PadButton.A, (b & ButtonA) != 0, false);
            Handle(PadButton.B, (b & ButtonB) != 0, false);
            Handle(PadButton.X, (b & ButtonX) != 0, false);
            Handle(PadButton.Y, (b & ButtonY) != 0, false);
            Handle(PadButton.LB, (b & ShoulderL) != 0, false);
            Handle(PadButton.RB, (b & ShoulderR) != 0, false);
        }

        private void Handle(PadButton button, bool pressed, bool repeat)
        {
            int i = (int)button;
            DateTime now = DateTime.UtcNow;
            if (pressed && !down[i])
            {
                down[i] = true;
                nextRepeat[i] = now + FirstRepeat;
                onPress(button);
            }
            else if (pressed && repeat && now >= nextRepeat[i])
            {
                nextRepeat[i] = now + NextRepeat;
                onPress(button);
            }
            else if (!pressed)
            {
                down[i] = false;
            }
        }
    }
}
