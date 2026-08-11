using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Windows.Devices.Enumeration;
using Windows.Devices.Usb;
using Windows.Storage.Streams;

namespace GoatShot.App.Services;

public interface IAndroidDeviceTransport
{
    Task<IReadOnlyList<AndroidTransportDevice>> DiscoverAsync(CancellationToken cancellationToken);
    Task<AndroidTransportCommandResult> ExecuteAsync(
        string deviceId,
        string command,
        long maxBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed record AndroidTransportDevice(
    string Id,
    string Name,
    bool Authorized,
    string Product,
    string Model,
    string Device);

public sealed record AndroidTransportCommandResult(bool Succeeded, byte[] Output, string Message);

public sealed class WindowsUsbAdbTransport : IAndroidDeviceTransport
{
    private readonly string _keyRoot;

    public WindowsUsbAdbTransport(string localRoot)
    {
        _keyRoot = Path.Combine(localRoot, "adb-authorization");
    }

    public async Task<IReadOnlyList<AndroidTransportDevice>> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var results = new List<AndroidTransportDevice>();
        var devices = await FindDeviceInformationAsync();
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = StableId(device.Id);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await using var session = await UsbAdbSession.OpenAsync(device, _keyRoot, timeout.Token);
                results.Add(new AndroidTransportDevice(
                    id,
                    device.Name,
                    session.Authorized,
                    session.BannerValue("product"),
                    session.BannerValue("model"),
                    session.BannerValue("device")));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                results.Add(new AndroidTransportDevice(id, device.Name, false, string.Empty, device.Name, string.Empty));
            }
            catch
            {
                results.Add(new AndroidTransportDevice(id, device.Name, false, string.Empty, device.Name, string.Empty));
            }
        }

        return results;
    }

    public async Task<AndroidTransportCommandResult> ExecuteAsync(
        string deviceId,
        string command,
        long maxBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout);
        try
        {
            var devices = await FindDeviceInformationAsync();
            var selected = devices.FirstOrDefault(device => StableId(device.Id).Equals(deviceId, StringComparison.OrdinalIgnoreCase));
            if (selected is null) return new AndroidTransportCommandResult(false, [], "The selected WinUSB Android device is no longer connected.");
            await using var session = await UsbAdbSession.OpenAsync(selected, _keyRoot, bounded.Token);
            if (!session.Authorized)
            {
                return new AndroidTransportCommandResult(false, [], "Unlock the Android device and approve this computer for USB debugging.");
            }

            var output = await session.ExecuteAsync(command, maxBytes, bounded.Token);
            return new AndroidTransportCommandResult(true, output, "In-process WinUSB ADB command completed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AndroidTransportCommandResult(false, [], "The bounded WinUSB ADB operation timed out.");
        }
        catch (Exception exception)
        {
            return new AndroidTransportCommandResult(false, [], SensitiveTextDetector.Redact(exception.Message));
        }
    }

    private static async Task<IReadOnlyList<DeviceInformation>> FindDeviceInformationAsync()
    {
        var deviceClass = new UsbDeviceClass
        {
            ClassCode = 0xFF,
            SubclassCode = 0x42,
            ProtocolCode = 0x01
        };
        var selector = UsbDevice.GetDeviceClassSelector(deviceClass);
        return (await DeviceInformation.FindAllAsync(selector)).ToList();
    }

    private static string StableId(string value) =>
        "usb-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();

    private sealed class UsbAdbSession : IAsyncDisposable
    {
        private const uint Version = 0x01000001;
        private const uint MaxPayload = 1024 * 1024;
        private readonly UsbDevice _device;
        private readonly DataReader _reader;
        private readonly DataWriter _writer;
        private readonly AdbAuthorizationKey _authorizationKey;
        private uint _nextLocalId = 1;

        private UsbAdbSession(UsbDevice device, AdbAuthorizationKey authorizationKey)
        {
            _device = device;
            var input = device.DefaultInterface.BulkInPipes.FirstOrDefault()
                ?? throw new InvalidOperationException("The Android ADB WinUSB interface has no bulk input pipe.");
            var output = device.DefaultInterface.BulkOutPipes.FirstOrDefault()
                ?? throw new InvalidOperationException("The Android ADB WinUSB interface has no bulk output pipe.");
            _reader = new DataReader(input.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            _writer = new DataWriter(output.OutputStream);
            _authorizationKey = authorizationKey;
        }

        public bool Authorized { get; private set; }
        public string Banner { get; private set; } = string.Empty;

        public string BannerValue(string key)
        {
            var marker = key + ":";
            var value = Banner.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(part => part.StartsWith(marker, StringComparison.OrdinalIgnoreCase));
            return value is null ? string.Empty : value[marker.Length..].Replace('_', ' ');
        }

        public static async Task<UsbAdbSession> OpenAsync(DeviceInformation information, string keyRoot, CancellationToken cancellationToken)
        {
            var device = await UsbDevice.FromIdAsync(information.Id)
                ?? throw new InvalidOperationException("Windows could not open the Android WinUSB interface. Install/enable a compatible user-mode USB driver.");
            var session = new UsbAdbSession(device, AdbAuthorizationKey.LoadOrCreate(keyRoot));
            try
            {
                await session.HandshakeAsync(cancellationToken);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        private async Task HandshakeAsync(CancellationToken cancellationToken)
        {
            await WritePacketAsync("CNXN", Version, MaxPayload, Encoding.UTF8.GetBytes("host::features=shell_v2,cmd,stat_v2\0"), cancellationToken);
            var signatureSent = false;
            while (true)
            {
                var packet = await ReadPacketAsync(cancellationToken);
                if (packet.Command == "CNXN")
                {
                    Authorized = true;
                    Banner = Encoding.UTF8.GetString(packet.Data).TrimEnd('\0');
                    return;
                }

                if (packet.Command != "AUTH" || packet.Arg0 != 1)
                {
                    continue;
                }

                if (!signatureSent)
                {
                    await WritePacketAsync("AUTH", 2, 0, _authorizationKey.SignToken(packet.Data), cancellationToken);
                    signatureSent = true;
                }
                else
                {
                    await WritePacketAsync("AUTH", 3, 0, _authorizationKey.PublicKeyMessage, cancellationToken);
                }
            }
        }

        public async Task<byte[]> ExecuteAsync(string command, long maxBytes, CancellationToken cancellationToken)
        {
            if (!Authorized) throw new InvalidOperationException("The Android device has not authorized this user key.");
            var localId = _nextLocalId++;
            await WritePacketAsync("OPEN", localId, 0, Encoding.UTF8.GetBytes("exec:" + command + "\0"), cancellationToken);
            uint remoteId = 0;
            using var output = new MemoryStream();
            while (true)
            {
                var packet = await ReadPacketAsync(cancellationToken);
                if (packet.Command == "OKAY" && packet.Arg1 == localId)
                {
                    remoteId = packet.Arg0;
                    continue;
                }

                if (packet.Command == "WRTE" && packet.Arg1 == localId)
                {
                    remoteId = packet.Arg0;
                    if (output.Length + packet.Data.Length > maxBytes)
                    {
                        await WritePacketAsync("CLSE", localId, remoteId, [], cancellationToken);
                        throw new InvalidOperationException($"Android output exceeded the {maxBytes} byte safety limit.");
                    }
                    await output.WriteAsync(packet.Data, cancellationToken);
                    await WritePacketAsync("OKAY", localId, remoteId, [], cancellationToken);
                    continue;
                }

                if (packet.Command == "CLSE" && packet.Arg1 == localId)
                {
                    if (remoteId != 0) await WritePacketAsync("CLSE", localId, remoteId, [], cancellationToken);
                    return output.ToArray();
                }
            }
        }

        private async Task WritePacketAsync(string command, uint arg0, uint arg1, byte[] data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commandValue = BinaryPrimitives.ReadUInt32LittleEndian(Encoding.ASCII.GetBytes(command));
            var header = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), commandValue);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), arg0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), arg1);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16, 4), unchecked((uint)data.Sum(value => value)));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20, 4), commandValue ^ uint.MaxValue);
            _writer.WriteBytes(header);
            if (data.Length > 0) _writer.WriteBytes(data);
            await _writer.StoreAsync().AsTask(cancellationToken);
        }

        private async Task<AdbPacket> ReadPacketAsync(CancellationToken cancellationToken)
        {
            var header = await ReadExactAsync(24, cancellationToken);
            var commandValue = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
            if (length > MaxPayload) throw new InvalidDataException("Android ADB packet exceeded the negotiated payload size.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20, 4)) != (commandValue ^ uint.MaxValue))
                throw new InvalidDataException("Android ADB packet command integrity check failed.");
            var data = length == 0 ? [] : await ReadExactAsync((int)length, cancellationToken);
            if (unchecked((uint)data.Sum(value => value)) != BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16, 4)))
                throw new InvalidDataException("Android ADB packet payload checksum failed.");
            return new AdbPacket(
                Encoding.ASCII.GetString(header, 0, 4),
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4)),
                data);
        }

        private async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken)
        {
            var result = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var loaded = await _reader.LoadAsync((uint)(count - offset)).AsTask(cancellationToken);
                if (loaded == 0) throw new EndOfStreamException("Android ADB USB connection closed.");
                var chunk = new byte[loaded];
                _reader.ReadBytes(chunk);
                System.Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }
            return result;
        }

        public ValueTask DisposeAsync()
        {
            _reader.DetachStream();
            _writer.DetachStream();
            _reader.Dispose();
            _writer.Dispose();
            _device.Dispose();
            _authorizationKey.Dispose();
            return ValueTask.CompletedTask;
        }

        private sealed record AdbPacket(string Command, uint Arg0, uint Arg1, byte[] Data);
    }

    private sealed class AdbAuthorizationKey : IDisposable
    {
        private readonly RSA _rsa;

        private AdbAuthorizationKey(RSA rsa, byte[] publicKeyMessage)
        {
            _rsa = rsa;
            PublicKeyMessage = publicKeyMessage;
        }

        public byte[] PublicKeyMessage { get; }
        public byte[] SignToken(byte[] token) => _rsa.SignHash(token, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);

        public static AdbAuthorizationKey LoadOrCreate(string root)
        {
            Directory.CreateDirectory(root);
            var privatePath = Path.Combine(root, "adbkey.pk8");
            var rsa = RSA.Create(2048);
            if (File.Exists(privatePath)) rsa.ImportPkcs8PrivateKey(File.ReadAllBytes(privatePath), out _);
            else File.WriteAllBytes(privatePath, rsa.ExportPkcs8PrivateKey());
            var publicBytes = BuildAndroidPublicKey(rsa.ExportParameters(false));
            var publicMessage = Encoding.UTF8.GetBytes(Convert.ToBase64String(publicBytes) + $" {Environment.UserName}@{Environment.MachineName}\0");
            File.WriteAllBytes(Path.Combine(root, "adbkey.pub"), publicMessage);
            return new AdbAuthorizationKey(rsa, publicMessage);
        }

        private static byte[] BuildAndroidPublicKey(RSAParameters parameters)
        {
            var modulusBigEndian = parameters.Modulus ?? throw new InvalidOperationException("RSA modulus is missing.");
            var modulusLittleEndian = modulusBigEndian.Reverse().Concat(new byte[] { 0 }).ToArray();
            var modulus = new BigInteger(modulusLittleEndian);
            var rr = (BigInteger.One << 4096) % modulus;
            var modulusBytes = ToFixedLittleEndian(modulus, 256);
            var rrBytes = ToFixedLittleEndian(rr, 256);
            var n0 = BinaryPrimitives.ReadUInt32LittleEndian(modulusBytes);
            uint inverse = 1;
            for (var index = 0; index < 5; index++) inverse = unchecked(inverse * (2u - (n0 * inverse)));
            var output = new byte[524];
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0, 4), 64);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4, 4), unchecked(0u - inverse));
            modulusBytes.CopyTo(output, 8);
            rrBytes.CopyTo(output, 264);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(520, 4), 65537);
            return output;
        }

        private static byte[] ToFixedLittleEndian(BigInteger value, int length)
        {
            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
            if (bytes.Length > length) throw new InvalidOperationException("RSA value exceeded the ADB key field size.");
            Array.Resize(ref bytes, length);
            return bytes;
        }

        public void Dispose() => _rsa.Dispose();
    }
}
