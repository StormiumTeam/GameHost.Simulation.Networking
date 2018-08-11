using LiteNetLib.Utils;
using package.stormiumteam.networking;
using Scripts.Utilities;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking
{
    public static class NetDataExtension
    {
        public static void PutChannelId(this NetDataWriter dataWriter, NetworkChannelIdent ident)
        {
            dataWriter.Put(ident.Id);
            dataWriter.Put(ident.Port);
        }

        public static NetworkChannelIdent GetChannelId(this NetDataReader dataReader)
        {
            var ident = new NetworkChannelIdent(dataReader.GetString(), dataReader.GetInt());
            return ident;
        }

        public static NetworkChannelIdent GetChannelId(this MessageReader reader)
        {
            return GetChannelId(reader.Data);
        }

        public static void PutUserId(this NetDataWriter dataWriter, NetUser user)
        {
            dataWriter.Put(user.Index);
        }

        public static NetUser GetUserId(this NetDataReader dataReader, NetworkInstance netInstance)
        {
            var ident = new NetUser(netInstance.PeerInstance, netInstance, dataReader.GetULong());
            return ident;
        }

        public static NetUser GetUserId(this MessageReader reader, NetworkInstance netInstance)
        {
            return GetUserId(reader.Data, netInstance);
        }

        public static void Put(this NetDataWriter dataWriter, SeqId ident128)
        {
            dataWriter.Put(ident128.M1);
            dataWriter.Put(ident128.U1);
            dataWriter.Put(ident128.U2);
        }

        public static SeqId GetIdent128(this NetDataReader dataReader)
        {
            var m1 = dataReader.GetULong();
            var u1 = dataReader.GetUInt();
            var u2 = dataReader.GetUInt();

            return new SeqId(m1, u1, u2);
        }
        
        public static SeqId GetIdent128(this MessageReader reader)
        {
            return GetIdent128(reader.Data);
        }

        public static void Put(this NetDataWriter dataWriter, Entity entity)
        {
            dataWriter.Put(entity.Index);
            dataWriter.Put(entity.Version);
        }
        
        public static Entity GetEntity(this NetDataReader dataReader)
        {
            var index = dataReader.GetInt();
            var version = dataReader.GetInt();

            return new Entity()
            {
                Index = index,
                Version = version
            };
        }
        
        public static Entity GetEntity(this MessageReader reader)
        {
            return GetEntity(reader.Data);
        }

        public static void Put(this NetDataWriter dataWriter, Vector2 vec2)
        {
            dataWriter.Put(vec2.x);
            dataWriter.Put(vec2.y);
        }

        public static Vector2 GetVec2(this NetDataReader dataReader)
        {
            return new Vector2(dataReader.GetFloat(), dataReader.GetFloat());
        }
        
        public static void Put(this NetDataWriter dataWriter, Vector3 vec3)
        {
            dataWriter.Put(vec3.x);
            dataWriter.Put(vec3.y);
            dataWriter.Put(vec3.z);
        }
        
        public static Vector3 GetVec3(this NetDataReader dataReader)
        {
            return new Vector3(dataReader.GetFloat(), dataReader.GetFloat(), dataReader.GetFloat());
        }
    }
}