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
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics.PerformanceData;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using Microsoft.Diagnostics.Tracing.Parsers.ClrPrivate;


namespace EDR.Agent
{
    public class FileIOEventData
    {
        // Represents the name of the event.
        public string EventName { get; set; }

        // Represents the process ID associated with the event.
        public int ProcessId { get; set; }

        // Represents the name of the file associated with the event.
        public string FileName { get; set; }

        // Represents the timestamp when the event occurred.
        public DateTime TimeStamp { get; set; }

        // Represents the hash code associated with the file.
        public string HashCode { get; set; }

        // Represents the hash code associated specifically with executable files.
        public string ExecutableHashCode { get; set; }

        // Represents the bytes of the file.
        public byte[] fileBytes { get; set; }

    }
    public class EDRProcessor
    {
        public readonly TraceEventSession kernelSession;//->Represents a TraceEventSession for kernel events.
        private Action<string> sendData;//=>Action delegate used to send data.
        private readonly Dictionary<string, byte[]> executableBytesDictionary;//=> Dictionary to store byte arrays representing executable files, with file names as keys.
        private string[] protectedFiles = { "Desktop.txt" };//=>Array of file names representing protected files.
        private Dictionary<string, string> executableHashDictionary = new Dictionary<string, string>();//=>Dictionary to store hashes of executable files, with file names as keys.
        private List<string> protectedFolderPath = new List<string>();//->List of folder paths representing protected folders.


        /// <summary>
        /// Initializes a new instance of the EDRProcessor class with the specified callback action.
        /// </summary>
        /// <param name="sendDataCallback">The callback action to send processed data.</param>
        public EDRProcessor(Action<string> sendDataCallback)
        {

            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Console.WriteLine("Please run as administrator");
                Console.ReadKey();
                return;
            }
            protectedFolderPath.Add(@"C:\Test");//For Test

            kernelSession = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) { kernelSession.Dispose(); };

            sendData = sendDataCallback;
            executableBytesDictionary = new Dictionary<string, byte[]>();
        }

        /// <summary>
        /// Starts monitoring system events related to file I/O, process, and image loading.
        /// </summary>
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

            //sendData?.Invoke("Monitoring started");
        }

        /// <summary>
        /// Stops monitoring system events.
        /// </summary>
        public void StopMonitoring()
        {
            kernelSession.Dispose();
            //sendData?.Invoke("Monitoring stopped");
        }

        /// <summary>
        /// Handles the trace event by filtering file I/O events and processing them if they match specific criteria.
        /// </summary>
        /// <param name="traceEvent">The trace event to handle.</param>
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


        /// <summary>
        /// Handles the event when a DLL is loaded into a process.
        /// </summary>
        /// <param name="data">The event data for image loading.</param>
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

        /// <summary>
        /// Handles file I/O read and write events, processes the event data, calculates hash codes for involved files,
        /// and sends processed data to the specified callback action asynchronously.
        /// </summary>
        /// <param name="data">The event data for file I/O read or write operation.</param>
        private async void fileIOReadWrite(FileIOReadWriteTraceData data)
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
                //eventData.HashCode = GenerateHashCode(eventData.ProcessId, eventData.FileName, eventData.TimeStamp);
                try
                {
                    string AbsolutePath = Path.GetFullPath(eventData.FileName);
                }catch (Exception ex) { Console.WriteLine($" path have bad format - {ex}"); }


                //Works = > return the full path of the executables
                string executableFilePath;
                try
                {
                    Process process = Process.GetProcessById(data.ProcessID);
                    executableFilePath = process.MainModule.FileName;
                }
                catch ( ArgumentException )
                {
                    executableFilePath = Process.GetCurrentProcess().MainModule.FileName;
                }catch (System.ComponentModel.Win32Exception) 
                {
                    executableFilePath = Process.GetCurrentProcess().MainModule.FileName;
                }

                string executableHashCode = null;
                byte[] fileBytes = null;
                string lastName = eventData.FileName.Split('\\').Last();
                if (protectedFiles.Contains(lastName) || protectedFolderPath.Any(path => eventData.FileName.StartsWith(path))) {
                    

                    //HashCode
                    if (!executableHashDictionary.ContainsKey(executableFilePath))
                    {

                        executableHashCode = CalculateFileHash(executableFilePath);
                        if (executableHashCode != null)
                        {
                            executableHashDictionary[executableFilePath] = executableHashCode;
                        }

                    }
                    else 
                    {
                        executableHashCode= executableHashDictionary[executableFilePath];
                    }
                }


                eventData.fileBytes = fileBytes;
                eventData.ExecutableHashCode = executableHashCode;

                //Console.WriteLine($"File Bytes using function2 : {string.Join("", fileBytes)}");
                string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(eventData);
                sendData?.Invoke(jsonData);

                
            }
        }

        /// <summary>
        /// Retrieves the bytes of a file located at the specified path.
        /// </summary>
        /// <param name="filePath">The path of the file to retrieve bytes from.</param>
        /// <returns>The byte array representing the contents of the file.</returns>
        public byte[] GetFileBytes(string filePath)
        {
           
            try
            {
                // Read all bytes from the file
                byte[] fileBytes;
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    fileBytes = new byte[fs.Length];
                    fs.Read(fileBytes, 0, fileBytes.Length);
                }
                //string base64String = Convert.ToBase64String(fileBytes);
                //Console.WriteLine($"Base64 encoded file content: {base64String}");
                //Console.WriteLine($"bytes:\n {string.Join("", fileBytes)}");

                return fileBytes;
            }
            catch (FileNotFoundException ex)
            {
                //Console.WriteLine($"File not found: {ex.FileName}");
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                //Console.WriteLine($"Unauthorized access: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Calculates the SHA-256 hash code for a given file.
        /// </summary>
        /// <param name="filePath">The path of the file to calculate the hash code for.</param>
        /// <returns>The SHA-256 hash code of the file.</returns>
        private string CalculateFileHash(string filePath) 
        {
            try
            {
                //if (!File.Exists(filePath))
                //{
                //    Console.WriteLine("The file do not exist (message from hash function)");
                //    return null;
                //}


                using (var stream = File.OpenRead(filePath))
                {
                    using (var sha256 = SHA256.Create())
                    {
                        byte[] hashBytes = sha256.ComputeHash(stream);
                        return BitConverter.ToString(hashBytes).Replace("-", "");

                    }
                }
            }
            catch (UnauthorizedAccessException UEA)
            {
                //Console.WriteLine($"Unauthorized access to file:{UEA.Message}. Skipping..");
                return null; 
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Error Proccesing file: {ex}. Skipping..");
                return null;
            }
        }

        /// <summary>
        /// Handles the event when a process starts.
        /// </summary>
        /// <param name="data">The event data for process start.</param>

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

        /// <summary>
        /// Handles the event when a process ends.
        /// </summary>
        /// <param name="data">The event data for process end.</param>
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

        /// <summary>
        /// Checks permissions for a given process.
        /// </summary>
        /// <param name="processId">The ID of the process to check permissions for.</param>
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
