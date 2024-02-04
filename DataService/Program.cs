using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8080/");
        listener.Start();

        Console.WriteLine("Server is listening on http://localhost:8080/");

        while (true)
        {
            var context = await listener.GetContextAsync();
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

    static async void ProcessWebSocketRequest(HttpListenerContext context)
    {
        HttpListenerWebSocketContext webSocketContext = null;

        try
        {
            webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
            Console.WriteLine("WebSocket connection established.");
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
            
            var message = $"Event at {DateTime.Now.ToString("HH:mm:ss")}";
            var buffer = Encoding.UTF8.GetBytes(message);

            await webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, buffer.Length), WebSocketMessageType.Text, true, CancellationToken.None);

            await Task.Delay(1000); // Simulate events every 1 second
        }

        Console.WriteLine("WebSocket connection closed.");
    }
}
