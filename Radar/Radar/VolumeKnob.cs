using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Radar
{
    /// <summary>
    /// The keyboard volume knob (VK_VOLUME_UP / VK_VOLUME_DOWN) as an input of one window.
    ///
    /// Windows changes the system volume before the keys reach the window, so marking
    /// them handled in WPF is not enough. A low-level keyboard hook sees them first:
    /// while the window is in the foreground they are taken out of the input stream and
    /// raised as Turned, otherwise they pass on and the knob controls the volume as usual.
    /// </summary>
    internal sealed class VolumeKnob : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_VOLUME_DOWN = 0xAE;
        private const int VK_VOLUME_UP = 0xAF;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private readonly Window window;
        private readonly LowLevelKeyboardProc proc;     // kept alive, the hook calls it from native code
        private IntPtr hook;

        /// <summary>The knob was turned while the window was active: +1 = up, -1 = down.</summary>
        public event Action<int> Turned;

        /// <summary>Installs the hook. The window must already have its handle (SourceInitialized).</summary>
        public VolumeKnob(Window window)
        {
            this.window = window;
            proc = HookProc;

            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(module.ModuleName), 0);
            }
            if (hook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }

        // Runs on the UI thread (the thread that installed the hook), must return quickly:
        // Windows drops a hook that keeps the keyboard waiting too long
        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var key = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                bool volumeKey = key.vkCode == VK_VOLUME_UP || key.vkCode == VK_VOLUME_DOWN;

                if (volumeKey && GetForegroundWindow() == new WindowInteropHelper(window).Handle)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        int direction = key.vkCode == VK_VOLUME_UP ? 1 : -1;
                        window.Dispatcher.BeginInvoke(new Action(() => Turned?.Invoke(direction)));
                    }
                    return (IntPtr)1;   // key down and key up both stay away from the system volume
                }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }
    }
}
