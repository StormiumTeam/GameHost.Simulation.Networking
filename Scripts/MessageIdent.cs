using System;

namespace package.stormiumteam.networking
{
    public struct MessageIdent
    {
        public string Id;
        public byte   Version;

        public static readonly MessageIdent Zero = new MessageIdent("zero_id", Byte.MaxValue);

        public MessageIdent(string id, byte version = 0)
        {
            Id = id;
            Version = version;
        }

        public bool Equals(MessageIdent other) => Id == other.Id;

        public override string ToString()
        {
            return $"msg$({Id}, v.{Version})";
        }

        /// <summary>
        /// Compare two instances of the message identifier
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both objects got the same identifier</returns>
        public static bool operator ==(MessageIdent obj1, MessageIdent obj2)
        {
            return obj1.Id == obj2.Id;
        }
        
        /// <summary>
        /// Compare two instances of the message identifier
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both objects don't have the same identifier</returns>
        public static bool operator !=(MessageIdent obj1, MessageIdent obj2)
        {
            return obj1.Id != obj2.Id;
        }
        
        /// <summary>
        /// Compare two instances of the message version and check if object 1 is bigger
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both object 1 version is bigger</returns>
        public static bool operator >(MessageIdent obj1, MessageIdent obj2)
        {
            return obj1.Version > obj2.Version;
        }
        
        /// <summary>
        /// Compare two instances of the message version and check if object 2 is bigger
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both object 2 version is bigger</returns>
        public static bool operator <(MessageIdent obj1, MessageIdent obj2)
        {
            return obj1.Version < obj2.Version;
        }
        
        /// <summary>
        /// Compare two instances of the message version and check if object 1 is bigger or equal
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both object 1 version is bigger</returns>
        public static bool operator >=(MessageIdent obj1, MessageIdent obj2)
        {
            return obj1.Version >= obj2.Version;
        }
        
        /// <summary>
        /// Compare two instances of the message version and check if object 2 is bigger or equal
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both object 2 version is bigger</returns>
        public static bool operator <=(MessageIdent obj1, MessageIdent obj2)
        {
            return obj1.Version <= obj2.Version;
        }

        public static implicit operator MessageIdent(string ident)
        {
            if (ident.Length < 5)
                throw new Exception();
            var version = byte.Parse(ident.Substring(0, 3));
            var id = ident.Substring(3, ident.Length - 3);
            
            return new MessageIdent(id, version);
        }
    }
}