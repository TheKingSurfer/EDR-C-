using System;
using System.Collections.Generic;
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
            }
            else
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
            connectedClients.Add(context.Request.RemoteEndPoint.ToString());
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

            // You can add more functionality here to handle real-time events
        }

        Console.WriteLine("WebSocket connection closed.");
        // Remove the disconnected client information from the list
        connectedClients.Remove(context.Request.RemoteEndPoint.ToString());
    }

    private static void ListenForClients()
    {
        while (true)
        {
            var context = httpListener.GetContext();
            if (context.Request.HttpMethod == "POST" && context.Request.Url.AbsolutePath == "/notify")
            {
                HandleNotification(context);
            }
            else if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/connected-clients")
            {
                ReturnConnectedClients(context);
            }
        }
    }

    private static void HandleNotification(HttpListenerContext context)
    {
        using (var reader = new System.IO.StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
        {
            string message = reader.ReadToEnd();
            Console.WriteLine($"Notification received: {message}");
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

        // Extract and clean up IP addresses for better display
        List<string> cleanedUpAddresses = connectedClients.ConvertAll(address => CleanUpIpAddress(address));

        // Return the list of connected clients as a JSON array
        string response = Newtonsoft.Json.JsonConvert.SerializeObject(cleanedUpAddresses);
        byte[] buffer = Encoding.UTF8.GetBytes(response);

        context.Response.ContentType = "application/json"; // Set content type to JSON
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.StatusCode = 200;
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
