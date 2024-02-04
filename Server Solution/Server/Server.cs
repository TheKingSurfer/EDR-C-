using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProcessC;

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
            Console.WriteLine($"Client Detected {clientCounter} : IP - {client.ToString()}");

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
                        CheckSpecialEvents(jsonObject,clientStream);

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
        }
        finally
        {
            tcpClient.Close();
        }
    }

    static void CheckSpecialEvents(JToken jsonObject, NetworkStream nwStream)
    {
        // Check if the object has a "FileName" property
        if (jsonObject["FileName"] != null)
        {
            string fileName = jsonObject["FileName"].ToString();
            int ProcessId=0;

            if (Array.Exists(protectedFiles, file => fileName.Contains(file)))
            {
                foreach (var property in (JObject)jsonObject)
                {
                    Console.WriteLine($"{property.Key} : {property.Value}");
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
                pauseCommunication = true;

                Console.WriteLine("**************************************");
                Console.WriteLine("Protected file found! Sending command to agent...");
                Console.WriteLine("Press any key to resume process");
                PrintProcessState(ProcessId);

                // Send a command to suspend the process
                var command = new ProcessCommand("SuspendProcess", ProcessId);
                ProcessCommand.SendCommandToAgent(nwStream, command);
                Console.WriteLine($"* suspicious process is currently suspended *");


                // Wait for a key press
                Console.ReadKey(true);




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

    static void Main(string[] args)
    {
        Server server = new Server();
        Console.ReadLine();
    }
}
