using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class TorpedoGameServer
{
    private const int Port = 65432;
    private static ConcurrentDictionary<int, TcpClient> clients = new ConcurrentDictionary<int, TcpClient>();
    private static int clientIdCounter = 1;

    public static async Task StartServer()
    {
        TcpListener server = new TcpListener(IPAddress.Any, Port);
        server.Start();
        Console.WriteLine($"Server listening on port {Port}");

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            int clientId = clientIdCounter++;
            clients.TryAdd(clientId, client);
            Console.WriteLine($"Client {clientId} connected.");

            // Send the player ID to the client immediately upon connection
            if (!await SendPlayerID(client, clientId))
            {
                Console.WriteLine($"Client {clientId} has been disconnected due to invalid player ID.");
                client.Close();
                continue; // Skip further processing for this client
            }

            // Start handling the client
            _ = HandleClient(client, clientId);
        }
    }

    private static async Task<bool> SendPlayerID(TcpClient client, int clientId)
    {
        string playerIdMessage;

        // Check the value of clientId and respond accordingly
        if (clientId > 2)
        {
            playerIdMessage = "PlayerID:-1"; // Send -1 to indicate an invalid player ID
        }
        else
        {
            playerIdMessage = $"PlayerID:{clientId}"; // Format the message to indicate it's a valid player ID
        }

        byte[] responseData = Encoding.UTF8.GetBytes(playerIdMessage);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(responseData, 0, responseData.Length);

        return clientId <= 2; // Return true if valid, false if invalid
    }

    private static async Task HandleClient(TcpClient client, int clientId)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
            {
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Received from Client {clientId}: {message}");

                // Process the message (e.g., hit a cell)
                string responseMessage = ProcessMessage(message, clientId);
                byte[] responseData = Encoding.UTF8.GetBytes(responseMessage);
                await stream.WriteAsync(responseData, 0, responseData.Length);
            }
        }

        clients.TryRemove(clientId, out _);
        Console.WriteLine($"Client {clientId} disconnected.");
    }

    private static string ProcessMessage(string message, int clientId)
    {
        // Here you can implement logic to process the message
        // For now, we will just echo the message back with the client ID
        return $"Client {clientId} says: {message}";
    }

    static void Main(string[] args)
    {
        StartServer().GetAwaiter().GetResult();
    }
}