using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dao.QueueExecutor;
#if NETCOREAPP3_0_OR_GREATER
using System.Collections.ObjectModel;
#endif

namespace Dao.FileSystemWatcherPlus
{
    public class FileSystemWatcherPlus : IDisposable
    {
        readonly WatcherTypes enabledWatchers;

        ConcurrentQueue<WatcherEventArgs> queue = new ConcurrentQueue<WatcherEventArgs>();
        Catcher cather;
        FileSystemWatcher watcher;

        volatile int state;
        volatile bool isWatching;

        public FileSystemWatcherPlus(WatcherTypes enabledWatchers, string path = null, params string[] filters)
        {
            this.enabledWatchers = enabledWatchers;

            InitializeCatcher();
            InitializeWatcher(enabledWatchers, path, filters);
        }

        #region Publics

        public event Func<WatcherEventArgs, Task> OnWatching;
        public event Action<Exception> OnException;

        public string Path
        {
            get => this.watcher.Path;
            set => this.watcher.Path = value;
        }

        public string Filter
        {
            get => this.watcher.Filter;
            set => this.watcher.Filter = value;
        }

#if NETCOREAPP3_0_OR_GREATER
        public Collection<string> Filters => this.watcher.Filters;
#endif

        public NotifyFilters NotifyFilter
        {
            get => this.watcher.NotifyFilter;
            set => this.watcher.NotifyFilter = value;
        }

        public bool IncludeSubdirectories
        {
            get => this.watcher.IncludeSubdirectories;
            set => this.watcher.IncludeSubdirectories = value;
        }

        #endregion

        #region Events

        async Task Cather_Catch()
        {
            this.isWatching = true;

            var onWatching = OnWatching;
            try
            {
                while (this.queue.TryDequeue(out var arg))
                {
                    if (onWatching != null)
                        await onWatching(arg).ConfigureAwait(false);
                }
            }
            finally
            {
                this.isWatching = false;
            }
        }

        void Watcher_Error(object sender, ErrorEventArgs e)
        {
            var onException = OnException;
            onException?.Invoke(e.GetException());
        }

        void Watcher_Modified(object sender, FileSystemEventArgs e) =>
            FireWatcher((WatcherTypes)((int)e.ChangeType * 2), e.FullPath, e.Name);

        void Watcher_Renamed(object sender, RenamedEventArgs e) =>
            FireWatcher((WatcherTypes)((int)e.ChangeType * 2), e.FullPath, e.Name, e.OldFullPath, e.OldName);

        void FireWatcher(WatcherTypes watcherType, string fullPath, string name, string oldFullPath = null, string oldName = null)
        {
            Retry(watcherType, fullPath, name, oldFullPath, oldName);
            this.cather.Throw();
        }

        #endregion

        #region Initialize

        void InitializeCatcher()
        {
            if (this.cather != null)
                return;

            this.cather = new Catcher();
            this.cather.Catch += Cather_Catch;
        }

        void InitializeWatcher(WatcherTypes enabledWatchers, string path, params string[] filters)
        {
            if (this.watcher != null)
                return;

            this.watcher = new FileSystemWatcher();
            if (!string.IsNullOrWhiteSpace(path))
                Path = path;

            if (filters != null && filters.Length > 0)
            {
#if NETCOREAPP3_0_OR_GREATER
                foreach (var filter in filters)
                    Filters.Add(filter);
#else
                Filter = filters.First();
#endif
            }

            this.watcher.Error += Watcher_Error;

            if (enabledWatchers.HasFlag(WatcherTypes.Created))
                this.watcher.Created += Watcher_Modified;
            if (enabledWatchers.HasFlag(WatcherTypes.Deleted))
                this.watcher.Deleted += Watcher_Modified;
            if (enabledWatchers.HasFlag(WatcherTypes.Changed))
                this.watcher.Changed += Watcher_Modified;
            if (enabledWatchers.HasFlag(WatcherTypes.Renamed))
                this.watcher.Renamed += Watcher_Renamed;
        }

        #endregion

        void LoadExisted()
        {
            if (!this.enabledWatchers.HasFlag(WatcherTypes.Existed)
                || string.IsNullOrWhiteSpace(Path))
                return;

            var hasFile = false;
            foreach (var file in Directory.EnumerateFiles(Path, Filter, IncludeSubdirectories
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly)
                .Select(s => new FileInfo(s)))
            {
                Retry(WatcherTypes.Existed, file.FullName, file.Name);
                hasFile = true;
            }

            if (hasFile)
                this.cather.Throw();
        }

        public void Retry(WatcherTypes watcherType, string fullPath, string name, string oldFullPath = null, string oldName = null) => Retry(new WatcherEventArgs
        {
            WatcherType = watcherType,
            FullPath = fullPath,
            Name = name,
            OldFullPath = oldFullPath,
            OldName = oldName
        });

        public void Retry(WatcherEventArgs arg) => this.queue.Enqueue(arg);

        public void Start()
        {
            CheckDisposed();

            if (this.state == 1)
                return;

            LoadExisted();

            if (this.enabledWatchers > WatcherTypes.Existed && !this.watcher.EnableRaisingEvents)
                this.watcher.EnableRaisingEvents = true;

            this.state = 1;
        }

        public void Stop()
        {
            CheckDisposed();

            if (this.state == 2)
                return;

            if (this.enabledWatchers > WatcherTypes.Existed && this.watcher.EnableRaisingEvents)
                this.watcher.EnableRaisingEvents = false;

            this.state = 2;
        }

        #region Dispose

        public void Dispose()
        {
            CheckDisposed();

            Stop();
            DisposeWatcher();
            DisposeCatcher();
            WaitWatching();
            DisposeQueue();

            OnWatching = null;
            OnException = null;

            this.state = -1;
        }

        void CheckDisposed()
        {
            if (this.state < 0)
                throw new ObjectDisposedException(nameof(FileSystemWatcherPlus));
        }

        void DisposeWatcher()
        {
            if (this.watcher == null)
                return;

            if (this.enabledWatchers.HasFlag(WatcherTypes.Created))
                this.watcher.Created -= Watcher_Modified;
            if (this.enabledWatchers.HasFlag(WatcherTypes.Deleted))
                this.watcher.Deleted -= Watcher_Modified;
            if (this.enabledWatchers.HasFlag(WatcherTypes.Changed))
                this.watcher.Changed -= Watcher_Modified;
            if (this.enabledWatchers.HasFlag(WatcherTypes.Renamed))
                this.watcher.Renamed -= Watcher_Renamed;
            this.watcher.Error -= Watcher_Error;
            this.watcher.Dispose();
            this.watcher = null;
        }

        void DisposeCatcher()
        {
            if (this.cather == null)
                return;

            this.cather.Catch -= Cather_Catch;
            this.cather = null;
        }

        void WaitWatching()
        {
            var sw = new SpinWait();
            while (this.isWatching)
            {
                sw.SpinOnce();
            }
        }

        void DisposeQueue()
        {
#if NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
            this.queue.Clear();
#else
            this.queue = new ConcurrentQueue<WatcherEventArgs>();
#endif
        }

        #endregion
    }
}