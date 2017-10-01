using System;
using System.Net.Sockets;
using System.Threading;
using TcpGames.Shared;

namespace TcpGames.TcpGamesServer
{
    // https://16bpp.net/tutorials/csharp-networking/04d
    public class GuessMyNumberGame : IGame
    {
        private TcpGameServer _server;
        private TcpClient _player;
        private Random _rng;
        private bool _needToDisconnectClient = false;


        public string Name {
            get {
                return "Guess My Number";
            }
        }        

        public int RequiredPlayers {
            get {
                return 1;
            }
        }

        public GuessMyNumberGame(TcpGameServer server)
        {
            _server = server;
            _rng = new Random();
        }

        public bool AddPlayer(TcpClient client)
        {
            // Make sure only one player was added
            if (_player == null)
            {
                _player = client;
                return true;
            }

            return false;
        }

        // If the client who disconnected is ours, we need to quit our game
        public void DisconnectClient(TcpClient client)
        {
            _needToDisconnectClient = (client == _player);
        }

        public void Run()
        {
            // Make sure we have a player
            bool running = (_player != null);
            if (running)
            {
                // Send an instruction packet
                Packet introPacket = new Packet("message",
                    "Welcome player, I want you to guess a number.\n" +
                    "It's somewhere between (and including) 1 and 100.\n");

                _server.SendPacket(_player, introPacket).GetAwaiter().GetResult();
            } else
            {
                return;
            }

            // Should be [1, 100]
            int theNumber = _rng.Next(1, 101);
            Console.WriteLine($"Our number is {theNumber}");

            // Bools for game state
            bool correct = false;
            bool clientConnected = true;
            bool clientDisconnectedGracefully = false;
            
            // Game Loop
            while (running)
            {
                // Poll for input
                Packet inputPacket = new Packet("input", "Your guess: ");
                _server.SendPacket(_player, inputPacket).GetAwaiter().GetResult();

                // Read their answer
                Packet answerPacket = null;
                while (answerPacket == null)
                {
                    answerPacket = _server.ReceivePacket(_player).GetAwaiter().GetResult();
                    Thread.Sleep(10);
                }

                // Check for graceful disconnect
                if (answerPacket.Command == "bye")
                {
                    _server.HandleDisconnectedClient(_player);
                    clientDisconnectedGracefully = true;
                }

                // Check input
                if (answerPacket.Command == "input")
                {
                    Packet responsePacket = new Packet("message");

                    int theirGuess;
                    if (int.TryParse(answerPacket.Message, out theirGuess))
                    {
                        // See if they won
                        if (theirGuess == theNumber)
                        {
                            correct = true;
                            responsePacket.Message = "Correct! You win!\n";
                        } else if (theirGuess < theNumber)
                        {
                            responsePacket.Message = "Too low.\n";
                        } else if (theirGuess > theNumber)
                        {
                            responsePacket.Message = "Too high.\n";
                        }
                    } else
                    {
                        responsePacket.Message = "That wasn't a valid number, try again.\n";
                    }

                    // Send the message
                    _server.SendPacket(_player, responsePacket).GetAwaiter().GetResult();
                }

                // Take a nap
                Thread.Sleep(10);

                // If they aren't correct, keep them here
                running &= !correct;

                // Check for disconnect
                if (!_needToDisconnectClient && !clientDisconnectedGracefully)
                {
                    clientConnected &= !TcpGameServer.IsDisconnected(_player);
                } else
                {
                    clientConnected = false;
                }

                running &= clientConnected;
            }

            // Check for disconnect, may have happened gracefully before
            if (!_needToDisconnectClient && !clientDisconnectedGracefully)
            {
                clientConnected &= !TcpGameServer.IsDisconnected(_player);
            }
            else
            {
                Console.WriteLine("Client disconnected from game.");
            }

            Console.WriteLine($"Ending a \"{Name}\" game.");
        }
    }
}