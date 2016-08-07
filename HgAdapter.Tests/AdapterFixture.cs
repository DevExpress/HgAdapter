using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace HgAdapter.Tests {

    public abstract class AdapterFixture {
        protected string TempRepoDir;
        protected State State;
        protected ConcurrentQueue<string> LoggerOutput;
        StringBuilder _adaperOutputBuilder;

        protected string AdapterOutput { get { return _adaperOutputBuilder.ToString(); } }

        [SetUp]
        public virtual void SetUp() {
            TempRepoDir = Path.Combine(Directory.GetCurrentDirectory(), "repo_" + Guid.NewGuid());

            State = new State();

            LoggerOutput = new ConcurrentQueue<string>();
            Logger.FileOverride = LoggerOutput;

            _adaperOutputBuilder = new StringBuilder();
            
            Directory.CreateDirectory(TempRepoDir);
            ExecHG("init .");
        }

        [TearDown]
        public virtual void TearDown() {
            if(Directory.Exists(TempRepoDir))
                Directory.Delete(TempRepoDir, true);        
        }

        protected void ExecAdapter(params object[] args) {
            var adapter = new Program();
            adapter.StateOverride = State;
            adapter.StdOut = adapter.StdErr = new StringWriter(_adaperOutputBuilder);
            if(adapter.Run(args.Select(Convert.ToString).Concat(new[] { "--repo=" + TempRepoDir }).ToArray()) != 0)
                throw new Exception(AdapterOutput);
        }

        protected void CommitFile(string fileName, string user, string message) {
            File.WriteAllText(Path.Combine(TempRepoDir, fileName), "test");
            Commit(user, message);
        }

        protected void Commit(string user, string message) {
            ExecHG("commit -A -u \"" + user + "\" -m \"" + message + "\" -d \"2001-02-03 +0300\"");
        }

        protected IEnumerable<XElement> ParseModifications() {
            return XDocument.Parse(AdapterOutput).Descendants("Modification");
        }

        protected void AssertModificationElement(XElement m, string type, DateTime modifiedTime, string fileName, string userName, string comment) {
            Assert.AreEqual(type, m.Element("Type").Value);
            Assert.AreEqual(modifiedTime.ToString("s"), DateTime.Parse(m.Element("ModifiedTime").Value).ToString("s"));
            Assert.AreEqual(userName, m.Element("UserName").Value);
            Assert.AreEqual(fileName, m.Element("FileName").Value);
            Assert.AreEqual(comment, m.Element("Comment").Value);
        }

        protected void ExecHG(string args) {
            var p = Process.Start(new ProcessStartInfo {
                FileName = "hg.exe",
                Arguments = args,
                WorkingDirectory = TempRepoDir,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p.WaitForExit();
            if(p.ExitCode > 0)
                throw new Exception("ExecHG failed");
        }

    }

}
