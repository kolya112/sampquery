﻿using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Globalization;
using System.Threading.Tasks;

namespace SampQueryApi
{
    public class SampQuery
    {
        private readonly int receiveArraySize = 2048;
        private readonly int timeoutMilliseconds = 5000;
        private readonly IPAddress serverIp;
        private readonly ushort serverPort;
        private readonly string serverIpString;
        private readonly IPEndPoint serverEndPoint;
        private readonly string password;
        private readonly char[] socketHeader;
        private Socket serverSocket;
        private DateTime transmitMS;

        public SampQuery(string host, ushort port)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); 

            if (!IPAddress.TryParse(host, out this.serverIp)) {
                int ipIdx = host == "localhost" ? 1 : 0; // Ignoring Ipv6
                this.serverIp = Dns.GetHostAddresses(host)[ipIdx];
            }

            this.serverEndPoint = new IPEndPoint(this.serverIp, port);

            this.serverIpString = this.serverIp.ToString();
            this.serverPort = port;

            this.socketHeader = "SAMP".ToCharArray();
        }
        public SampQuery(IPAddress ip, ushort port) : this(ip.ToString(), port) { }
        public SampQuery(string host, ushort port, string password) : this(host, port)
        {
            this.password = password;
        }
        public SampQuery(IPAddress ip, ushort port, string password) : this(ip.ToString(), port, password) {}

        private async Task<byte[]> SendSocketToServerAsync(char packetType, string cmd = null)
        {
            this.serverSocket = new Socket(this.serverEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            using(var stream = new MemoryStream())
            {
                using(var writer = new BinaryWriter(stream))
                {
                    string[] splitIp = this.serverIpString.Split('.');

                    writer.Write(this.socketHeader);

                    for (sbyte i = 0; i < splitIp.Length; i++)
                    {
                        writer.Write(Convert.ToByte(Convert.ToInt16(splitIp[i])));
                    }

                    writer.Write(this.serverPort);
                    writer.Write(packetType);

                    if (packetType == ServerPacketTypes.Rcon) {
                        writer.Write((ushort)this.password.Length);
                        writer.Write(this.password.ToCharArray());

                        writer.Write((ushort)cmd.Length);
                        writer.Write(cmd.ToCharArray());
                    }

                    this.transmitMS = DateTime.Now; 

                    await this.serverSocket.SendToAsync(stream.ToArray(), SocketFlags.None, this.serverEndPoint);
                    EndPoint rawPoint = this.serverEndPoint;
                    var data = new byte[this.receiveArraySize];

                    var task = this.serverSocket.ReceiveFromAsync(data, SocketFlags.None, rawPoint);

                    if (await Task.WhenAny(task, Task.Delay(this.timeoutMilliseconds)) != task) 
                    {
                        this.serverSocket.Close();
                        throw new SocketException(10060); // Operation timed out
                    } 

                    this.serverSocket.Close();
                    return data;
                }
                
            }
            
        }
        private byte[] SendSocketToServer(char packetType, string cmd = null)
        {
            this.serverSocket = new Socket(this.serverEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
            {
                SendTimeout = this.timeoutMilliseconds,
                ReceiveTimeout = this.timeoutMilliseconds
            };

            using(var stream = new MemoryStream())
            {
                using(var writer = new BinaryWriter(stream))
                {
                    string[] splitIp = this.serverIpString.Split('.');

                    writer.Write(this.socketHeader);

                    for (sbyte i = 0; i < splitIp.Length; i++)
                    {
                        writer.Write(Convert.ToByte(Convert.ToInt16(splitIp[i])));
                    }

                    writer.Write(this.serverPort);
                    writer.Write(packetType);

                    if (packetType == ServerPacketTypes.Rcon) {
                        writer.Write((ushort)this.password.Length);
                        writer.Write(this.password.ToCharArray());

                        writer.Write((ushort)cmd.Length);
                        writer.Write(cmd.ToCharArray());
                    }

                    this.transmitMS = DateTime.Now; 

                    this.serverSocket.SendTo(stream.ToArray(), SocketFlags.None, this.serverEndPoint);

                    EndPoint rawPoint = this.serverEndPoint;
                    var szReceive = new byte[this.receiveArraySize];

                    this.serverSocket.ReceiveFrom(szReceive, SocketFlags.None, ref rawPoint);

                    this.serverSocket.Close();
                    return szReceive;
                }
                
            }
            
        }
        /// <summary>
        /// Execute RCON command
        /// </summary>
        /// <param name="command">Command name. See https://sampwiki.blast.hk/wiki/Controlling_Your_Server#RCON_Commands</param>
        /// <returns>Server response</returns>
        /// <exception cref="System.ArgumentException">Thrown when command or RCON password is an empty string</exception>
        /// <exception cref="System.ArgumentNullException">Thrown when command or RCON password is null</exception>
        /// <exception cref="System.Net.Sockets.SocketException">Thrown when operation timed out</exception>
        /// <exception cref="SampQueryApi.RconPasswordException">Thrown when RCON password is invalid (changeme or incorrect)</exception>
        public string SendRconCommand(string command)
        {
            Helpers.CheckNullOrEmpty(command, nameof(command));
            Helpers.CheckNullOrEmpty(this.password, nameof(this.password));
            if (this.password == "changeme") throw new RconPasswordException(RconPasswordExceptionMessages.CHANGEME_NOT_ALLOWED);

            byte[] data = SendSocketToServer(ServerPacketTypes.Rcon, command);
            string response = CollectRconAnswerFromByteArray(data);

            if (response == "Invalid RCON password.\n") throw new RconPasswordException(RconPasswordExceptionMessages.INVALD_RCON_PASSWORD);

            return response;
        }

        /// <summary>
        /// Execute RCON command
        /// </summary>
        /// <param name="command">Command name. See https://sampwiki.blast.hk/wiki/Controlling_Your_Server#RCON_Commands</param>
        /// <returns>An asynchronous task that completes with the server response</returns>
        /// <exception cref="System.ArgumentException">Thrown when command or RCON password is an empty string</exception>
        /// <exception cref="System.ArgumentNullException">Thrown when command or RCON password is null</exception>
        /// <exception cref="System.Net.Sockets.SocketException">Thrown when operation timed out</exception>
        /// <exception cref="SampQueryApi.RconPasswordException">Thrown when RCON password is invalid (changeme or incorrect)</exception>
        public async Task<string> SendRconCommandAsync(string command)
        {
            Helpers.CheckNullOrEmpty(command, nameof(command));
            Helpers.CheckNullOrEmpty(this.password, nameof(this.password));
            if (this.password == "changeme") throw new RconPasswordException(RconPasswordExceptionMessages.CHANGEME_NOT_ALLOWED);

            byte[] data = await SendSocketToServerAsync(ServerPacketTypes.Rcon, command);
            string response = CollectRconAnswerFromByteArray(data);
            
            if (response == "Invalid RCON password.\n") throw new RconPasswordException(RconPasswordExceptionMessages.INVALD_RCON_PASSWORD);

            return response;
        }
        /// <summary>
        /// Get server players
        /// </summary>
        /// <returns>An asynchronous task that completes with the collection of SampServerPlayerData instances</returns>
        /// <exception cref="System.Net.Sockets.SocketException">Thrown when operation timed out</exception>
        public async Task<IEnumerable<SampServerPlayerData>> GetServerPlayersAsync()
        {
            byte[] data = await SendSocketToServerAsync(ServerPacketTypes.Players);
            return CollectServerPlayersInfoFromByteArray(data);
        }
        /// <summary>
        /// Get server players
        /// </summary>
        /// <returns>Collection of SampServerPlayerData instances</returns>
        /// <exception cref="System.Net.Sockets.SocketException">Thrown when operation timed out</exception>
        public IEnumerable<SampServerPlayerData> GetServerPlayers()
        {
            byte[] data = SendSocketToServer(ServerPacketTypes.Players);
            return CollectServerPlayersInfoFromByteArray(data);
        }
        /// <summary>
        /// Get information about server
        /// </summary>
        /// <returns>An asynchronous task that completes with an instance of SampServerPlayerData</returns>
        /// <exception cref="System.Net.Sockets.SocketException">Thrown when operation timed out</exception>
        public async Task<SampServerInfoData> GetServerInfoAsync()
        {
            byte[] data = await SendSocketToServerAsync(ServerPacketTypes.Info);
            return CollectServerInfoFromByteArray(data);
        }
        /// <summary>
        /// Get information about server
        /// </summary>
        /// <returns>An instance of SampServerPlayerData</returns>
        /// <exception cref="System.Net.Sockets.SocketException">Thrown when operation timed out</exception>
        public SampServerInfoData GetServerInfo()
        {
            byte[] data = SendSocketToServer(ServerPacketTypes.Info);
            return CollectServerInfoFromByteArray(data);
        }
        /// <summary>
        /// Get server rules
        /// </summary>
        /// <returns>An asynchronous task that completes with an instance of SampServerRulesData</returns>
        /// <exception cref="System.Net.Sockets.SocketException">Thrown when operation timed out</exception>
        public async Task<SampServerRulesData> GetServerRulesAsync()
        {
            byte[] data = await SendSocketToServerAsync(ServerPacketTypes.Rules);
            return CollectServerRulesFromByteArray(data);
        }
        /// <summary>
        /// Get server rules
        /// </summary>
        /// <returns>An instance of SampServerRulesData</returns>
        /// <exception cref="System.Net.Sockets.SocketException">Thrown when operation timed out</exception>
        public SampServerRulesData GetServerRules()
        {
            byte[] data = SendSocketToServer(ServerPacketTypes.Rules);
            return CollectServerRulesFromByteArray(data);
        }
        private string CollectRconAnswerFromByteArray(byte[] data)
        {
            string result = string.Empty;

            using (MemoryStream stream = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(stream, Encoding.GetEncoding(1251)))
                {
                    reader.ReadBytes(11);
                    short len;

                    while ((len = reader.ReadInt16()) != 0)
                        result += new string(reader.ReadChars(len)) + "\n";

                    return result;
                }
            }
        }
        private IEnumerable<SampServerPlayerData> CollectServerPlayersInfoFromByteArray(byte[] data) {
            List<SampServerPlayerData> returnData = new List<SampServerPlayerData>();

            using(var stream = new MemoryStream(data))
            {
                using(BinaryReader read = new BinaryReader(stream))
                {
                    read.ReadBytes(10);
                    read.ReadChar();

                    for (int i = 0, iTotalPlayers = read.ReadInt16(); i < iTotalPlayers; i++)
                    {
                        returnData.Add(new SampServerPlayerData
                        {
                            PlayerId = Convert.ToByte(read.ReadByte()),
                            PlayerName = new string(read.ReadChars(read.ReadByte())),
                            PlayerScore = read.ReadInt32(),
                            PlayerPing = read.ReadInt32()
                        });
                    }
                }
            }

            return returnData;
        }
        private SampServerInfoData CollectServerInfoFromByteArray(byte[] data) {
            using (var stream = new MemoryStream(data))
            {
                using (BinaryReader read = new BinaryReader(stream, Encoding.GetEncoding(1251)))
                {
                    read.ReadBytes(10);
                    read.ReadChar();

                    return new SampServerInfoData
                    {
                        Password = Convert.ToBoolean(read.ReadByte()),
                        Players = read.ReadUInt16(),
                        MaxPlayers = read.ReadUInt16(),

                        HostName = new string(read.ReadChars(read.ReadInt32())),
                        GameMode = new string(read.ReadChars(read.ReadInt32())),
                        Language = new string(read.ReadChars(read.ReadInt32())),

                        ServerPing = DateTime.Now.Subtract(this.transmitMS).Milliseconds,
                    };
                }
            }
        }
        private SampServerRulesData CollectServerRulesFromByteArray(byte[] data) {
            var sampServerRulesData = new SampServerRulesData();

            using (var stream = new MemoryStream(data))
            {
                using (BinaryReader read = new BinaryReader(stream, Encoding.GetEncoding(1251)))
                {
                    read.ReadBytes(10);
                    read.ReadChar();

                    string value;
                    object val;

                    for (int i = 0, iRules = read.ReadInt16(); i < iRules; i++)
                    {
                        PropertyInfo property = sampServerRulesData.GetType().GetProperty(new string(read.ReadChars(read.ReadByte())).Replace(' ', '_'), BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                        value = new string(read.ReadChars(read.ReadByte()));

                        if (property != null)
                        {
                            if (property.PropertyType == typeof(bool)) val = value == "On";
                            else if (property.PropertyType == typeof(Uri)) val = new Uri("http://" + value, UriKind.Absolute);
                            else if (property.PropertyType == typeof(DateTime)) val = DateTime.Today.Add(TimeSpan.Parse(value));
                            else val = Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture);

                            property.SetValue(sampServerRulesData, val);
                        }
                    }
                    return sampServerRulesData;
                }
            }
        }
    }
}