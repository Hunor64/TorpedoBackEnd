using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq; // For LINQ methods
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class TorpedoGameServer
{
    private const int Port = 65432;

    // Use clientId as a unique identifier for each connection
    private static int clientIdCounter = 1;

    // Manage clients with a dictionary: clientId -> ClientInfo
    private static ConcurrentDictionary<int, ClientInfo> clients = new ConcurrentDictionary<int, ClientInfo>();

    private static ConcurrentQueue<int> availablePlayerIds = new ConcurrentQueue<int>(new[] { 1, 2 });
    private static ConcurrentDictionary<int, bool> shipsPlaced = new ConcurrentDictionary<int, bool>();
    private static ConcurrentDictionary<int, List<string>> playerShips = new ConcurrentDictionary<int, List<string>>();

    public static async Task StartServer()
    {
        TcpListener server = new TcpListener(IPAddress.Any, Port);
        server.Start();
        Console.WriteLine($"Server listening on port {Port}");

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            int clientId = clientIdCounter++;

            // Try to assign a player ID from the available pool
            int playerId;
            if (!availablePlayerIds.TryDequeue(out playerId))
            {
                playerId = -1; // No player ID available, assign -1
            }

            ClientInfo clientInfo = new ClientInfo
            {
                Client = client,
                PlayerId = playerId
            };
            clients.TryAdd(clientId, clientInfo);
            Console.WriteLine($"Client {clientId} connected with Player ID {playerId}.");

            // Send the player ID to the client
            await SendPlayerID(client, playerId);

            // Start handling the client
            _ = HandleClient(clientId);
        }
    }

    private static async Task SendPlayerID(TcpClient client, int playerId)
    {
        string playerIdMessage = $"PlayerID:{playerId}";
        byte[] responseData = Encoding.UTF8.GetBytes(playerIdMessage);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(responseData, 0, responseData.Length);
        Console.WriteLine($"Sent to Client: {playerIdMessage}");
    }

    private static async Task HandleClient(int clientId)
    {
        if (!clients.TryGetValue(clientId, out ClientInfo clientInfo))
            return;

        TcpClient client = clientInfo.Client;
        int playerId = clientInfo.PlayerId;

        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[1024];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"Received from Client {clientId} (Player {playerId}): {message}");

                    // Process the message
                    string responseMessage = ProcessMessage(message, clientId);
                    byte[] responseData = Encoding.UTF8.GetBytes(responseMessage);
                    await stream.WriteAsync(responseData, 0, responseData.Length);
                    Console.WriteLine($"Sent to Client {clientId}: {responseMessage}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client {clientId}: {ex.Message}");
        }
        finally
        {
            // Clean up when client disconnects
            clients.TryRemove(clientId, out _);

            if (playerId != -1)
            {
                // Return the player ID to the pool
                availablePlayerIds.Enqueue(playerId);
                shipsPlaced.TryRemove(playerId, out _);
                Console.WriteLine($"Client {clientId} disconnected. Player ID {playerId} is now available.");
            }
            else
            {
                Console.WriteLine($"Client {clientId} disconnected.");
            }
        }
    }

    private static string ProcessMessage(string message, int clientId)
    {
        if (!clients.TryGetValue(clientId, out ClientInfo clientInfo))
            return "Invalid client.";

        int playerId = clientInfo.PlayerId;

        if (playerId == -1)
        {
            // Handle messages from spectators
            return "Spectator: Commands are limited.";
        }

        if (message.StartsWith("SHIPSPLACED_"))
        {
            // Example: message format could be "SHIPSPLACED_1,2,3" for ship positions
            var shipPositions = message.Substring("SHIPSPLACED_".Length).Split(',').ToList();
            playerShips[playerId] = shipPositions;

            shipsPlaced[playerId] = true;

            // Check if both players have placed their ships
            if (shipsPlaced.Count == 2 && shipsPlaced.Values.All(placed => placed))
            {
                // Both players are ready, send "READY" message and ship positions to both
                SendReadyMessageToPlayers();
                SendShipPositionsToPlayers();
            }
            return $"Player {playerId} has placed ships.";
        }

        // Handle other messages
        return $"Player {playerId} says: {message}";
    }

    private static void SendReadyMessageToPlayers()
    {
        foreach (var clientEntry in clients.Values)
        {
            if (clientEntry.PlayerId != -1)
            {
                string readyMessage = "READY";
                byte[] responseData = Encoding.UTF8.GetBytes(readyMessage);
                NetworkStream stream = clientEntry.Client.GetStream();
                _ = stream.WriteAsync(responseData, 0, responseData.Length);
                Console.WriteLine($"Sent to Player {clientEntry.PlayerId}: {readyMessage}");
            }
        }

        // Optionally, clear the shipsPlaced status if you want to reset for a new game
        shipsPlaced.Clear();
    }

    private static void SendShipPositionsToPlayers()
    {
        // Assuming player IDs are 1 and 2
        foreach (var clientEntry in clients.Values)
        {
            if (clientEntry.PlayerId != -1)
            {
                // Get the other player's ID
                int otherPlayerId = clientEntry.PlayerId == 1 ? 2 : 1;

                // Get the ship positions of the other player
                if (playerShips.TryGetValue(otherPlayerId, out var shipPositions))
                {
                    var shipPositionsMessage = $"SHIPPOSITIONS:{string.Join(",", shipPositions)}";
                    byte[] responseData = Encoding.UTF8.GetBytes(shipPositionsMessage);
                    NetworkStream stream = clientEntry.Client.GetStream();
                    _ = stream.WriteAsync(responseData, 0, responseData.Length);
                    Console.WriteLine($"Sent to Player {clientEntry.PlayerId}: {shipPositionsMessage}");
                }
            }
        }
    }

    static void Main(string[] args)
    {
        StartServer().GetAwaiter().GetResult();
    }
}

// Define the ClientInfo class
public class ClientInfo
{
    public TcpClient Client { get; set; }
    public int PlayerId { get; set; } // Player ID (1, 2, or -1 for spectators)
}