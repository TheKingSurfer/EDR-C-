﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static HttpListener httpListener;
    private static Thread listenerThread;
    private static List<string> connectedClients = new List<string>();
    private static List<WebSocket> webSocketConnection = new List<WebSocket>();//_> a list of the websocket connections
    //a dictionary that map event data to each according to the right client
    private static Dictionary<string, List<string>> clientEventDataDict = new Dictionary<string, List<string>>();
    //a dictionary that stores data in the following format "ip:portnumber":("IP","Port", Websocket Connection)
    private static Dictionary<string, (string connIp, string connPort, WebSocket)> clientWebSocketDict = new Dictionary<string, (string, string, WebSocket)>();
    //dict that stores the data and the client identifier -> "ip:port" , (data)   
    private static Dictionary<string, List<string>> PVDataDict = new Dictionary<string, List<string>>(); 
    //  the websocket connection that requesting the pv data 
    private static Dictionary<string,WebSocket> WebSocketsOfPV = new Dictionary<string, WebSocket>();
    // a list that contains the identifier of the websocket connection that want PV data
    private static List<string> connectionsForPV = new List<string>();


    




   





    /// <summary>
    /// Main entry point of the program that starts an HTTP listener to handle various requests.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// The method initializes an HTTP listener, starts it, and listens for incoming requests.
    /// Depending on the request path, it delegates the handling to specific methods.
    /// </remarks>
    static async Task Main()
    {
        httpListener = new HttpListener();
        httpListener.Prefixes.Add("http://localhost:8080/");
        httpListener.Start();

        Console.WriteLine("WebSocket Server is listening on http://localhost:8080/");

        //listenerThread = new Thread(ListenForClients);
        //listenerThread.Start();
        while (true)
        {
            var context = await httpListener.GetContextAsync();

            if (context.Request.Url.AbsolutePath == "/notify-remove")
            {
                HandleNotifyRemoveNotification(context);
            }
            else if (context.Request.Url.AbsolutePath == "/notify")
            {
                HandleNotification(context);
            }
            else if (context.Request.Url.AbsolutePath == "/event-notification")
            {
                ClientsDetailsNotification(context); // Call HandleNotifications for handling event notifications

            }
            else if (context.Request.Url.AbsolutePath == "/client-details-page")
            {
                ProcessWebSocketRequest(context);
            }
            else if (context.Request.Url.AbsolutePath == "/connected-clients")
            {
                ReturnConnectedClients(context);

            } else if(context.Request.Url.AbsolutePath == "/collect-PV")// this is the incoming data from the server 
            {
                CollectPV(context);
            }else if (context.Request.Url.AbsolutePath == "/view-processes-page")// when a websocket connection goes into the view processes
            {
                ProcessWebSocketRequest(context);
            }
            else if (context.Request.IsWebSocketRequest)
            {
                //ProcessWebSocketRequest(context);
                HandleFirstConnection(context);
            }

          
        }
    }

    //the incoming data will be sent to the js server here:
    /// <summary>
    /// Collects and processes data received from an HTTP listener context.
    /// </summary>
    /// <param name="context">The HTTP listener context containing the data.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task CollectPV(HttpListenerContext context) 
    {
        //Console.WriteLine("pv collected !");
        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
        {
            string message = reader.ReadToEnd();
            var receivedData = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(message);
            //Console.WriteLine($"received data from server: {message} ");

            foreach (var kvp in receivedData)
            {
                if (PVDataDict.ContainsKey(kvp.Key))
                {
                    PVDataDict[kvp.Key].AddRange(kvp.Value);
                }
                else
                {
                    PVDataDict[kvp.Key] = kvp.Value;
                }
            }

            foreach (var kvp in PVDataDict.Keys)
            {
                var websocket = WebSocketsOfPV[kvp];

                var data = JsonConvert.SerializeObject(PVDataDict[kvp]);

                var DataBuffer = Encoding.UTF8.GetBytes(data);

                // Send the event data to the WebSocket connection
                await websocket.SendAsync(new ArraySegment<byte>(DataBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }

            //Console.WriteLine(message);



        }

        context.Response.StatusCode = 200;
        context.Response.Close();
    }

    /// <summary>
    /// Processes a WebSocket request from an HTTP listener context.
    /// </summary>
    /// <param name="context">The HTTP listener context.</param>
    /// <remarks>
    /// This method accepts a WebSocket connection, handles incoming messages, and manages the WebSocket connection state.
    /// </remarks>
    private static async void ProcessWebSocketRequest(HttpListenerContext context)
    {
        

        try
        {
            HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
            Console.WriteLine("WebSocket connection established.");
            string ipAddress = context.Request.RemoteEndPoint.Address.ToString();
            int port = context.Request.RemoteEndPoint.Port;
            //Console.WriteLine($"WebSocket2 connected from IP: {ipAddress}, Port: {port}");


            WebSocket webSocket = webSocketContext.WebSocket;

            while (webSocket.State == WebSocketState.Open)
            {
                // Create a buffer to store incoming message data
                var buffer = new ArraySegment<byte>(new byte[4096]);

                // Receive message from WebSocket client
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);

                // Handle handshake messages
                try
                {
                    string message = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                    HandleHandshakeMessage(webSocket, message);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error handling handshake message: {e}");
                    // Handle the error as needed
                }
            }
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            context.Response.Close();
            Console.WriteLine($"WebSocket connection error: {ex.Message}");
            return;
        }
    }

    /// <summary>
    /// Notifies connected clients with the provided list of client information.
    /// </summary>
    /// <param name="connectedClients">The list of connected clients to notify.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task NotifyConnectedClient(List<string> connectedClients)
    {
        string clientInfo = JsonConvert.SerializeObject(connectedClients);

        foreach (var webSocket in webSocketConnection)
        {
            if (webSocket.State == WebSocketState.Open)
            {
                var buffer = Encoding.UTF8.GetBytes(clientInfo);
                await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Handles the notification to remove a client.
    /// </summary>
    /// <param name="context">The HttpListenerContext object representing the HTTP request and response.</param>
    /// <remarks>
    /// This method reads the message from the request input stream, removes the client from the connected clients list,
    /// prints a message to the console indicating the client disconnection, and then notifies the remaining connected clients.
    /// </remarks>
    private static void HandleNotifyRemoveNotification(HttpListenerContext context)
    {
        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
        {
            string message = reader.ReadToEnd();
                // Store the IP address and port number in a list
            connectedClients.Remove(message);
            Console.WriteLine($"Client disconnected: {message}");
                // Notify all connected clients about the new connection
            Task.Run(async () => await NotifyConnectedClient(connectedClients));
        }

        context.Response.StatusCode = 200;
        context.Response.Close();
    }

    /// <summary>
    /// Handles a notification received through an HTTP listener context.
    /// </summary>
    /// <param name="context">The HTTP listener context containing the notification.</param>
    /// <remarks>
    /// The notification message should be in the format 'IPAddress:Port'.
    /// If the message is valid, the client information is extracted, added to the list of connected clients,
    /// and a notification is sent to the connected client asynchronously.
    /// </remarks>
    private static void HandleNotification(HttpListenerContext context)
    {
        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
        {
            string message = reader.ReadToEnd();
            //Console.WriteLine($"Notification received: {message}");

            // Extract IP address and port number from the message
            string[] parts = message.Split(':');
            if (parts.Length == 2)
            {
                string ipAddress = parts[0];
                string port = parts[1];

                // Store the IP address and port number in a list
                string clientInfo = $"{ipAddress}:{port}";
                connectedClients.Add(clientInfo);
                Console.WriteLine($"Client connected: {clientInfo}");

                // Notify all connected clients about the new connection
                Task.Run(async () => await NotifyConnectedClient(connectedClients));
            }
            else
            {
                Console.WriteLine("Invalid notification format. Expected 'IPAddress:Port'.");
            }
        }

        context.Response.StatusCode = 200;
        context.Response.Close();
    }

    /// <summary>
    /// Returns the list of connected clients as a JSON string in the HTTP response.
    /// </summary>
    /// <param name="context">The HTTP listener context.</param>
    /// <remarks>
    /// This method sets the necessary CORS headers for allowing cross-origin requests.
    /// It serializes the list of connected clients into a JSON string and writes it to the response stream.
    /// </remarks>
    private static void ReturnConnectedClients(HttpListenerContext context)
    {
        // Enable CORS by adding the appropriate headers
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*"); // Allow requests from any origin
        context.Response.Headers.Add("Access-Control-Allow-Methods", "GET");
        context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        // Return the list of connected clients as a JSON array
        string response = Newtonsoft.Json.JsonConvert.SerializeObject(connectedClients);
        byte[] buffer = Encoding.UTF8.GetBytes(response);

        context.Response.ContentType = "application/json"; // Set content type to JSON
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.StatusCode = 200;
        context.Response.Close();
    }

    /// <summary>
    /// Handles the notification of client details received through an HTTP listener context.
    /// </summary>
    /// <param name="context">The HTTP listener context containing the request and response.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task ClientsDetailsNotification(HttpListenerContext context)
    {
        try
        {
            HttpListenerRequest request = context.Request;

            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                string message = reader.ReadToEnd();
                //Console.WriteLine($"Notification received: {message}");

                // Extract client IP address, port, and event data from the message
                JObject jsonNotification = JObject.Parse(message);

                string clientIpAddress = jsonNotification["ClientIpAddress"].ToString();
                string clientPort = jsonNotification["ClientPort"].ToString();
                string eventData = jsonNotification["EventData"].ToString();

                // Construct the client identifier using IP address and port number
                string clientIdentifier = $"{clientIpAddress}:{clientPort}";

                // Check if the client identifier exists in the dictionary
                if (!clientEventDataDict.ContainsKey(clientIdentifier))
                {
                    // If not, add a new entry with an empty list
                    clientEventDataDict[clientIdentifier] = new List<string>();
                }

                // Add the event data to the list corresponding to the client
                clientEventDataDict[clientIdentifier].Add(eventData);

                // Print the updated dictionary for debugging
                //Console.WriteLine("Client Event Data Dictionary:");
                //foreach (var kvp in clientEventDataDict)
                //{
                //    Console.WriteLine($"{kvp.Key}: {string.Join(", ", kvp.Value)}");
                //}

                // Check if the client WebSocket connection exists
                if (clientWebSocketDict.ContainsKey(clientIdentifier))
                {
                    var webSocket = clientWebSocketDict[clientIdentifier].Item3; // Get the WebSocket connection

                    // Serialize the event data
                    var eventDataJson = JsonConvert.SerializeObject(clientEventDataDict[clientIdentifier]);

                    // Convert the event data to bytes
                    var eventDataBuffer = Encoding.UTF8.GetBytes(eventDataJson);

                    // Send the event data to the WebSocket connection
                    await webSocket.SendAsync(new ArraySegment<byte>(eventDataBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }

            // Send a success response to the client
            context.Response.StatusCode = 200;
            context.Response.Close();
        }
        catch (Exception ex)
        {
            // Send an error response to the client
            context.Response.StatusCode = 500;
            context.Response.Close();
            Console.WriteLine($"WebSocket connection error: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns client events based on the provided IP address and port.
    /// </summary>
    /// <param name="context">The HttpListenerContext object representing the HTTP request and response.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    private static async Task ReturnClientEvents(HttpListenerContext context)
    {
        // Extract IP address and port number from query parameters
        string ipAddress = context.Request.QueryString.Get("ip");
        string port = context.Request.QueryString.Get("port");
        string clientKey = $"{ipAddress}:{port}";

        if (clientEventDataDict.ContainsKey(clientKey))
        {
            // Return client events as JSON response
            string response = Newtonsoft.Json.JsonConvert.SerializeObject(clientEventDataDict[clientKey]);
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(response);

            context.Response.ContentType = "application/json";
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.StatusCode = 200;
        }
        else
        {
            context.Response.StatusCode = 404; // Client events not found
        }

        context.Response.Close();
    }

    /// <summary>
    /// Cleans up an IP address by extracting the IP and port information.
    /// </summary>
    /// <param name="ipAddress">The IP address to clean up.</param>
    /// <returns>The cleaned up IP address with port information.</returns>
    /// <remarks>
    /// This method extracts the IP address and port from the input string. If the IP address is IPv6 loopback,
    /// it returns "localhost" along with the port. Otherwise, it returns the cleaned IP address with the port.
    /// </remarks>
    private static string CleanUpIpAddress(string ipAddress)
    {
        
        int startIndex = ipAddress.IndexOf("[") + 1;
        int endIndex = ipAddress.IndexOf("]");

        if (startIndex >= 0 && endIndex >= 0)
        {
            string cleanedIpAddress = ipAddress.Substring(startIndex, endIndex - startIndex);

            // Check if the IP address is the loopback address
            if (IPAddress.Parse(cleanedIpAddress).Equals(IPAddress.IPv6Loopback))
            {
                return $"localhost:{GetPortFromIpAddress(ipAddress)}";
            }

            return $"{cleanedIpAddress}:{GetPortFromIpAddress(ipAddress)}";
        }

        return ipAddress;
    }

    /// <summary>
    /// Extracts the port number from an IP address string.
    /// </summary>
    /// <param name="ipAddress">The IP address string containing the port number.</param>
    /// <returns>The port number extracted from the IP address string, or an empty string if not found.</returns>
    private static string GetPortFromIpAddress(string ipAddress)
    {
        // Extract the port number from the format [::1]:63614
        int startIndex = ipAddress.IndexOf(":") + 1;

        if (startIndex >= 0)
        {
            return ipAddress.Substring(startIndex);
        }

        return string.Empty;
    }

    /// <summary>
    /// Handles a handshake message received by a WebSocket.
    /// </summary>
    /// <param name="webSocket">The WebSocket instance.</param>
    /// <param name="message">The handshake message in JSON format.</param>
    /// <remarks>
    /// This method parses the JSON message to extract relevant information such as page, connection IP, connection port,
    /// client IP, and client port. It then processes the message based on the page type.
    /// </remarks>
    private static void HandleHandshakeMessage(WebSocket webSocket, string message)
    {
        JObject jsonMessage = JObject.Parse(message);
        string page = jsonMessage["page"].ToString();
        string connIp = jsonMessage["connIp"]?.ToString();
        string connPort = jsonMessage["connPort"]?.ToString();
        string clientIp = jsonMessage["clientIp"]?.ToString();
        string clientPort = jsonMessage["clientPort"]?.ToString();


        string connectionIdentifier = $"{connIp}:{connPort}";

        Console.WriteLine($"connIP: {connIp} , connPort: {connPort} , page: {page}");

        int webSocketHash = webSocket.GetHashCode();
        Console.WriteLine(webSocketHash);
       

        string clientIdentifier = $"{clientIp}:{clientPort}";

        if (page == "ClientDetailsPage")
        {

            clientWebSocketDict[clientIdentifier] = (connIp, connPort, webSocket);

            if (connIp != "" && connPort != "" && connectionsForPV.Contains(connectionIdentifier))
            {
                SendPVToServer(clientIp, clientPort, false);
                connectionsForPV.Remove(connectionIdentifier);
                WebSocketsOfPV.Remove(clientIdentifier);
                Console.WriteLine("PV breaked");
            }

            //if (alreadyExists)//for some reason this if statment does not work -because its opening and closing the websocket connection so it will be from different port each time
            //{
            //    SendPVToServer(clientIp, clientPort, false);// i found out in the prints that port number of the connected client is always changes
            //    WebSocketsOfPV.Remove(webSocket);
            //}

            if (clientEventDataDict.ContainsKey(clientIdentifier))
            {
                var eventData = clientEventDataDict[clientIdentifier];


                var eventDataJson = JsonConvert.SerializeObject(eventData);


                var eventDataBuffer = Encoding.UTF8.GetBytes(eventDataJson);
                webSocket.SendAsync(new ArraySegment<byte>(eventDataBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        else if (page == "MainContent")
        {

            clientWebSocketDict.Remove(clientIdentifier);
        } else if (page== "ViewProcessesPage") 
        {
            SendPVToServer(clientIp, clientPort, true);//sending the data to the server using the true flag
            Console.WriteLine("client is in the View Processes page!!");
            Console.WriteLine("WebSocket: " + webSocket);

            if (connIp!="" && connPort!= "" && !connectionsForPV.Contains(connectionIdentifier))
            {
                connectionsForPV.Add(connectionIdentifier);
                WebSocketsOfPV[clientIdentifier] = webSocket;
            }

            //if (!alreadyExists)
            //{
            //    WebSocketsOfPV.Add(webSocket);
            //}
        }
        
    }

    /// <summary>
    /// Handles the first connection request by accepting a WebSocket connection.
    /// </summary>
    /// <param name="context">The HttpListenerContext representing the connection request.</param>
    /// <remarks>
    /// This method accepts a WebSocket connection request and adds the WebSocket to the list of active connections.
    /// If an exception occurs during the connection establishment, an error response is sent back.
    /// </remarks>
    private static async void HandleFirstConnection(HttpListenerContext context)
    {


        try
        {
            HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
            Console.WriteLine("WebSocket connection established.");

            WebSocket webSocket = webSocketContext.WebSocket;
            webSocketConnection.Add(webSocket);
           




        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            context.Response.Close();
            Console.WriteLine($"WebSocket connection error: {ex.Message}");
            return;
        }
    }

    //PV=> Process View
    /// <summary>
    /// Function that indatcats to the server to start sending processes that running on a
    /// specific client (the one we send him)
    /// </summary>
    /// <param name="clientIP"></param>
    /// <param name="ClientPORT"></param>
    private static void SendPVToServer(string clientIP, string clientPort, bool sendPV)
    {
        try
        {
            // Construct the JSON object
            var requestData = new
            {
                ClientIP = clientIP,
                ClientPort = clientPort,
                SendProcessData = sendPV // Flag to indicate sending process data
            };

            // Serialize the JSON object into a string
            string jsonData = JsonConvert.SerializeObject(requestData);

           
            

            // Connect to the server
            using (TcpClient tcpClient = new TcpClient("127.0.0.1", 5000)) // Adjust the IP address and port accordingly
            using (NetworkStream stream = tcpClient.GetStream())
            using (StreamWriter writer = new StreamWriter(stream))
            {
                // Send the JSON data to the server
                writer.WriteLine(jsonData);
                writer.Flush(); // Flush the writer to ensure all data is sent
            }

            Console.WriteLine("Process data request sent to the server.");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending process data request: {ex.Message}");
        }
    }


}