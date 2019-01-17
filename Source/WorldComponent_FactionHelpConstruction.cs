using System ;
using System.Collections.Generic ;
using RimWorld ;
using RimWorld.Planet ;
using Verse ;
using UnityEngine;

namespace RoadsOfTheRim
{
    public class WorldComponent_FactionRoadConstructionHelp : WorldComponent
    {
        public const int helpCooldownTicks = 5 * GenDate.TicksPerDay; // A faction can only help on a construction site 5 days after it's finished helping on another one

        public const float helpRequestFailChance = 0.1f ;

        public const float helpBaseAmount = 600f ;

        public const float helpPerTickMedian = 25f;

        public const float helpPerTickVariance = 10f;

        public const float helpPerTickMin = 5f;

        private Dictionary<Faction, int> canHelpAgainAtTick ;

        private Dictionary<Faction, bool> currentlyHelping ;

        public WorldComponent_FactionRoadConstructionHelp(World world) : base(world)
        {
            canHelpAgainAtTick = new Dictionary<Faction, int>();
            currentlyHelping = new Dictionary<Faction, bool>();
        }

        // those lists are used for ExposeData() to load & save correctly
        private List<Faction> factionList_canHelpAgainAtTick = new List<Faction>() ;
        private List<Faction> factionList_currentlyHelping = new List<Faction>();
        private List<int> intList_canHelpAgainAtTick = new List<int>();
        private List<bool> boolList_currentlyHelping = new List<bool>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look<Faction, int>(ref canHelpAgainAtTick, "RotR_canHelpAgainAtTick", LookMode.Reference, LookMode.Value , ref factionList_canHelpAgainAtTick , ref intList_canHelpAgainAtTick) ;
            Scribe_Collections.Look<Faction, bool>(ref currentlyHelping, "RotR_currentlyHelping" , LookMode.Reference , LookMode.Value , ref factionList_currentlyHelping , ref boolList_currentlyHelping) ;
        }

        public void setHelpAgainTick(Faction faction, int tick)
        {
            canHelpAgainAtTick[faction] = tick;
        }

        public int? getHelpAgainTick(Faction faction)
        {
            int result;
            if (canHelpAgainAtTick.TryGetValue(faction, out result))
            {
                return result;
            }
            else
            {
                return null;
            }
        }

        public bool getCurrentlyHelping(Faction faction)
        {
            bool result;
            if (currentlyHelping.TryGetValue(faction, out result))
            {
                return result;
            }
            return false;
        }

        public void setCurrentlyHelping(Faction faction , bool value = true)
        {
            currentlyHelping[faction] = value ;
        }

        public void startHelping(Faction faction, RoadConstructionSite site, Pawn negotiator)
        {
            // Test success or failure of the negotiator, plus amount of help obtained (based on negotiation value & roll)
            float negotiationValue = negotiator.GetStatValue(StatDefOf.NegotiationAbility, true);
            float failChance = helpRequestFailChance / negotiationValue;
            float roll = Rand.Value ;
            float amountOfHelp = helpBaseAmount * ( 1 + negotiationValue * roll * 5);
            Log.Message(String.Format("[RotR] - Negotiation for road construction help : negotiation value = {0:0.00} , fail chance = {1:P} , roll = {2:0.00} , help = {3:0.00}", negotiationValue , failChance, roll , amountOfHelp));

            // Calculate how long the faction needs to start helping
            Settlement closestSettlement = site.closestSettlementOfFaction(faction);
            if (closestSettlement!=null)
            {
                Log.Message(String.Format("[RotR] - The closest settlement is {0}" , closestSettlement.Name));
            }
            else
            {
                Log.Message(String.Format("[RotR] - The closest settlement is NULL"));
            }
            int tick = Find.TickManager.TicksGame + CaravanArrivalTimeEstimator.EstimatedTicksToArrive(closestSettlement.Tile, site.Tile, null);

            // Determine amount of help per tick
            /*
            float amountPerTick = 0;
            for (int i=0; i<50;i++)
            {
                amountPerTick = Math.Max(Rand.Gaussian(helpPerTickMedian , helpPerTickVariance) , helpPerTickMin );
                Log.Message(String.Format("[RotR] - Gaussian : {0:0.00}" , amountPerTick));
            }
            */
            float amountPerTick = Math.Max(Rand.Gaussian(helpPerTickMedian, helpPerTickVariance), helpPerTickMin);

            setCurrentlyHelping(faction);
            site.initiateFactionHelp(faction, tick, amountOfHelp, amountPerTick);

            // trigger an event that will help construction of that site, with a delay, and for a certain amount of time. This can be put in the construction site (tick from where help starts, + amount of help)
        }

        public void helpFinished(Faction faction)
        {
            setCurrentlyHelping(faction , false) ;
            setHelpAgainTick(faction , Find.TickManager.TicksGame + helpCooldownTicks) ;
        }

        public bool inCooldown(Faction faction)
        {
            int? helpAgainTick = getHelpAgainTick(faction);
            if ((helpAgainTick == null) || (Find.TickManager.TicksGame >= getHelpAgainTick(faction)))
            {
                return false;
            }
            return true;
        }

        public float daysBeforeFactionCanHelp(Faction faction)
        {
            int? tick;
            try
            {
                tick = getHelpAgainTick(faction);
                if (tick == null)
                {
                    return 0;
                }
            }
            catch
            {
                return 0;
            }
            return (float)(getHelpAgainTick(faction) - Find.TickManager.TicksGame) / GenDate.TicksPerDay;
        }

    }
}