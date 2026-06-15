using HR_Codex_v0.Services;
using HR_Codex_v0.Services.RecorderProtocol;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HR_Codex_v0.Tests
{
    internal static class Program
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private static int Main()
        {
            try
            {
                TestSessionLifecycleAndFiles();
                TestLocalAlignmentFallback();
                TestUdpServicePreservesDataPath();
                TestTcpJsonLinesProtocol().GetAwaiter().GetResult();
                TestTcpPortConflictDoesNotThrow();
                Console.WriteLine("PASS: HR2.3 recorder self-test completed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                return 1;
            }
        }

        private static void TestSessionLifecycleAndFiles()
        {
            string outputDir = CreateTempDirectory("external-clock");
            using (var session = new Hr23RecorderSession())
            {
                AssertEqual("idle", session.GetStatus().State, "initial state");
                AssertEqual("not_prepared", session.Start().Error, "start before prepare");

                double masterStart = EpochNow() - 1.0;
                var prepare = session.Prepare(new Hr23RecorderPrepareRequest
                {
                    SessionId = "session_self_test",
                    OutputDir = outputDir,
                    TimeBase = new Dictionary<string, object>
                    {
                        { "master", "debug_monitor" },
                        { "recordingStartEpochS", masterStart }
                    },
                    Metadata = new Dictionary<string, object>
                    {
                        { "source", "self_test" }
                    }
                });

                AssertTrue(prepare.Ok, "prepare succeeds");
                AssertEqual("prepared", prepare.State, "prepared state");
                AssertFileExists(outputDir, "raw.dat");
                AssertFileExists(outputDir, "packets.csv");
                AssertFileExists(outputDir, "events.csv");
                AssertFileExists(outputDir, "metadata.json");
                AssertEqual("already_prepared", session.Prepare(new Hr23RecorderPrepareRequest()).Error, "duplicate prepare");

                var start = session.Start();
                AssertTrue(start.Ok, "start succeeds");
                AssertEqual("recording", start.State, "recording state");

                byte[] first = { 0xAA, 0xBB, 0x55, 0x66, 0x10 };
                byte[] second = { 0x20, 0x21, 0x22 };
                session.OnUdpPacket(new UdpPacketReceivedEventArgs(first, new IPEndPoint(IPAddress.Loopback, 20202)));
                session.OnUdpPacket(new UdpPacketReceivedEventArgs(second, new IPEndPoint(IPAddress.Loopback, 20202)));

                var stop = session.Stop();
                AssertTrue(stop.Ok, "stop succeeds");
                AssertEqual("stopped", stop.State, "stop state");
                AssertEqual(2L, stop.PacketCount, "packet count");
                AssertEqual(8L, stop.TotalBytes, "byte count");
                AssertTrue(stop.RawFileClosedUtc.HasValue, "raw close timestamp");

                byte[] raw = File.ReadAllBytes(Path.Combine(outputDir, "raw.dat"));
                AssertSequence(first.Concat(second).ToArray(), raw, "raw payload is unchanged");

                string[] packetLines = File.ReadAllLines(Path.Combine(outputDir, "packets.csv"));
                AssertEqual(3, packetLines.Length, "packet csv header plus rows");
                AssertTrue(packetLines[1].EndsWith(",true", StringComparison.Ordinal), "frame header marker");
                AssertTrue(packetLines[2].Contains(",5,8,"), "raw offset and total bytes");

                var metadata = ReadJsonObject(Path.Combine(outputDir, "metadata.json"));
                AssertEqual("stopped", Convert.ToString(metadata["state"], CultureInfo.InvariantCulture), "metadata state");
                AssertEqual("debug_monitor_recording_start_epoch", Convert.ToString(metadata["alignmentMode"], CultureInfo.InvariantCulture), "external alignment mode");
                AssertEqual(2L, Convert.ToInt64(metadata["packetCount"], CultureInfo.InvariantCulture), "metadata packet count");

                string eventsText = File.ReadAllText(Path.Combine(outputDir, "events.csv"));
                AssertContains(eventsText, ",prepared,", "prepared event");
                AssertContains(eventsText, ",started,", "started event");
                AssertContains(eventsText, ",first_packet,", "first packet event");
                AssertContains(eventsText, ",stopped,", "stopped event");
                AssertContains(eventsText, ",closed,", "closed event");

                AssertEqual("stopped", session.Stop().State, "idempotent stop");

                string preparedOnlyDir = CreateTempDirectory("prepared-only");
                AssertTrue(session.Prepare(new Hr23RecorderPrepareRequest
                {
                    SessionId = "session_prepared_only",
                    OutputDir = preparedOnlyDir
                }).Ok, "prepare after stopped");
                AssertEqual("stopped", session.Stop().State, "stop prepared session");
                string preparedOnlyEvents = File.ReadAllText(Path.Combine(preparedOnlyDir, "events.csv"));
                AssertContains(preparedOnlyEvents, ",warning,", "prepared stop warning");
                AssertContains(preparedOnlyEvents, ",stopped,", "prepared stop state event");
                AssertContains(preparedOnlyEvents, ",closed,", "prepared stop close event");
            }
        }

        private static void TestLocalAlignmentFallback()
        {
            string outputDir = CreateTempDirectory("local-clock");
            using (var session = new Hr23RecorderSession())
            {
                AssertTrue(session.Prepare(new Hr23RecorderPrepareRequest
                {
                    SessionId = "session_local_clock",
                    OutputDir = outputDir,
                    TimeBase = new Dictionary<string, object> { { "master", "debug_monitor" } }
                }).Ok, "fallback prepare");
                AssertTrue(session.Start().Ok, "fallback start");
                session.OnUdpPacket(new UdpPacketReceivedEventArgs(new byte[] { 1 }, new IPEndPoint(IPAddress.Loopback, 23480)));
                AssertTrue(session.Stop().Ok, "fallback stop");

                var metadata = ReadJsonObject(Path.Combine(outputDir, "metadata.json"));
                AssertEqual("local_recorder_start_epoch", Convert.ToString(metadata["alignmentMode"], CultureInfo.InvariantCulture), "local alignment mode");
                AssertTrue(metadata["recordingStartEpochS"] != null, "local start epoch recorded");
            }
        }

        private static async Task TestTcpJsonLinesProtocol()
        {
            string outputDir = CreateTempDirectory("tcp");
            using (var session = new Hr23RecorderSession())
            using (var server = new Hr23RecorderServer(session, IPAddress.Loopback, 0))
            {
                AssertTrue(server.Start(), "server starts on an ephemeral TCP port");
                AssertTrue(server.Port > 0, "server exposes bound port");

                var status = await SendCommandAsync(server.Port, new Dictionary<string, object> { { "cmd", "status" } });
                AssertEqual(true, Convert.ToBoolean(status["ok"], CultureInfo.InvariantCulture), "TCP status ok");
                AssertEqual("idle", Convert.ToString(status["state"], CultureInfo.InvariantCulture), "TCP status state");

                var prepare = await SendCommandAsync(server.Port, new Dictionary<string, object>
                {
                    { "cmd", "prepare" },
                    { "sessionId", "session_tcp" },
                    { "outputDir", outputDir },
                    { "timeBase", new Dictionary<string, object> { { "master", "debug_monitor" } } },
                    { "metadata", new Dictionary<string, object> { { "source", "self_test" } } }
                });
                AssertEqual("prepared", Convert.ToString(prepare["state"], CultureInfo.InvariantCulture), "TCP prepare state");

                var start = await SendCommandAsync(server.Port, new Dictionary<string, object> { { "cmd", "start" } });
                AssertEqual("recording", Convert.ToString(start["state"], CultureInfo.InvariantCulture), "TCP start state");
                session.OnUdpPacket(new UdpPacketReceivedEventArgs(new byte[] { 9, 8, 7 }, new IPEndPoint(IPAddress.Loopback, 20202)));

                var stop = await SendCommandAsync(server.Port, new Dictionary<string, object> { { "cmd", "stop" } });
                AssertEqual(true, Convert.ToBoolean(stop["ok"], CultureInfo.InvariantCulture), "TCP stop ok");
                AssertEqual("stopped", Convert.ToString(stop["state"], CultureInfo.InvariantCulture), "TCP stop state");
                AssertEqual(1L, Convert.ToInt64(stop["packetCount"], CultureInfo.InvariantCulture), "TCP packet count");

                var unknown = await SendCommandAsync(server.Port, new Dictionary<string, object> { { "cmd", "missing" } });
                AssertEqual(false, Convert.ToBoolean(unknown["ok"], CultureInfo.InvariantCulture), "unknown command rejected");
                AssertEqual("unknown_command", Convert.ToString(unknown["error"], CultureInfo.InvariantCulture), "unknown command error");

                server.Stop();
            }
        }

        private static void TestUdpServicePreservesDataPath()
        {
            int localPort = ReserveUdpPort();
            int remotePort = ReserveUdpPort();
            var order = new List<string>();
            var completed = new ManualResetEventSlim(false);
            var service = new UdpService();
            UdpPacketReceivedEventArgs enhancedPacket = null;

            service.PacketReceived += (sender, packet) =>
            {
                lock (order)
                    order.Add("packet");
                enhancedPacket = packet;
            };
            service.DataReceived += (sender, data) =>
            {
                lock (order)
                    order.Add("data");
                completed.Set();
            };

            service.Connect("127.0.0.1", localPort, "127.0.0.1", remotePort);
            AssertTrue(service.StartReceiving(), "UDP receive loop starts");
            using (var sender = new UdpClient())
                sender.Send(new byte[] { 0xAA, 0xBB, 0x55, 0x66 }, 4, new IPEndPoint(IPAddress.Loopback, localPort));

            AssertTrue(completed.Wait(TimeSpan.FromSeconds(3)), "UDP packet reaches existing DataReceived path");
            service.Disconnect();

            AssertTrue(enhancedPacket != null, "enhanced packet event fired");
            AssertEqual(4, enhancedPacket.Length, "enhanced packet length");
            AssertEqual("127.0.0.1", enhancedPacket.SourceEndPoint.Address.ToString(), "enhanced source IP");
            lock (order)
                AssertSequence(new[] { "packet", "data" }, order.ToArray(), "enhanced event order");
        }

        private static void TestTcpPortConflictDoesNotThrow()
        {
            var occupied = new TcpListener(IPAddress.Loopback, 0);
            occupied.Start();
            int port = ((IPEndPoint)occupied.LocalEndpoint).Port;
            try
            {
                using (var session = new Hr23RecorderSession())
                using (var server = new Hr23RecorderServer(session, IPAddress.Loopback, port))
                {
                    AssertEqual(false, server.Start(), "occupied TCP port returns false");
                    AssertTrue(!string.IsNullOrWhiteSpace(server.LastError), "occupied TCP port exposes an error");
                }
            }
            finally
            {
                occupied.Stop();
            }
        }

        private static async Task<Dictionary<string, object>> SendCommandAsync(int port, Dictionary<string, object> command)
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                using (NetworkStream stream = client.GetStream())
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                {
                    await writer.WriteLineAsync(Json.Serialize(command));
                    string line = await reader.ReadLineAsync();
                    AssertTrue(!string.IsNullOrWhiteSpace(line), "server returns one JSON line");
                    return Json.Deserialize<Dictionary<string, object>>(line);
                }
            }
        }

        private static Dictionary<string, object> ReadJsonObject(string path)
        {
            return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
        }

        private static string CreateTempDirectory(string suffix)
        {
            string path = Path.Combine(Path.GetTempPath(), "hr23-recorder-self-test", suffix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static int ReserveUdpPort()
        {
            using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                return ((IPEndPoint)udp.Client.LocalEndPoint).Port;
        }

        private static double EpochNow()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }

        private static void AssertFileExists(string directory, string fileName)
        {
            AssertTrue(File.Exists(Path.Combine(directory, fileName)), fileName + " exists");
        }

        private static void AssertContains(string value, string expected, string message)
        {
            AssertTrue(value != null && value.Contains(expected), message);
        }

        private static void AssertSequence(byte[] expected, byte[] actual, string message)
        {
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException(message + ": byte sequences differ");
        }

        private static void AssertSequence(string[] expected, string[] actual, string message)
        {
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException(message + ": expected " + string.Join(",", expected) + ", actual " + string.Join(",", actual));
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(message + ": expected " + expected + ", actual " + actual);
        }
    }
}
