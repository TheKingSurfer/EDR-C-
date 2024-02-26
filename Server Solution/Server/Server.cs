using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
using Microsoft.Diagnostics.Tracing.Parsers.JSDumpHeap;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProcessC;

class Server
{
    const int PORT_NO = 5000; // Port number for the server
    const string SERVER_IP = "127.0.0.1"; // IP address for the server

    private TcpListener tcpListener;
    private Thread listenerThread;
    private static List<string> connectedClients = new List<string>();
    private static bool TaskManagerFlag = false;



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
            TcpClient client = this.tcpListener.AcceptTcpClient();

            // Notify the WebSocket server when a new client is connected


            NotifyWebSocketServer(client);
            Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientComm));


            // Get the client's IP address and port number
            var endPoint = (IPEndPoint)client.Client.RemoteEndPoint;
            string ipAddress = endPoint.Address.ToString();
            int port = endPoint.Port;


            //checks the type of the connection (PV or not)
            if (CheckForProcessViewRequest(client))
            {
                Console.WriteLine("Good!!!!!!!!");
                //TODO: activate some function that will Send the processes data to specific clients
                continue;
            }
            
            Console.WriteLine($"Client Detected {clientCounter} : IP - {ipAddress}:{port}");
            clientThread.Start(client);
        }
    }


    public static bool CheckForProcessViewRequest(object clientObj)
    {
        TcpClient tcpClient = (TcpClient)clientObj;
        NetworkStream clientStream = tcpClient.GetStream();

        // Using a StreamReader to read the message from the client stream
        StreamReader reader = new StreamReader(clientStream, Encoding.UTF8);
        Console.WriteLine("Checking type of connection");

        // Read the message from the client
        string message = reader.ReadLine();

        // Check if the message contains "SendProcessData" in its data
        if (message != null && message.Contains("SendProcessData"))
        {
            Console.WriteLine("Process view request received.");
            return true; // Return true if the message contains "SendProcessData"
        }
        else
        {
            Console.WriteLine("Not a process view request.");
            return false; // Return false if the message does not contain "SendProcessData"
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
                        CheckSpecialEvents(jsonObject, clientStream, tcpClient);

                        //foreach (var property in (JObject)jsonObject)
                        //{
                        //    Console.WriteLine($"{property.Key} : {property.Value}");
                        //}

                        //Console.WriteLine("\n");
                    }
                }
                catch (JsonReaderException)
                {
                    Console.WriteLine("Invalid JSON format, skipping...");
                    Console.WriteLine($"Received data: {jsonData}\n");
                    Console.WriteLine("\n");

                    continue;
                }
            }
        }
        catch (IOException)
        {
            Console.WriteLine("Client disconnected...");

            // Remove the disconnected client from the connected clients list
            var endPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
            string ipAddress = endPoint.Address.ToString();
            int port = endPoint.Port;
            string clientInfo = $"{ipAddress}:{port}";
            connectedClients.Remove(clientInfo);
            NotifyWebSocketServerRemoved(clientInfo);


        }
        finally
        {
            tcpClient.Close();
        }
    }


    static void CheckSpecialEvents(JToken jsonObject, NetworkStream nwStream, TcpClient client)
    {
        // Check if the object has a "FileName" property
        if (jsonObject["FileName"] != null)
        {
            string fileName = jsonObject["FileName"].ToString();
            int ProcessId = 0;
            string eventDataString = "";
            if (Array.Exists(protectedFiles, file => fileName.Contains(file)))
            {
                foreach (var property in (JObject)jsonObject)
                {
                    Console.WriteLine($"{property.Key} : {property.Value}");
                    eventDataString += $"{property.Key}: {property.Value}\n";
                    //extracting the process id 
                    if (property.Key == "ProcessId")
                    {
                        try
                        {
                            //parsing the value in the json
                            ProcessId = int.Parse(jsonObject["ProcessId"].ToString());
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    }
                }
                var endPoint = (IPEndPoint)client.Client.RemoteEndPoint;
                string ipAddress = endPoint.Address.ToString();
                int port = endPoint.Port;
                SendEventDataToWebSocketServer(ipAddress, port, eventDataString);
                pauseCommunication = true;

                Console.WriteLine("**************************************");
                Console.WriteLine("Protected file found! Sending command to agent...");
                //Console.WriteLine("Press any key to resume process");
                PrintProcessState(ProcessId);

                // Send a command to suspend the process
                var command = new ProcessCommand("SuspendProcess", ProcessId);
                ProcessCommand.SendCommandToAgent(nwStream, command);
                Console.WriteLine($"* suspicious process is currently suspended *");


                // Wait for a key press
                //Console.ReadKey(true);




                Console.WriteLine("*suspicious process is resumed*");
                // Send a command to resume the process
                command = new ProcessCommand("ResumeProcess", ProcessId); // You need to get the actual processId
                ProcessCommand.SendCommandToAgent(nwStream, command);
                pauseCommunication = true;
            }
        }
    }



    static void PrintProcessState(int processId)
    {
        try
        {
            System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);

            Console.WriteLine($"Process ID: {processId}");
            //Console.WriteLine($"Process State: {(process.HasExited ? "Exited" : "Running")}"); System.ComponentModel.Win32Exception: 'Access is denied'
            Console.WriteLine($"Process Name : {process.ProcessName}");

            Console.WriteLine($"Is Suspended: {AreAllThreadsSuspended(process)}");

        }
        catch (ArgumentException)
        {
            Console.WriteLine($"Process with ID {processId} not found.");
        }

        //Microsoft.Diagnostics.Tracing.AutomatedAnalysis.Process process = Microsoft.Diagnostics.Tracing.AutomatedAnalysis.Process. ;
        //Console.WriteLine($"process path : {process.}");
    }
    static bool AreAllThreadsSuspended(System.Diagnostics.Process process)
    {
        foreach (ProcessThread thread in process.Threads)
        {
            if (thread.ThreadState == System.Diagnostics.ThreadState.Wait)
            {
                return false;
            }
        }
        return true;
    }


    private static void NotifyWebSocketServerRemoved(string message)
    {
        

        using (var webClient = new WebClient())
        {
            
            webClient.Headers[HttpRequestHeader.ContentType] = "application/json"; // Set content type to JSON
            webClient.UploadString("http://localhost:8080/notify-remove", message);
        }
    }


    private static void NotifyWebSocketServer(TcpClient client)
    {
        // Get the client's IP address and port number
        var endPoint = (IPEndPoint)client.Client.RemoteEndPoint;
        string ipAddress = endPoint.Address.ToString();
        int port = endPoint.Port;

        // Use a WebClient to send a notification to the WebSocket server
        using (var webClient = new WebClient())
        {
            // Include the IP address and port number in the message in the correct format
            string message = $"{ipAddress}:{port}";
            connectedClients.Add(message);// add the client info into the array
            webClient.Headers[HttpRequestHeader.ContentType] = "application/json"; // Set content type to JSON
            webClient.UploadString("http://localhost:8080/notify", message);
        }
    }



    /// <summary>
    /// function that sends task manager-like data the data service and from there the data 
    /// will be tranferd to the right websocket connection - this happens only if the websocket
    /// connection activates the TaskManagerFlag
    /// </summary>
    /// <param name="ipaddr"></param>
    /// <param name="portNumber"></param>
    /// <param name="Data"></param>
    private static void SendAllDataToWebsocket(string ipaddr, string portNumber, string Data ) 
    {

    }

    private static void SendEventDataToWebSocketServer(string ipAddress, int port, string eventData)
    {
        try
        {
            // Create a dictionary with the required data
            var data = new Dictionary<string, object>
        {
            { "ClientIpAddress", ipAddress },
            { "ClientPort", port.ToString() }, // Convert port to string
            { "EventData", eventData }
        };

            // Serialize the dictionary to JSON format
            string jsonData = JsonConvert.SerializeObject(data);

            // Use a WebClient to send a notification to the WebSocket server
            using (var webClient = new WebClient())
            {
                webClient.Headers[HttpRequestHeader.ContentType] = "application/json"; // Set content type to JSON
                webClient.UploadString("http://localhost:8080/event-notification", jsonData);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending event data to WebSocket server: {ex.Message}");
        }
    }




    static void Main(string[] args)
    {
        Server server = new Server();
        Console.ReadLine();
    }
}
