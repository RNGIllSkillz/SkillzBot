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
            using (FileStream fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (StreamWriter writer = new StreamWriter(fileStream))
            {
                writer.WriteLine(lineToAdd);
            }
        }
    }
}
