using System.Net.Sockets;

namespace TcpGames.TcpGamesServer
{
    // This interface should be implemented by any game using this framework
    interface IGame
    {
        // Name of the game
        string Name { get; }

        // How many players are needed to start
        int RequiredPlayers { get; }

        // Add a player to the game
        bool AddPlayer(TcpClient player);

        // Tells the server to disconnect a player
        void DisconnectClient(TcpClient client);

        // The main game loop
        // This method will be run on its own thread
        void Run();
    }
}
