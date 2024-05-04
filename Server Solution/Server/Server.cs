using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
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
    private static Dictionary<byte[] , string> VTCheckedBytes = new Dictionary<byte[] , string>(); // => will host byte array with a string that will store the detection from the vt
    private static string[] protectedFiles = { "Desktop.txt" };// this array will be set by the user - protected files
    private static Dictionary<IPEndPoint, string[]> protectedFilesToAgent = new Dictionary<IPEndPoint, string[]>();
    private static Dictionary<string,string>VTCheckedHash = new Dictionary<string,string>();// => will host the hash code of a file and the string of the check itself from the vt

  


    // Flag to indicate whether communication should be paused
    private static bool pauseCommunication = false;

    public Server()
    {
        this.tcpListener = new TcpListener(IPAddress.Any, PORT_NO);
        this.listenerThread = new Thread(new ThreadStart(ListenForClients));
        this.listenerThread.Start();
    }

    /// <summary>
    /// Initializes a TCP listener and starts listening for client connections. Handles incoming client connections and starts a thread to handle communication with each client.
    /// </summary>
    private void ListenForClients()
    {
        this.tcpListener.Start();
        Console.WriteLine($"Server Started - Server IP{SERVER_IP} : Port {PORT_NO}");
        int clientCounter = 0;

        //string answer  = await CheckFileHashCode("275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f");
        //await Console.Out.WriteLineAsync(answer); - > works but ListenForClients has to be async


        while (true)
        {
            clientCounter++;
            TcpClient client = this.tcpListener.AcceptTcpClient();


            if (CheckForProcessViewRequest(client))
            {
                Console.WriteLine("Good!!!!!!!!");
                continue;
            }

            // Notify the WebSocket server when a new client is connected


            NotifyWebSocketServer(client);
            Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientComm));


            // Get the client's IP address and port number
            var endPoint = (IPEndPoint)client.Client.RemoteEndPoint;
            string ipAddress = endPoint.Address.ToString();
            int port = endPoint.Port;
            //if (!protectedFilesToAgent.ContainsKey(endPoint))
            //{
            //    protectedFilesToAgent.Add(endPoint, new string[0]);

            //}

            ////temporary
            //if(!protectedFilesToAgent[endPoint].Contains("Desktop.txt"))
            //{
            //    protectedFilesToAgent[endPoint].Append("Desktop");
            //}


            //checks the type of the connection (PV or not)
            //maybe i dont need it and i only do will do the whole check on the handle communication
           
            
            Console.WriteLine($"Client Detected {clientCounter} : IP - {ipAddress}:{port}");
            clientThread.Start(client);
        }
    }






    /// <summary>
    /// Checks if a client request is for process view. If so, adds or removes the client from the list of clients interested in process view data.
    /// </summary>
    /// <param name="clientObj">The client object representing the TCP connection.</param>
    /// <returns>True if the message contains a request for process view data; otherwise, false.</returns>
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



    /// <summary>
    /// Handles communication with a client over TCP/IP. Parses incoming JSON data, processes it, and manages a list of events for each client. 
    /// Also checks for special events such as file I/O read/write operations and processes started or ended. Maintains a list of client-specific data and sends it if requested by connected clients.
    /// </summary>
    /// <param name="clientObj">The client object representing the TCP connection.</param>

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
                            var executableHashCode = jsonObject["ExecutableHashCode"]?.ToString();
                            if (!string.IsNullOrEmpty(executableHashCode))
                            {
                                // TODO: call the function and use VT to check the file hash code 
                            }
                        }else if (jsonObject["EventName"]?.ToString() == "ProcessStarted")
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

    /// <summary>
    /// Checks the hash code of a file using VirusTotal API.
    /// </summary>
    /// <param name="HashCode">The hash code of the file to check.</param>
    /// <returns>A task representing the asynchronous operation, returning the scan results.</returns>

    public static async Task<string> CheckFileHashCode(string HashCode)
    {
        VirusTotal virusTotal = new VirusTotal("eb8e004f4740126a984d7db424e9aad0fe368a13a50d995ac2c00bbe5e3675a2");
        virusTotal.UseTLS = true;

        FileReport fileReport = await virusTotal.GetFileReportAsync(HashCode);
        bool hasFileBeenScannedBefore = fileReport.ResponseCode == FileReportResponseCode.Present;
        Console.WriteLine("File has been scanned before (Using Hash): " + (hasFileBeenScannedBefore ? "Yes" : "No"));
        string fileRep = ScanReturn(fileReport);
        //Console.WriteLine(fileRep);
        return fileRep;

    }

    /// <summary>
    /// Checks the bytes of a file using VirusTotal API.
    /// </summary>
    /// <param name="bytes">The byte array representing the file contents.</param>
    /// <returns>A task representing the asynchronous operation, returning the scan results.</returns>
    public static  async Task<string> CheckFileBytes(byte[]bytes )
    {
        VirusTotal virusTotal = new VirusTotal("eb8e004f4740126a984d7db424e9aad0fe368a13a50d995ac2c00bbe5e3675a2");

        //Use HTTPS instead of HTTP
        virusTotal.UseTLS = true;

        
        //byte[] eicar = Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        //Console.WriteLine($"eicar bytes : {string.Join(" ",eicar)}"); => prints out  89 98 123 and so forth

        //Check if the file has been scanned before - using its bytes.
        //FileReport fileReport = await virusTotal.GetFileReportAsync(bytes);

        FileReport fileReport = await virusTotal.GetFileReportAsync(bytes);

        bool hasFileBeenScannedBefore = fileReport.ResponseCode == FileReportResponseCode.Present;

        Console.WriteLine("File has been scanned before: " + (hasFileBeenScannedBefore ? "Yes" : "No"));
        string fileRep = ScanReturn(fileReport);
        //Console.WriteLine(fileRep);
        return fileRep;
       
    }


    /// <summary>
    /// Sends process view data for a specific client to the WebSocket server.
    /// </summary>
    /// <param name="clientIdentifier">The identifier for the client.</param>
    /// <param name="data">The process view data to send.</param>

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

    /// <summary>
    /// Checks for special events such as protected files or suspicious activities and takes appropriate actions.
    /// </summary>
    /// <param name="jsonObject">The JSON object containing event data.</param>
    /// <param name="nwStream">The network stream associated with the client connection.</param>
    /// <param name="client">The TCP client object representing the connection.</param>
    static async void CheckSpecialEvents(JToken jsonObject, NetworkStream nwStream, TcpClient client)
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
                    //For Hash
                    if (property.Key == "ExecutableHashCode")
                    {
                        string HashCode = property.Value.ToString();

                        //i can solve this using the lock function.
                        if (!VTCheckedHash.ContainsKey(HashCode))
                        {
                            string pairedValue = await CheckFileHashCode(HashCode);
                            if (VTCheckedHash.ContainsKey(HashCode)==false)
                            {
                                VTCheckedHash.Add(HashCode, pairedValue);
                            }
                        }
                    }
                    else 
                    { 
                    }

                    //for bytes
                    if (property.Key == "fileBytes")
                    {
                        string base64EncodedString = property.Value.ToString();
                        byte[]bytes= Convert.FromBase64String(base64EncodedString);
                        if (!VTCheckedBytes.ContainsKey(bytes))
                        {
                            //string pairedValue = await CheckFileHashCode(bytes);
                            //VTCheckedBytes.Add(bytes, pairedValue);
                        }
                        
                        

                        //Console.WriteLine($"{property.Key} : {string.Join("",bytes)}");
                        //CheckFileHashCode(bytes);

                    }
                    else
                    {
                        Console.WriteLine($"{property.Key} : {property.Value}");
                        eventDataString +=$"{property.Key} : {property.Value}\n";
                    }
                    
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

    /// <summary>
    /// Prints the scan results of a file report from VirusTotal.
    /// </summary>
    /// <param name="fileReport">The file report containing the scan results.</param>
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

    /// <summary>
    /// Returns the scan results of a file report from VirusTotal as a string.
    /// </summary>
    /// <param name="fileReport">The file report containing the scan results.</param>
    /// <returns>The scan results as a formatted string.</returns>
    public static string ScanReturn(FileReport fileReport)
    {

        string message = "";

        if (fileReport.ResponseCode == FileReportResponseCode.Present)
        {
            foreach (KeyValuePair<string, ScanEngine> scan in fileReport.Scans)
            {
                message += $"{scan.Key} Detected: {scan.Value.Detected}" + "\n";
            }
        }
        return message;
        
    }
    /// <summary>
    /// Prints the state of a process identified by its process ID.
    /// </summary>
    /// <param name="processId">The process ID of the process to print state for.</param>
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
    /// <summary>
    /// Checks if all threads of a process are suspended.
    /// </summary>
    /// <param name="process">The process to check threads for.</param>
    /// <returns>True if all threads are suspended; otherwise, false.</returns>
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

    /// <summary>
    /// Notifies the WebSocket server about a client disconnection.
    /// </summary>
    /// <param name="message">The message containing client information.</param>
    private static void NotifyWebSocketServerRemoved(string message)
    {
        

        using (var webClient = new WebClient())
        {
            
            webClient.Headers[HttpRequestHeader.ContentType] = "application/json"; // Set content type to JSON
            webClient.UploadString("http://localhost:8080/notify-remove", message);
        }
    }

    /// <summary>
    /// Notifies the WebSocket server about a client connection.
    /// </summary>
    /// <param name="client">The TCP client object representing the connected client.</param>
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
