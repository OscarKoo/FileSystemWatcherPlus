using System;

namespace Dao.FileSystemWatcherPlus
{
    [Flags]
    public enum WatcherTypes
    {
        Existed = 1,
        /// <summary>The creation of a file or folder.</summary>
        Created = 2,
        /// <summary>The deletion of a file or folder.</summary>
        Deleted = 4,
        /// <summary>The change of a file or folder. The types of changes include: changes to size, attributes, security settings, last write, and last access time.</summary>
        Changed = 8,
        /// <summary>The renaming of a file or folder.</summary>
        Renamed = 16
    }
}