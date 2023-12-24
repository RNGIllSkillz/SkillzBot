using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace SkillzBot.Writers
{
    internal class FileManipulator
    {
        public static bool DeleteLineFromFile(string filePath, string lineToDelete)
        {
            string[] lines = File.ReadAllLines(filePath);
            var updatedLines = new List<string>();
            bool fileChanged = false;
            foreach (string line in lines)
            {
                if (line != lineToDelete)
                {
                    updatedLines.Add(line);
                }
                else
                {
                    fileChanged = true;
                }
            }
            if (fileChanged)
            {
                File.WriteAllLines(filePath, updatedLines.ToArray());
            }
            return fileChanged;
        }
        public static void AddLineToFile(string filePath, string lineToAdd)
        {
            string tempFilePath = Path.GetTempFileName();
            using (StreamWriter writer = new StreamWriter(tempFilePath))
            {
                writer.WriteLine(lineToAdd);
                string[] existingLines = File.ReadAllLines(filePath);
                foreach (string existingLine in existingLines)
                {
                    writer.WriteLine(existingLine);
                }
            }
            // After writing the new content to the temporary file, replace the original file.
            File.Delete(filePath); // Delete the original file.
            File.Move(tempFilePath, filePath);
        }
    }
}
