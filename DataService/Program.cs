﻿using Newtonsoft.Json;
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
    //private static Dictionary<string, (List<WebSocket> sockets, string page)> clientWebSocketDict = new Dictionary<string, (List<WebSocket>, string)>();
    private static Dictionary<(string connIp, string connPort), (string clientIp, string clientPort, WebSocket)> clientWebSocketDict = new Dictionary<(string, string), (string, string, WebSocket)>();




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
            if (context.Request.IsWebSocketRequest)
            {
                //ProcessWebSocketRequest(context);
                HandleFirstConnection(context);
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

            }
            //else
            //{
            //    context.Response.StatusCode = 400;
            //    context.Response.Close();
            //}
        }
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
                }
                else if ( context.Request.Url.AbsolutePath == "/connected-clients")
                {
                    ReturnConnectedClients(context);
                }
                else if ( context.Request.Url.AbsolutePath == "/event-notification")
                {
                    // ClientsDetailsNotification(context); // Call HandleNotifications for handling event notifications
                    ProcessWebSocketRequest(context);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ListenForClients: {ex.Message}");
            // You might want to handle the error here, such as logging it or taking appropriate action
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

        if (page == "ClientDetailsPage")
        {
            // Store client WebSocket instance with associated IP and port information
            clientWebSocketDict[(connIp, connPort)] = (clientIp, clientPort, webSocket);
        }
        else if (page == "MainContent")
        {
            // Remove client WebSocket instance when it navigates away from ClientDetailsPage
            clientWebSocketDict.Remove((connIp, connPort));
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


}