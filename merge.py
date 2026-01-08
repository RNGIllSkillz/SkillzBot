import os
import re

def remove_comments(text):
    """
    Removes C# style comments (// and /* */) while preserving strings.
    """
    pattern = r'("[^"\\]*(?:\\.[^"\\]*)*")|(//[^\n]*)|(/\*[\s\S]*?\*/)'
    
    def replacer(match):
        if match.group(1):
            return match.group(1) # It's a string, keep it
        return "" # It's a comment, remove it

    cleaned_text = re.sub(pattern, replacer, text)
    cleaned_text = re.sub(r'^\s*$', '', cleaned_text, flags=re.MULTILINE)
    return cleaned_text.strip()

def merge_cs_files(output_file="project_context.txt", source_dir="."):
    EXCLUDE_DIRS = {'bin', 'obj', '.git', '.vs', '.idea', '.vscode'}

    print(f"Scanning directory: {os.path.abspath(source_dir)}")
    
    # We write the output as standard utf-8
    with open(output_file, "w", encoding="utf-8") as outfile:
        file_count = 0
        
        for root, dirs, files in os.walk(source_dir):
            dirs[:] = [d for d in dirs if d.lower() not in EXCLUDE_DIRS]
            
            for file in files:
                if file.endswith(".cs"):
                    full_path = os.path.join(root, file)
                    relative_path = os.path.relpath(full_path, source_dir)
                    formatted_path = relative_path.replace(os.sep, "/")
                    
                    try:
                        # CHANGE HERE: Use 'utf-8-sig' to handle the BOM automatically
                        with open(full_path, "r", encoding="utf-8-sig", errors="replace") as infile:
                            content = infile.read()
                            
                            cleaned_content = remove_comments(content)
                            
                            if cleaned_content:
                                outfile.write(f"# {formatted_path}\n")
                                outfile.write(cleaned_content)
                                outfile.write("\n\n")
                                file_count += 1
                                print(f"Added: {formatted_path}")
                                
                    except Exception as e:
                        print(f"Error reading file {formatted_path}: {e}")

    print(f"\nDone! merged {file_count} files into '{output_file}'.")

if __name__ == "__main__":
    merge_cs_files("full_codebase.txt")