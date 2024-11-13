using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TorpedoGameServer
{
    class Program
    {
        static void Main(string[] args)
        {
            ExecuteServer();
        }

        public static void ExecuteServer()
        {
            IPHostEntry ipHost = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress ipAddr = ipHost.AddressList[0];
            IPEndPoint localEndPoint = new IPEndPoint(ipAddr, 11111);
            Socket listener = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(10);

                Console.WriteLine("Waiting for player connections...");

                while (true)
                {
                    Socket clientSocket = listener.Accept();
                    Console.WriteLine("Player connected!");
                    HandlePlayer(clientSocket);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e}");
            }
        }

        private static void HandlePlayer(Socket clientSocket)
        {
            // Data buffer and message handling
            while (true)
            {
                byte[] bytes = new byte[1024];
                string data = null;

                while (true)
                {
                    int numByte = clientSocket.Receive(bytes);
                    data += Encoding.ASCII.GetString(bytes, 0, numByte);
                    if (data.IndexOf("<EOF>") > -1)
                    {
                        data = data.Substring(0, data.IndexOf("<EOF>"));
                        Console.WriteLine("Received command: {0}", data);
                        break; // Break if EOF received, waiting for a new message
                    }
                }

                if (data.ToLower() == "exit")
                {
                    Console.WriteLine("Player disconnected.");
                    break; // Close connection on "exit" command
                }

                // Process game commands
                string response = ProcessGameCommand(data);
                byte[] message = Encoding.ASCII.GetBytes(response);
                clientSocket.Send(message);
            }

            clientSocket.Shutdown(SocketShutdown.Both);
            clientSocket.Close();
        }

        private static string ProcessGameCommand(string command)
        {
            // Placeholder for game command processing logic
            // For example, handling commands like "shoot", "move", "status", etc.
            switch (command.ToLower())
            {
                case "shoot":
                    return "Torpedo fired!<EOF>";
                case "status":
                    return "Game status: All systems operational.<EOF>";
                case "move":
                    return "Moved to new location.<EOF>";
                default:
                    return "Unknown command. Please try again.<EOF>";
            }
        }
    }
}