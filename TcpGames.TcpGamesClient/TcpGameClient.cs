using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TcpGames.Shared;

namespace TcpGames.TcpGamesClient
{
    public class TcpGameClient
    {
        public readonly string ServerAddress;
        public readonly int Port;
        public bool Running { get; private set; }
        private TcpClient _client;
        private bool _clientRequestedDisconnect = false;

        // Messaging
        private NetworkStream _msgStream = null;
        private Dictionary<string, Func<string, Task>> _commandHandlers = new Dictionary<string, Func<string, Task>>();

        public TcpGameClient(string serverAddress, int port)
        {
            // Create non-connected TcpClient
            _client = new TcpClient();
            Running = false;

            ServerAddress = serverAddress;
            Port = port;
        }

        // Clean up leftovers
        private void _cleanupNetworkResources()
        {
            _msgStream?.Close();
            _msgStream = null;
            _client.Close();
        }

        // Connect to the games server
        public void Connect()
        {
            try
            {
                _client.Connect(ServerAddress, Port);
            } catch (SocketException se)
            {
                Console.WriteLine($"[ERROR] {se.Message}");
            }

            // Check that we've connected
            if (_client.Connected)
            {
                Console.WriteLine($"Connected to server at {_client.Client.RemoteEndPoint}");
                Running = true;

                // Get message stream
                _msgStream = _client.GetStream();

                // Hook up some packet command handlers
                _commandHandlers["bye"] = _handleBye;
                _commandHandlers["message"] = _handleMessage;
                _commandHandlers["input"] = _handleInput;
            } else
            {
                // Connection failed
                _cleanupNetworkResources();
                Console.WriteLine($"Wasn't able to connect to server at {ServerAddress}:{Port}");
            }
        }

        // Request disconnect, sends "bye"
        // Should only be called by user
        public void Disconnect()
        {
            Console.WriteLine("Disconnecting from the server...");
            Running = false;
            _clientRequestedDisconnect = true;
            _sendPacket(new Packet("bye")).GetAwaiter().GetResult();
        }

        public void Run()
        {
            bool wasRunning = Running;

            // Listen for messages
            List<Task> tasks = new List<Task>();
            while (Running)
            {
                // check for new packets
                tasks.Add(_handleIncomingPackets());

                // Nap
                Thread.Sleep(10);

                // Make sure we didn't disconnect
                if (_isDisconnected(_client) && !_clientRequestedDisconnect)
                {
                    Running = false;
                    Console.WriteLine("The server has disconnected from us ungracefully.\n:[");
                }
            }

            // Allow extra time for additional packets
            Task.WaitAll(tasks.ToArray(), 1000);

            // Cleanup
            _cleanupNetworkResources();
            if (wasRunning)
            {
                Console.WriteLine("Disconnected.");
            }
        }

        private async Task _sendPacket(Packet packet)
        {
            try
            {
                byte[] jsonBuffer = Encoding.UTF8.GetBytes(packet.ToJson());
                byte[] lengthBuffer = BitConverter.GetBytes(Convert.ToUInt16(jsonBuffer.Length));

                // Join the buffers
                byte[] packetBuffer = new byte[lengthBuffer.Length + jsonBuffer.Length];
                lengthBuffer.CopyTo(packetBuffer, 0);
                jsonBuffer.CopyTo(packetBuffer, lengthBuffer.Length);

                // Send the packet
                await _msgStream.WriteAsync(packetBuffer, 0, packetBuffer.Length);
            }
            catch (Exception) { }
        }

        // Checks for new incoming messages and handles them
        // This method will handle one packet at a time, even if more than one is in memory stream
        private async Task _handleIncomingPackets()
        {
            try
            {
                // Check for new incoming messages
                if (_client.Available > 0)
                {
                    // There must be some incoming data
                    byte[] lengthBuffer = new byte[2];
                    await _msgStream.ReadAsync(lengthBuffer, 0, 2);
                    ushort packetByteSize = BitConverter.ToUInt16(lengthBuffer, 0);

                    // Now read that many bytes
                    byte[] jsonBuffer = new byte[packetByteSize];
                    await _msgStream.ReadAsync(jsonBuffer, 0, jsonBuffer.Length);

                    // Convert it to a packet datatype
                    string jsonString = Encoding.UTF8.GetString(jsonBuffer);
                    Packet packet = Packet.FromJson(jsonString);

                    // Dispatch it
                    try
                    {
                        await _commandHandlers[packet.Command](packet.Message);
                    } catch (KeyNotFoundException) { }
                }
            } catch (Exception) { }
        }

        #region Command Handlers
        private Task _handleBye(string message)
        {
            Console.WriteLine("The server is disconnecting us with this message:");
            Console.WriteLine(message);

            Running = false;
            return Task.CompletedTask;
        }

        private Task _handleMessage(string message)
        {
            Console.Write(message);
            return Task.CompletedTask;
        }

        private async Task _handleInput(string message)
        {
            Console.Write(message);
            string responseMsg = Console.ReadLine();

            // Send the response
            Packet resp = new Packet("input", responseMsg);
            await _sendPacket(resp);
        }
        #endregion Command Handlers

        private bool _isDisconnected(TcpClient client)
        {
            try
            {
                Socket sock = client.Client;
                return sock.Poll(10 * 1000, SelectMode.SelectRead) && (sock.Available == 0);
            } catch (SocketException)
            {
                return true;
            }
        }
    }
}
