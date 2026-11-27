using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

// A loopback FTP protocol fixture: services connect over real TCP and passive data channels.
sealed class LocalFtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _sessions = [];
    private readonly Task _accept;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public ConcurrentQueue<string> Commands { get; } = new();
    public bool DenyWrites { get; set; }
    public bool DropUploadConnection { get; set; }
    public LocalFtpServer() { _listener.Start(); _accept = AcceptAsync(); }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                lock (_sessions) _sessions.Add(SessionAsync(socket));
            }
        }
        catch (OperationCanceledException) { }
    }

    private static string Absolute(string input, string current) => DevSAK.Services.FtpDestinationValidation.NormalizePath(input.StartsWith('/') ? input : current + "/" + input);
    private static string[] ChildDirectories(string path) => path switch
    {
        "/" => ["deploy", "pasta com espaço"],
        "/deploy" => ["releases"],
        _ => []
    };
    private static bool DirectoryExists(string path) => path is "/" or "/deploy" or "/deploy/releases" or "/pasta com espaço";

    private async Task SessionAsync(TcpClient control)
    {
        using (control)
        {
            TcpListener? data = null;
            try
            {
                using var reader = new StreamReader(control.GetStream(), Encoding.UTF8);
                using var writer = new StreamWriter(control.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
                string cwd = "/";
                await writer.WriteLineAsync("220 Local FTP test server");
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    int space = line.IndexOf(' ');
                    string command = (space < 0 ? line : line[..space]).ToUpperInvariant();
                    string argument = space < 0 ? "" : line[(space + 1)..];
                    Commands.Enqueue(command == "PASS" ? "PASS [omitted]" : line);
                    switch (command)
                    {
                        case "USER": await writer.WriteLineAsync("331 Password required"); break;
                        case "PASS": await writer.WriteLineAsync(argument == "test-secret" ? "230 Logged in" : "530 Login incorrect: invalid credentials"); break;
                        case "FEAT": await writer.WriteLineAsync("211-Features\r\n UTF8\r\n SIZE\r\n MDTM\r\n MLST type*;size*;modify*;\r\n EPSV\r\n211 End"); break;
                        case "SYST": await writer.WriteLineAsync("215 UNIX Type: L8"); break;
                        case "PWD": case "XPWD": await writer.WriteLineAsync($"257 \"{cwd}\" is current directory"); break;
                        case "CWD":
                            var path = Absolute(argument, cwd);
                            if (DirectoryExists(path)) { cwd = path; await writer.WriteLineAsync("250 Directory changed"); }
                            else await writer.WriteLineAsync("550 Permission denied or directory not found");
                            break;
                        case "TYPE": case "OPTS": case "NOOP": await writer.WriteLineAsync("200 OK"); break;
                        case "EPSV": case "PASV":
                            data?.Stop();
                            data = new TcpListener(IPAddress.Loopback, 0);
                            data.Start();
                            int port = ((IPEndPoint)data.LocalEndpoint).Port;
                            await writer.WriteLineAsync(command == "EPSV" ? $"229 Entering Extended Passive Mode (|||{port}|)" : $"227 Entering Passive Mode (127,0,0,1,{port / 256},{port % 256})");
                            break;
                        case "SIZE":
                            if (Files.TryGetValue(Absolute(argument, cwd), out var bytes)) await writer.WriteLineAsync("213 " + bytes.Length);
                            else await writer.WriteLineAsync("550 File not found");
                            break;
                        case "MLSD": case "LIST": case "NLST":
                            if (data == null) { await writer.WriteLineAsync("425 No data channel"); break; }
                            string listingPath = string.IsNullOrWhiteSpace(argument) || argument.StartsWith('-') ? cwd : Absolute(argument, cwd);
                            await writer.WriteLineAsync("150 Opening data connection");
                            using (var connection = await data.AcceptTcpClientAsync(_stop.Token))
                            using (var listing = new StreamWriter(connection.GetStream(), new UTF8Encoding(false)) { NewLine = "\r\n" })
                            {
                                foreach (string name in ChildDirectories(listingPath))
                                    await listing.WriteLineAsync(command == "MLSD" ? $"type=dir;modify=20260101000000; {name}" : command == "LIST" ? $"drwxr-xr-x 1 ftp ftp 0 Jan 01 2026 {name}" : name);
                                foreach (var file in Files.Where(file => DevSAK.Services.FtpDestinationValidation.ParentPath(file.Key) == listingPath))
                                {
                                    string name = file.Key[(file.Key.LastIndexOf('/') + 1)..];
                                    await listing.WriteLineAsync(command == "MLSD" ? $"type=file;size={file.Value.Length};modify=20260101000000; {name}" : command == "LIST" ? $"-rw-r--r-- 1 ftp ftp {file.Value.Length} Jan 01 2026 {name}" : name);
                                }
                            }
                            data.Stop(); data = null;
                            await writer.WriteLineAsync("226 Listing complete");
                            break;
                        case "STOR":
                            if (DenyWrites) { await writer.WriteLineAsync("550 Write permission denied (RW required)"); data?.Stop(); data = null; break; }
                            if (DropUploadConnection) return;
                            if (data == null) { await writer.WriteLineAsync("425 No data channel"); break; }
                            await writer.WriteLineAsync("150 Ready to receive");
                            using (var connection = await data.AcceptTcpClientAsync(_stop.Token))
                            using (var received = new MemoryStream())
                            {
                                await connection.GetStream().CopyToAsync(received, _stop.Token);
                                Files[Absolute(argument, cwd)] = received.ToArray();
                            }
                            data.Stop(); data = null;
                            await writer.WriteLineAsync("226 Transfer complete");
                            break;
                        case "DELE": Files.TryRemove(Absolute(argument, cwd), out _); await writer.WriteLineAsync("250 Deleted"); break;
                        case "QUIT": await writer.WriteLineAsync("221 Goodbye"); return;
                        default: await writer.WriteLineAsync("502 Command not implemented"); break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            finally { data?.Stop(); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _accept;
        Task[] sessions;
        lock (_sessions) sessions = _sessions.ToArray();
        await Task.WhenAll(sessions);
        _stop.Dispose();
    }
}
