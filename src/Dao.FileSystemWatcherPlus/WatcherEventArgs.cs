namespace Dao.FileSystemWatcherPlus
{
    public class WatcherEventArgs
    {
        public WatcherTypes WatcherType { get; set; }
        public string FullPath { get; set; }
        public string Name { get; set; }
        public string OldFullPath { get; set; }
        public string OldName { get; set; }
    }
}