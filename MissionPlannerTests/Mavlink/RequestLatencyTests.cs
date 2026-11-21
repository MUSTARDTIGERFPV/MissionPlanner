using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.Comms;

namespace MissionPlannerTests.Mavlink
{
    /// <summary>
    /// A vehicle that answers requests the instant they are written, with a reader thread
    /// that behaves like MainV2's SerialReader (reads only while giveComport is false).
    /// </summary>
    [TestClass]
    public class RequestLatencyTests
    {
        const byte VehicleSys = 1, VehicleComp = 1;

        [TestMethod]
        public void GetWPClaimsThePortBeforeTheRequestSoAFastReplyIsNotLost()
        {
            using (var rig = new Rig())
            {
                bool? claimedWhenSent = null;
                rig.Serial.OnPacketWritten = msg =>
                {
                    if (msg.msgid != (uint)MAVLink.MAVLINK_MSG_ID.MISSION_REQUEST)
                        return;
                    claimedWhenSent = rig.Port.giveComport;
                    var request = msg.ToStructure<MAVLink.mavlink_mission_request_t>();
                    rig.Serial.Feed(Rig.Packet(MAVLink.MAVLINK_MSG_ID.MISSION_ITEM, new MAVLink.mavlink_mission_item_t
                    {
                        seq = request.seq,
                        command = (ushort)MAVLink.MAV_CMD.WAYPOINT,
                        x = -35.1f,
                        y = 149.2f,
                        z = 42,
                        target_system = 255,
                        target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER,
                    }));
                };
                rig.StartReader();

                var sw = Stopwatch.StartNew();
                var wp = rig.Port.getWP(VehicleSys, VehicleComp, 3);
                sw.Stop();

                Assert.AreEqual(true, claimedWhenSent, "MISSION_REQUEST went out before giveComport was set");
                Assert.AreEqual(42f, wp.alt);
                Assert.AreEqual(-35.1, wp.lat, 1e-4);
                Assert.IsFalse(rig.Port.giveComport);
                // a lost reply costs a 2.5 s retry
                Assert.IsTrue(sw.ElapsedMilliseconds < 2000, "took " + sw.ElapsedMilliseconds + " ms");
            }
        }

        [TestMethod]
        public void GetWPReleasesThePortWhenTheRequestCannotBeWritten()
        {
            using (var rig = new Rig())
            {
                // USB unplugged / UDP send error: the write throws after giveComport was claimed
                rig.Serial.FailWrites = true;

                bool threw = false;
                try
                {
                    rig.Port.getWP(VehicleSys, VehicleComp, 3);
                }
                catch (Exception)
                {
                    threw = true;
                }

                Assert.IsTrue(threw, "the write error should reach the caller");

                // a port left claimed stops the SerialReader reading it for good
                Assert.IsFalse(rig.Port.giveComport);
            }
        }

        [TestMethod]
        public void GetVersionSeesAnImmediateReplyAndCapabilitiesAreApplied()
        {
            using (var rig = new Rig())
            {
                const ulong caps = (ulong)(MAVLink.MAV_PROTOCOL_CAPABILITY.FTP | MAVLink.MAV_PROTOCOL_CAPABILITY.MISSION_INT);
                int replies = 0;
                rig.Serial.OnPacketWritten = msg =>
                {
                    // answer only the first of the three requests, before the others are even written
                    if (msg.msgid != (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG || replies > 0)
                        return;
                    replies++;
                    rig.Serial.Feed(Rig.Packet(MAVLink.MAVLINK_MSG_ID.AUTOPILOT_VERSION, new MAVLink.mavlink_autopilot_version_t
                    {
                        capabilities = caps,
                        flight_sw_version = 0x04060000,
                        uid2 = new byte[18],
                        flight_custom_version = new byte[8],
                        middleware_custom_version = new byte[8],
                        os_custom_version = new byte[8],
                    }));
                    // let the reader consume it before the remaining requests are sent
                    var until = DateTime.UtcNow.AddMilliseconds(500);
                    while (rig.Serial.BytesToRead > 0 && DateTime.UtcNow < until)
                        Thread.Sleep(1);
                };
                rig.StartReader();

                var sw = Stopwatch.StartNew();
                Assert.IsTrue(rig.Port.getVersion(VehicleSys, VehicleComp));
                sw.Stop();

                Assert.AreEqual(1, replies);
                // callers read the capabilities straight after getVersion returns
                Assert.AreEqual((uint)caps, rig.Port.MAVlist[VehicleSys, VehicleComp].cs.capabilities & (uint)caps);
                // the old code subscribed after sending, missed this reply and needed the 500 ms retry
                Assert.IsTrue(sw.ElapsedMilliseconds < 450, "took " + sw.ElapsedMilliseconds + " ms");
            }
        }

        sealed class Rig : IDisposable
        {
            public readonly MAVLinkInterface Port = new MAVLinkInterface();
            public readonly LoopSerial Serial = new LoopSerial();
            readonly CancellationTokenSource stop = new CancellationTokenSource();
            Thread reader;
            readonly uint oldGcs;

            public Rig()
            {
                oldGcs = MAVLinkInterface.gcssysid;
                MAVLinkInterface.gcssysid = 255;
                Port.BaseStream = Serial;
                Port.printbps = false;
                // make the vehicle known before any request
                Serial.Feed(Packet(MAVLink.MAVLINK_MSG_ID.HEARTBEAT, new MAVLink.mavlink_heartbeat_t
                {
                    type = (byte)MAVLink.MAV_TYPE.QUADROTOR,
                    autopilot = (byte)MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA,
                    mavlink_version = 3,
                }));
                Assert.AreEqual((uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT, Port.readPacket().msgid);
            }

            public static byte[] Packet(MAVLink.MAVLINK_MSG_ID id, object data)
            {
                return new MAVLink.MavlinkParse().GenerateMAVLinkPacket20(id, data, false, VehicleSys, VehicleComp);
            }

            /// <summary>like MainV2.SerialReader: only reads while bytes are waiting and nobody claimed the port</summary>
            public void StartReader()
            {
                reader = new Thread(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        if (Serial.BytesToRead > 0 && Port.giveComport == false)
                        {
                            try { Port.readPacket(); }
                            catch (Exception) { }
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                    }
                }) { IsBackground = true, Name = "test SerialReader" };
                reader.Start();
            }

            public void Dispose()
            {
                stop.Cancel();
                reader?.Join(2000);
                Port.Dispose();
                MAVLinkInterface.gcssysid = oldGcs;
            }
        }

        sealed class LoopSerial : ICommsSerial
        {
            readonly Queue<byte> incoming = new Queue<byte>();
            readonly MemoryStream written = new MemoryStream();
            /// <summary>called with each packet the GCS writes, inside the write</summary>
            public Action<MAVLink.MAVLinkMessage> OnPacketWritten;
            /// <summary>makes every write throw, like a link that went away</summary>
            public bool FailWrites;

            public void Feed(byte[] bytes) { lock (incoming) foreach (byte b in bytes) incoming.Enqueue(b); }
            public Stream BaseStream { get { return written; } }
            public int BaudRate { get; set; }
            public int BytesToRead { get { lock (incoming) return incoming.Count; } }
            public int BytesToWrite { get { return 0; } }
            public int DataBits { get; set; }
            public bool DtrEnable { get; set; }
            public bool IsOpen { get; private set; } = true;
            public string PortName { get; set; } = "loop";
            public int ReadBufferSize { get; set; }
            public int ReadTimeout { get; set; } = 100;
            public bool RtsEnable { get; set; }
            public int WriteBufferSize { get; set; }
            public int WriteTimeout { get; set; }
            public void Close() { IsOpen = false; }
            public void Open() { IsOpen = true; }
            public void DiscardInBuffer() { lock (incoming) incoming.Clear(); }
            public int Read(byte[] buffer, int offset, int count)
            {
                lock (incoming)
                {
                    int n = Math.Min(count, incoming.Count);
                    for (int i = 0; i < n; i++) buffer[offset + i] = incoming.Dequeue();
                    return n;
                }
            }
            public int ReadByte() { lock (incoming) return incoming.Dequeue(); }
            public int ReadChar() { return ReadByte(); }
            public string ReadExisting() { return ""; }
            public string ReadLine() { return ""; }
            public void Write(string text) { var bytes = Encoding.ASCII.GetBytes(text); Write(bytes, 0, bytes.Length); }
            public void Write(byte[] buffer, int offset, int count)
            {
                if (FailWrites)
                    throw new IOException("link gone");
                var packet = new byte[count];
                Array.Copy(buffer, offset, packet, 0, count);
                lock (written) written.Write(packet, 0, count);
                var msg = new MAVLink.MavlinkParse().ReadPacket(new MemoryStream(packet));
                if (msg != null)
                    OnPacketWritten?.Invoke(msg);
            }
            public void WriteLine(string text) { Write(text + "\n"); }
            public void toggleDTR() { }
            public void Dispose() { Close(); }
        }
    }
}
