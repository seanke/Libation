﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FileManager
{
    /// <summary>
    /// Tracks actual locations of files. This is especially useful for clicking button to navigate to the book's files.
    /// 
    /// Note: this is no longer how Libation manages "Liberated" state. That is not statefully managed in the database.
    /// This paradigm is what allows users to manually choose to not download books. Also allows them to manually toggle
    /// this state and download again.
    /// </summary>
    internal class BackgroundFileSystem
    {
        public string RootDirectory { get; private set; }
        public string SearchPattern { get; private set; }
        public SearchOption SearchOption { get; private set; }

        private FileSystemWatcher fileSystemWatcher { get; set; }
        private BlockingCollection<FileSystemEventArgs> directoryChangesEvents { get; set; }
        private Task backgroundScanner { get; set; }

        private object fsCacheLocker { get; } = new();
        private List<string> fsCache { get; } = new();

        public BackgroundFileSystem(string rootDirectory, string searchPattern, SearchOption searchOptions)
        {
            RootDirectory = rootDirectory;
            SearchPattern = searchPattern;
            SearchOption = searchOptions;

            Init();
        }

        public string FindFile(System.Text.RegularExpressions.Regex regex)
        {
            lock (fsCacheLocker)
                return fsCache.FirstOrDefault(s => regex.IsMatch(s));
        }

        public void RefreshFiles()
        {
            lock (fsCacheLocker)
            {
                fsCache.Clear();
                fsCache.AddRange(Directory.EnumerateFiles(RootDirectory, SearchPattern, SearchOption));
            }
        }

        private void Init()
        {
            Stop();

            lock (fsCacheLocker)
                fsCache.AddRange(Directory.EnumerateFiles(RootDirectory, SearchPattern, SearchOption));

            directoryChangesEvents = new BlockingCollection<FileSystemEventArgs>();
			fileSystemWatcher = new FileSystemWatcher(RootDirectory)
			{
				IncludeSubdirectories = true,
				EnableRaisingEvents = true
			};
			fileSystemWatcher.Created += FileSystemWatcher_Changed;
            fileSystemWatcher.Deleted += FileSystemWatcher_Changed;
            fileSystemWatcher.Renamed += FileSystemWatcher_Changed;
            fileSystemWatcher.Error += FileSystemWatcher_Error;

            backgroundScanner = new Task(BackgroundScanner);
            backgroundScanner.Start();
        }
        private void Stop()
        {
            //Stop raising events
            fileSystemWatcher?.Dispose();

            //Calling CompleteAdding() will cause background scanner to terminate.
            directoryChangesEvents?.CompleteAdding();

            //Wait for background scanner to terminate before reinitializing.
            backgroundScanner?.Wait();

            //Dispose of directoryChangesEvents after backgroundScanner exists.
            directoryChangesEvents?.Dispose();

            lock (fsCacheLocker)
                fsCache.Clear();
        }

        private void FileSystemWatcher_Error(object sender, ErrorEventArgs e)
        {
            Stop();
            Init();
        }

        private void FileSystemWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            directoryChangesEvents.Add(e);
        }

        #region Background Thread
        private void BackgroundScanner()
        {
            while (directoryChangesEvents.TryTake(out FileSystemEventArgs change, -1))
            {
                lock (fsCacheLocker)
                    UpdateLocalCache(change);
            }
        }

        private void UpdateLocalCache(FileSystemEventArgs change)
        {
            if (change.ChangeType == WatcherChangeTypes.Deleted)
            {
                RemovePath(change.FullPath);
            }
            else if (change.ChangeType == WatcherChangeTypes.Created)
            {
                AddPath(change.FullPath);
            }
            else if (change.ChangeType == WatcherChangeTypes.Renamed && change is RenamedEventArgs renameChange)
            {
                RemovePath(renameChange.OldFullPath);
                AddPath(renameChange.FullPath);
            }
        }

        private void RemovePath(string path)
        {
            var pathsToRemove = fsCache.Where(p => p.StartsWith(path)).ToArray();

            foreach (var p in pathsToRemove)
                fsCache.Remove(p);
        }

        private void AddPath(string path)
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                AddUniqueFiles(Directory.EnumerateFiles(path, SearchPattern, SearchOption));
            else
                AddUniqueFile(path);
        }
        private void AddUniqueFiles(IEnumerable<string> newFiles)
        {
            foreach (var file in newFiles)
                AddUniqueFile(file);
        }
        private void AddUniqueFile(string newFile)
        {
            if (!fsCache.Contains(newFile))
                fsCache.Add(newFile);
        }

        #endregion

        ~BackgroundFileSystem() => Stop();
    }
}
