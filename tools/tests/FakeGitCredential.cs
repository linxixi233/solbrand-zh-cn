using System;
using System.IO;
using System.Text;

// Offline fixture for the Git credential wire protocol; no real credentials.
static class FakeGitCredential {
    static int Main() {
        using (var bytes = new MemoryStream()) {
            Console.OpenStandardInput().CopyTo(bytes);
            var request = bytes.ToArray();
            if (request.Length >= 3 && request[0] == 0xef && request[1] == 0xbb && request[2] == 0xbf) {
                Console.Error.WriteLine("fatal: refusing to work with credential missing protocol field");
                return 128;
            }
            var text = Encoding.UTF8.GetString(request).Replace("\r\n", "\n");
            if (!text.StartsWith("protocol=https\n") || !text.Contains("host=github.com\n") || !text.EndsWith("\n\n")) return 128;
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.Write("protocol=https\nhost=github.com\nusername=fixture\npassword=fixture-not-a-real-token\n\n");
            return 0;
        }
    }
}
