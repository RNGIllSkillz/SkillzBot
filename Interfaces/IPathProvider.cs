public interface IPathProvider
{
    string DataPath { get; }
    string SharedPath { get; }
    string ConfigPath { get; }
    string GetFullPath(string relativePath, bool isShared = false);
}