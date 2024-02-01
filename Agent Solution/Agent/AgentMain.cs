using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

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

            // Create three threads
            Thread sendDataThread = new Thread(() => StartThread("SendDataThread", () => edrProcessor.StartMonitoring()));
            Thread receiveDataThread = new Thread(() => StartThread("ReceiveDataThread", () => ReceiveDataFromServer(nwStream)));
            Thread executeCommandsThread = new Thread(() => StartThread("ExecuteCommandsThread", () => ExecuteCommands()));

            // Start the threads
            sendDataThread.Start();
            receiveDataThread.Start();
            executeCommandsThread.Start();

            Console.WriteLine("Agent is running. Press Enter to stop monitoring.");
            Console.ReadLine();

            // Stop the monitoring and wait for threads to finish
            edrProcessor.StopMonitoring();
            sendDataThread.Join();
            receiveDataThread.Join();
            executeCommandsThread.Join();

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

        static void ReceiveDataFromServer(NetworkStream nwStream)
        {
            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead;

                while ((bytesRead = nwStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"Received from server: {receivedData}");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ReceiveDataFromServer: {ex.Message}");
            }
        }

        static void ExecuteCommands()
        {
            
        }

        static void StartThread(string threadName, Action threadFunction)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{threadName} started.");
            Console.ResetColor();

            threadFunction.Invoke();
        }
    }
}
