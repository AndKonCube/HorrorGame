using System;

namespace FearMe.Net
{
    // What the co-op overlay (F3) shows. The online layer fills it in when a
    // session is running; with none, there is nothing to report.
    public static class CoopDebug
    {
        public static Func<string> Report;
    }
}
