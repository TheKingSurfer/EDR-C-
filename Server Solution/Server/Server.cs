using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class Server
{
    const int PORT_NO = 5000; // Port number for the server
    const string SERVER_IP = "127.0.0.1"; // IP address for the server

    private TcpListener tcpListener;
    private Thread listenerThread;

   
    private static string[] protectedFiles = { "Desktop.txt" };// this array will be set by the user - protected files

    // Flag to indicate whether communication should be paused
    private static bool pauseCommunication = false;

    public Server()
    {
        this.tcpListener = new TcpListener(IPAddress.Any, PORT_NO);
        this.listenerThread = new Thread(new ThreadStart(ListenForClients));
        this.listenerThread.Start();
    }

    private void ListenForClients()
    {
        this.tcpListener.Start();
        Console.WriteLine($"Server Started - Server IP{SERVER_IP} : Port {PORT_NO}");
        int clientCounter = 0;

        while (true)
        {
            clientCounter++;
            // Blocks until a client has connected to the server
            TcpClient client = this.tcpListener.AcceptTcpClient();

            // Create a thread to handle communication with the connected client
            Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientComm));
            Console.WriteLine($"Client Detected {clientCounter}");
            clientThread.Start(client);
        }
    }

    private void HandleClientComm(object clientObj)
    {
        TcpClient tcpClient = (TcpClient)clientObj;
        NetworkStream clientStream = tcpClient.GetStream();

        // Using a StreamReader to simplify reading from the stream
        StreamReader reader = new StreamReader(clientStream, Encoding.UTF8);

        Console.WriteLine("Client connected...");

        try
        {
            while (true)
            {
                // Read a line from the stream
                string jsonData = reader.ReadLine();

                if (jsonData == null)
                    break;

                try
                {
                    // Try parsing the JSON array
                    JArray jsonArray = JArray.Parse(jsonData);

                    // Process each JSON object in the array
                    foreach (var jsonObject in jsonArray)
                    {
                        // Check for protected files
                        CheckSpecialEvents(jsonObject);

                        // Print each key-value pair
                        foreach (var property in (JObject)jsonObject)
                        {
                            Console.WriteLine($"{property.Key} : {property.Value}");
                        }

                        Console.WriteLine("\n");
                    }
                }
                catch (JsonReaderException)
                {
                    Console.WriteLine("Invalid JSON format, skipping...");
                    Console.WriteLine($"Received data: {jsonData}");
                    Console.WriteLine("\n");

                    continue;
                }
            }
        }
        catch (IOException)
        {
            Console.WriteLine("Client disconnected...");
        }
        finally
        {
            tcpClient.Close();
        }
    }

    static void CheckSpecialEvents(JToken jsonObject)
    {
        // Check if the object has a "FileName" property
        if (jsonObject["FileName"] != null)
        {
            string fileName = jsonObject["FileName"].ToString();

            
            if (Array.Exists(protectedFiles, file => fileName.Contains(file)))
            {
                foreach (var property in (JObject)jsonObject)
                {
                    Console.WriteLine($"{property.Key} : {property.Value}");
                }
                Console.WriteLine("**************************************");
                Console.WriteLine("Protected file found! Communication paused. Press any key to resume...");

                // pause communication
                pauseCommunication = true;

                // Wait for a key press
                Console.ReadKey(true);

                // resume communication
                pauseCommunication = false;
            }
        }
    }

    static void Main(string[] args)
    {
        Server server = new Server();
        Console.ReadLine();
    }
}
