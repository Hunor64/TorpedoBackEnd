using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class TorpedoGameServer
{
    private const int Port = 65432;

    public static async Task StartServer()
    {
        TcpListener server = new TcpListener(IPAddress.Any, Port);
        server.Start();
        Console.WriteLine($"Server listening on port {Port}");

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            Console.WriteLine("Client connected.");
            _ = HandleClient(client);
        }
    }

    private static async Task HandleClient(TcpClient client)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
            {
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Received: {message}");

                // Send back the received message
                byte[] responseData = Encoding.UTF8.GetBytes(message);
                await stream.WriteAsync(responseData, 0, responseData.Length);
            }
        }
    }

    static void Main(string[] args)
    {
        StartServer().GetAwaiter().GetResult();
    }
}