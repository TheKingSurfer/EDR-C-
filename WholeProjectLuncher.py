import subprocess
import os
import threading

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
        else:
            # Asynchronously read and print the output and errors
            def print_output(process):
                for line in process.stdout:
                    if line:
                        print(line.strip())
                for line in process.stderr:
                    if line:
                        print(line.strip())

            # Create a thread to handle the output printing
            threading.Thread(target=print_output, args=(process,), daemon=True).start()
    except Exception as e:
        print(f"An error occurred while starting process: {e}")

def build_project(solution_path):
    # Path to MSBuild.exe, change this according to your Visual Studio installation
    msbuild_path = r"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    
    # Build the project
    build_command = f'"{msbuild_path}" "{solution_path}" /p:Configuration=Debug'
    result = subprocess.run(build_command, shell=True, capture_output=True, text=True)
    
    if result.returncode == 0:
        print("Build succeeded")
        return True
    else:
        print("Build failed")
        print(result.stdout)
        print(result.stderr)
        return False

def main():
    website_project_path = r"C:\Users\Owner\Desktop\edr-website-mui"
    
    print("Starting npm project...")
    start_process("npm start", cwd=website_project_path, wait=True)
    
   

   

if __name__ == "__main__":
    main()
