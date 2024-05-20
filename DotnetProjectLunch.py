import subprocess
import platform
import signal
import sys
import os 
import pathlib


# Global variables to store subprocesses
server_process = None
data_service_process = None
agent_process = None

def build_and_run_project(solution_path):
    global server_process, data_service_process, agent_process
    current_file_dir = os.path.dirname(os.path.abspath(__file__))
    
    # Change the working directory to the directory of the current Python file
    os.chdir(current_file_dir)
    
    # Build the solution using msbuild
    msbuild_path = r'c:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe'
    build_command = [msbuild_path, solution_path]
    build_process = subprocess.Popen(build_command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    build_output, build_error = build_process.communicate()

    if build_process.returncode == 0:
        print("Build successful.")
        
        # Paths to each executable
        agent_exe_path = r"Agent Solution\Agent\bin\Debug\Agent.exe"
        server_exe_path = r"Server Solution\Server\bin\Debug\Server.exe"
        data_service_exe_path = r"DataService\bin\Debug\DataService.exe"
        
        # Function to open each executable in a separate command prompt window
        def open_in_command_prompt(executable_path):
            if platform.system() == "Windows":
                return subprocess.Popen(["start", "cmd", "/c", "start", "cmd", "/k", executable_path], shell=True)
            else:
                return subprocess.Popen(["x-terminal-emulator", "-e", executable_path])
        
        # Run each executable in a separate command prompt window
        server_process = open_in_command_prompt(server_exe_path)
        print(f"Server.exe is running.")

        data_service_process = open_in_command_prompt(data_service_exe_path)
        print(f"DataService.exe is running.")

        agent_process = open_in_command_prompt(agent_exe_path)
        print(f"Agent.exe is running.")

        # Set up signal handler for termination signal (SIGTERM)
        signal.signal(signal.SIGTERM, signal_handler)
    else:
        print("Build failed.")
        print(build_error.decode('utf-8'))

def signal_handler(sig, frame):
    global server_process, data_service_process, agent_process
    
    print("Terminating servers...")
    if server_process:
        server_process.terminate()
    if data_service_process:
        data_service_process.terminate()
    if agent_process:
        agent_process.terminate()
    sys.exit(0)

if __name__ == "__main__":
    solution_path = r"C:\Users\Owner\Desktop\EDR\EDR project\Merged\EDR\EDR.sln"
    build_and_run_project(solution_path)
