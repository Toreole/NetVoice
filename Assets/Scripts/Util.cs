using System;

public static class Util
{


    public static ulong GetTimeMillis()
    {
        return (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();
    }

}