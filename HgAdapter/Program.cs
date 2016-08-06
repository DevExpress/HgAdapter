using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;

[assembly: InternalsVisibleTo("HgAdapter.Tests")]

namespace HgAdapter {

    class Program {
        ExtraArguments _extra;
        Logger _logger;
        string _stateFileName;
        State _state;

        internal TextWriter StdOut = Console.Out;
        internal TextWriter StdErr = Console.Error;
        internal State StateOverride;

        string _tempListFile;

        static int Main(string[] args) {
            return new Program().Run(args);
        }

        internal int Run(string[] args) {
            try {
                _extra = new ExtraArguments(args);

                var uid = CalcFileDifferentiator();

                _logger = new Logger("HgAdapter." + uid + ".log", StdErr);
                _logger.PutToFile("----------");

                _stateFileName = "HgAdapterState." + uid + ".xml";
                _state = LoadState();

                var action = args[0];

                if(action == "GETMODS") {
                    // Executed on the master CCNet server
                    // State is saved to \\ciserver\LocalProjects
                    // CCNet ops confirm that projects with shared LocalProject are allowed

                    var hg = new HgCli(_extra.RepoPath, _logger);
                    var integrationDate = ParseDate(args[1]);
                    var prevIntegrationDate = ParseDate(args[2]);
                    var prevTip = _state.GetTip();
                    var isInitialCheck = String.IsNullOrEmpty(prevTip);

                    _logger.PutToFile("checking modifications from " + prevIntegrationDate.ToString("s") + " to " + integrationDate.ToString("s"));

                    if(isInitialCheck || new HgInternals(_extra.RepoPath, _logger).HasRepoChangedSince(prevIntegrationDate)) {
                        var newTip = hg.GetTip(_extra.RevSet, prevTip);
                        if(!String.IsNullOrEmpty(newTip) && newTip != prevTip) {
                            _logger.PutToFile("new checkpoint: " + newTip);
                            _state.AddCheckpoint(integrationDate, newTip);
                        } else {
                            _logger.PutToFile("no new changesets");
                        }
                    }

                    var trimCount = _state.Checkpoints.RemoveAll(c => (DateTime.Now - c.Date).TotalDays > 7);
                    if(trimCount > 0)
                        _logger.PutToFile(trimCount + " old checkpoints trimmed");

                    StdOut.Write("<ArrayOfModification>");
                    PrintModifications(hg, integrationDate, prevIntegrationDate);
                    StdOut.Write("</ArrayOfModification>");

                    SaveState();
                    return 0;
                }


                if(action == "GETSOURCE") {
                    // Executed on a worker VM
                    // State changes are not saved

                    GetSource(ParseDate(args[2]), args[1]);
                    return 0;
                }

                throw new NotSupportedException();

            } catch(Exception x) {
                if(_logger != null)
                    _logger.Error(x.Message);
                else
                    StdErr.WriteLine(x);
                return 1;
            } finally {
                DeleteTempListFile();
            }

        }

        State LoadState() {
            if(StateOverride != null)
                return StateOverride;

            State result = null;

            try {
                var serializer = new XmlSerializer(typeof(State));                
                using(var stream = File.OpenRead(_stateFileName)) {
                    result = (State)serializer.Deserialize(stream);
                }                
            } catch {
            }

            if(result == null) {
                return new State {
                    RepoPath = _extra.RepoPath,
                    RevSet = _extra.RevSet,
                    Include = _extra.Include
                };
            }

            return result;
        }

        void SaveState() {
            if(StateOverride != null)
                return;

            var serializer = new XmlSerializer(typeof(State));
            using(var writer = new StreamWriter(_stateFileName)) {
                serializer.Serialize(writer, _state); 
            }
        }

        void PrintModifications(HgCli hg, DateTime integrationDate, DateTime prevIntegrationDate) {
            var range = _state.DateRangeToHashRange(prevIntegrationDate, integrationDate);
            _logger.PutToFile("range: " + range);
            if(range.IsGood())
                PrintModifications(hg, integrationDate, "(" + _extra.RevSet + ") and (" + range.ToRevSet() + ")");
            else
                _logger.PutToFile("range is empty or not defined");
        }

        void PrintModifications(HgCli hg, DateTime integrationDate, string revset) {
            var log = hg.GetModifications(revset, NormalizeInclude(_extra.Include));

            _logger.PutToFile("hg log entries: " + log.Entries.Count);

            foreach(var entry in log.Entries) {
                foreach(var path in entry.PathItems) {
                    var element = new XElement("Modification");
                    element.Add(new XElement("Comment", entry.Msg));
                    element.Add(new XElement("FileName", path.Path));
                    element.Add(new XElement("ModifiedTime", integrationDate));
                    element.Add(new XElement("Type", entry.Hash.Substring(0, 12) + " (" + path.Action + ")"));
                    element.Add(new XElement("UserName", entry.Author.Name));
                    StdOut.Write(element);
                }
            }
        }
      
        void GetSource(DateTime integrationDate, string targetPath) {
            var dateStr = integrationDate.ToString("s");
            _logger.PutToFile("getting source for date " + dateStr);

            if(!String.IsNullOrEmpty(_extra.SubDir))
                targetPath = Path.Combine(targetPath, _extra.SubDir);

            if(!Path.IsPathRooted(targetPath))
                throw new InvalidOperationException("Target path must be rooted");

            var revset = _state.GetTip(integrationDate);
            if(String.IsNullOrEmpty(revset)) {
                _logger.PutToFile("no tips known to date " + dateStr + ", falling back to last known tip");
                revset = _state.GetTip();
                if(String.IsNullOrEmpty(revset)) {
                    _logger.PutToFile("no tips known, fallback to " + _extra.RevSet);
                    revset = _extra.RevSet;
                } else {
                    _logger.PutToFile("last known tip is " + revset);
                }
            } else {
                _logger.PutToFile("tip to date " + dateStr + " is " + revset);
            }

            new HgCli(_extra.RepoPath, _logger).GetFiles(revset, NormalizeInclude(_extra.Include), targetPath);
        }

        static DateTime ParseDate(string text) {
            switch(text) { 
                case "(NOW)":
                    return DateTime.Now;
                case "(MAX)":
                    return DateTime.MaxValue;
            }
            return DateTime.Parse(text);
        }

        string CalcFileDifferentiator() { 
            var key = String.Join("|", _extra.RepoPath, _extra.RevSet);
            using(var sha1 = new SHA1Managed()) {
                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(key));
                return String.Join("", hash.Select(b => b.ToString("x2")));
            }        
        }

        string NormalizeInclude(string value) {
            if(value == null || value == "." || value == "/")
                return null;

            const string dxBuildFilesPrefix = "files:";
            const string listPrefix = "listfile:";

            if(value.StartsWith(dxBuildFilesPrefix)) {
                _tempListFile = Path.GetTempFileName();
                File.WriteAllLines(_tempListFile, Regex.Split(value.Substring(dxBuildFilesPrefix.Length).Trim(), "\\s+"));
                value = listPrefix + _tempListFile;
            }

            if(value.StartsWith(listPrefix)) {
                var path = value.Substring(listPrefix.Length);

                if(File.Exists(path) && new FileInfo(path).Length >= 3) {
                    using(var stream = File.OpenRead(path)) {
                        var bytes = new byte[3];
                        stream.Read(bytes, 0, 3);
                        if(Encoding.UTF8.GetPreamble().SequenceEqual(bytes))
                            throw new Exception("Unicode BOM detected in " + path);
                    }                    
                }

                if(!Path.IsPathRooted(path))
                    return listPrefix + Path.Combine(Directory.GetCurrentDirectory(), path);
            }

            return value;
        }

        void DeleteTempListFile() {
            if(String.IsNullOrEmpty(_tempListFile))
                return;

            try {
                File.Delete(_tempListFile);
            } catch { 
            }        
        }

    }
}
