// -----------------------------------------------------------------------------
//  NEBULA NINE - wire protocol.
//
//  Compact binary messages over UDP.  The host is authoritative for every game
//  decision; clients send *requests* and render *results*.  A small sequence /
//  ack layer gives reliability to the messages that must not be dropped (roster,
//  phase changes, kills, votes, ejections) while snapshots stay unreliable.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Nebula.Core;

namespace Nebula.Net
{
    public enum NetMsg : byte
    {
        Discover = 1,       // client -> broadcast: "any rooms?"
        Beacon = 2,         // host -> broadcast: room advertisement
        JoinRequest = 3,
        JoinAccept = 4,
        JoinReject = 5,
        Leave = 6,
        Ping = 7,
        Pong = 8,

        Roster = 20,        // full player list + settings (reliable)
        PhaseChange = 21,   // reliable
        Snapshot = 22,      // unreliable, 15 Hz
        InputState = 23,    // client -> host, unreliable

        RequestKill = 40,
        RequestReport = 41,
        RequestEmergency = 42,
        RequestTaskStage = 43,
        RequestVote = 44,
        RequestSabotage = 45,
        RequestVent = 46,
        RequestChat = 47,

        EventKill = 60,     // reliable
        EventMeeting = 61,
        EventVote = 62,
        EventEject = 63,
        EventTask = 64,
        EventSabotage = 65,
        EventSabotageEnd = 66,
        EventChat = 67,
        EventGameOver = 68,
        EventVent = 69,
        EventDoors = 70,

        Ack = 90,
    }

    public class RoomInfo
    {
        public string Code;
        public string HostName;
        public int Players;
        public int Capacity;
        public string Address;
        public int Port;
        public float LastSeen;
        public bool Public = true;
    }

    /// <summary>Small helper around BinaryWriter with the game's primitives.</summary>
    public class NetWriter : IDisposable
    {
        private readonly MemoryStream _stream;
        private readonly BinaryWriter _writer;

        public NetWriter(NetMsg type, uint sequence = 0)
        {
            _stream = new MemoryStream(256);
            _writer = new BinaryWriter(_stream, Encoding.UTF8);
            _writer.Write((byte)type);
            _writer.Write(sequence);
        }

        public NetWriter Byte(byte v) { _writer.Write(v); return this; }
        public NetWriter Bool(bool v) { _writer.Write(v); return this; }
        public NetWriter Int(int v) { _writer.Write(v); return this; }
        public NetWriter UInt(uint v) { _writer.Write(v); return this; }
        public NetWriter Float(float v) { _writer.Write(v); return this; }
        public NetWriter Str(string v) { _writer.Write(v ?? ""); return this; }

        public NetWriter Vec(Vector3 v)
        {
            _writer.Write(v.x); _writer.Write(v.y); _writer.Write(v.z);
            return this;
        }

        public byte[] ToArray()
        {
            _writer.Flush();
            return _stream.ToArray();
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _stream?.Dispose();
        }
    }

    public class NetReader : IDisposable
    {
        private readonly MemoryStream _stream;
        private readonly BinaryReader _reader;

        public NetMsg Type { get; }
        public uint Sequence { get; }
        public bool Valid { get; private set; } = true;

        public NetReader(byte[] data)
        {
            _stream = new MemoryStream(data);
            _reader = new BinaryReader(_stream, Encoding.UTF8);
            try
            {
                Type = (NetMsg)_reader.ReadByte();
                Sequence = _reader.ReadUInt32();
            }
            catch (Exception)
            {
                Valid = false;
            }
        }

        public byte Byte() { try { return _reader.ReadByte(); } catch { Valid = false; return 0; } }
        public bool Bool() { try { return _reader.ReadBoolean(); } catch { Valid = false; return false; } }
        public int Int() { try { return _reader.ReadInt32(); } catch { Valid = false; return 0; } }
        public uint UInt() { try { return _reader.ReadUInt32(); } catch { Valid = false; return 0; } }
        public float Float() { try { return _reader.ReadSingle(); } catch { Valid = false; return 0f; } }
        public string Str() { try { return _reader.ReadString(); } catch { Valid = false; return ""; } }
        public Vector3 Vec() { return new Vector3(Float(), Float(), Float()); }

        public bool HasMore => _stream.Position < _stream.Length;

        public void Dispose()
        {
            _reader?.Dispose();
            _stream?.Dispose();
        }
    }

    public static class RoomCode
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static string Generate()
        {
            var sb = new StringBuilder(6);
            var rnd = new System.Random(Environment.TickCount ^ (int)(Time.realtimeSinceStartup * 1000f));
            for (int i = 0; i < 6; i++) sb.Append(Alphabet[rnd.Next(Alphabet.Length)]);
            return sb.ToString();
        }

        public static bool IsValid(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 6) return false;
            foreach (var c in code.ToUpperInvariant())
                if (Alphabet.IndexOf(c) < 0) return false;
            return true;
        }

        public static string Normalise(string code) => (code ?? "").Trim().ToUpperInvariant();
    }

    /// <summary>Per-connection bookkeeping on the host.</summary>
    public class NetPeer
    {
        public int PlayerId = -1;
        public string Endpoint;         // "ip:port"
        public string Token;            // reconnect token
        public float LastHeard;
        public bool Connected = true;
        public uint LastInputSequence;
        public readonly Dictionary<uint, ReliableEntry> Pending = new Dictionary<uint, ReliableEntry>();
    }

    public class ReliableEntry
    {
        public byte[] Payload;
        public float NextSend;
        public int Attempts;
    }
}
