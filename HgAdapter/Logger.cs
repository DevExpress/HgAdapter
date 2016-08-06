using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HgAdapter {

    class Logger {
        internal static ConcurrentQueue<string> FileOverride;

        const string CONSOLE_PREFIX = "[HgAdapter] ";
        const int FILE_MAX_SIZE = 2 * 1024 * 1024;

        string _fileName;
        TextWriter _stdErr;

        public Logger(string fileName, TextWriter stdErr) {
            _fileName = fileName;
            _stdErr = stdErr;
            TrimFile();
        }

        public void PutToFile(string text) {
            if(FileOverride != null)
                FileOverride.Enqueue(text);
            else
                File.AppendAllText(_fileName, "[" + DateTime.Now.ToString("s") + "] " + text + "\n");
        }

        public void Error(string text) {
            text = "[ERROR] " + text;
            _stdErr.WriteLine(CONSOLE_PREFIX + text);
            PutToFile(text);
        }

        void TrimFile() {
            if(File.Exists(_fileName) && new FileInfo(_fileName).Length > FILE_MAX_SIZE) {
                var lines = File.ReadAllLines(_fileName);
                File.WriteAllLines(_fileName, lines.Skip(lines.Length / 2));
            }
        }

    }


}
