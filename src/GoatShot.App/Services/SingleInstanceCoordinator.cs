using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace GoatShot.App.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly FileStream? _instanceLock;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _serverTask;

    public SingleInstanceCoordinator()
        : this(CreateIdentitySuffix())
    {
    }

    internal SingleInstanceCoordinator(string identitySuffix)
    {
        var safeSuffix = string.IsNullOrWhiteSpace(identitySuffix) ? "default" : identitySuffix;
        _pipeName = $"GoatShot.Personal.{safeSuffix}";
        var lockRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GoatShot",
            "instance-locks");
        Directory.CreateDirectory(lockRoot);
        try
        {
            _instanceLock = new FileStream(
                Path.Combine(lockRoot, $"{safeSuffix}.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            _instanceLock = null;
        }
    }

    public bool IsPrimary => _instanceLock is not null;

    public void StartServer(Func<SingleInstanceMessage, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!IsPrimary || _serverTask is not null)
        {
            return;
        }

        _serverTask = RunServerAsync(handler, _shutdown.Token);
    }

    public async Task<bool> SendAsync(SingleInstanceMessage message, TimeSpan? timeout = null)
    {
        using var client = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            await writer.WriteLineAsync(message.ToString().AsMemory(), timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task RunServerAsync(Func<SingleInstanceMessage, Task> handler, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var raw = await reader.ReadLineAsync(cancellationToken);
                if (Enum.TryParse<SingleInstanceMessage>(raw, ignoreCase: true, out var message))
                {
                    await handler(message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A client can exit between connecting and writing; keep serving future requests.
            }
        }
    }

    private static string CreateIdentitySuffix()
    {
        string identity;
        try
        {
            identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        }
        catch
        {
            identity = Environment.UserName;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _instanceLock?.Dispose();
        _shutdown.Dispose();
    }
}

public enum SingleInstanceMessage
{
    Activate,
    PrepareForUpdate,
    Shutdown
}
