using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static HttpListener httpListener;
    private static Thread listenerThread;
    private static List<string> connectedClients = new List<string>();
    private static List<WebSocket> webSocketConnection = new List<WebSocket>();
    private static Dictionary<string, List<string>> clientEventDataDict = new Dictionary<string, List<string>>();
    private static Dictionary<string, (string connIp, string connPort, WebSocket)> clientWebSocketDict = new Dictionary<string, (string, string, WebSocket)>();
    private static Dictionary<string, List<string>> PVDataDict = new Dictionary<string, List<string>>(); //dict that stores the data and the client identifier -> "ip:port" , (data)   
    private static List<WebSocket> WebSocketsOfPV = new List<WebSocket>(); // => the websocket connection that requesting the pv data


    




    // Inside the HandleHandshakeMessage method:





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


    private static async void CollectPV() 
    {
        
    }

    private static async void ProcessWebSocketRequest(HttpListenerContext context)
    {
        

        try
        {
            HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
            Console.WriteLine("WebSocket connection established.");

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


    //not in use
    private static void ListenForClients()
    {
        try
        {
            while (true)
            {
                var context = httpListener.GetContext();
                if (context.Request.Url.AbsolutePath == "/notify")
                {
                    HandleNotification(context);
                } else if(context.Request.Url.AbsolutePath == "/notify-remove")
                {
                    HandleNotifyRemoveNotification(context);
                }
                else if (context.Request.Url.AbsolutePath == "/connected-clients")
                {
                    ReturnConnectedClients(context);
                }
                else if (context.Request.Url.AbsolutePath == "/event-notification")
                {
                    
                    ProcessWebSocketRequest(context);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ListenForClients: {ex.Message}");
          
        }
    }


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
                Console.WriteLine("Client Event Data Dictionary:");
                foreach (var kvp in clientEventDataDict)
                {
                    Console.WriteLine($"{kvp.Key}: {string.Join(", ", kvp.Value)}");
                }

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


    private static void HandleHandshakeMessage(WebSocket webSocket, string message)
    {
        JObject jsonMessage = JObject.Parse(message);
        string page = jsonMessage["page"].ToString();
        string connIp = jsonMessage["connIp"]?.ToString();
        string connPort = jsonMessage["connPort"]?.ToString();
        string clientIp = jsonMessage["clientIp"]?.ToString();
        string clientPort = jsonMessage["clientPort"]?.ToString();

        
        string clientIdentifier = $"{clientIp}:{clientPort}";

        if (page == "ClientDetailsPage")
        {

            clientWebSocketDict[clientIdentifier] = (connIp, connPort, webSocket);


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
        }
        
    }
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
