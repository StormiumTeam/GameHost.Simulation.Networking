using System;
using PATTERN_ID_TYPE = System.Int32;

namespace package.stormiumteam.networking.extensions
{
    /// <summary>
    /// This is the result of registering a pattern.
    /// It's used instead of comparing <see cref="PatternIdent"/> (as it use a string internally).
    /// The value shouldn't be used outside of other worlds.
    /// </summary>
    public struct PatternResult
    {
        // ReSharper disable once BuiltInTypeReferenceStyle
        public PATTERN_ID_TYPE Id;
        public PatternIdent InternalIdent;
        
        public static readonly int HeaderSize = sizeof(int) + sizeof(byte);
        
        public bool Equals(PatternResult other) => Id == other.Id;
        public bool FullEquals(PatternResult other) => InternalIdent == other.InternalIdent;
        
        /// <summary>
        /// Compare two instances of the result id
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both objects got the same identifier</returns>
        public static bool operator ==(PatternResult obj1, PatternResult obj2)
        {
            return obj1.Id == obj2.Id;
        }
        
        /// <summary>
        /// Compare two instances of the result id
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both objects don't have the same identifier</returns>
        public static bool operator !=(PatternResult obj1, PatternResult obj2)
        {
            return obj1.Id != obj2.Id;
        }
    }
    
    /// <summary>
    /// An ident that represent a message pattern.
    /// For faster comparison, use <see cref="PatternResult"/>.
    /// </summary>
    public struct PatternIdent
    {
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is PatternIdent && Equals((PatternIdent) obj);
        }
        
        public string Name;
        public byte   Version;

        public static readonly int HeaderSize = sizeof(int) + sizeof(byte);

        public PatternIdent(string name, byte version = 0)
        {
            Name      = name;
            Version = version;
        }

        public bool Equals(PatternIdent other) => Name == other.Name;

        public override string ToString()
        {
            return $"msg$({Name}, v.{Version})";
        }

        /// <summary>
        /// Compare two instances of the message identifier
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both objects got the same identifier</returns>
        public static bool operator ==(PatternIdent obj1, PatternIdent obj2)
        {
            return obj1.Name == obj2.Name;
        }

        /// <summary>
        /// Compare two instances of the message identifier
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both objects don't have the same identifier</returns>
        public static bool operator !=(PatternIdent obj1, PatternIdent obj2)
        {
            return obj1.Name != obj2.Name;
        }

        /// <summary>
        /// Compare two instances of the message version and check if object 1 is bigger
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both object 1 version is bigger</returns>
        public static bool operator >(PatternIdent obj1, PatternIdent obj2)
        {
            return obj1.Version > obj2.Version;
        }

        /// <summary>
        /// Compare two instances of the message version and check if object 2 is bigger
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both object 2 version is bigger</returns>
        public static bool operator <(PatternIdent obj1, PatternIdent obj2)
        {
            return obj1.Version < obj2.Version;
        }

        /// <summary>
        /// Compare two instances of the message version and check if object 1 is bigger or equal
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both object 1 version is bigger</returns>
        public static bool operator >=(PatternIdent obj1, PatternIdent obj2)
        {
            return obj1.Version >= obj2.Version;
        }

        /// <summary>
        /// Compare two instances of the message version and check if object 2 is bigger or equal
        /// </summary>
        /// <param name="obj1">Object 1</param>
        /// <param name="obj2">Object 2</param>
        /// <returns>Return true if both object 2 version is bigger</returns>
        public static bool operator <=(PatternIdent obj1, PatternIdent obj2)
        {
            return obj1.Version <= obj2.Version;
        }

        public static implicit operator PatternIdent(string ident)
        {
            if (ident.Length < 5)
                throw new Exception();
            var version = byte.Parse(ident.Substring(0, 3));
            var name      = ident.Substring(3, ident.Length - 3);

            return new PatternIdent(name, version);
        }
        
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? Name.GetHashCode() : 0) * 397) ^ Version.GetHashCode();
            }
        }
    }
}