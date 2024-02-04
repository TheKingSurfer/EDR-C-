using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftWindowsTCPIP;

namespace EDR.Agent
{
    public class FileIOEventData
    {
        public string EventName { get; set; }
        public int ProcessId { get; set; }
        public string FileName { get; set; }
        public DateTime TimeStamp { get; set; }
        public string HashCode { get; set; }
    }
    public class EDRProcessor
    {
        public readonly TraceEventSession kernelSession;
        private Action<string> sendData;


        //static void Main(string[] args)
        //{
        //    EDRProcessor edrProcessor = new EDRProcessor();

        //    // Start monitoring
        //    edrProcessor.StartMonitoring();

        //    // ... Do other work ...

        //    // Stop monitoring
        //    edrProcessor.StopMonitoring();

        //    // Monitor event log
        //    edrProcessor.MonitorEventLog("Security", new[] { 4663, 4656, 4670 });
        //}

        public EDRProcessor(Action<string> sendDataCallback)
        {

            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Console.WriteLine("Please run as administrator");
                Console.ReadKey();
                return;
            }

            kernelSession = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) { kernelSession.Dispose(); };

            sendData = sendDataCallback;
        }

        public void StartMonitoring()
        {
            


            kernelSession.EnableKernelProvider(
                KernelTraceEventParser.Keywords.DiskFileIO |
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.Thread |
                KernelTraceEventParser.Keywords.FileIO |
                KernelTraceEventParser.Keywords.ImageLoad |
                KernelTraceEventParser.Keywords.Process);
            // Create a trace event source from the session name
            var source = new ETWTraceEventSource(KernelTraceEventParser.KernelSessionName, TraceEventSourceType.Session);

            // Create a dynamic trace event parser from the source
            var parser = new DynamicTraceEventParser(source);

            // Subscribe to the All event and provide the event handler
            parser.All += HandleEvent;


            kernelSession.Source.Kernel.FileIORead+= fileIOReadWrite;
            kernelSession.Source.Kernel.FileIOWrite+= fileIOReadWrite;

            kernelSession.Source.Kernel.ImageLoad += dllLoaded;
            kernelSession.Source.Kernel.ProcessStart += processStarted;
            kernelSession.Source.Kernel.ProcessStop += processEnded;




            kernelSession.Source.Process();

            sendData?.Invoke("Monitoring started");
        }

        public void StopMonitoring()
        {
            kernelSession.Dispose();
            sendData?.Invoke("Monitoring stopped");
        }
        static void HandleEvent(TraceEvent traceEvent)
        {
            // Filter the events by file name or file object
            if (traceEvent.EventName.Contains("FileIo"))
            {
                // Assuming the payload is a string, replace the 0 with the appropriate index
                string payloadString = traceEvent.PayloadString(0, null) as string;

                if (payloadString != null && payloadString.Contains(@"C:\Users\Owner\Desktop\Desktop.txt"))
                {
                    // Print the event details
                    //Console.WriteLine($"************{traceEvent.TimeStamp} - {traceEvent.ProcessName} ({traceEvent.ProcessID}) - {traceEvent.EventName} - {payloadString} ************");
                }
            }
        }



        private void dllLoaded(ImageLoadTraceData data)
        {
            var eventData = new
            {
                EventName = data.EventName,
                ProcessId = data.ProcessID,
                FileName = data.FileName
            };

            

            string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(eventData);
            sendData?.Invoke(jsonData);

            //CheckProcessPermissions(data.ProcessID);
        }


        private void fileIOReadWrite(FileIOReadWriteTraceData data)
        {
            var eventData = new FileIOEventData
            {
                EventName = "********* FileIOReadWrite *********",
                ProcessId = data.ProcessID,
                FileName = data.FileName,
                TimeStamp = data.TimeStamp
            };

            if (!string.IsNullOrEmpty(eventData.FileName))
            {
                // Generate hash code for the involved values
                eventData.HashCode = GenerateHashCode(eventData.ProcessId, eventData.FileName, eventData.TimeStamp);

                // Convert the named class object to JSON
                string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(eventData);
                sendData?.Invoke(jsonData);

                // Print the event details with colored console output
                //Console.ForegroundColor = ConsoleColor.Yellow; // print in yellow
                //Console.WriteLine($"File IO: {data.FileName} - Hash Code: {eventData.HashCode}");
                //Console.ResetColor(); // Reset the console color
            }
        }

        // Helper method to generate SHA-256 hash code
        private string GenerateHashCode(int processId, string fileName, DateTime timeStamp)
        {
            string concatenatedString = $"{processId}-{fileName}-{timeStamp}";

            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(concatenatedString));
                return BitConverter.ToString(hashBytes).Replace("-", "");
            }
        }




        private void processStarted(ProcessTraceData data)
        {
            var eventData = new
            {
                EventName = "ProcessStarted",
                ProcessId = data.ProcessID,
                ParentProcessId = data.ParentID
            };

            string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(eventData);
            sendData?.Invoke(jsonData);
        }

        private void processEnded(ProcessTraceData data)
        {
            var eventData = new
            {
                EventName = "ProcessEnded",
                ProcessId = data.ProcessID
            };

            string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(eventData);
            sendData?.Invoke(jsonData);
        }

        private void CheckProcessPermissions(int processId)
        {
            try
            {
                Process process = Process.GetProcessById(processId);

                Console.WriteLine($"Checking permissions for Process {processId}:");

                string processFilePath = process.MainModule.FileName;

                // Get the security information for the executable file
                var fileSecurity = File.GetAccessControl(processFilePath);

                // Get the owner of the file
                var fileOwner = fileSecurity.GetOwner(typeof(NTAccount));

                Console.WriteLine($"  Owner: {fileOwner}");

                // Get the list of access rules
                AuthorizationRuleCollection rules = fileSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier));

                foreach (AuthorizationRule rule in rules)
                {
                    if (rule is FileSystemAccessRule fileRule)
                    {
                        Console.WriteLine($"  Identity: {fileRule.IdentityReference.Value}");
                        Console.WriteLine($"  Type: {fileRule.AccessControlType}");
                        Console.WriteLine($"  Rights: {fileRule.FileSystemRights}");
                        Console.WriteLine($"  Inherited: {fileRule.IsInherited}");
                        Console.WriteLine();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking permissions for Process {processId}: {ex.Message}");
            }
        }
    }
}
