// -----------------------------------------------------------------------------
//  NEBULA NINE - UDP transport, room discovery and reliability layer.
//
//  Runs entirely on plain sockets, so the project has no external networking
//  package to resolve and works on LAN / hotspot / direct IP out of the box.
//  Receive happens on a background thread; everything is dispatched back onto the
//  Unity main thread in Update().
// -----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Nebula.Core;

namespace Nebula.Net
{
    public class NetworkService : MonoBehaviour
    {
        public static NetworkService Instance { get; private set; }

        public const int DiscoveryPort = 47820;
        public const int BaseGamePort = 47800;
        public const float PeerTimeout = 12f;

        public NetRole Role { get; private set; } = NetRole.Offline;
        public string RoomCodeValue { get; private set; } = "";
        public LobbyVisibility Visibility { get; set; } = LobbyVisibility.Public;
        public int Capacity { get; private set; } = 15;
        public string LocalToken { get; private set; } = "";
        public bool Connected { get; private set; }
        public string LastError { get; private set; } = "";

        public readonly List<RoomInfo> DiscoveredRooms = new List<RoomInfo>();
        public readonly Dictionary<string, NetPeer> Peers = new Dictionary<string, NetPeer>();

        /// <summary>Raised on the main thread for every well formed message.</summary>
        public event Action<NetReader, string> OnMessage;
        public event Action<NetPeer> OnPeerJoined;
        public event Action<NetPeer> OnPeerLost;
        public event Action OnConnectedToHost;
        public event Action<string> OnRejected;

        private UdpClient _gameSocket;
        private UdpClient _discoverySocket;
        private Thread _gameThread, _discoveryThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<Packet> _inbox = new ConcurrentQueue<Packet>();

        private IPEndPoint _hostEndpoint;
        private int _gamePort = BaseGamePort;
        private uint _sequence = 1;
        private float _discoverTimer;
        private float _resendTimer;
        private float _pingTimer;
        private readonly Dictionary<string, HashSet<uint>> _seen = new Dictionary<string, HashSet<uint>>();
        private readonly Dictionary<uint, ReliableEntry> _outgoing = new Dictionary<uint, ReliableEntry>();

        private struct Packet
        {
            public byte[] Data;
            public IPEndPoint From;
            public bool Discovery;
        }

        // ==================================================================
        private void Awake()
        {
            Instance = this;
            LocalToken = PlayerPrefs.GetString("nebula.token", "");
            if (string.IsNullOrEmpty(LocalToken))
            {
                LocalToken = Guid.NewGuid().ToString("N").Substring(0, 12);
                PlayerPrefs.SetString("nebula.token", LocalToken);
            }
        }

        private void OnDestroy() => Shutdown();

        // ==================================================================
        //  lifecycle
        // ==================================================================
        public bool StartHost(int capacity, LobbyVisibility visibility)
        {
            Shutdown();
            Capacity = Mathf.Clamp(capacity, 4, 15);
            Visibility = visibility;
            RoomCodeValue = RoomCode.Generate();

            if (!OpenGameSocket(true)) return false;
            OpenDiscoverySocket();

            Role = NetRole.Host;
            Connected = true;
            LastError = "";
            Debug.Log($"[Nebula] Host started. Room {RoomCodeValue} on port {_gamePort}.");
            return true;
        }

        public bool StartClient(string code)
        {
            code = RoomCode.Normalise(code);
            Shutdown();
            if (!OpenGameSocket(false)) return false;

            Role = NetRole.Client;
            RoomCodeValue = code;
            Connected = false;
            LastError = "";
            _discoverTimer = 0f;
            StartDiscovery();
            return true;
        }

        /// <summary>Passive discovery (used by the menu to list rooms).</summary>
        public void StartDiscovery()
        {
            if (_gameSocket == null) OpenGameSocket(false);
            _discoverTimer = 0f;
        }

        public void ConnectTo(string address, int port)
        {
            try
            {
                _hostEndpoint = new IPEndPoint(IPAddress.Parse(address), port);
                SendJoinRequest();
            }
            catch (Exception e)
            {
                LastError = e.Message;
            }
        }

        public void Shutdown()
        {
            _running = false;
            try { _gameSocket?.Close(); } catch (Exception) { }
            try { _discoverySocket?.Close(); } catch (Exception) { }
            _gameSocket = null;
            _discoverySocket = null;
            if (_gameThread != null && _gameThread.IsAlive) _gameThread.Join(80);
            if (_discoveryThread != null && _discoveryThread.IsAlive) _discoveryThread.Join(80);
            _gameThread = null;
            _discoveryThread = null;

            Peers.Clear();
            _outgoing.Clear();
            _seen.Clear();
            while (_inbox.TryDequeue(out _)) { }
            Role = NetRole.Offline;
            Connected = false;
            _hostEndpoint = null;
        }

        private bool OpenGameSocket(bool fixedPort)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    int port = fixedPort ? BaseGamePort + attempt : 0;
                    _gameSocket = new UdpClient(AddressFamily.InterNetwork);
                    _gameSocket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _gameSocket.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                    _gameSocket.EnableBroadcast = true;
                    _gamePort = ((IPEndPoint)_gameSocket.Client.LocalEndPoint).Port;

                    _running = true;
                    _gameThread = new Thread(() => ReceiveLoop(_gameSocket, false)) { IsBackground = true };
                    _gameThread.Start();
                    return true;
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    try { _gameSocket?.Close(); } catch (Exception) { }
                    _gameSocket = null;
                    if (!fixedPort) break;
                }
            }
            Debug.LogWarning("[Nebula] Could not open a UDP socket: " + LastError);
            return false;
        }

        private void OpenDiscoverySocket()
        {
            try
            {
                _discoverySocket = new UdpClient(AddressFamily.InterNetwork);
                _discoverySocket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _discoverySocket.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _discoverySocket.EnableBroadcast = true;
                _discoveryThread = new Thread(() => ReceiveLoop(_discoverySocket, true)) { IsBackground = true };
                _discoveryThread.Start();
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogWarning("[Nebula] Discovery socket unavailable: " + e.Message);
            }
        }

        private void ReceiveLoop(UdpClient socket, bool discovery)
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running && socket != null)
            {
                try
                {
                    var data = socket.Receive(ref any);
                    if (data != null && data.Length > 0)
                        _inbox.Enqueue(new Packet { Data = data, From = new IPEndPoint(any.Address, any.Port), Discovery = discovery });
                }
                catch (SocketException)
                {
                    if (!_running) return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception)
                {
                    // keep the thread alive on transient errors
                }
            }
        }

        // ==================================================================
        //  main thread pump
        // ==================================================================
        private void Update()
        {
            if (Role == NetRole.Offline && _gameSocket == null) return;

            while (_inbox.TryDequeue(out var packet)) Handle(packet);

            float dt = Time.unscaledDeltaTime;

            if (Role == NetRole.Client || Role == NetRole.Offline)
            {
                _discoverTimer -= dt;
                if (_discoverTimer <= 0f)
                {
                    _discoverTimer = 2f;
                    BroadcastDiscover();
                    PruneRooms();
                }
            }

            if (Role == NetRole.Host)
            {
                foreach (var kv in new List<KeyValuePair<string, NetPeer>>(Peers))
                {
                    if (Time.unscaledTime - kv.Value.LastHeard > PeerTimeout && kv.Value.Connected)
                    {
                        kv.Value.Connected = false;
                        OnPeerLost?.Invoke(kv.Value);
                    }
                }
            }

            _pingTimer -= dt;
            if (_pingTimer <= 0f)
            {
                _pingTimer = 2f;
                if (Role == NetRole.Client && _hostEndpoint != null)
                    RawSend(new NetWriter(NetMsg.Ping).Str(LocalToken).ToArray(), _hostEndpoint);
            }

            _resendTimer -= dt;
            if (_resendTimer <= 0f)
            {
                _resendTimer = 0.22f;
                ResendReliable();
            }
        }

        private void PruneRooms()
        {
            for (int i = DiscoveredRooms.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - DiscoveredRooms[i].LastSeen > 6f) DiscoveredRooms.RemoveAt(i);
        }

        private void BroadcastDiscover()
        {
            if (_gameSocket == null) return;
            try
            {
                var data = new NetWriter(NetMsg.Discover).Str(RoomCodeValue ?? "").ToArray();
                _gameSocket.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            }
            catch (Exception) { }
        }

        // ==================================================================
        //  message handling
        // ==================================================================
        private void Handle(Packet packet)
        {
            using (var reader = new NetReader(packet.Data))
            {
                if (!reader.Valid) return;
                string endpoint = packet.From.ToString();

                switch (reader.Type)
                {
                    case NetMsg.Discover:
                        if (Role == NetRole.Host && Visibility == LobbyVisibility.Public) SendBeacon(packet.From);
                        else if (Role == NetRole.Host)
                        {
                            string wanted = reader.Str();
                            if (RoomCode.Normalise(wanted) == RoomCodeValue) SendBeacon(packet.From);
                        }
                        return;

                    case NetMsg.Beacon:
                    {
                        var info = new RoomInfo
                        {
                            Code = reader.Str(),
                            HostName = reader.Str(),
                            Players = reader.Int(),
                            Capacity = reader.Int(),
                            Port = reader.Int(),
                            Address = packet.From.Address.ToString(),
                            LastSeen = Time.unscaledTime,
                        };
                        RegisterRoom(info);
                        if (Role == NetRole.Client && !Connected &&
                            (string.IsNullOrEmpty(RoomCodeValue) || info.Code == RoomCodeValue))
                        {
                            ConnectTo(info.Address, info.Port);
                        }
                        return;
                    }

                    case NetMsg.Ack:
                    {
                        uint acked = reader.UInt();
                        _outgoing.Remove(acked);
                        return;
                    }

                    case NetMsg.Ping:
                        if (Role == NetRole.Host)
                        {
                            TouchPeer(endpoint);
                            RawSend(new NetWriter(NetMsg.Pong).ToArray(), packet.From);
                        }
                        return;

                    case NetMsg.Pong:
                        Connected = true;
                        return;

                    case NetMsg.JoinRequest:
                        if (Role == NetRole.Host) HandleJoinRequest(reader, packet.From);
                        return;

                    case NetMsg.JoinAccept:
                        Connected = true;
                        _hostEndpoint = packet.From;
                        OnConnectedToHost?.Invoke();
                        break;   // also forwarded to listeners below

                    case NetMsg.JoinReject:
                        OnRejected?.Invoke(reader.Str());
                        return;
                }

                // reliability: ack + dedupe
                if (reader.Sequence != 0)
                {
                    RawSend(new NetWriter(NetMsg.Ack).UInt(reader.Sequence).ToArray(), packet.From);
                    if (!_seen.TryGetValue(endpoint, out var set))
                    {
                        set = new HashSet<uint>();
                        _seen[endpoint] = set;
                    }
                    if (!set.Add(reader.Sequence)) return;
                    if (set.Count > 512) set.Clear();
                }

                if (Role == NetRole.Host) TouchPeer(endpoint);
                OnMessage?.Invoke(reader, endpoint);
            }
        }

        private void RegisterRoom(RoomInfo info)
        {
            foreach (var room in DiscoveredRooms)
            {
                if (room.Code == info.Code)
                {
                    room.Players = info.Players;
                    room.LastSeen = info.LastSeen;
                    room.Address = info.Address;
                    room.Port = info.Port;
                    return;
                }
            }
            DiscoveredRooms.Add(info);
        }

        private void SendBeacon(IPEndPoint to)
        {
            int players = 1;
            foreach (var kv in Peers) if (kv.Value.Connected) players++;
            var data = new NetWriter(NetMsg.Beacon)
                .Str(RoomCodeValue)
                .Str(GameSettings.Profile.DisplayName)
                .Int(players)
                .Int(Capacity)
                .Int(_gamePort)
                .ToArray();
            RawSend(data, to);
        }

        private void SendJoinRequest()
        {
            if (_hostEndpoint == null) return;
            var profile = GameSettings.Profile;
            var data = new NetWriter(NetMsg.JoinRequest, NextSequence())
                .Str(LocalToken)
                .Str(profile.DisplayName)
                .Int(profile.ColorIndex)
                .Int(profile.HatIndex)
                .Int(profile.OutfitIndex)
                .Int(profile.AccessoryIndex)
                .Int(profile.TrailIndex)
                .Str(RoomCodeValue)
                .ToArray();
            SendReliableRaw(data, _hostEndpoint);
        }

        private void HandleJoinRequest(NetReader reader, IPEndPoint from)
        {
            string token = reader.Str();
            string name = reader.Str();
            reader.Int(); reader.Int(); reader.Int(); reader.Int(); reader.Int();
            string code = RoomCode.Normalise(reader.Str());

            if (!string.IsNullOrEmpty(code) && code != RoomCodeValue)
            {
                RawSend(new NetWriter(NetMsg.JoinReject).Str("Неверный код комнаты").ToArray(), from);
                return;
            }

            string endpoint = from.ToString();
            NetPeer peer = null;

            // reconnect: same token gets the same seat back
            foreach (var kv in Peers)
            {
                if (kv.Value.Token == token) { peer = kv.Value; break; }
            }

            if (peer == null)
            {
                int connected = 1;
                foreach (var kv in Peers) if (kv.Value.Connected) connected++;
                if (connected >= Capacity)
                {
                    RawSend(new NetWriter(NetMsg.JoinReject).Str("Комната заполнена").ToArray(), from);
                    return;
                }
                peer = new NetPeer { Token = token, Endpoint = endpoint };
            }
            else
            {
                Peers.Remove(peer.Endpoint);
                peer.Endpoint = endpoint;
            }

            peer.Connected = true;
            peer.LastHeard = Time.unscaledTime;
            Peers[endpoint] = peer;

            RawSend(new NetWriter(NetMsg.JoinAccept).Str(RoomCodeValue).Int(peer.PlayerId).ToArray(), from);
            OnPeerJoined?.Invoke(peer);
        }

        private void TouchPeer(string endpoint)
        {
            if (Peers.TryGetValue(endpoint, out var peer))
            {
                peer.LastHeard = Time.unscaledTime;
                if (!peer.Connected)
                {
                    peer.Connected = true;
                    OnPeerJoined?.Invoke(peer);
                }
            }
        }

        // ==================================================================
        //  sending
        // ==================================================================
        public uint NextSequence() => _sequence++;

        private void RawSend(byte[] data, IPEndPoint to)
        {
            if (_gameSocket == null || to == null) return;
            try { _gameSocket.Send(data, data.Length, to); }
            catch (Exception) { }
        }

        private void SendReliableRaw(byte[] data, IPEndPoint to)
        {
            RawSend(data, to);
            using (var probe = new NetReader(data))
            {
                if (probe.Sequence != 0)
                {
                    _outgoing[probe.Sequence] = new ReliableEntry
                    {
                        Payload = data,
                        NextSend = Time.unscaledTime + 0.25f,
                        Attempts = 0,
                    };
                    _reliableTargets[probe.Sequence] = to;
                }
            }
        }

        private readonly Dictionary<uint, IPEndPoint> _reliableTargets = new Dictionary<uint, IPEndPoint>();

        private void ResendReliable()
        {
            if (_outgoing.Count == 0) return;
            var expired = new List<uint>();
            foreach (var kv in _outgoing)
            {
                if (Time.unscaledTime < kv.Value.NextSend) continue;
                kv.Value.Attempts++;
                if (kv.Value.Attempts > 8) { expired.Add(kv.Key); continue; }
                kv.Value.NextSend = Time.unscaledTime + 0.25f * kv.Value.Attempts;
                if (_reliableTargets.TryGetValue(kv.Key, out var target)) RawSend(kv.Value.Payload, target);
            }
            foreach (var key in expired)
            {
                _outgoing.Remove(key);
                _reliableTargets.Remove(key);
            }
        }

        public void SendToHost(NetWriter writer, bool reliable = false)
        {
            if (_hostEndpoint == null) { writer.Dispose(); return; }
            var data = writer.ToArray();
            writer.Dispose();
            if (reliable) SendReliableRaw(data, _hostEndpoint);
            else RawSend(data, _hostEndpoint);
        }

        public void SendToAll(NetWriter writer, bool reliable = false)
        {
            var data = writer.ToArray();
            writer.Dispose();
            foreach (var kv in Peers)
            {
                if (!kv.Value.Connected) continue;
                var target = Parse(kv.Key);
                if (target == null) continue;
                if (reliable) SendReliableRaw(data, target);
                else RawSend(data, target);
            }
        }

        private static IPEndPoint Parse(string endpoint)
        {
            try
            {
                int idx = endpoint.LastIndexOf(':');
                if (idx <= 0) return null;
                var ip = IPAddress.Parse(endpoint.Substring(0, idx));
                int port = int.Parse(endpoint.Substring(idx + 1));
                return new IPEndPoint(ip, port);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public int ConnectedPeerCount()
        {
            int n = 0;
            foreach (var kv in Peers) if (kv.Value.Connected) n++;
            return n;
        }

        public NetPeer PeerOf(string endpoint) => Peers.TryGetValue(endpoint, out var p) ? p : null;
    }
}
