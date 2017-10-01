using System;

namespace TcpGames.TcpGamesServer
{
    class Program
    {
        public static TcpGameServer gamesServer;

        // On Ctrl-C Press
        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            gamesServer?.Shutdown();
        }

        static void Main(string[] args)
        {
            string name = "Bad BBS"; // args[0];
            int port = 6000; // int.Parse(args[1]);

            Console.CancelKeyPress += InterruptHandler;

            // Create and run the server
            gamesServer = new TcpGameServer(name, port);
            gamesServer.Run();
        }
    }
}
