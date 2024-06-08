import subprocess
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

def main():
    # Corrected path to the npm project folder
    website_project_path = r"..\..\..\..\edr-Website-mui"
    
    print("Starting npm project...")
    start_process("npm start", cwd=website_project_path, wait=True)

if __name__ == "__main__":
    main()
