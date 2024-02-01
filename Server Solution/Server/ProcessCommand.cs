// ProcessCommand.cs
using Newtonsoft.Json;
using System;
using System.Net.Sockets;
using System.Text;

namespace ProcessC
{
    public class ProcessCommand
    {
        public string Command { get; set; } // Change the type to string
        public int ProcessId { get; set; }

        public ProcessCommand(string command, int processId) // Change the type of the parameter
        {
            Command = command;
            ProcessId = processId;
        }

        public static void SendCommandToAgent(NetworkStream nwStream, ProcessCommand command)
        {
            // Convert the command to JSON
            string commandJson = JsonConvert.SerializeObject(command);

            // Create a JSON array from the command
            string jsonArray = "[" + commandJson + "]";

            // Send the command to the agent
            SendDataToAgent(nwStream, jsonArray);
        }

        private static void SendDataToAgent(NetworkStream nwStream, string data)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(data + Environment.NewLine);
            nwStream.Write(buffer, 0, buffer.Length);
        }
    }
}
