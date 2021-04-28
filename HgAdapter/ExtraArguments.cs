using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HgAdapter {

    class ExtraArguments {
        public string RepoPath { get; private set; }
        public string RevSet { get; private set; }
        public string Include { get; private set; }
        public string SubDir { get; private set; }
        public int TimeoutInMilliseconds { get; private set; }

        public ExtraArguments(string[] argv) {
            foreach(var arg in argv) {
                if(arg.StartsWith("--repo"))
                    RepoPath = GetValue(arg);
                else if(arg.StartsWith("--revset"))
                    RevSet = GetValue(arg);
                else if(arg.StartsWith("--include"))
                    Include = GetValue(arg);
                else if(arg.StartsWith("--subdir"))
                    SubDir = GetValue(arg);
                else if(arg.StartsWith("--timeout"))
                    TimeoutInMilliseconds = Int32.Parse(GetValue(arg));
            }

            if(String.IsNullOrEmpty(RepoPath))
                throw new Exception("--repo argument is required");

            if(String.IsNullOrEmpty(RevSet))
                RevSet = "branch(default)";

            if(TimeoutInMilliseconds == default)
                TimeoutInMilliseconds = Int32.MaxValue;
        }

        static string GetValue(string arg) {
            return arg.Substring(1 + arg.IndexOf("="));
        }

    }

}
