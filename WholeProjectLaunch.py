import subprocess
import platform
import signal
import sys
import os
import threading

# Global variables to store subprocesses
server_process = None
data_service_process = None
agent_process = None

# Helper function to find the base path
def get_base_path():
    if hasattr(sys, '_MEIPASS'):
        return sys._MEIPASS
    return os.path.dirname(os.path.abspath(__file__))

def build_and_run_project(solution_path):
    global server_process, data_service_process, agent_process
    
    # Get the base path
    base_path = get_base_path()
    print(f"Base path: {base_path}")
    
    # Change the working directory to the directory of the current Python file
    os.chdir(base_path)
    print(f"Changed working directory to: {base_path}")
    
    # Build the solution using msbuild
    msbuild_path = r'c:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe'
    build_command = [msbuild_path, solution_path]
    print(f"Build command: {' '.join(build_command)}")
    build_process = subprocess.Popen(build_command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    build_output, build_error = build_process.communicate()

    if build_process.returncode == 0:
        print("Build successful.")
        
        # Paths to each executable
        agent_exe_path = os.path.join(base_path, "Agent Solution", "Agent", "bin", "Debug", "Agent.exe")
        server_exe_path = os.path.join(base_path, "Server Solution", "Server", "bin", "Debug", "Server.exe")
        data_service_exe_path = os.path.join(base_path, "DataService", "bin", "Debug", "DataService.exe")
        
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
        print("Build output:\n", build_output.decode('utf-8'))
        print("Build error:\n", build_error.decode('utf-8'))

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

def build_and_run_website():
    website_project_path = r"..\..\..\..\edr-Website-mui"
    
    print("Starting npm project...")
    start_process("npm start", cwd=website_project_path, wait=True)

def start_process(command, cwd=None, wait=False):
    try:
        # Start the process without waiting for it to finish unless specified
        process = subprocess.Popen(command, shell=True, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        
        if wait:
            # Read and print the output
            while True:
                output = process.stdout.readline()
                if output == '' and process.poll() is not None:
                    break
                if output:
                    print(output.strip())

            # Read and print any errors
            while True:
                error = process.stderr.readline()
                if error == '' and process.poll() is not None:
                    break
                if error:
                    print(error.strip())
            
            # Close process streams
            process.stdout.close()
            process.stderr.close()
            process.wait()
        else:
            # Asynchronously read and print the output and errors
            def print_output(process):
                for line in process.stdout:
                    if line:
                        print(line.strip())
                for line in process.stderr:
                    if line:
                        print(line.strip())

                # Close process streams after reading
                process.stdout.close()
                process.stderr.close()
                process.wait()

            # Create a thread to handle the output printing
            threading.Thread(target=print_output, args=(process,), daemon=True).start()
    except Exception as e:
        print(f"An error occurred while starting process: {e}")


if __name__ == "__main__":
    base_path = get_base_path()
    solution_path = os.path.join(base_path, "EDR.sln")
    print(f"Solution path: {solution_path}")
    build_and_run_project(solution_path)
    build_and_run_website()
