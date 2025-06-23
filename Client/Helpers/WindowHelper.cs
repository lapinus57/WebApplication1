using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Client.Helpers
{
    public static class WindowHelper
    {
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new(-2);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public static void SetTopMost(Window window, bool topMost, bool activate)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            uint flags = SWP_NOMOVE | SWP_NOSIZE | (activate ? 0u : SWP_NOACTIVATE);
            SetWindowPos(hwnd, topMost ? HWND_TOPMOST : HWND_NOTOPMOST,
                0, 0, 0, 0, flags);
        }

        public static bool IsForeground(Window window)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            return GetForegroundWindow() == hwnd;
        }
    }
}
