namespace Dayforce.CSharp.ProjectAssets
{
    public class NullLog : ILog
    {
        public static readonly ILog Default = new NullLog();
        public void Save(string projectAssetsJsonFilePath) { }
        public string GetRelativeFilePath(string filePath) => null;
        public void WriteVerbose(object obj) { }
        public void WriteVerbose(string fmt, object arg) { }
        public void WriteVerbose(string fmt, object arg1, object arg2) { }
        public void WriteVerbose(string fmt, object arg1, object arg2, object arg3) { }
        public void WriteVerbose(string fmt, params object[] args) { }
    }
}
