using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace HgAdapter {

    // http://mercurial.selenic.com/wiki/FileFormats

    class HgInternals {
        string _repoPath;
        Logger _logger;
        int _timeoutInMilliseconds;
        int transactionSleepDelay = 1000;

        public HgInternals(string repoPath, int timeoutInMilliseconds, Logger logger) {
            _repoPath = repoPath;
            _logger = logger;
            _timeoutInMilliseconds = timeoutInMilliseconds;
        }

        bool WaitWhileInTransaction(out bool longRunning) {
            var wasInTransaction = false;
            var storePath = GetStorePath();
            var lockPath = Path.Combine(storePath, "lock");
            var journalPath = Path.Combine(storePath, "journal");
            var remainingMilliseconds = this._timeoutInMilliseconds;
            longRunning = false;

            while(File.Exists(lockPath) || File.Exists(journalPath)) {
                wasInTransaction = true;
                _logger.PutToFile("transaction in progress, waiting...");
                Thread.Sleep(this.transactionSleepDelay);
                remainingMilliseconds -= this.transactionSleepDelay;
                if(remainingMilliseconds <= 0) {
                    _logger.PutToFile("transaction waiting time exceeded, aborting");
                    longRunning = true;
                    break;
                }
            }

            return wasInTransaction;
        }

        public bool HasRepoChangedSince(DateTime date, out bool changeInProgress) {
            var wasInTransaction = WaitWhileInTransaction(out changeInProgress);
            if(wasInTransaction)
                return true;

            var prevJournalPath = Path.Combine(GetStorePath(), "undo");
            if(!File.Exists(prevJournalPath))
                return true;

            var transactionDate = GetFileChangeTime(prevJournalPath);
            var changed = transactionDate > date;
            if(!changed)
                _logger.PutToFile("repo not changed (last transaction at " + transactionDate.ToString("s") + ")");
            return changed;
        }

        static DateTime GetFileChangeTime(string path) {
            try {
                return Win32.GetFileChangeTime(path);
            } catch {
                return File.GetLastWriteTime(path);
            }
        }

        string GetStorePath() {
            return Path.Combine(_repoPath, ".hg/store");
        }


    }

}
