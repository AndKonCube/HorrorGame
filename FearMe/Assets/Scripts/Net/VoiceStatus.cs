namespace FearMe.Net
{
    // What the voice chat is doing, for the HUD and the lobby to show. Written
    // by the voice layer when it is installed; left at its defaults (off)
    // when it is not, so nothing that reads it needs Vivox to compile.
    public static class VoiceStatus
    {
        // Connected to the session's voice channel.
        public static bool Active;

        // Transmitting right now - open mic, or push-to-talk held.
        public static bool Transmitting;

        public static bool LocalSpeaking;
        public static bool PartnerSpeaking;
        public static bool PushToTalk;

        // Why voice is not working, in plain words; empty when it is fine.
        public static string Problem = string.Empty;

        // Bumped on every change, so a screen can redraw only when needed.
        public static int Version;
    }
}
