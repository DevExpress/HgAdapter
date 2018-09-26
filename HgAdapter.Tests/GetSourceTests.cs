using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HgAdapter.Tests {

    [TestFixture]
    public class GetSourceTests : AdapterFixture {
        const string GETSOURCE = "GETSOURCE";
        string _targetDir;

        [SetUp]
        public override void SetUp() {
            base.SetUp();
            _targetDir = Path.Combine(Directory.GetCurrentDirectory(), "target_" + Guid.NewGuid());
        }

        [TearDown]
        public override void TearDown() {
            base.TearDown();
            if(Directory.Exists(_targetDir))
                Directory.Delete(_targetDir, true);
        }

        [Test]
        public void EmptyState() {
            CommitFile("file1", "x", "x");
            ExecAdapter(GETSOURCE, _targetDir, "(MAX)");

            CollectionAssert.Contains(LoggerOutput, "no tips known to date 9999-12-31T23:59:59, falling back to last known tip");
            CollectionAssert.Contains(LoggerOutput, "no tips known, fallback to branch(default)");
            Assert.IsTrue(LoggerOutput.Any(line => line.Contains("-r \"branch(default)\"")));
            Assert.IsFalse(LoggerOutput.Any(line => line.Contains("pull")));

            Assert.IsTrue(File.Exists(Path.Combine(_targetDir, "file1")));
        }

        [TestCase("before first")]
        [TestCase("at first")]
        [TestCase("between")]
        [TestCase("at second")]
        [TestCase("after second")]
        public void SpecificDates(string scenario) {
            var date1 = new DateTime(2001, 01, 01, 9, 0, 0);
            CommitFile("1", "x", "x");
            State.AddCheckpoint(date1, "8849afd50f950ce7f0fb93eead5260604e860246");

            var date2 = new DateTime(2001, 01, 01, 9, 5, 0);
            CommitFile("2", "x", "x");
            State.AddCheckpoint(date2, "a6544c4c325287f0d9886a5aeb54007069ce3fc3");

            var archived2 = Path.Combine(_targetDir, "2");

            switch(scenario) {
                case "before first":
                    ExecAdapter(GETSOURCE, _targetDir, date1.AddMinutes(-1));
                    CollectionAssert.Contains(LoggerOutput, "no tips known to date 2001-01-01T08:59:00, falling back to last known tip");
                    CollectionAssert.Contains(LoggerOutput, "last known tip is a6544c4c325287f0d9886a5aeb54007069ce3fc3");
                    Assert.IsTrue(File.Exists(archived2));
                    break;

                case "at first":
                    ExecAdapter(GETSOURCE, _targetDir, date1);
                    CollectionAssert.Contains(LoggerOutput, "tip to date 2001-01-01T09:00:00 is 8849afd50f950ce7f0fb93eead5260604e860246");
                    Assert.IsFalse(File.Exists(archived2));
                    break;

                case "between":
                    ExecAdapter(GETSOURCE, _targetDir, date1.AddMinutes(1));
                    CollectionAssert.Contains(LoggerOutput, "tip to date 2001-01-01T09:01:00 is 8849afd50f950ce7f0fb93eead5260604e860246");
                    Assert.IsFalse(File.Exists(archived2));
                    break;

                case "at second":
                    ExecAdapter(GETSOURCE, _targetDir, date2);
                    CollectionAssert.Contains(LoggerOutput, "tip to date 2001-01-01T09:05:00 is a6544c4c325287f0d9886a5aeb54007069ce3fc3");
                    Assert.IsTrue(File.Exists(archived2));
                    break;

                case "after second":
                    ExecAdapter(GETSOURCE, _targetDir, date2.AddMinutes(1));
                    CollectionAssert.Contains(LoggerOutput, "tip to date 2001-01-01T09:06:00 is a6544c4c325287f0d9886a5aeb54007069ce3fc3");
                    Assert.IsTrue(File.Exists(archived2));
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        [Test]
        public void SubDir() {
            CommitFile("file1", "x", "x");
            ExecAdapter(GETSOURCE, _targetDir, "(MAX)", "--subdir=subdir");
            Assert.IsTrue(File.Exists(Path.Combine(_targetDir, "subdir", "file1")));
        }

        [Test]
        public void NotRootedTarget() {
            CommitFile("file1", "x", "x");

            Assert.Throws<Exception>(delegate() {
                ExecAdapter(GETSOURCE, ".", "(MAX)");
            });

            Assert.That(AdapterOutput, Is.StringContaining("[ERROR] Target path must be rooted"));
        }

        [Test]
        public void Include() {
            File.WriteAllText(Path.Combine(TempRepoDir, "a"), "a");
            File.WriteAllText(Path.Combine(TempRepoDir, "b"), "b");
            Commit("x", "x");

            ExecAdapter(GETSOURCE, _targetDir, "(MAX)", "--include=a");

            Assert.That(File.Exists(Path.Combine(_targetDir, "a")), Is.True);
            Assert.That(File.Exists(Path.Combine(_targetDir, "b")), Is.False);
        }

        [Test]
        public void ListFileRooting() {
            UsingTempFile("file1", listName => {
                CommitFile("file1", "x", "x");
                ExecAdapter(GETSOURCE, _targetDir, "(MAX)", "--include=listfile:" + listName);

                var log = String.Join(Environment.NewLine, LoggerOutput);
                var match = Regex.Match(log, "listfile:(.+" + listName + ")", RegexOptions.Multiline);
                var path = match.Groups[1].Value;
                Assert.IsTrue(Path.IsPathRooted(path));
            });
        }

        [Test]
        public void ListFileBOM() {
            UsingTempFile(new byte[] { 0xef, 0xbb, 0xbf, 0xe2, 0x98, 0x83 }, listName => {
                CommitFile("a", "b", "c");

                Assert.Throws<Exception>(delegate() {
                    ExecAdapter(GETSOURCE, _targetDir, "(MAX)", "--include=listfile:" + listName);
                });
            });

            Assert.That(AdapterOutput, Is.StringContaining("[ERROR] Unicode BOM detected"));
        }

        [Test]
        public void DxBuildInlineFiles() {
            CommitFile("1", "x", "x");
            CommitFile("2", "x", "x");
            CommitFile("3", "x", "x");

            ExecAdapter(GETSOURCE, _targetDir, "(MAX)", "--include=files: \t 1 \t 3 \t ");

            Assert.IsTrue(File.Exists(Path.Combine(_targetDir, "1")));
            Assert.IsFalse(File.Exists(Path.Combine(_targetDir, "2")));
            Assert.IsTrue(File.Exists(Path.Combine(_targetDir, "3")));
        }

        static void UsingTempFile(byte[] content, Action<string> action) {
            var name = Guid.NewGuid() + ".txt";
            File.WriteAllBytes(name, content);
            try {
                action(name);
            } finally {
                File.Delete(name);
            }
        }

        static void UsingTempFile(string content, Action<string> action) {
            UsingTempFile(Encoding.UTF8.GetBytes(content), action);
        }

    }

}
