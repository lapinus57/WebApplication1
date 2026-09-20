using System;
using System.Runtime.InteropServices;

namespace Client.Helpers
{
    /// <summary>
    /// Adds a force-close command to the standard Windows system menu (title bar,
    /// Alt+Space, and Shift+right-click on the taskbar button).
    /// </summary>
    internal sealed class NativeWindowForceCloseMenu : IDisposable
    {
        private const uint ForceCloseCommand = 0x1FF0;
        private const uint WmSysCommand = 0x0112;
        private const uint WmNcRightButtonUp = 0x00A5;
        private const uint HitTestClose = 20;
        private const uint MfByCommand = 0x00000000;
        private const uint MfSeparator = 0x00000800;
        private const uint MfString = 0x00000000;
        private const uint ScClose = 0xF060;
        private const uint TrackPopupReturnCommand = 0x0100;
        private const uint TrackPopupRightButton = 0x0002;
        private const nuint SubclassId = 1;

        private readonly IntPtr _windowHandle;
        private readonly Action _forceClose;
        private readonly SubclassProc _subclassProc;
        private bool _disposed;

        public NativeWindowForceCloseMenu(IntPtr windowHandle, Action forceClose)
        {
            _windowHandle = windowHandle;
            _forceClose = forceClose;
            _subclassProc = WindowSubclassProc;

            var systemMenu = GetSystemMenu(windowHandle, false);
            if (systemMenu != IntPtr.Zero)
            {
                InsertMenu(systemMenu, ScClose, MfByCommand | MfSeparator, UIntPtr.Zero, null);
                InsertMenu(systemMenu, ScClose, MfByCommand | MfString, (UIntPtr)ForceCloseCommand, "Forcer la fermeture");
            }

            SetWindowSubclass(windowHandle, _subclassProc, SubclassId, UIntPtr.Zero);
        }

        private IntPtr WindowSubclassProc(
            IntPtr windowHandle,
            uint message,
            UIntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
            if (message == WmSysCommand && ((uint)wParam.ToUInt64() & 0xFFF0) == ForceCloseCommand)
            {
                _forceClose();
                return IntPtr.Zero;
            }

            if (message == WmNcRightButtonUp && (uint)wParam.ToUInt64() == HitTestClose)
            {
                ShowSystemMenuAtPointer();
                return IntPtr.Zero;
            }

            return DefSubclassProc(windowHandle, message, wParam, lParam);
        }

        private void ShowSystemMenuAtPointer()
        {
            var systemMenu = GetSystemMenu(_windowHandle, false);
            if (systemMenu == IntPtr.Zero || !GetCursorPos(out var point))
                return;

            var command = TrackPopupMenuEx(
                systemMenu,
                TrackPopupReturnCommand | TrackPopupRightButton,
                point.X,
                point.Y,
                _windowHandle,
                IntPtr.Zero);

            if (command == ForceCloseCommand)
            {
                _forceClose();
            }
            else if (command != 0)
            {
                PostMessage(_windowHandle, WmSysCommand, (UIntPtr)command, IntPtr.Zero);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            RemoveWindowSubclass(_windowHandle, _subclassProc, SubclassId);
        }

        private delegate IntPtr SubclassProc(
            IntPtr windowHandle,
            uint message,
            UIntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr windowHandle, bool revert);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(
            IntPtr menuHandle,
            uint flags,
            int x,
            int y,
            IntPtr windowHandle,
            IntPtr parameters);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(
            IntPtr windowHandle,
            uint message,
            UIntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool InsertMenu(
            IntPtr menuHandle,
            uint position,
            uint flags,
            UIntPtr newItemId,
            string? newItem);

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(
            IntPtr windowHandle,
            SubclassProc subclassProc,
            nuint subclassId,
            UIntPtr referenceData);

        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(
            IntPtr windowHandle,
            SubclassProc subclassProc,
            nuint subclassId);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(
            IntPtr windowHandle,
            uint message,
            UIntPtr wParam,
            IntPtr lParam);
    }
}
