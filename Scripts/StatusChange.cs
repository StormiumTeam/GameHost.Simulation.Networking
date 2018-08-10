using System;

namespace package.stormiumteam.networking
{
    [Flags]
    public enum StatusChange
    {
        Unknow = 0,
        Added = 1 << 1,
        Removed = 1 << 2,
        MainUser = 1 << 3,
        NewUserAsMain = Added & MainUser
    }
}