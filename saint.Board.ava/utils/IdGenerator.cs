using System;
using System.Threading;

namespace saint.Board.ava.utils;

public class IdGenerator
{
    public static string GenerateUuid(bool hyphens = true)
    {
        Guid guid = Guid.NewGuid();
        return hyphens ? 
            guid.ToString("D") :  // with `-`
            guid.ToString("N");   // without
    }
    
    private static int _id = int.MinValue;

    public static int GetUniqueId()
    {
        return Interlocked.Increment(ref _id);
    }
}