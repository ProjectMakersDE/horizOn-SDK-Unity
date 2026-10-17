using System.Diagnostics;
using System.Reflection;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Network.Responses;

static class ChatHistoryContractTests
{
    const string User = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Channel = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    static ChatHistoryStore Store(string root, string key = "local-fixture-key") => new(root, key, User);
    static ChatMessage Message(int n) => new() { id = n.ToString("x8") + "-cccc-4ccc-8ccc-cccccccccccc", channelId = Channel, authorId = User,
        text = "local-only 😀\n" + n, createdAt = "2026-10-17T00:00:00.000Z", expiresAt = "2026-10-17T00:00:30.000Z" };
    public static bool TryRunChild(string[] args)
    {
        if (args.Length != 2 || args[0] != "--chat-history-restart") return false;
        var store = Store(args[1]); Require(store.Read(Channel).messages.Length == 50, "real restarted process reads archive");
        Require(!store.Append(Channel, new[] { Message(149) }), "restart does not duplicate");
        Require(store.Append(Channel, new[] { Message(150) }), "restarted process appends"); return true;
    }
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "horizon-chat-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = Store(root);
            for (int i = 0; i < 150; i++) Require(s.Append(Channel, new[] { Message(i) }), "new message");
            Require(!s.Append(Channel, new[] { Message(149) }), "duplicate no-op");
            var changed = Message(149); changed.text = "wrong"; Reject(() => s.Append(Channel, new[] { changed }), "conflicting immutable text");
            var first = s.Read(Channel, limit: 20); Require(first.messages.Length == 20 && first.messages[0].id == Message(149).id, "bounded latest page");
            var ids = new HashSet<string>(); string cursor = null;
            do { var p = s.Read(Channel, cursor, 20); foreach (var m in p.messages) Require(ids.Add(m.id), "non-overlapping pages"); cursor = p.nextCursor; } while (cursor != null);
            Require(ids.Count == 150, "no hidden history cap");
            var info = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            info.ArgumentList.Add("--chat-history-restart"); info.ArgumentList.Add(root);
            using (var child = Process.Start(info)) { Require(child.WaitForExit(20000) && child.ExitCode == 0, "separate process restart"); }
            Require(s.Read(Channel, limit: 1).messages[0].id == Message(150).id, "existing reader follows appended tail");
            Require(Store(root, "different-key").Read(Channel).messages.Length == 0, "project separation");
            Require(new ChatHistoryStore(root, "local-fixture-key", Channel).Read(Channel).messages.Length == 0, "user separation");
            Require(!Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(f => f.Contains("local-fixture-key")), "no key in path");
            s.Clear(Channel); s.Append(Channel, new[] { Message(1) }); Reject(() => s.Read(Channel, first.nextCursor), "clear invalidates old cursor");
            s.ClearAll(); Require(s.Read(Channel).messages.Length == 0, "explicit all clear");
            string bad = Path.Combine(root, "not-a-directory"); File.WriteAllText(bad, "test"); Reject(() => Store(bad).Append(Channel, new[] { Message(1) }), "real IO failure");
            s.Append(Channel, new[] { Message(1) }); var file = Directory.EnumerateFiles(root, Channel + ".jsonl", SearchOption.AllDirectories).Single();
            File.AppendAllText(file, "{\"id\":"); Reject(() => s.Read(Channel), "partial append visible");
            Require(File.ReadAllText(file).EndsWith("{\"id\":"), "partial record never overwritten");
            foreach (string invalid in new[] { "\ud800", "\u0000", new string('x', 501), "\r" }) Reject(() => ChatJson.Text(invalid), "invalid message text");
            Require(ChatJson.Text(string.Concat(Enumerable.Repeat("😀", 500))).Length == 1000, "500 Unicode scalars/2000 bytes accepted");
            Console.WriteLine("Chat local history: 151 messages, bounded pagination, real second process, identity isolation, conflict, clear, corruption and IO checks passed.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    static void Require(bool yes, string what) { if (!yes) throw new Exception("Chat: " + what); }
    static void Reject(Action action, string what)
    { try { action(); } catch (Exception e) when (e is InvalidDataException || e is IOException || e is FormatException || e is ArgumentException) { return; } throw new Exception("Chat accepted: " + what); }
}
