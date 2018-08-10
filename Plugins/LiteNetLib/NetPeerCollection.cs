﻿using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace LiteNetLib
{
    internal sealed class IPEndPointComparer : IEqualityComparer<IPEndPoint>
    {
        public bool Equals(IPEndPoint x, IPEndPoint y)
        {
            return x.Equals(y);
        }

        public int GetHashCode(IPEndPoint obj)
        {
            return obj.GetHashCode();
        }
    }

    internal sealed class NetPeerCollection
    {
        private readonly Dictionary<IPEndPoint, NetPeer> _peersDict;
        private readonly ReaderWriterLockSlim _lock;
        public int Count;
        public volatile NetPeer HeadPeer;

        public NetPeerCollection()
        {
            _peersDict = new Dictionary<IPEndPoint, NetPeer>(new IPEndPointComparer());
            _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        }

        public bool TryGetValue(IPEndPoint endPoint, out NetPeer peer)
        {
            _lock.EnterReadLock();
            bool result = _peersDict.TryGetValue(endPoint, out peer);
            _lock.ExitReadLock();
            return result;
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            HeadPeer = null;
            _peersDict.Clear();
            Count = 0;
            _lock.ExitWriteLock();
        }

        public bool TryAdd(NetPeer peer)
        {
            _lock.EnterUpgradeableReadLock();
            if (_peersDict.ContainsKey(peer.EndPoint))
            {
                _lock.ExitUpgradeableReadLock();
                return false;
            }
            _lock.EnterWriteLock();
   
            peer.NextPeer = HeadPeer;
            if (HeadPeer != null)
            {
                HeadPeer.PrevPeer = peer;
            }
            HeadPeer = peer;
            _peersDict.Add(peer.EndPoint, peer);
            Count++;
            _lock.ExitWriteLock();
            _lock.ExitUpgradeableReadLock();
            return true;
        }

        public void RemovePeers(List<NetPeer> peersList)
        {
            if (peersList.Count == 0)
                return;
            _lock.EnterWriteLock();
            for (int i = 0; i < peersList.Count; i++)
            {
                RemovePeerInternal(peersList[i]);
            }
            _lock.ExitWriteLock();
        }

        public void RemovePeer(NetPeer peer)
        {
            _lock.EnterWriteLock();
            RemovePeerInternal(peer);
            _lock.ExitWriteLock();
        }

        private void RemovePeerInternal(NetPeer peer)
        {
            if (!_peersDict.Remove(peer.EndPoint))
            {
                return;
            }
            if (peer == HeadPeer)
            {
                HeadPeer = peer.NextPeer;
            }
            if (peer.PrevPeer != null)
            {
                peer.PrevPeer.NextPeer = peer.NextPeer;
                peer.PrevPeer = null;
            }
            if (peer.NextPeer != null)
            {
                peer.NextPeer.PrevPeer = peer.PrevPeer;
                peer.NextPeer = null;
            }
            Count--;
        }
    }
}
