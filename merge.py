import os

def merge_cs_files(output_file="project_context.txt", source_dir="."):
    """
    Walks through the source_dir, finds all .cs files (ignoring bin/obj),
    and merges them into output_file with a specific header format.
    """
    
    # Folders to exclude to keep the context clean
    EXCLUDE_DIRS = {'bin', 'obj', '.git', '.vs', '.idea', '.vscode'}

    print(f"Scanning directory: {os.path.abspath(source_dir)}")
    
    with open(output_file, "w", encoding="utf-8") as outfile:
        file_count = 0
        
        for root, dirs, files in os.walk(source_dir):
            # Modify dirs in-place to prevent walking into excluded directories
            dirs[:] = [d for d in dirs if d.lower() not in EXCLUDE_DIRS]
            
            for file in files:
                if file.endswith(".cs"):
                    full_path = os.path.join(root, file)
                    
                    # Create a relative path (e.g., "Controllers/HomeController.cs")
                    relative_path = os.path.relpath(full_path, source_dir)
                    
                    # Ensure forward slashes for the header even on Windows
                    # This helps the AI understand the structure better
                    formatted_path = relative_path.replace(os.sep, "/")
                    
                    try:
                        with open(full_path, "r", encoding="utf-8", errors="replace") as infile:
                            content = infile.read()
                            
                            # Write the header as requested
                            outfile.write(f"# {formatted_path}\n")
                            # Write the content
                            outfile.write(content)
                            # Add a few newlines to separate files cleanly
                            outfile.write("\n\n")
                            
                            print(f"Added: {formatted_path}")
                            file_count += 1
                    except Exception as e:
                        print(f"Error reading file {formatted_path}: {e}")

    print(f"\nDone! merged {file_count} files into '{output_file}'.")

if __name__ == "__main__":
    # You can change the filename below if you want
    merge_cs_files("full_codebase.txt")