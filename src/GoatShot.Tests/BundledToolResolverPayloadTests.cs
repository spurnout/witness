using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BundledToolResolverPayloadTests
{
    private const int PeOffset = 0x40;
    private const int HeaderLength = 0x200;
    // Odd length so the signed fixture needs alignment padding before its certificate table.
    private static readonly byte[] Payload = [1, 2, 0, 0, 9];

    [TestMethod]
    public void OpenAssetPayload_ReadsFooterAtEndOfUnsignedExecutable()
    {
        using var executable = new MemoryStream(BuildExecutable(signed: false));

        CollectionAssert.AreEqual(Payload, ReadPayload(executable));
    }

    [TestMethod]
    public void OpenAssetPayload_ReadsFooterBeforeAuthenticodeCertificateTable()
    {
        using var executable = new MemoryStream(BuildExecutable(signed: true));

        CollectionAssert.AreEqual(Payload, ReadPayload(executable));
    }

    [TestMethod]
    public void ResolvePayloadEnd_IgnoresCertificateTableThatIsNotAtEndOfFile()
    {
        var bytes = BuildExecutable(signed: false);
        // A security entry pointing into the middle of the file is not a trailing signature.
        WriteSecurityDirectory(bytes, offset: 0x100, size: 0x10);
        using var executable = new MemoryStream(bytes);

        Assert.AreEqual(bytes.Length, BundledToolResolver.ResolvePayloadEnd(executable));
    }

    [TestMethod]
    public void ResolvePayloadEnd_TreatsNonPeStreamAsUnsigned()
    {
        using var executable = new MemoryStream(new byte[16]);

        Assert.AreEqual(16, BundledToolResolver.ResolvePayloadEnd(executable));
    }

    private static byte[] ReadPayload(Stream executable)
    {
        using var payload = BundledToolResolver.OpenAssetPayload(executable);
        using var copy = new MemoryStream();
        payload.CopyTo(copy);
        return copy.ToArray();
    }

    private static byte[] BuildExecutable(bool signed)
    {
        using var stream = new MemoryStream();
        var header = new byte[HeaderLength];
        header[0] = (byte)'M';
        header[1] = (byte)'Z';
        BitConverter.GetBytes(PeOffset).CopyTo(header, 0x3C);
        header[PeOffset] = (byte)'P';
        header[PeOffset + 1] = (byte)'E';
        BitConverter.GetBytes((ushort)0x20b).CopyTo(header, PeOffset + 24);
        stream.Write(header);

        stream.Write(Payload);
        stream.Write(BitConverter.GetBytes((long)Payload.Length));
        stream.Write("GOATSHOTASSET1!!"u8);
        if (!signed)
        {
            return stream.ToArray();
        }

        while (stream.Length % 8 != 0)
        {
            stream.WriteByte(0);
        }

        var certificateOffset = (int)stream.Length;
        var certificate = new byte[24];
        certificate[0] = 0xAB;
        stream.Write(certificate);
        var bytes = stream.ToArray();
        WriteSecurityDirectory(bytes, certificateOffset, certificate.Length);
        return bytes;
    }

    private static void WriteSecurityDirectory(byte[] bytes, int offset, int size)
    {
        // PE32+ data directories start 112 bytes into the optional header; security is entry 4.
        var entry = PeOffset + 24 + 112 + (4 * 8);
        BitConverter.GetBytes(offset).CopyTo(bytes, entry);
        BitConverter.GetBytes(size).CopyTo(bytes, entry + 4);
    }
}
