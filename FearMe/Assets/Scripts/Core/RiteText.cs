namespace FearMe.Core
{
    // The words of the banishing rite, one verse per page. Shown on screen
    // when a page is found and while the rite is read, so the players can say
    // them out loud - which, with voice chat on, the demon can hear as well.
    //
    // Stands in for real page artwork until there is some.
    public static class RiteText
    {
        public const string Title = "THE RITE OF BANISHMENT";

        private static readonly string[] Verses =
        {
            "Exorcizo te, immunde spiritus.\nBy the light you cannot bear, be gone from these halls.",
            "What hid in the dark is named, and what is named is bound.\nBy this name I bind you. Be bound.",
            "By the three pages and the voice that reads them, I cast you out.\nLeave this place, and do not return."
        };

        public static int Count => Verses.Length;

        // The verse on the nth page found (0-based).
        public static string Verse(int index)
        {
            if (index < 0) index = 0;
            return Verses[index % Verses.Length];
        }

        // The whole reading for however many pages are held, one line each.
        public static string[] Lines(int pages)
        {
            int count = System.Math.Max(1, System.Math.Min(pages, Verses.Length));
            System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < count; i++) lines.AddRange(Verses[i].Split('\n'));
            return lines.ToArray();
        }
    }
}
