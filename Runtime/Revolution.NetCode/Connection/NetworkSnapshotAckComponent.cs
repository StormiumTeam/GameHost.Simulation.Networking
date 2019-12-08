using Unity.Entities;
using Unity.Mathematics;
using Unity.Networking.Transport.Utilities;

namespace Unity.NetCode
{
    public struct NetworkSnapshotAckComponent : IComponentData
    {
        public void UpdateReceivedByRemote(uint tick)
        {
            if (tick == 0)
            {
                LastReceivedSnapshotByRemote = 0;
            }
            else if (LastReceivedSnapshotByRemote == 0)
            {
                LastReceivedSnapshotByRemote = tick;
            }
            else if (SequenceHelpers.IsNewer(tick, LastReceivedSnapshotByRemote))
            {
                LastReceivedSnapshotByRemote = tick;
            }
        }

        public uint LastReceivedSnapshotByRemote;
        public uint LastReceivedSnapshotByLocal;
        
        public void UpdateRemoteTime(uint remoteTime, uint localTimeMinusRTT, uint localTime)
        {
            if (remoteTime != 0 && SequenceHelpers.IsNewer(remoteTime, LastReceivedRemoteTime))
            {
                LastReceivedRemoteTime = remoteTime;
                LastReceiveTimestamp = localTime;
                if (localTimeMinusRTT == 0)
                    return;
                uint lastReceivedRTT = localTime - localTimeMinusRTT;
                if (EstimatedRTT == 0)
                    EstimatedRTT = lastReceivedRTT;
                else
                    EstimatedRTT = EstimatedRTT * 0.875f + lastReceivedRTT * 0.125f;
                DeviationRTT = DeviationRTT * 0.75f + math.abs(lastReceivedRTT - EstimatedRTT) * 0.25f;
            }
        }

        public uint LastReceivedRemoteTime;
        public uint LastReceiveTimestamp;
        public float EstimatedRTT;
        public float DeviationRTT;
        public int ServerCommandAge;
    }
}
