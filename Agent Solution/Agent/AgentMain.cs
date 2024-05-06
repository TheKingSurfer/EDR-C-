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

        /// <summary>
        /// Entry point of the application that establishes a connection to a server via TCP, 
        /// sets up data processing and communication threads,
        /// and manages the monitoring and execution of commands.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        
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

        /// <summary>Sends data to the server through a network stream.</summary>
        /// <param name="nwStream">The network stream used for communication.</param>
        /// <param name="data">The data to be sent to the server.</param>
        static void SendDataToServer(NetworkStream nwStream, string data)
        {
            // Create a JSON array from the provided data
            string jsonArray = "[" + string.Join(",", data) + "]";

            byte[] buffer = Encoding.UTF8.GetBytes(jsonArray + Environment.NewLine);
                nwStream.Write(buffer, 0, buffer.Length);
        }

        /// <summary>Receives data from the server via a network stream.</summary>
        /// <param name="nwStream">The network stream used for communication.</param>
        /// <remarks>This method reads data from the network stream in chunks of 1024 bytes,
        /// decodes the bytes to UTF-8 encoded string, and displays the received data on the console.</remarks>
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
        /// <summary>
        /// Starts a new thread with the specified name and function.
        /// </summary>
        /// <param name="threadName">The name of the thread.</param>
        /// <param name="threadFunction">The function to be executed in the thread.</param>
        static void StartThread(string threadName, Action threadFunction)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{threadName} started.");
            Console.ResetColor();

            threadFunction.Invoke();
        }
    }
}
