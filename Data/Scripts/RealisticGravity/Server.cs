using Sandbox.Game;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using ProtoBuf;
using VRage.Game.ModAPI.Ingame;

namespace RealisticGravity
{
    public class Networking
    {
        public readonly ushort PacketId;

        public readonly List<IMyPlayer> TempPlayers = new List<IMyPlayer>();

        public Networking(ushort packetId)
        {
            PacketId = packetId;
        }

        public void Register()
        {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(PacketId, ReceivedPacket);
        }

        public void Unregister()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(PacketId, ReceivedPacket);
        }

        private void ReceivedPacket(ushort id, byte[] rawData, ulong recipient, bool reliable)
        {
            try
            {
                if (MyAPIGateway.Session.IsServer)
                {
                    SendGameState();
                }
                else
                {
                    if (rawData.Length <= 2)
                        return; // invalid packet

                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketGameState>(rawData);
                    packet.Received();
                }
            }
            catch (Exception e)
            {

            }
        }

        public void RequestGameState()
        {
            MyAPIGateway.Multiplayer.SendMessageToServer(PacketId, new byte[] { 0 });
        }

        public void SendToPlayers(PacketGameState packet)
        {
            TempPlayers.Clear();
            MyAPIGateway.Players.GetPlayers(TempPlayers);

            var bytes = MyAPIGateway.Utilities.SerializeToBinary(packet);

            foreach (var player in TempPlayers)
            {
                if (player.SteamUserId == MyAPIGateway.Multiplayer.ServerId)
                    continue;

                MyAPIGateway.Multiplayer.SendMessageTo(PacketId, bytes, player.SteamUserId);
            }
        }

        public void SendGameState()
        {
            SendToPlayers(new PacketGameState(ref RealisticGravityCore.Instance.gridDataTableClient));
        }
    }

    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketGameState
    {
        [ProtoMember(1)]
        public List<long> gridDataTableClientKeys;
        [ProtoMember(2)]
        public List<GridGravityDataClient> gridDataTableClientValues;

        public PacketGameState() { }

        public PacketGameState(ref Dictionary<long, GridGravityDataClient> gridDataTableClient)
        {
            this.gridDataTableClientKeys = new List<long>(gridDataTableClient.Keys);
            this.gridDataTableClientValues = new List<GridGravityDataClient>(gridDataTableClient.Values);
        }

        public void Received()
        {
            RestoreGridInfo();
        }

        private void RestoreGridInfo()
        {
            var toRemoveWhitelist = new HashSet<long>(RealisticGravityCore.Instance.gridDataTableClient.Keys);
            //MyVisualScriptLogicProvider.ShowNotification($"GRIDINFO: {gridDataTableClientKeys.Count} :  {gridDataTableClientValues.Count}", 3000);

            for (int i = 0; i < gridDataTableClientKeys.Count; ++i)
            {
                if (RealisticGravityCore.Instance.gridDataTableClient.ContainsKey(gridDataTableClientKeys[i]))
                {
                    var data = gridDataTableClientValues[i];
                    RealisticGravityCore.Instance.gridDataTableClient[gridDataTableClientKeys[i]].CopyData(ref data);
                    toRemoveWhitelist.Remove(gridDataTableClientKeys[i]);
                }
                else
                {
                    RealisticGravityCore.Instance.gridDataTableClient.Add(gridDataTableClientKeys[i], gridDataTableClientValues[i]);
                }
            }

            foreach (var remove in toRemoveWhitelist)
            {
                RealisticGravityCore.Instance.gridDataTableClient[remove].ClearGPS();
                RealisticGravityCore.Instance.gridDataTableClient.Remove(remove);
            }
        }
    }
}
