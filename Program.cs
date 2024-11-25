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
            _ = HandleClient(client, clientId);
        }
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
        // For example, if the message is a hit on a cell, you can log it or update game state
        // For now, we will just echo the message back with the client ID

        return $"Client {clientId} says: {message}";
    }

    static void Main(string[] args)
    {
        StartServer().GetAwaiter().GetResult();
    }
}