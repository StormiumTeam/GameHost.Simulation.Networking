using System;

namespace package.stormiumteam.networking
{
    public sealed class PatternAttribute : Attribute
    {
        public string Name;
        public byte   Version;

        public PatternAttribute(string name, byte version)
        {
            Name    = name;
            Version = version;
        }
    }
    
    public sealed class PatternNameAttribute : Attribute
    {
        public string Value;

        public PatternNameAttribute(string value)
        {
            Value = value;
        }
    }

    public sealed class PatternVersionAttribute : Attribute
    {
        public byte Value;

        public PatternVersionAttribute(byte value)
        {
            Value = value;
        }
    }
}