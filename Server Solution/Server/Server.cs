﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
using Microsoft.Diagnostics.Tracing.Parsers.ClrPrivate;
using Microsoft.Diagnostics.Tracing.Parsers.JSDumpHeap;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftWindowsWPF;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProcessC;
using VirusTotalNet;
using VirusTotalNet.Objects;
using VirusTotalNet.ResponseCodes;
using VirusTotalNet.Results;



class Server
{
    const int PORT_NO = 5000; // Port number for the server
    const string SERVER_IP = "127.0.0.1"; // IP address for the server

    private TcpListener tcpListener;
    private Thread listenerThread;
    private static List<string> connectedClients = new List<string>();

    private static List<string> connectedClientsForPV = new List<string>();
    private static bool ProcessViewFlag = false; // if its true the there will be alwayas data added to a certain dict
    private static Dictionary<string, List<string>> PVData = new Dictionary<string, List<string>>();
    



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
            if (CheckForProcessViewRequest(client))
            {

                Console.WriteLine("Good!!!!!!!!");
                //TODO: activate some function that will Send the processes data to specific clients

                continue;
            }

            // Notify the WebSocket server when a new client is connected


            NotifyWebSocketServer(client);
            Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientComm));


            // Get the client's IP address and port number
            var endPoint = (IPEndPoint)client.Client.RemoteEndPoint;
            string ipAddress = endPoint.Address.ToString();
            int port = endPoint.Port;


            //checks the type of the connection (PV or not)
            //maybe i dont need it and i only do will do the whole check on the handle communication
           
            
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

            JObject requestData = JObject.Parse(message);
            string clientIdentifier = $"{requestData["ClientIP"]}:{requestData["ClientPort"]}";

            // Add client to the list if the flag is true
            if ((bool)requestData["SendProcessData"])
            {
                connectedClientsForPV.Add(clientIdentifier);
                ProcessViewFlag = true;
            }
            else
            {
                // Remove the client from the list if the flag is false
                connectedClientsForPV.RemoveAll(identifier => identifier == clientIdentifier);
                if (connectedClientsForPV.Count() == 0)
                {
                    Console.WriteLine("the list is empty - changing the flag to false ");
                    ProcessViewFlag = false;
                }
            }

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
        var endPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
        string ipAddress = endPoint.Address.ToString();
        int port = endPoint.Port;

        string clientIdentifier = $"{ipAddress}:{port}";

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
                    // Try parsing the JSON array => it works
                    JArray jsonArray = JArray.Parse(jsonData);

                    // Process each JSON object in the array
                    foreach (var jsonObject in jsonArray)
                    {
                        //print when found processes that has been started
                        JObject jObject = jsonObject as JObject;
                        CheckSpecialEvents(jsonObject, clientStream, tcpClient);
                        
                        //NEW code snippet - more efficient
                        //check if there is already previeos data of a certain client
                        if (!PVData.ContainsKey(clientIdentifier))
                        {
                            PVData[clientIdentifier] = new List<string>();

                                
                        }

                        if (jsonObject["EventName"]?.ToString() == "********* FileIOReadWrite *********")
                        {
                            var fileHashCode = jsonObject["FileHashCode"]?.ToString();
                            if (!string.IsNullOrEmpty(fileHashCode))
                            {
                                //Console.WriteLine( fileHashCode);
                                // TODO: call the function and use VT to check the file hash code 
                            }
                        }
                        
                        if (jsonObject["EventName"]?.ToString() == "ProcessStarted")
                        {
                            var jObj = (JObject)jsonObject;
                            foreach (var property in jObj.Properties())
                            {
                                PVData[clientIdentifier].Add($"{property.Name}: {property.Value}");
                            }
                        }
                        else if (jsonObject["EventName"]?.ToString() == "ProcessEnded")
                        {
                            var endedProcessId = (int?)jsonObject["ProcessId"];
                            if (endedProcessId.HasValue)
                            {
                                PVData[clientIdentifier].RemoveAll(item => item.Contains($"ProcessId: {endedProcessId}"));
                            }
                        }

                        if (connectedClientsForPV.Contains(clientIdentifier)&& !(PVData[clientIdentifier].Count() == 0))
                        {
                            SendPVDataOfertainClient(clientIdentifier, PVData[clientIdentifier]);
                        }
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
            
            string clientInfo = $"{ipAddress}:{port}";
            connectedClients.Remove(clientInfo);
            NotifyWebSocketServerRemoved(clientInfo);


        }
        finally
        {
            tcpClient.Close();
        }
    }

    //will check the file hashcode using VT
    public static async Task CheckFileHashCode(byte[]bytes )
    {
        VirusTotal virusTotal = new VirusTotal("eb8e004f4740126a984d7db424e9aad0fe368a13a50d995ac2c00bbe5e3675a2");

        //Use HTTPS instead of HTTP
        virusTotal.UseTLS = true;

        
        //byte[] eicar = Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        //Console.WriteLine($"eicar bytes : {string.Join(" ",eicar)}"); => prints out  89 98 123 and so forth

        //Check if the file has been scanned before.
        FileReport fileReport = await virusTotal.GetFileReportAsync(bytes);

        bool hasFileBeenScannedBefore = fileReport.ResponseCode == FileReportResponseCode.Present;

        Console.WriteLine("File has been scanned before: " + (hasFileBeenScannedBefore ? "Yes" : "No"));
        PrintScan(fileReport);
       
    }



    // should sends everything that is already in a big dictionary that stores all of the processes, and the function will activate a asycn task that will always update the connection
    private static void SendPVDataOfertainClient(string clientIdentifier , List<string> data) 
    {
        try
        {

            //TODO add an if that will check if the list is empty - if empty do not send


            // Create a dictionary with the required data
            var dict = new Dictionary<string, List<string>>();
            dict.Add(clientIdentifier, data);

            // Serialize the dictionary to JSON format
            string jsonData = JsonConvert.SerializeObject(dict);

            // Use a WebClient to send a notification to the WebSocket server
            using (var webClient = new WebClient())
            {
                webClient.Headers[HttpRequestHeader.ContentType] = "application/json"; // Set content type to JSON
                webClient.UploadString("http://localhost:8080/collect-PV", jsonData);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending event data to WebSocket server: {ex.Message}");
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
                    //for bytes
                    if (property.Key == "fileBytes")
                    {
                        string base64EncodedString = property.Value.ToString();
                        byte[]bytes= Convert.FromBase64String(base64EncodedString);
                        //Console.WriteLine($"{property.Key} : {string.Join("",bytes)}");
                        //CheckFileHashCode(bytes);

                    }
                    else
                    {
                        Console.WriteLine($"{property.Key} : {property.Value}");
                    }
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


    public static void PrintScan(FileReport fileReport)
    {
        Console.WriteLine("Scan ID: " + fileReport.ScanId);
        Console.WriteLine("Message: " + fileReport.VerboseMsg);

        if (fileReport.ResponseCode == FileReportResponseCode.Present)
        {
            foreach (KeyValuePair<string, ScanEngine> scan in fileReport.Scans)
            {
                Console.WriteLine("{0,-25} Detected: {1}", scan.Key, scan.Value.Detected);
            }
        }

        Console.WriteLine();
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
