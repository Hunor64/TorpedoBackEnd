using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq; // For LINQ methods
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
    private static ConcurrentDictionary<int, List<Ship>> playerShips = new ConcurrentDictionary<int, List<Ship>>();

    // Track the current player's turn
    private static int currentPlayerTurn = 1;

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
                byte[] buffer = new byte[4096];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"Received from Client {clientId} (Player {playerId}): {message}");

                    // Process the message
                    string responseMessage = ProcessMessage(message, clientId);

                    if (!string.IsNullOrEmpty(responseMessage))
                    {
                        byte[] responseData = Encoding.UTF8.GetBytes(responseMessage);
                        await stream.WriteAsync(responseData, 0, responseData.Length);
                        Console.WriteLine($"Sent to Client {clientId}: {responseMessage}");
                    }
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
                playerShips.TryRemove(playerId, out _);
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
            // Receive ship positions from the client
            var json = message.Substring("SHIPSPLACED_".Length);
            var shipsData = JsonSerializer.Deserialize<List<ShipData>>(json);

            if (shipsData != null)
            {
                var ships = shipsData.Select(sd => new Ship
                {
                    Name = sd.Name,
                    Size = sd.Cells.Count,
                    Cells = sd.Cells.Select(c => new Cell { X = c.X, Y = c.Y, IsHit = false }).ToList()
                }).ToList();

                playerShips[playerId] = ships;
                shipsPlaced[playerId] = true;
                Console.WriteLine($"Player {playerId} has placed ships.");

                // Check if both players have placed their ships
                if (shipsPlaced.Count == 2 && shipsPlaced.Values.All(placed => placed))
                {
                    // Both players are ready, send "READY" message to both
                    SendMessageToPlayers("READY");
                }

                return $"Player {playerId} ships received.";
            }
            else
            {
                return "Invalid ship data.";
            }
        }
        else if (message.StartsWith("FIRE_"))
        {
            // Handle firing action
            var parts = message.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y))
            {
                return HandleFiringAction(playerId, x, y);
            }
            else
            {
                return "Invalid fire command.";
            }
        }

        // Handle other messages
        return $"Player {playerId} says: {message}";
    }

    private static string HandleFiringAction(int playerId, int x, int y)
    {
        int opponentId = playerId == 1 ? 2 : 1;

        if (!playerShips.TryGetValue(opponentId, out var opponentShips))
            return "Opponent ships not available.";

        var opponentClient = clients.Values.FirstOrDefault(c => c.PlayerId == opponentId);

        if (opponentClient == null)
            return "Opponent not connected.";

        // Check if the cell has already been hit
        foreach (var ship in opponentShips)
        {
            var cell = ship.Cells.FirstOrDefault(c => c.X == x && c.Y == y);
            if (cell != null)
            {
                if (cell.IsHit)
                {
                    return "Cell already hit.";
                }
                else
                {
                    cell.IsHit = true;

                    // Check if the ship is sunk
                    bool isSunk = ship.Cells.All(c => c.IsHit);

                    // Check if the game is over
                    bool gameOver = opponentShips.All(s => s.Cells.All(c => c.IsHit));

                    // Send hit result to the firing player
                    SendMessageToClient(playerId, $"FIRE_RESULT_HIT_{x}_{y}");

                    // Notify the opponent that their ship was hit
                    SendMessageToClient(opponentId, $"OPPONENT_HIT_{x}_{y}");

                    if (isSunk)
                    {
                        SendMessageToClient(playerId, $"SHIP_SUNK_{ship.Name}");
                        SendMessageToClient(opponentId, $"YOUR_SHIP_SUNK_{ship.Name}");
                    }

                    if (gameOver)
                    {
                        SendMessageToPlayers($"GAME_OVER_Player_{playerId}_Wins");
                    }
                    else
                    {
                        // Switch turns
                        currentPlayerTurn = opponentId;
                        SendMessageToPlayers($"NEXT_TURN_{currentPlayerTurn}");
                    }

                    return null; // Response already sent
                }
            }
        }

        // Missed shot
        SendMessageToClient(playerId, $"FIRE_RESULT_MISS_{x}_{y}");
        SendMessageToClient(opponentId, $"OPPONENT_MISS_{x}_{y}");

        // Switch turns
        currentPlayerTurn = opponentId;
        SendMessageToPlayers($"NEXT_TURN_{currentPlayerTurn}");

        return null; // Response already sent
    }

    private static void SendMessageToClient(int playerId, string message)
    {
        var clientInfo = clients.Values.FirstOrDefault(c => c.PlayerId == playerId);
        if (clientInfo != null)
        {
            NetworkStream stream = clientInfo.Client.GetStream();
            byte[] responseData = Encoding.UTF8.GetBytes(message);
            _ = stream.WriteAsync(responseData, 0, responseData.Length);
            Console.WriteLine($"Sent to Player {playerId}: {message}");
        }
    }

    private static void SendMessageToPlayers(string message)
    {
        foreach (var clientEntry in clients.Values)
        {
            if (clientEntry.PlayerId != -1)
            {
                NetworkStream stream = clientEntry.Client.GetStream();
                byte[] responseData = Encoding.UTF8.GetBytes(message);
                _ = stream.WriteAsync(responseData, 0, responseData.Length);
                Console.WriteLine($"Sent to Player {clientEntry.PlayerId}: {message}");
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

// Define the Ship and Cell classes
public class Ship
{
    public string Name { get; set; }
    public int Size { get; set; }
    public List<Cell> Cells { get; set; } = new List<Cell>();
}

public class Cell
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsHit { get; set; }
}

// Data transfer objects for serialization
public class ShipData
{
    public string Name { get; set; }
    public List<CellData> Cells { get; set; }
}

public class CellData
{
    public int X { get; set; }
    public int Y { get; set; }
}
