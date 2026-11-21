using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Microsoft.Win32.SafeHandles;

namespace MissionPlanner.Comms
{
    public class SerialPort : ICommsSerial
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(SerialPort));

        public static Func<SerialPort, string, int, ICommsSerial> DefaultType { get; set; } = (self, portname, baudrate) =>
        {
            return new WinSerialPort();
        };

        public ICommsSerial _baseport;

        public SerialPort()
        {
            _baseport = DefaultType.Invoke(this, null, 0);
        }

        public SerialPort(string portname, int baudrate)
        {
            _baseport = DefaultType.Invoke(this, portname, baudrate);
            PortName = portname;
            BaudRate = baudrate;
        }

        public SerialPort(string portname)
        {
            _baseport = DefaultType.Invoke(this, portname, 0);
            PortName = portname;
        }

        public void Dispose()
        {
            _baseport.Dispose();
        }

        public Stream BaseStream => _baseport.BaseStream;

        public int BaudRate
        {
            get
            {
                if (_baseport == null) return 0; return _baseport.BaudRate; }
            set
            {
                if (_baseport == null)
                    _baseport = DefaultType.Invoke(this, PortName, value);
                if(_baseport == null) // we are trying to set baudrate on a port that has no portname/doesnt exist
                    return;
                _baseport.BaudRate = value;
            }
        }

        public int BytesToRead => _baseport.BytesToRead;

        public int BytesToWrite => _baseport.BytesToWrite;

        public int DataBits
        {
            get => _baseport.DataBits;
            set => _baseport.DataBits = value;
        }

        public bool DtrEnable
        {
            get => _baseport.DtrEnable;
            set => _baseport.DtrEnable = value;
        }

        public bool IsOpen
        {
            get
            {
                if (_baseport == null) return false;
                return _baseport.IsOpen;
            }
        }

        public string PortName
        {
            get
            {
                if (_baseport == null) return "";
                return _baseport.PortName;
            }
            set
            {
                if (_baseport != null)
                    _baseport.PortName = value;
                if (_baseport == null)
                    _baseport = DefaultType.Invoke(this, value, 0);
            }
        }

        public int ReadBufferSize
        {
            get => _baseport.ReadBufferSize;
            set => _baseport.ReadBufferSize = value;
        }

        public int ReadTimeout
        {
            get => _baseport.ReadTimeout;
            set => _baseport.ReadTimeout = value;
        }

        public bool RtsEnable
        {
            get => _baseport.RtsEnable;
            set => _baseport.RtsEnable = value;
        }

        public int WriteBufferSize
        {
            get => _baseport.WriteBufferSize;
            set => _baseport.WriteBufferSize = value;
        }

        public int WriteTimeout
        {
            get => _baseport.WriteTimeout;
            set => _baseport.WriteTimeout = value;
        }

        public void Close()
        {
            _baseport.Close();
        }

        public void DiscardInBuffer()
        {
            _baseport.DiscardInBuffer();
        }

        public void Open()
        {
            if (espFix && _baseport is WinSerialPort)
            {
                ((WinSerialPort)_baseport).espFix = true;
            }
            _baseport.Open();
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return _baseport.Read(buffer, offset, count);
        }

        public int ReadByte()
        {
            return _baseport.ReadByte();
        }

        public int ReadChar()
        {
            return _baseport.ReadChar();
        }

        public string ReadExisting()
        {
            return _baseport.ReadExisting();
        }

        public string ReadLine()
        {
            return _baseport.ReadLine();
        }

        public void Write(string text)
        {
            _baseport.Write(text);
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            _baseport.Write(buffer, offset, count);
        }

        public void WriteLine(string text)
        {
            _baseport.WriteLine(text);
        }

        public void toggleDTR()
        {
            _baseport.toggleDTR();
        }


        public bool espFix = false;

        private static readonly object locker = new object();

        private static readonly Dictionary<string, string> comportnamecache = new Dictionary<string, string>();

        private const int SystemPortEnumerationTimeoutMs = 2000;

        // last enumeration still running (a driver can stall it) and the last list it produced
        private static Task<string[]> systemPortEnumeration;
        private static string[] lastSystemPorts = Array.Empty<string>();

        private static readonly TimeSpan PortListCollectInterval = TimeSpan.FromSeconds(5);
        private static DateTime lastPortListCollect = DateTime.MinValue;

        public new static string[] GetPortNames()
        {
            // prevent hammering
            lock (locker)
            {
                var allPorts = new List<string>();

                if (Directory.Exists("/dev/"))
                {
                    // cleanup now - releases leaked handles. a full collection pauses every thread
                    // (the UI included), so callers that poll the port list only pay for it every few seconds
                    if (DateTime.UtcNow - lastPortListCollect > PortListCollectInterval)
                    {
                        lastPortListCollect = DateTime.UtcNow;
                        GC.Collect();
                    }
                    // mono is failing in here on linux "too many open files"
                    try
                    {
                        if (Directory.Exists("/dev/serial/by-id/"))
                            allPorts.AddRange(Directory.GetFiles("/dev/serial/by-id/", "*"));
                    }
                    catch
                    {
                    }

                    try
                    {
                        allPorts.AddRange(Directory.GetFiles("/dev/", "ttyACM*"));
                    }
                    catch
                    {
                    }

                    try
                    {
                        allPorts.AddRange(Directory.GetFiles("/dev/", "ttyUSB*"));
                    }
                    catch
                    {
                    }

                    try
                    {
                        allPorts.AddRange(Directory.GetFiles("/dev/", "rfcomm*"));
                    }
                    catch
                    {
                    }

                    try
                    {
                        allPorts.AddRange(Directory.GetFiles("/dev/", "*usb*"));
                    }
                    catch
                    {
                    }

                    try
                    {
                        allPorts.AddRange(Directory.GetFiles("/dev/", "tty.*"));
                    }
                    catch
                    {
                    }

                    try
                    {
                        allPorts.AddRange(Directory.GetFiles("/dev/", "cu.*"));
                    }
                    catch
                    {
                    }
                }

                string[] ports = null;

                try
                {
                    ports = GetSystemPortNames();
                    // any exceptions will still result in a list
                    ports = ports.Select(p => p?.TrimEnd()).ToArray();
                    ports = ports.Select(FixBlueToothPortNameBug).ToArray();
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }

                if (ports != null)
                    allPorts.AddRange(ports);

                if (GetCustomPorts != null)
                {
                    try
                    {
                        allPorts.AddRange(GetCustomPorts.Invoke());
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex);
                    }
                }

                // a port removed mid-enumeration can leave a null entry, which ComboBox.Items.AddRange rejects
                return allPorts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            }
        }

        /// <summary>
        /// System.IO.Ports.SerialPort.GetPortNames with a timeout. On Windows it reads
        /// HARDWARE\DEVICEMAP\SERIALCOMM, which a misbehaving (e.g. Bluetooth) serial driver can
        /// stall. On a timeout or failure the previous list is returned, and while a stalled
        /// enumeration is still running no new one is started, so callers do not block again.
        /// </summary>
        private static string[] GetSystemPortNames()
        {
            if (systemPortEnumeration != null && !systemPortEnumeration.IsCompleted)
                return lastSystemPorts;

            systemPortEnumeration = Task.Run(() => System.IO.Ports.SerialPort.GetPortNames());

            try
            {
                if (!systemPortEnumeration.Wait(SystemPortEnumerationTimeoutMs))
                {
                    log.Warn($"System.IO.Ports.SerialPort.GetPortNames did not return within {SystemPortEnumerationTimeoutMs} ms, using the previous port list.");
                    return lastSystemPorts;
                }

                lastSystemPorts = systemPortEnumeration.Result ?? Array.Empty<string>();
            }
            catch (AggregateException ex)
            {
                log.Error("Failed to enumerate system serial ports, using the previous port list.", ex.InnerException ?? ex);
            }

            return lastSystemPorts;
        }

        public static Func<List<string>> GetCustomPorts;

        // guards comportnamecache and the background name lookup; separate from locker so a name
        // lookup never holds up GetPortNames
        private static readonly object namelocker = new object();

        // ports waiting for the background lookup, and whether that lookup is running
        private static readonly HashSet<string> pendingNamePorts = new HashSet<string>();
        private static Task nameLookup = Task.CompletedTask;
        private static bool nameLookupRunning;

        // ports that came back without a name are asked about again, but no more often than this
        private static readonly TimeSpan EmptyNameRetryInterval = TimeSpan.FromSeconds(10);
        private static DateTime lastEmptyNameRetry = DateTime.MinValue;

        private static bool IsNetworkPort(string port)
        {
            return port == "AUTO" || port == "UDP" || port == "UDPCl" || port == "TCP" || port == "WS";
        }

        /// <summary>
        /// Raised, on a background thread, when the background lookup added or changed nice names.
        /// </summary>
        public static event EventHandler NiceNamesChanged;

        /// <summary>
        /// The nice name of a port if it has been looked up already. Never blocks, so it is safe to
        /// call while painting; a port that is not cached yet can be queued with RefreshNiceNames.
        /// </summary>
        public static bool TryGetCachedNiceName(string port, out string name)
        {
            name = "";
            if (port == null || IsNetworkPort(port))
                return true;

            lock (namelocker)
            {
                if (comportnamecache.TryGetValue(port, out var cached))
                {
                    name = cached;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Queue a background lookup of the ports that have no cached nice name (all of them in one
        /// GetDeviceNames call) and return without waiting. NiceNamesChanged is raised once they are known.
        /// </summary>
        /// <returns>the lookup serving the request, for callers that want to wait for it</returns>
        public static Task RefreshNiceNames(IEnumerable<string> ports)
        {
            lock (namelocker)
            {
                var retryEmpty = DateTime.UtcNow - lastEmptyNameRetry > EmptyNameRetryInterval;
                var retriedEmpty = false;

                foreach (var port in ports ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(port) || IsNetworkPort(port))
                        continue;

                    if (comportnamecache.TryGetValue(port, out var cached))
                    {
                        if (cached != "" || !retryEmpty)
                            continue;
                        retriedEmpty = true;
                    }

                    pendingNamePorts.Add(port);
                }

                if (retriedEmpty)
                    lastEmptyNameRetry = DateTime.UtcNow;

                if (pendingNamePorts.Count > 0 && !nameLookupRunning)
                {
                    nameLookupRunning = true;
                    nameLookup = Task.Run(LookupPendingNames);
                }

                return nameLookup;
            }
        }

        public static string GetNiceName(string port)
        {
            if (port == null || IsNetworkPort(port))
                return "";

            if (TryGetCachedNiceName(port, out var name))
            {
                // a port with no name is asked about again in the background, rate limited
                if (name == "")
                    RefreshNiceNames(new[] { port });
                return name;
            }

            // on timeout the lookup is left to finish in the background: aborting it mid WMI/COM call is
            // unsafe. nothing is cached for the port until it does, so a later call can still get the name
            var lookup = RefreshNiceNames(new[] { port });
            try
            {
                lookup.Wait(1000);
            }
            catch (AggregateException ex)
            {
                log.Error(ex.InnerException ?? ex);
            }

            TryGetCachedNiceName(port, out name);
            return name;
        }

        private static void LookupPendingNames()
        {
            // let a burst of requests (e.g. every item of a dropdown being painted) join one lookup
            Thread.Sleep(50);

            while (true)
            {
                string[] ports;
                lock (namelocker)
                {
                    if (pendingNamePorts.Count == 0)
                    {
                        nameLookupRunning = false;
                        return;
                    }

                    ports = pendingNamePorts.ToArray();
                    pendingNamePorts.Clear();
                }

                var names = LookupNames(ports);

                var changed = false;
                lock (namelocker)
                {
                    foreach (var item in names)
                    {
                        if (comportnamecache.TryGetValue(item.Key, out var old) && old == item.Value)
                            continue;
                        comportnamecache[item.Key] = item.Value;
                        changed = true;
                    }
                }

                if (changed)
                {
                    try
                    {
                        NiceNamesChanged?.Invoke(null, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex);
                    }
                }
            }
        }

        private static Dictionary<string, string> LookupNames(string[] ports)
        {
            log.Info("start GetNiceName " + string.Join(",", ports));

            // every name the batch providers know, whether asked for or not, so ports appearing later are cached too
            var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var batch = GetDeviceNames;
            if (batch != null)
            {
                foreach (EventArgsDeviceNames provider in batch.GetInvocationList())
                {
                    try
                    {
                        var names = provider(ports);
                        if (names == null)
                            continue;
                        foreach (var item in names)
                        {
                            if (!string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrEmpty(item.Value) &&
                                !known.ContainsKey(item.Key))
                                known[item.Key] = item.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        // the ports are cached without a name and so retried, rate limited, by RefreshNiceNames
                        log.Error("Failed to look up serial port names", ex);
                    }
                }
            }

            var result = new Dictionary<string, string>();
            foreach (var item in known)
                result[item.Key] = item.Value;

            foreach (var port in ports)
            {
                if (known.TryGetValue(port, out var name))
                {
                    result[port] = name;
                    continue;
                }

                // per port providers, for anything the batch providers did not name
                result[port] = GetDeviceName != null ? GetName(port) : "";
            }

            log.Info("done GetNiceName " + string.Join(", ", ports.Select(p => p + " = " + result[p])));

            return result;
        }

        public delegate string EventArgsDeviceName(string port);

        /// <summary>
        /// Looks up the nice name of one port. Kept for existing subscribers; GetDeviceNames is
        /// preferred since it names every port in one call. Called on a background thread.
        /// </summary>
        public static event EventArgsDeviceName GetDeviceName;

        /// <param name="ports">the ports being asked about; the result may name others too</param>
        /// <returns>nice names keyed by port name, ports without a name may be left out</returns>
        public delegate Dictionary<string, string> EventArgsDeviceNames(string[] ports);

        /// <summary>
        /// Looks up the nice names of many ports at once. Called on a background thread.
        /// </summary>
        public static event EventArgsDeviceNames GetDeviceNames;

        private static string GetName(string port)
        {
            try
            {
                var ans = GetDeviceName?.Invoke(port);
                if (!String.IsNullOrEmpty(ans))
                    return ans;
            }
            catch
            {
            }

            return "";
        }

        // .NET bug: sometimes bluetooth ports are enumerated with bogus characters
        // eg 'COM10' becomes 'COM10c' - one workaround is to remove the non numeric
        // char. Annoyingly, sometimes a numeric char is added, which means this
        // does not work in all cases.
        // See http://connect.microsoft.com/VisualStudio/feedback/details/236183/system-io-ports-serialport-getportnames-error-with-bluetooth
        private static string FixBlueToothPortNameBug(string portName)
        {
            if (portName == null)
                return null;
            if (!portName.StartsWith("COM"))
                return portName;
            var newPortName = "COM"; // Start over with "COM"
            foreach (var portChar in portName.Substring(3).Take(3)
            ) //  Remove "COM", put the rest in a character array
                if (char.IsDigit(portChar))
                    newPortName += portChar.ToString(); // Good character, append to portName
            //  else
            //log.WarnFormat("Bad (Non Numeric) character in port name '{0}' - removing", portName);

            return newPortName;
        }
    }

    public class WinSerialPort : System.IO.Ports.SerialPort, ICommsSerial
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(WinSerialPort));

        public bool espFix = false;
        public WinSerialPort()
        {
        }

        public new bool DtrEnable
        {
            get => base.DtrEnable;
            set
            {
                log.Info(base.PortName + " DtrEnable " + value);
                if (base.DtrEnable == value) return;
                base.DtrEnable = value;
            }
        }

        public new bool RtsEnable
        {
            get => base.RtsEnable;
            set
            {
                log.Info(PortName + " RtsEnable " + value);
                if (base.RtsEnable == value) return;
                base.RtsEnable = value;
            }
        }

        /*
        protected override void Dispose(bool disposing)
        {
            try
            {
                try
                {
                    Type mytype = typeof(System.IO.Ports.SerialPort);
                    FieldInfo field = mytype.GetField("internalSerialStream", BindingFlags.Instance | BindingFlags.NonPublic);

                    if (field != null)
                    {
                        Stream stream = (Stream)field.GetValue(this);

                        if (stream != null)
                        {
                            try
                            {
                                stream.Dispose();
                            }
                            catch (Exception ex) { Console.WriteLine("1 " + ex.ToString()); }
                            stream = null;
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine("2 " + ex.ToString()); }

                base.Dispose(disposing);
            }
            catch (Exception ex) { Console.WriteLine("3 " + ex.ToString()); }
        }
        */

        public new void Open()
        {
            // 500ms write timeout - win32 api default
            base.WriteTimeout = 500;


            if (base.IsOpen)
                return;

            try
            {
                // this causes element not found with bluetooth devices.
                if (base.BaudRate >= 115200)
                {
                    //Console.WriteLine("Doing SerialPortFixer");
                    //SerialPortFixer.Execute(PortName);
                    //Console.WriteLine("Done SerialPortFixer");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            if (PortName.StartsWith("/"))
                if (!File.Exists(PortName))
                    throw new Exception("No such device");


            if (espFix)
                base.Handshake = System.IO.Ports.Handshake.RequestToSend;

            try
            {
                base.Open();
            }
            catch
            {
                try
                {
                    Close();
                }
                catch
                {
                }

                throw;
            }
            if (espFix)
                base.Handshake = System.IO.Ports.Handshake.None;
        }

        public new void Close()
        {
            log.Info("Closing port " + PortName);
            base.Close();
        }

        public void toggleDTR()
        {
            var open = base.IsOpen;
            Console.WriteLine("toggleDTR " + IsOpen);
            try
            {
                if (!open)
                    Open();
            }
            catch
            {
            }

            base.DtrEnable = false;
            base.RtsEnable = false;

            Thread.Sleep(50);

            base.DtrEnable = true;
            base.RtsEnable = true;

            Thread.Sleep(50);

            try
            {
                if (!open)
                    Close();
            }
            catch
            {
            }

            Console.WriteLine("toggleDTR done " + IsOpen);
        }
    }

    public sealed class SerialPortFixer : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(SerialPortFixer));

        #region IDisposable Members

        public void Dispose()
        {
            if (m_Handle != null)
            {
                m_Handle.Dispose();
                m_Handle = null;
            }

            GC.SuppressFinalize(this);
        }

        #endregion IDisposable Members

        public static void Execute(string portName)
        {
            using (new SerialPortFixer(portName))
            {
            }
        }

        #region Implementation

        private const int DcbFlagAbortOnError = 14;
        private const int CommStateRetries = 10;
        private SafeFileHandle m_Handle;

        private SerialPortFixer(string portName)
        {
            const int dwFlagsAndAttributes = 0x40000000;
            const int dwAccess = unchecked((int) 0xC0000000);
            if (portName == null || !portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid Serial Port", "portName");
            var hFile = NativeMethods.CreateFile(@"\\.\" + portName, dwAccess, 0, IntPtr.Zero, 3, dwFlagsAndAttributes,
                IntPtr.Zero);
            if (hFile.IsInvalid) WinIoError();
            try
            {
                var fileType = NativeMethods.GetFileType(hFile);
                if (fileType != 2 && fileType != 0) throw new ArgumentException("Invalid Serial Port", "portName");
                m_Handle = hFile;
                InitializeDcb();
            }
            catch
            {
                hFile.Dispose();
                m_Handle = null;
                throw;
            }
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern int FormatMessage(int dwFlags, HandleRef lpSource, int dwMessageId, int dwLanguageId,
                StringBuilder lpBuffer, int nSize, IntPtr arguments);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool GetCommState(SafeFileHandle hFile, ref Dcb lpDcb);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool SetCommState(SafeFileHandle hFile, ref Dcb lpDcb);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool ClearCommError(SafeFileHandle hFile, ref int lpErrors, ref Comstat lpStat);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern SafeFileHandle CreateFile(string lpFileName, int dwDesiredAccess, int dwShareMode,
                IntPtr securityAttrs, int dwCreationDisposition,
                int dwFlagsAndAttributes, IntPtr hTemplateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern int GetFileType(SafeFileHandle hFile);
        }

        private void InitializeDcb()
        {
            var dcb = new Dcb();
            GetCommStateNative(ref dcb);
            log.Info("before dcb flags: " + dcb.Flags);
            dcb.Flags &= ~(1u << DcbFlagAbortOnError);
            log.Info("after dcb flags: " + dcb.Flags);
            SetCommStateNative(ref dcb);
        }

        private static string GetMessage(int errorCode)
        {
            var lpBuffer = new StringBuilder(0x200);
            if (
                NativeMethods.FormatMessage(0x3200, new HandleRef(null, IntPtr.Zero), errorCode, 0, lpBuffer,
                    lpBuffer.Capacity,
                    IntPtr.Zero) != 0)
                return lpBuffer.ToString();
            return "Unknown Error";
        }

        private static int MakeHrFromErrorCode(int errorCode)
        {
            return (int) (0x80070000 | (uint) errorCode);
        }

        private static void WinIoError()
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new IOException(GetMessage(errorCode), MakeHrFromErrorCode(errorCode));
        }

        private void GetCommStateNative(ref Dcb lpDcb)
        {
            var commErrors = 0;
            var comStat = new Comstat();

            for (var i = 0; i < CommStateRetries; i++)
            {
                if (!NativeMethods.ClearCommError(m_Handle, ref commErrors, ref comStat)) WinIoError();
                if (NativeMethods.GetCommState(m_Handle, ref lpDcb)) break;
                if (i == CommStateRetries - 1) WinIoError();
            }
        }

        private void SetCommStateNative(ref Dcb lpDcb)
        {
            var commErrors = 0;
            var comStat = new Comstat();
            for (var i = 0; i < CommStateRetries; i++)
            {
                if (!NativeMethods.ClearCommError(m_Handle, ref commErrors, ref comStat)) WinIoError();
                if (NativeMethods.SetCommState(m_Handle, ref lpDcb)) break;
                if (i == CommStateRetries - 1) WinIoError();
            }
        }

        #region Nested type: COMSTAT

        [StructLayout(LayoutKind.Sequential)]
        private struct Comstat
        {
            public readonly uint Flags;
            public readonly uint cbInQue;
            public readonly uint cbOutQue;
        }

        #endregion Nested type: COMSTAT

        #region Nested type: DCB

        /*
         * https://msdn.microsoft.com/en-us/library/windows/desktop/aa363214(v=vs.85).aspx
  DWORD fBinary  :1;
  DWORD fParity  :1;
  DWORD fOutxCtsFlow  :1;
  DWORD fOutxDsrFlow  :1;
  DWORD fDtrControl  :2;
  DWORD fDsrSensitivity  :1;
  DWORD fTXContinueOnXoff  :1;
  DWORD fOutX  :1;
  DWORD fInX  :1;
  DWORD fErrorChar  :1;
  DWORD fNull  :1;
  DWORD fRtsControl  :2;
  DWORD fAbortOnError  :1;
  DWORD fDummy2  :17;
         */

        [StructLayout(LayoutKind.Sequential)]
        public struct Dcb
        {
            public readonly uint DCBlength;
            public readonly uint BaudRate;
            public uint Flags;
            public readonly ushort wReserved;
            public readonly ushort XonLim;
            public readonly ushort XoffLim;
            public readonly byte ByteSize;
            public readonly byte Parity;
            public readonly byte StopBits;
            public readonly byte XonChar;
            public readonly byte XoffChar;
            public readonly byte ErrorChar;
            public readonly byte EofChar;
            public readonly byte EvtChar;
            public readonly ushort wReserved1;
        }

        #endregion Nested type: DCB

        #endregion Implementation
    }
}