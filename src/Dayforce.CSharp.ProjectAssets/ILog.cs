namespace Dayforce.CSharp.ProjectAssets
{
    public static class Log
    {
        public static ILog Instance = NullLog.Default;
    }

    public interface ILog
    {
        void Save(string projectAssetsJsonFilePath);
        string GetRelativeFilePath(string filePath);
        void WriteVerbose(object obj);
        void WriteVerbose(string fmt, object arg);
        void WriteVerbose(string fmt, object arg1, object arg2);
        void WriteVerbose(string fmt, object arg1, object arg2, object arg3);
        void WriteVerbose(string fmt, params object[] args);
    }
}
