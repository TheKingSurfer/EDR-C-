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
    private static Dictionary<string, List<string>> clientEventDataDict = new Dictionary<string, List<string>>();

    static async Task Main()
    {
        httpListener = new HttpListener();
        httpListener.Prefixes.Add("http://localhost:8080/");
        httpListener.Start();

        Console.WriteLine("WebSocket Server is listening on http://localhost:8080/");

        listenerThread = new Thread(ListenForClients);
        listenerThread.Start();

        while (true)
        {
            var context = await httpListener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                ProcessWebSocketRequest(context);
            } else if (context.Request.HttpMethod == "POST" || context.Request.Url.AbsolutePath == "/notify")
            {
                HandleNotification(context);
            }else
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }
    }

    private static async void ProcessWebSocketRequest(HttpListenerContext context)
    {
        HttpListenerWebSocketContext webSocketContext = null;

        try
        {
            webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
            Console.WriteLine("WebSocket connection established.");

            // Add the connected client information to the list
            //connectedClients.Add(context.Request.RemoteEndPoint.ToString());
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            context.Response.Close();
            Console.WriteLine($"WebSocket connection error: {ex.Message}");
            return;
        }

        var webSocket = webSocketContext.WebSocket;

        while (webSocket.State == WebSocketState.Open)
        {
            await Task.Delay(1000); // Adjust this delay as needed

            
        }

        Console.WriteLine("WebSocket connection closed.");
        // Remove the disconnected client information from the list
        connectedClients.Remove(context.Request.RemoteEndPoint.ToString());
    }

    private static void ListenForClients()
    {
        try
        {
            while (true)
            {
                var context = httpListener.GetContext();
                if (context.Request.HttpMethod == "POST" || context.Request.Url.AbsolutePath == "/notify")
                {
                    HandleNotification(context);
                }
                else if (context.Request.HttpMethod == "GET" || context.Request.Url.AbsolutePath == "/connected-clients")
                {
                    ReturnConnectedClients(context);
                }
                else if (context.Request.HttpMethod == "POST" && context.Request.Url.AbsolutePath == "/event-notification")
                {
                    ClientsDetailsNotification(context); // Call HandleNotifications for handling event notifications
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ListenForClients: {ex.Message}");
            // You might want to handle the error here, such as logging it or taking appropriate action
        }
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






    private static async void ClientsDetailsNotification(HttpListenerContext context)
    {
        HttpListenerWebSocketContext webSocketContext = null;

        try
        {
            webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
            Console.WriteLine("WebSocket connection established.");

            var webSocket = webSocketContext.WebSocket;


            while (webSocket.State == WebSocketState.Open)
            {
                // Buffer to store incoming data
                byte[] buffer = new byte[1024];
                ArraySegment<byte> bufferSegment = new ArraySegment<byte>(buffer);

                // Receive data from the WebSocket client
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(bufferSegment, CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    // Convert the received data to a string
                    string receivedData = Encoding.UTF8.GetString(bufferSegment.Array, 0, result.Count);

                    // Parse the received JSON object
                    JObject jsonNotification = JObject.Parse(receivedData);

                    // Extract client IP address, port, and event data from the JSON object
                    string clientIpAddress = jsonNotification["ClientIpAddress"].ToString();
                    int clientPort = int.Parse(jsonNotification["ClientPort"].ToString());
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
                }
            }

            Console.WriteLine("WebSocket connection closed.");
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            context.Response.Close();
            Console.WriteLine($"WebSocket connection error: {ex.Message}");
            return;
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

}
