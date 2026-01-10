using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace SkillzBot.Writers
{
    internal class FileManipulator
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public static async Task<bool> DeleteLineFromFileAsync(string filePath, string lineToDelete)
        {
            await _semaphore.WaitAsync();
            try
            {
                if (!File.Exists(filePath)) return false;

                string[] lines = await File.ReadAllLinesAsync(filePath);
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
                    await File.WriteAllLinesAsync(filePath, updatedLines.ToArray());
                }
                return fileChanged;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public static async Task AddLineToFileAsync(string filePath, string lineToAdd)
        {
            await _semaphore.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(filePath, lineToAdd + Environment.NewLine);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}