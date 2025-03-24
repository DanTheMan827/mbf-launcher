using System.Net.Sockets;
using System.Text;

Console.WriteLine($"{AdbDetector.IsAdbDevice("10.10.0.119", 5555)}");
public class AdbDetector
{
    // Helper method to create a CNXN handshake message.
    private static byte[] CreateCnxnMessage()
    {
        // Example values—these should match your ADB protocol version and system requirements.
        int command = 0x4e584e43; // "CNXN"
        int arg0 = 0x10000001;    // version
        int arg1 = 0;          // max data
        int payloadLength = 0;
        int checksum = 0;
        int magic = ~command;

        using (MemoryStream ms = new MemoryStream(24))
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write(command);
            writer.Write(arg0);
            writer.Write(arg1);
            writer.Write(payloadLength);
            writer.Write(checksum);
            writer.Write(magic);
            return ms.ToArray();
        }
    }

    // Method to determine if a TCP socket is an ADB device.
    public static bool IsAdbDevice(string host, int port = 5555)
    {
        try
        {
            using (TcpClient client = new TcpClient(host, port))
            using (NetworkStream stream = client.GetStream())
            {
                // Send the handshake request
                byte[] handshake = [
                    0x43, 0x4e, 0x58, 0x4e, // CNXN
                    0x01, 0x00, 0x00, 0x10, // Version
                    0x00, 0x00, 0x00, 0x00, // Max data
                    0x00, 0x00, 0x00, 0x00, // Payload length
                    0x00, 0x00, 0x00, 0x00, // Checksum
                    0xbc, 0xb1, 0xa7, 0xb1  // Magic
                ];
                stream.Write(handshake, 0, handshake.Length);
                stream.Flush();

                // Wait for a response
                byte[] headerBytes = new byte[24];
                int bytesRead = 0;
                while (bytesRead < headerBytes.Length)
                {
                    int read = stream.Read(headerBytes, bytesRead, headerBytes.Length - bytesRead);
                    if (read <= 0)
                        throw new IOException("Stream closed unexpectedly while reading header.");
                    bytesRead += read;
                }

                // Parse the response header
                using (MemoryStream headerStream = new MemoryStream(headerBytes))
                using (BinaryReader headerReader = new BinaryReader(headerStream))
                {
                    int responseCommand = headerReader.ReadInt32();
                    int responseArg0 = headerReader.ReadInt32();
                    int responseArg1 = headerReader.ReadInt32();
                    int responsePayloadLength = headerReader.ReadInt32();
                    int responseChecksum = headerReader.ReadInt32();
                    int responseMagic = headerReader.ReadInt32();

                    string responseCommandString = Encoding.ASCII.GetString(BitConverter.GetBytes(responseCommand));

                    // Validate magic field: it should be the bitwise NOT of responseCommand.
                    if (responseCommand != ~responseMagic)
                        return false;

                    string[] validCommands = {
                        "SYNC",
                        "CLSE",
                        "WRTE",
                        "AUTH",
                        "OPEN",
                        "CNXN",
                        "STLS",
                        "OKAY"
                    };

                    return validCommands.Contains(responseCommandString);
                }
            }
        }
        catch (Exception)
        {
            // If any exceptions occur, it's likely not a valid ADB device.
            return false;
        }
    }
}

