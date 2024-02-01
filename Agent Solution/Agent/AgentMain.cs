using System;
using System.Net.Sockets;
using System.Text;

namespace EDR.Agent
{
    class AgentMain
    {
        const int PORT_NO = 5000; // Port number for the server
        const string SERVER_IP = "127.0.0.1"; // IP address for the server

        static void Main(string[] args)
        {
            TcpClient client = new TcpClient();
            client.Connect(SERVER_IP, PORT_NO);

            NetworkStream nwStream = client.GetStream();

            EDRProcessor edrProcessor = new EDRProcessor(data => SendDataToServer(nwStream, data));

            edrProcessor.StartMonitoring();

            
            Console.WriteLine("Agent is running. Press Enter to stop monitoring.");

            Console.ReadLine();  

            edrProcessor.StopMonitoring();

            // Close the connection
            client.Close();
        }

        static void SendDataToServer(NetworkStream nwStream, string data)
        {
            // Create a JSON array from the provided data
            string jsonArray = "[" + string.Join(",", data) + "]";

            
            byte[] buffer = Encoding.UTF8.GetBytes(jsonArray + Environment.NewLine);
            nwStream.Write(buffer, 0, buffer.Length);

            
        }
    }
}
