using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TcpGames.Shared;

namespace TcpGames.TcpGamesServer
{
    // https://16bpp.net/tutorials/csharp-networking/04c
    public class TcpGameServer
    {
        // Listen for new incoming connections
        private TcpListener _listener;

        // Client objects
        private List<TcpClient> _clients = new List<TcpClient>();
        private List<TcpClient> _waitingLobby = new List<TcpClient>();

        // Game stuff
        private Dictionary<TcpClient, IGame> _gameClientIsIn = new Dictionary<TcpClient, IGame>();
        private List<IGame> _games = new List<IGame>();
        private List<Thread> _gameThreads = new List<Thread>();
        private IGame _nextGame;

        // Other data
        public readonly string Name;
        public readonly int Port;
        public bool Running { get; private set; }

        // Create new Games Server
        public TcpGameServer(string name, int port)
        {
            Name = name;
            Port = port;
            Running = false;

            // Create the listener
            _listener = new TcpListener(IPAddress.Any, Port);
        }

        // Shutdown the server if it is running
        public void Shutdown()
        {
            if (Running)
            {
                Running = false;
                Console.WriteLine("Shutting down the Game(s) Server...");
            }
        }

        // The main loop for the games server
        public void Run()
        {
            Console.WriteLine($"Starting the \"{Name}\" Game(s) Server on port {Port}");
            Console.WriteLine("Press Ctrl-C to shutdown the server at any time.");

            // Start the next game
            _nextGame = new GuessMyNumberGame(this);

            // Start running the server
            _listener.Start();
            Running = true;
            List<Task> newConnectionTasks = new List<Task>();
            Console.WriteLine("Waiting for incoming connections...");

            while (Running)
            {
                // Handle any new clients
                if (_listener.Pending())
                {
                    newConnectionTasks.Add(_handleNewConnections());
                }

                // Once we have enough clients for the next game, add them in and start the game
                if (_waitingLobby.Count >= _nextGame.RequiredPlayers)
                {
                    // Get that many players from the waiting lobby and start the game
                    int numPlayers = 0;
                    while (numPlayers < _nextGame.RequiredPlayers)
                    {
                        // Pop the first one off
                        TcpClient player = _waitingLobby[0];
                        _waitingLobby.RemoveAt(0);

                        // Try adding it to the game. If failure, put it back in the lobby
                        if (_nextGame.AddPlayer(player))
                        {
                            numPlayers++;
                        } else
                        {
                            _waitingLobby.Add(player);
                        }
                    }

                    // Start the game in a new thread!
                    Console.WriteLine($"Starting a \"{_nextGame.Name}\" game.");
                    Thread gameThread = new Thread(new ThreadStart(_nextGame.Run));
                    gameThread.Start();
                    _games.Add(_nextGame);
                    _gameThreads.Add(gameThread);

                    // Create a new game
                    _nextGame = new GuessMyNumberGame(this);
                }

                // Check if any clients have disconnected in wiating, gracefully or not
                // This should be parallelized
                foreach (TcpClient client in _waitingLobby.ToArray())
                {
                    EndPoint endPoint = client.Client.RemoteEndPoint;
                    bool disconnected = false;

                    // Check for graceful disconnect first
                    Packet pack = ReceivePacket(client).GetAwaiter().GetResult();
                    disconnected = (pack?.Command == "bye");

                    // Then ungraceful
                    disconnected |= IsDisconnected(client);

                    if (disconnected)
                    {
                        HandleDisconnectedClient(client);
                        Console.WriteLine($"Client {endPoint} has disconnected from the Game(s) Server.");
                    }
                }

                // Take a short nap
                Thread.Sleep(10);
            }

            // Give time for clients that just connected to finish connection.
            Task.WaitAll(newConnectionTasks.ToArray(), 1000);

            // Shutdown all of the threads, whether they are done or not
            foreach (Thread thread in _gameThreads)
            {
                thread.Abort();
            }

            // Disconnect any clients still here
            Parallel.ForEach(_clients, (client) =>
            {
                DisconnectClient(client, "The Game(s) Server is being shutdown.");
            });

            // Cleanup our resources
            _listener.Stop();

            // Info
            Console.WriteLine("The server has been shut down.");
        }

        private async Task _handleNewConnections()
        {
            // Get the new client using a Future
            TcpClient newClient = await _listener.AcceptTcpClientAsync();
            Console.WriteLine($"New connection from {newClient.Client.RemoteEndPoint}");

            // Store them and put them in the witing lobby
            _clients.Add(newClient);
            _waitingLobby.Add(newClient);

            // Send a welcome message
            string msg = $"Welcome to the \"{Name}\" Games Server.\n";
            await SendPacket(newClient, new Packet("message", msg));
        }

        // Attempt to gracefully disconnect a TcpClient
        public void DisconnectClient(TcpClient client, string message="")
        {
            Console.WriteLine($"Disconnecting the client from {client.Client.RemoteEndPoint}");

            // There wasn't a message set, so use default
            if (message == "")
            {
                message = "Goodbye.";
            }

            // Send goodbye message
            Task byePacket = SendPacket(client, new Packet("bye", message));

            // Notify a game that might have them
            try
            {
                _gameClientIsIn[client]?.DisconnectClient(client);
            } catch (KeyNotFoundException) { }

            // Give client time to send and process graceful disconnect
            Thread.Sleep(100);

            // Cleanup resources
            byePacket.GetAwaiter().GetResult();
            HandleDisconnectedClient(client);
        }

        // Clean up resources
        public void HandleDisconnectedClient(TcpClient client)
        {
            _clients.Remove(client);
            _waitingLobby.Remove(client);
            _cleanupClient(client);
        }

        #region Packet Transmission Methods
        // Sends a packet to a client asynchronously
        public async Task SendPacket(TcpClient client, Packet packet)
        {
            try
            {
                // We send json with length so that packets can be split accurately
                // Convert JSON to buffer and its length to a 16 bit unsgiend integer buffer
                byte[] jsonBuffer = Encoding.UTF8.GetBytes(packet.ToJson());
                byte[] lengthBuffer = BitConverter.GetBytes(Convert.ToUInt16(jsonBuffer.Length));

                // Join the buffers
                byte[] msgBuffer = new byte[lengthBuffer.Length + jsonBuffer.Length];
                lengthBuffer.CopyTo(msgBuffer, 0);
                jsonBuffer.CopyTo(msgBuffer, lengthBuffer.Length);

                // Send the packet
                await client.GetStream().WriteAsync(msgBuffer, 0, msgBuffer.Length);
            } catch (Exception ex)
            {
                // There was an issue sending
                Console.WriteLine($"There was an issue sending the packet to {client.Client.RemoteEndPoint}.");
                Console.WriteLine($"Reason: {ex.Message}");
            }
        }

        public async Task<Packet> ReceivePacket(TcpClient client)
        {
            Packet packet = null;
            try
            {
                // Check if data is available
                if (client.Available == 0)
                {
                    return null;
                }

                NetworkStream msgStream = client.GetStream();

                // There must be some data, get size
                byte[] lengthBuffer = new byte[2];
                await msgStream.ReadAsync(lengthBuffer, 0, 2);
                ushort packetByteSize = BitConverter.ToUInt16(lengthBuffer, 0);

                // Now read that many bytes from the stream
                byte[] jsonBuffer = new byte[packetByteSize];
                await msgStream.ReadAsync(jsonBuffer, 0, jsonBuffer.Length);

                // Convert it into a packet datatype
                string jsonString = Encoding.UTF8.GetString(jsonBuffer);
                packet = Packet.FromJson(jsonString);
            } catch (Exception ex)
            {
                // There was an issue receiving
                Console.WriteLine($"There was an issue reading the packet from {client.Client.RemoteEndPoint}.");
                Console.WriteLine($"Reason: {ex.Message}");
            }
            return packet;
        }
        #endregion Packet Transmission Methods

        #region TcpClient Helper Methods
        public static bool IsDisconnected(TcpClient client)
        {
            try
            {
                Socket sock = client.Client;
                return sock.Poll(10 * 1000, SelectMode.SelectRead) && (sock.Available == 0);
            } catch(SocketException) {
                // We got a socket error, assume its disconnected
                return true;
            }
        }

        private static void _cleanupClient(TcpClient client)
        {
            client.GetStream().Close();
            client.Close();
        }
        #endregion TcpClient Helper Methods
    }
}
