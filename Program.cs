using System;
using System.IO;
using System.Windows.Forms;

namespace ClipboardKeeper
{
    static class Program
    {
        static int failures = 0;

        static void Check(string name, Action act)
        {
            try { act(); Console.WriteLine("PASS  " + name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL  " + name + "  ->  " + ex.Message); }
        }

        static void AssertTrue(bool cond, string msg) { if (!cond) throw new Exception(msg); }

        // 自检模式：HistoryStore 的行为测试（TDD），--selftest 运行
        static int SelfTest()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ck-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            Func<string, string> P = f => Path.Combine(dir, f);

            Check("add puts new item first with timestamp", delegate
            {
                var s = new HistoryStore(P("a.json"));
                s.Add("one"); s.Add("two");
                AssertTrue(s.Items.Count == 2, "count");
                AssertTrue(s.Items[0].Text == "two", "newest first");
                AssertTrue(s.Items[0].At <= DateTime.Now && s.Items[0].At > DateTime.Now.AddMinutes(-1), "timestamp");
            });

            Check("duplicate copy moves item to top instead of duplicating", delegate
            {
                var s = new HistoryStore(P("b.json"));
                s.Add("one"); s.Add("two"); s.Add("one");
                AssertTrue(s.Items.Count == 2, "no duplicate entry, got " + s.Items.Count);
                AssertTrue(s.Items[0].Text == "one", "moved to top");
            });

            Check("items beyond the cap are dropped oldest first", delegate
            {
                var s = new HistoryStore(P("c.json"), 3);
                s.Add("1"); s.Add("2"); s.Add("3"); s.Add("4");
                AssertTrue(s.Items.Count == 3, "capped count");
                AssertTrue(s.Items[0].Text == "4" && s.Items[2].Text == "2", "oldest dropped");
            });

            Check("save then load on a fresh store keeps all records (reboot survives)", delegate
            {
                string path = P("d.json");
                var s1 = new HistoryStore(path);
                s1.Add("alpha"); s1.Add("beta"); s1.Save();
                var s2 = new HistoryStore(path);
                s2.Load();
                AssertTrue(s2.Items.Count == 2, "count after reload");
                AssertTrue(s2.Items[0].Text == "beta", "order after reload");
                AssertTrue(s2.Items[1].Text == "alpha", "older kept");
            });

            Check("load with no file yet yields empty history, not a crash", delegate
            {
                var s = new HistoryStore(P("missing.json"));
                s.Load();
                AssertTrue(s.Items.Count == 0, "empty");
            });

            Check("clear empties history and persists", delegate
            {
                string path = P("e.json");
                var s = new HistoryStore(path);
                s.Add("x"); s.Clear(); s.Save();
                var s2 = new HistoryStore(path); s2.Load();
                AssertTrue(s2.Items.Count == 0, "cleared");
            });

            Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : "\n" + failures + " TEST(S) FAILED");
            return failures;
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 兜底：后台工具遇到意外异常只记日志继续运行，不弹崩溃框
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                LogError(e.Exception);
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                LogError(e.ExceptionObject as Exception);
            };

            Application.Run(new TrayContext());
            return 0;
        }

        static void LogError(Exception ex)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardKeeper");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"),
                    DateTime.Now + "  " + (ex == null ? "unknown" : ex.ToString()) + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
