using System;

namespace TcpGames.TcpGamesClient
{
    class Program
    {
        public static TcpGameClient gamesClient;

        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            gamesClient?.Disconnect();
        }

        static void Main(string[] args)
        {
            string host = "localhost"; // args[0].Trim();
            int port = 6000; // int.Parse(args[1].Trim());
            gamesClient = new TcpGameClient(host, port);

            // Add a handler for Ctrl-C
            Console.CancelKeyPress += InterruptHandler;

            gamesClient.Connect();
            gamesClient.Run();
        }
    }
}
