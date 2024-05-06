// ProcessCommand.cs
using Newtonsoft.Json;
using System;
using System.Net.Sockets;
using System.Text;

namespace ProcessC
{
    public class ProcessCommand
    {
        public string Command { get; set; } 
        public int ProcessId { get; set; }

        /// <summary>Initializes a new instance of the ProcessCommand class.</summary>
        /// <param name="command">The command to be executed.</param>
        /// <param name="processId">The ID of the process associated with the command.</param>
        public ProcessCommand(string command, int processId) 
        {
            Command = command;
            ProcessId = processId;
        }

        /// <summary>
        /// Sends a command to an agent through a network stream.
        /// </summary>
        /// <param name="nwStream">The network stream used for communication.</param>
        /// <param name="command">The command to be sent to the agent.</param>
        public static void SendCommandToAgent(NetworkStream nwStream, ProcessCommand command)
        {
            // Convert the command to JSON
            string commandJson = JsonConvert.SerializeObject(command);

            // Create a JSON array from the command
            string jsonArray = "[" + commandJson + "]";

            // Send the command to the agent
            SendDataToAgent(nwStream, jsonArray);
        }

        /// <summary>
        /// Sends data to the agent through the specified network stream.
        /// </summary>
        /// <param name="nwStream">The network stream used for communication.</param>
        /// <param name="data">The data to be sent to the agent.</param>
        private static void SendDataToAgent(NetworkStream nwStream, string data)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(data + Environment.NewLine);
            nwStream.Write(buffer, 0, buffer.Length);
        }
    }
}
