using HgAdapter.XmlLogFormat;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace HgAdapter {

    class HgCli {
        string _repoPath;
        Logger _logger;

        class ArgumentsBuilder {
            StringBuilder _result = new StringBuilder();

            public ArgumentsBuilder(string repositoryPath, string command) {
                Append(command);
                Append("-R", repositoryPath);
                Append("-y");
            }

            public ArgumentsBuilder Append(string name, string value = null) {
                if(_result.Length > 0)
                    _result.Append(" ");
                _result.Append(Quote(name));
                if(!String.IsNullOrEmpty(value)) {
                    _result.Append(" ");
                    _result.Append(Quote(value));
                }
                return this;
            }

            public override string ToString() {
                return _result.ToString();
            }

            static string Quote(string value) {
                if(Regex.IsMatch(value, "^[\\w.-]+$"))
                    return value;

                return "\"" + value.Replace("\"", "\\\"") + "\"";
            }
        }

        public HgCli(string repoPath, Logger logger) {
            _repoPath = repoPath;
            _logger = logger;
        }

        string Exec(ArgumentsBuilder args) {
            var pi = new ProcessStartInfo {
                FileName = GetHgExePath(),
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _repoPath,
                CreateNoWindow = true
            };

            _logger.PutToFile("[hg cli] " + pi.FileName + " " + pi.Arguments);

            using(var p = Process.Start(pi)) {
                string stdOut = p.StandardOutput.ReadToEnd();
                string stdErr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if(p.ExitCode != 0)
                    throw new Exception(stdErr);
                return stdOut;
            }
        }

        public string GetTip(string revset, string prevKnownTip) {
            // specifying prev tip makes search faster
            if(!String.IsNullOrEmpty(prevKnownTip))
                revset = "(" + revset + ") and (" + prevKnownTip + ":)";

            var nodes = new List<string>();

            while(true) {
                var args = new ArgumentsBuilder(_repoPath, "log")
                    .Append("-r", "max(" + revset + ")")
                    .Append("--template", "{node} {branch}");

                var output = Exec(args).Trim();
                if(String.IsNullOrEmpty(output))
                    break;

                var node = output.Substring(0, 40);
                if(node == prevKnownTip)
                    break;

                var branch = output.Substring(41);

                nodes.Add(node);
                revset += " - branch('" + branch + "')";
            }

            return nodes.LastOrDefault();
        }

        public void GetFiles(string revset, string include, string targetPath) {
            var args = new ArgumentsBuilder(_repoPath, "archive")
                .Append("--no-decode")
                .Append("--subrepos")
                .Append("-r", revset);

            if(!String.IsNullOrEmpty(include))
                args.Append("-I", include);

            args.Append(targetPath);

            Exec(args);
        }

        public Log GetModifications(string revset, string include) {
            var changes = GetModificationsCore(revset, include, false, true);

            // NOTE: -I flag does not work well for merges, http://narkive.com/6Xkb97HW:2.64.37
            var allMerges = GetModificationsCore(revset, null, true, false);
            foreach(var entry in allMerges.Entries) {
                if(DoesMergeAffectSubset(entry.Hash, include)) {
                    entry.PathItems = new List<PathInfo> { 
                        new PathInfo {
                            Action = "Merge",
                            Path = "-"
                        }
                    };
                    changes.Entries.Add(entry);
                } else {
                    _logger.PutToFile("Merge " + entry.Hash + " does not affect " + include);
                }
            }

            return changes;
        }

        Log GetModificationsCore(string revset, string include, bool mergesOnly, bool retreivePaths) {
            var args = new ArgumentsBuilder(_repoPath, "log")
                .Append("-r", revset)
                .Append("--style", "xml")
                .Append(mergesOnly ? "-m" : "-M");

            if(!String.IsNullOrEmpty(include))
                args.Append("-I", include);

            if(retreivePaths)
                args.Append("-v");

            string xmlLog = Exec(args);

            if(String.IsNullOrWhiteSpace(xmlLog))
                return new Log();

            using(var reader = new StringReader(xmlLog)) {
                return (Log)new XmlSerializer(typeof(Log)).Deserialize(reader);
            }
        }

        bool DoesMergeAffectSubset(string hash, string include) {
            if(String.IsNullOrEmpty(include))
                return true;

            var args = new ArgumentsBuilder(_repoPath, "stat")
                .Append("--change", hash)
                .Append("-I", include);

            return !String.IsNullOrWhiteSpace(Exec(args));
        }

        static string GetHgExePath() {
            var path = Path.GetDirectoryName(typeof(HgCli).Assembly.Location);
            path = Path.Combine(path, "hg.exe");
            if(File.Exists(path))
                return path;

            return "hg.exe";
        }

    }

}
