using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Runs a blocking vehicle request (doCommand, doARM, setParam, setWPCurrent, a mission download...)
    /// started from the UI thread, without freezing the window while it waits for the vehicle.
    /// </summary>
    /// <remarks>
    /// These requests used to run on the UI thread, so the window froze (and Windows marked it
    /// "Not Responding") for the whole retry cycle: up to 8 s for a command, 40 s for an arm and 50 s
    /// for a preflight calibration on a bad link. Here the request runs on the thread pool instead, and
    /// until it finishes all user input to the application is dropped, exactly as it went unanswered
    /// while the UI thread was blocked: no second request, mission transfer, vehicle switch or
    /// disconnect can be started from the UI in the meantime, so nothing new can interleave with it.
    /// Everything else keeps running, so the UI work that background threads post to the UI thread
    /// (the FlightData main loop's BeginInvokes, timers, painting) keeps the HUD and map live.
    ///
    /// Requests are also run one at a time, in the order they were asked for: each reads its replies
    /// off the link itself, so two at once would take each other's acks. One can still be asked for
    /// while another runs (a step of a longer sequence, a connect started by a background thread);
    /// it waits for the one before, with input ignored meanwhile.
    ///
    /// Only mouse button, mouse wheel and keyboard messages are dropped; paint, timer, mouse move
    /// and hover messages pass. The one button release that passes is to the window that held the
    /// mouse capture when input was first blocked, while it still holds it: that press started before
    /// the request (a Ctrl+click on the map starts one on mouse down), and dropping its release would
    /// leave the window dragging. Clicks on the window frame (including the close box) are dropped
    /// too, like when the window was frozen. A close requested from outside the message loop (the
    /// taskbar, the window manager on Mono) is a sent message that no filter can see, and was
    /// queued until the request ended before; now the window can close while a request is still
    /// waiting, and the request is abandoned with the link. Message filters are asked in the order
    /// they were added, so one added before the first request (the gimbal video's key handler) still
    /// sees keys first.
    ///
    /// Call it from the UI thread, and only touch controls before and after the await: the request
    /// itself runs on the thread pool. Called from another thread, it only runs the request on the
    /// thread pool, after any request before it. Prompts belong outside it, where the user can answer them.
    /// Exceptions thrown by the request are rethrown to the caller once input is restored.
    /// </remarks>
    public static class VehicleRequest
    {
        private static readonly InputBlocker blocker = new InputBlocker();
        private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private static int depth;
        private static bool waitCursorWas;
        // the window that held the mouse capture when input was blocked, until it gets its button release
        private static IntPtr captureWindow;

        public static async Task<T> RunAsync<T>(Func<T> request)
        {
            // off the UI thread there is no input to hold back and nothing to keep painting: just
            // run it, still one at a time
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    return await Task.Run(request).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }

            Begin();
            try
            {
                await gate.WaitAsync();
                try
                {
                    return await Task.Run(request);
                }
                finally
                {
                    gate.Release();
                }
            }
            finally
            {
                End();
            }
        }

        public static Task RunAsync(Action request)
        {
            return RunAsync<object>(() =>
            {
                request();
                return null;
            });
        }

        /// <summary>
        /// True while a request started with <see cref="RunAsync{T}"/> is running.
        /// </summary>
        public static bool IsRunning
        {
            get { return depth > 0; }
        }

        // the counters are only touched from the UI thread: RunAsync is called there and its
        // continuation comes back there through the WinForms synchronization context
        private static void Begin()
        {
            if (depth++ == 0)
            {
                captureWindow = GetCaptureWindow();
                Application.AddMessageFilter(blocker);
                try
                {
                    waitCursorWas = Application.UseWaitCursor;
                    Application.UseWaitCursor = true;
                }
                catch
                {
                    // only cosmetic
                }
            }
        }

        private static void End()
        {
            if (--depth == 0)
            {
                Application.RemoveMessageFilter(blocker);
                captureWindow = IntPtr.Zero;
                try
                {
                    Application.UseWaitCursor = waitCursorWas;
                }
                catch
                {
                    // only cosmetic
                }
            }
        }

        private static readonly bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        [DllImport("user32.dll")]
        private static extern IntPtr GetCapture();

        /// <summary>
        /// The window holding the mouse capture: a native window as well as a control on Windows, the
        /// control that says it holds it elsewhere (Mono has no user32).
        /// </summary>
        private static IntPtr GetCaptureWindow()
        {
            try
            {
                if (isWindows)
                    return GetCapture();

                foreach (Form form in Application.OpenForms)
                {
                    var control = FindCapture(form);
                    if (control != null)
                        return control.Handle;
                }
            }
            catch
            {
            }

            return IntPtr.Zero;
        }

        private static Control FindCapture(Control control)
        {
            if (control.IsHandleCreated && control.Capture)
                return control;

            foreach (Control child in control.Controls)
            {
                var found = FindCapture(child);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static bool HoldsCapture(IntPtr hwnd)
        {
            try
            {
                if (isWindows)
                    return GetCapture() == hwnd;

                var control = Control.FromHandle(hwnd);
                return control != null && control.Capture;
            }
            catch
            {
                return false;
            }
        }

        private class InputBlocker : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                switch (m.Msg)
                {
                    // keyboard: WM_KEYDOWN, WM_KEYUP, WM_CHAR, WM_DEADCHAR,
                    // WM_SYSKEYDOWN, WM_SYSKEYUP, WM_SYSCHAR, WM_SYSDEADCHAR
                    case 0x0100:
                    case 0x0101:
                    case 0x0102:
                    case 0x0103:
                    case 0x0104:
                    case 0x0105:
                    case 0x0106:
                    case 0x0107:
                    // mouse buttons in the client area: WM_LBUTTON*, WM_RBUTTON*, WM_MBUTTON* (down, up,
                    // double click), WM_MOUSEWHEEL, WM_XBUTTON*, WM_MOUSEHWHEEL
                    case 0x0201:
                    case 0x0203:
                    case 0x0204:
                    case 0x0206:
                    case 0x0207:
                    case 0x0209:
                    case 0x020A:
                    case 0x020B:
                    case 0x020D:
                    case 0x020E:
                    // mouse buttons on the window frame: WM_NCLBUTTON*, WM_NCRBUTTON*, WM_NCMBUTTON*,
                    // WM_NCXBUTTON*
                    case 0x00A1:
                    case 0x00A2:
                    case 0x00A3:
                    case 0x00A4:
                    case 0x00A5:
                    case 0x00A6:
                    case 0x00A7:
                    case 0x00A8:
                    case 0x00A9:
                    case 0x00AB:
                    case 0x00AC:
                    case 0x00AD:
                        return true;
                    // button releases: WM_LBUTTONUP, WM_RBUTTONUP, WM_MBUTTONUP, WM_XBUTTONUP
                    case 0x0202:
                    case 0x0205:
                    case 0x0208:
                    case 0x020C:
                        if (captureWindow != IntPtr.Zero && m.HWnd == captureWindow && HoldsCapture(m.HWnd))
                        {
                            // only its one release
                            captureWindow = IntPtr.Zero;
                            return false;
                        }

                        return true;
                    default:
                        return false;
                }
            }
        }
    }
}
