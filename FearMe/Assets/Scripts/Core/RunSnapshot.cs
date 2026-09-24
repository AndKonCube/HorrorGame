using System;

namespace FearMe.Core
{
    // Everything about a run's progress that both players have to agree on,
    // in one small struct. Only the authority (the host, or a solo player)
    // ever changes it; everyone else renders whatever it says. Keys, pages,
    // zones, the demon's banishment and the exit bolts all derive from this.
    [Serializable]
    public struct RunSnapshot : IEquatable<RunSnapshot>
    {
        public const int PageSlots = 3;

        public int keysHeld;       // keys are found in order, so a count is enough
        public int keySpot;        // spawn spot of the key on the map, -1 for none
        public int zonesUnlocked;  // highest open zone; 0 is the first
        public int pagesHeld;      // shared by the team
        public int pageSpot0;      // spawn spot per page slot, -1 when held or unplaced
        public int pageSpot1;
        public int pageSpot2;
        public bool banished;
        public int banishSerial;   // bumps every rite, so a repeat still reads as new
        public float banishSeconds;
        public int demonLevel;     // rises every time it comes back
        public int boltsOpen;

        public static RunSnapshot Initial => new RunSnapshot
        {
            keySpot = -1,
            pageSpot0 = -1,
            pageSpot1 = -1,
            pageSpot2 = -1
        };

        public int PageSpot(int slot)
        {
            switch (slot)
            {
                case 0: return pageSpot0;
                case 1: return pageSpot1;
                default: return pageSpot2;
            }
        }

        public void SetPageSpot(int slot, int spot)
        {
            switch (slot)
            {
                case 0: pageSpot0 = spot; break;
                case 1: pageSpot1 = spot; break;
                default: pageSpot2 = spot; break;
            }
        }

        public int PagesOnMap
        {
            get
            {
                int count = 0;
                for (int i = 0; i < PageSlots; i++)
                    if (PageSpot(i) >= 0) count++;
                return count;
            }
        }

        public bool Equals(RunSnapshot other)
        {
            return keysHeld == other.keysHeld && keySpot == other.keySpot &&
                   zonesUnlocked == other.zonesUnlocked && pagesHeld == other.pagesHeld &&
                   pageSpot0 == other.pageSpot0 && pageSpot1 == other.pageSpot1 && pageSpot2 == other.pageSpot2 &&
                   banished == other.banished && banishSerial == other.banishSerial &&
                   banishSeconds.Equals(other.banishSeconds) && demonLevel == other.demonLevel &&
                   boltsOpen == other.boltsOpen;
        }

        public override bool Equals(object obj) => obj is RunSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = keysHeld;
                hash = hash * 31 + keySpot;
                hash = hash * 31 + zonesUnlocked;
                hash = hash * 31 + pagesHeld;
                hash = hash * 31 + pageSpot0;
                hash = hash * 31 + pageSpot1;
                hash = hash * 31 + pageSpot2;
                hash = hash * 31 + banishSerial;
                hash = hash * 31 + demonLevel;
                hash = hash * 31 + boltsOpen;
                return hash;
            }
        }
    }

    // What a player can ask the authority to do.
    public enum RunRequest
    {
        TakeKey = 1,     // arg: spawn spot
        TakePage = 2,    // arg: page slot
        PerformRite = 3, // arg: unused
        UnlockZone = 4,  // arg: zone
        OpenBolt = 5     // arg: bolt index
    }
}
