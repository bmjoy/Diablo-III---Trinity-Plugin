using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using Trinity.Cache;
using Trinity.Combat.Abilities;
using Trinity.Config.Combat;
using Trinity.Coroutines.Town;
using Trinity.DbProvider;
using Trinity.Framework.Actors;
using Trinity.Framework.Helpers;
using Trinity.Framework.Objects.Memory;
using Trinity.Helpers;
using Trinity.Items;
using Trinity.Reference;
using Trinity.Settings.Loot;
using Trinity.Technicals;
using Zeta.Bot;
using Zeta.Bot.Navigation;
using Zeta.Bot.Profile.Common;
using Zeta.Bot.Settings;
using Zeta.Common;
using Zeta.Game;
using Zeta.Game.Internals.Actors;
using Zeta.Game.Internals.SNO;
using Logger = Trinity.Technicals.Logger;

namespace Trinity
{
    /// <summary>
    /// Prototype Weighting System
    /// </summary>
    public partial class TrinityPlugin
    {
        public partial class Weighting
        {
            #region Shrines

            public enum ShrineTypes
            {
                Unknown,
                //Regular
                Fortune, //Is this still in game?
                Frenzied,
                Reloaded, //Other Run Speed ???
                Enlightened,
                Glow,
                RunSpeed,
                //Goblin
                Hoarder,
                //Pylon
                Shield,
                Speed,
                Casting,
                Damage,
                Conduit
            }

            public static ShrineTypes GetShrineType(TrinityCacheObject cacheObject)
            {
                switch (cacheObject.ActorSNO)
                {
                    case (int) SNOActor.a4_Heaven_Shrine_Global_Fortune:
                    case (int) SNOActor.Shrine_Global_Fortune:
                        return ShrineTypes.Fortune;

                    case (int) SNOActor.a4_Heaven_Shrine_Global_Frenzied:
                    case (int) SNOActor.Shrine_Global_Frenzied:
                        return ShrineTypes.Frenzied;

                    case (int) SNOActor.a4_Heaven_Shrine_Global_Reloaded:
                    case (int) SNOActor.Shrine_Global_Reloaded:
                        return ShrineTypes.RunSpeed;

                    case (int) SNOActor.a4_Heaven_Shrine_Global_Enlightened:
                    case (int) SNOActor.Shrine_Global_Enlightened:
                        return ShrineTypes.Enlightened;

                    case (int) SNOActor.Shrine_Global_Glow:
                        return ShrineTypes.Glow;

                    case (int) SNOActor.a4_Heaven_Shrine_Global_Hoarder:
                    case (int) SNOActor.Shrine_Global_Hoarder:
                        return ShrineTypes.Hoarder;

                    case (int) SNOActor.x1_LR_Shrine_Infinite_Casting:
                        return ShrineTypes.Casting;

                    case (int) SNOActor.x1_LR_Shrine_Electrified_TieredRift:
                    case (int) SNOActor.x1_LR_Shrine_Electrified:
                        return ShrineTypes.Conduit;

                    case (int) SNOActor.x1_LR_Shrine_Invulnerable:
                        return ShrineTypes.Shield;

                    case (int) SNOActor.x1_LR_Shrine_Run_Speed:
                        return ShrineTypes.Shield;

                    case (int) SNOActor.x1_LR_Shrine_Damage:
                        return ShrineTypes.Damage;
                    default:
                        return ShrineTypes.Unknown;
                }
            }

            #endregion

            #region Helper Methods

            public const double MaxWeight = 50000d;
            private const double MinWeight = -1d;

            private static double GetLastHadUnitsInSights()
            {
                return Math.Max(DateTime.UtcNow.Subtract(lastHadUnitInSights).TotalMilliseconds,
                    DateTime.UtcNow.Subtract(lastHadEliteUnitInSights).TotalMilliseconds);
            }

            /// <summary>
            /// Gets the settings distances based on elite or not.
            /// </summary>
            /// <param name="cacheObject"></param>
            /// <returns></returns>
            public static float DistanceForObjectType(TrinityCacheObject cacheObject)
            {
                return cacheObject.CommonData.MonsterQualityLevel ==
                       Zeta.Game.Internals.Actors.MonsterQuality.Boss ||
                       cacheObject.CommonData.MonsterQualityLevel ==
                       Zeta.Game.Internals.Actors.MonsterQuality.Unique ||
                       cacheObject.CommonData.MonsterQualityLevel ==
                       Zeta.Game.Internals.Actors.MonsterQuality.Rare ||
                       cacheObject.CommonData.MonsterQualityLevel ==
                       Zeta.Game.Internals.Actors.MonsterQuality.Champion ||
                       cacheObject.CommonData.MonsterQualityLevel ==
                       Zeta.Game.Internals.Actors.MonsterQuality.Minion
                    ? Settings.Combat.Misc.EliteRange
                    : Settings.Combat.Misc.NonEliteRange;
            }

            public static double GoldFormula(TrinityCacheObject cacheObject)
            {
                return cacheObject.GoldAmount*0.05;
            }

            /// <summary>
            /// Gets the base weight for types of Elites/Rares/Champions/Uniques/Bosses
            /// </summary>
            /// <param name="cacheObject"></param>
            /// <returns></returns>
            public static double EliteFormula(TrinityCacheObject cacheObject)
            {
                if (cacheObject.CommonData.MonsterQualityLevel ==
                    Zeta.Game.Internals.Actors.MonsterQuality.Boss)
                    return 1500d;
                if (cacheObject.CommonData.MonsterQualityLevel ==
                    Zeta.Game.Internals.Actors.MonsterQuality.Unique)
                    return 400d;
                if (cacheObject.CommonData.MonsterQualityLevel ==
                    Zeta.Game.Internals.Actors.MonsterQuality.Rare && cacheObject.CommonData.MonsterQualityLevel !=
                    Zeta.Game.Internals.Actors.MonsterQuality.Minion)
                    return 300d;
                if (cacheObject.CommonData.MonsterQualityLevel ==
                    Zeta.Game.Internals.Actors.MonsterQuality.Champion)
                    return 200d;
                //if (cacheObject.CommonData.MonsterQualityLevel ==
                //    Zeta.Game.Internals.Actors.MonsterQuality.Minion)
                //    return 100d;
                return 100d;
            }

            /// <summary>
            /// Gets the weight for objects near AoE
            /// </summary>
            /// <param name="cacheObject"></param>
            /// <returns></returns>
            public static double AoENearFormula(TrinityCacheObject cacheObject)
            {
                double weight = 0;
                if (!Settings.Combat.Misc.KillMonstersInAoE)
                    return weight;
                var avoidances = CacheData.TimeBoundAvoidance.Where(u => u.Position.Distance(cacheObject.Position) < 15);
                foreach (var avoidance in avoidances)
                {
                    weight -= 25*(Math.Max(0, 15 - avoidance.Radius))/15;
                }

                return weight;
            }

            /// <summary>
            /// Gets the Weight of Objects that have AoE in their path from us. 
            /// </summary>
            /// <param name="cacheObject"></param>
            /// <returns></returns>
            public static double AoEInPathFormula(TrinityCacheObject cacheObject)
            {
                double weight = 0;
                if (!Settings.Combat.Misc.KillMonstersInAoE)
                    return weight;
                var avoidances =
                    CacheData.TimeBoundAvoidance.Where(
                        aoe => MathUtil.IntersectsPath(aoe.Position, aoe.Radius, Player.Position,
                            cacheObject.Position));
                foreach (var avoidance in avoidances)
                {
                    weight -= 10*(Math.Max(0, 15 - avoidance.Radius))/15;
                }
                return weight;
            }

            /// <summary>
            /// Gets the weight for objects near Reflective Damage
            /// </summary>
            /// <param name="cacheObject"></param>
            /// <param name="monstersWithReflectActive"></param>
            /// <returns></returns>
            public static double ReflectiveMonsterNearFormula(TrinityCacheObject cacheObject,
                List<TrinityCacheObject> monstersWithReflectActive)
            {
                double weight = 0;
                if (!Settings.Combat.Misc.IgnoreMonstersWhileReflectingDamage)
                    return 0;
                var monsters = monstersWithReflectActive.Where(u => u.Position.Distance(cacheObject.Position) < 10f);
                foreach (var monster in monsters)
                {
                    weight -= 50*(Math.Max(0, 10 - monster.RadiusDistance))/10;
                }
                return weight;
            }

            /// <summary>
            /// Gets the weight for Objects near Elites.
            /// </summary>
            /// <param name="cacheObject"></param>
            /// <param name="eliteMonsters"></param>
            /// <returns></returns>
            public static double EliteMonsterNearFormula(TrinityCacheObject cacheObject,
                List<TrinityCacheObject> eliteMonsters)
            {
                double weight = 0;
                if (!Settings.Combat.Misc.IgnoreElites && !Settings.Combat.Misc.IgnoreChampions &&
                    !Settings.Combat.Misc.IgnoreRares)
                    return 0;
                var monsters = eliteMonsters.Where(u => u.Position.Distance(cacheObject.Position) < 10f);
                foreach (var monster in monsters)
                {
                    weight -= 50*(Math.Max(0, 10 - monster.RadiusDistance))/10;
                }
                return weight;
            }

            /// <summary>
            /// Gets the weight based on distance of object.  The closer the unit the more priority.
            /// </summary>
            /// <param name="cacheObject"></param>
            /// <returns></returns>
            public static double ObjectDistanceFormula(TrinityCacheObject cacheObject)
            {
                var multipler = 500d;

                // DemonHunter is very fragile and should never run past close mobs, so increase distance weighting.
                if (cacheObject.IsUnit && TrinityPlugin.Player.ActorClass == ActorClass.DemonHunter)
                    multipler = 1000d;

                // not units (items etc) shouldnt be impacted by the trash/non-trash slider setting.
                var range = 80f;

                if (cacheObject.Type == TrinityObjectType.Unit)
                {
                    range = cacheObject.IsBossOrEliteRareUnique || cacheObject.IsEliteRareUnique
                                        ? Settings.Combat.Misc.EliteRange
                                        : Settings.Combat.Misc.NonEliteRange;
                }

                return multipler * ((range - cacheObject.RadiusDistance)/range);
            }

            public static double PackDensityFormula(TrinityCacheObject cacheObject)
            {
                // Fix for questing/bounty mode when kill-all is required
                if (CombatBase.CombatOverrides.EffectiveTrashSize == 1)
                    return 0;

                //todo: find out why this formula is being applied to non-unit actors - destructibles, globes etc.

                var pack = TrinityPlugin.ObjectCache.Where(
                        x => x.Position.Distance(cacheObject.Position) < CombatBase.CombatOverrides.EffectiveTrashRadius && (!Settings.Combat.Misc.IgnoreElites || !x.IsEliteRareUnique))
                        .ToList();

                var packDistanceValue = pack.Sum(mob => 100d * ((CombatBase.CombatOverrides.EffectiveTrashRadius - cacheObject.RadiusDistance) / CombatBase.CombatOverrides.EffectiveTrashRadius));

                return packDistanceValue < 0 ? 0 : packDistanceValue;
            }

            public static double RiftValueFormula(TrinityCacheObject cacheObject)
            {
                var result = 0d;

                if (!RiftProgression.IsInRift || !cacheObject.IsUnit)
                    return result;

                // get all other units within cluster radius of this unit.
                var pack = TrinityPlugin.ObjectCache.Where(x => 
                    x.Position.Distance(cacheObject.Position) < CombatBase.CombatOverrides.EffectiveTrashRadius && 
                    (!Settings.Combat.Misc.IgnoreElites || !x.IsEliteRareUnique))
                        .ToList();

                cacheObject.RiftValueInRadius = pack.Sum(mob => mob.RiftValuePct);

                // Only boost weight of this unit if above the total weight setting.
                if (cacheObject.RiftValueInRadius > TrinityPlugin.Settings.Combat.Misc.RiftValueAlwaysKillClusterValue)
                     result = 100d * ((CombatBase.CombatOverrides.EffectiveTrashRadius - cacheObject.RadiusDistance) / CombatBase.CombatOverrides.EffectiveTrashRadius);


                return result <= 0 ? 0 : result;
            }

            /// <summary>
            /// Gets the weight based on the Objects Health Percent.  Lower health gets more priority
            /// </summary>
            /// <param name="cacheObject"></param>
            /// <returns></returns>
            public static double UnitHealthFormula(TrinityCacheObject cacheObject)
            {
                // Sometimes the game returns infinity hitpoints for whatever reason
                if (double.IsInfinity(cacheObject.HitPointsPct))
                    return 1;

                // Fix for near-zero rounding errors health=-0.000441553586736365
                if (Math.Abs(cacheObject.HitPointsPct - 1) < double.Epsilon)
                    return 0;

                return 200d*((1 - cacheObject.HitPointsPct)/100);
            }

            /// <summary>
            /// Gets the weight based on the Objects distance from us and if they are in our path or next hotspot.
            /// </summary>
            /// <param name="cacheObject"></param>
            /// <returns></returns>
            public static double PathBlockedFormula(TrinityCacheObject cacheObject)
            {
                if (cacheObject.ActorSNO == 3349) // Belial, can't be pathed to.
                    return 0;

                if (Player.ActorClass == ActorClass.Monk || Player.ActorClass == ActorClass.Barbarian && Skills.Barbarian.Whirlwind.IsActive)
                    return 0;

                if (BlockingObjects(cacheObject) > 0)
                    return -MaxWeight;
                
                // todo fix this its causing massive bouts of the bot doing nothing while standing in groups of mobs.             
                //if(!cacheObject.IsUnit)
                //    return BlockingMonsterObjects(cacheObject) * -100d;

                if (!cacheObject.IsUnit || PlayerMover.IsBlocked && cacheObject.Distance > 15f && !cacheObject.IsEliteRareUnique && Settings.Combat.Misc.AttackWhenBlocked)
                    return -MaxWeight;

                return 0;
            }

            public static int BlockingMonsterObjects(TrinityCacheObject cacheObject)
            {
                var monsterCount = CacheData.MonsterObstacles.Count(
                    ob => MathUtil.IntersectsPath(ob.Position, ob.Radius, CacheData.Player.Position,
                        cacheObject.Position));

                return monsterCount;
            }

            /// <summary>
            /// Navigation obstacles are more critical than monster obstacles, these include script locked gates, large barricades etc
            /// They cannot be walked passed, and everything beyond them needs to be ignored.
            /// </summary>
            public static int BlockingObjects(TrinityCacheObject cacheObject)
            {
                var navigationCount = CacheData.NavigationObstacles.Count(
                    ob => MathUtil.IntersectsPath(ob.Position, ob.Radius, CacheData.Player.Position,
                        cacheObject.Position));

                var gate = CacheData.NavigationObstacles.FirstOrDefault(o => o.ActorSNO == 108466);
                if (gate != null)
                    Logger.Log("NavigationObstacles contains gate {0} blockingCount: {1}={2}", gate.Name, cacheObject.InternalName, navigationCount);

                return navigationCount;
            }

            public static bool IsNavBlocked(TrinityCacheObject cacheObject)
            {
                return PlayerMover.IsBlocked || BlockingMonsterObjects(cacheObject) >= 5;
            }

            public static double LastTargetFormula(TrinityCacheObject cacheObject)
            {                
                if (PlayerMover.IsBlocked)
                    return 0;

                return cacheObject.RActorGuid == LastTargetRactorGUID ? 250d : 0d;
            }

            #endregion

            public static void RefreshDiaGetWeights()
            {

                using (new PerformanceLogger("RefreshDiaObjectCache.Weighting"))
                {
                    #region Variables
                    //var LootableItems = ZetaDia.Actors.GetActorsOfType<ACDItem>().Where(a => a.IsAccountBound).Select(x => x.ACDId).ToList();

                    bool IsHighLevelGrift = Player.TieredLootRunlevel > 55;

                    double movementSpeed = PlayerMover.GetMovementSpeed();

                    int eliteCount = CombatBase.IgnoringElites
                        ? 0
                        : ObjectCache.Count(u => u.IsUnit && u.IsBossOrEliteRareUnique);
                    int avoidanceCount = Settings.Combat.Misc.AvoidAOE
                        ? 0
                        : ObjectCache.Count(o => o.Type == TrinityObjectType.Avoidance && o.Distance <= 50f);

                    bool avoidanceNearby = Settings.Combat.Misc.AvoidAOE &&
                                           ObjectCache.Any(
                                               o => o.Type == TrinityObjectType.Avoidance && o.Distance <= 15f);

                    bool prioritizeCloseRangeUnits = (avoidanceNearby || _forceCloseRangeTarget || Player.IsRooted ||
                                                      DateTime.UtcNow.Subtract(PlayerMover.LastRecordedAnyStuck)
                                                          .TotalMilliseconds < 1000 &&
                                                      ObjectCache.Count(u => u.IsUnit && u.RadiusDistance < 12f) >= 5);

                    bool hiPriorityHealthGlobes = Settings.Combat.Misc.HiPriorityHG;

                    bool healthGlobeEmergency = (Player.CurrentHealthPct <= CombatBase.EmergencyHealthGlobeLimit ||
                                                 Player.PrimaryResourcePct <= CombatBase.HealthGlobeResource && Legendary.ReapersWraps.IsEquipped) &&
                                                ObjectCache.Any(g => g.Type == TrinityObjectType.HealthGlobe) && Settings.Combat.Misc.CollectHealthGlobe;

                    bool hiPriorityShrine = Settings.WorldObject.HiPriorityShrines;

                    bool getHiPriorityShrine = ObjectCache.Any(s => s.Type == TrinityObjectType.Shrine) &&
                                               hiPriorityShrine;

                    bool getHiPriorityContainer = Settings.WorldObject.HiPriorityContainers &&
                                                  ObjectCache.Any(c => c.Type == TrinityObjectType.Container);

                    bool profileTagCheck = false;

                    string behaviorName = "";
                    if (ProfileManager.CurrentProfileBehavior != null)
                    {
                        Type behaviorType = ProfileManager.CurrentProfileBehavior.GetType();
                        behaviorName = behaviorType.Name;
                        if (!Settings.Combat.Misc.ProfileTagOverride && CombatBase.IsQuestingMode ||
                            behaviorType == typeof (WaitTimerTag) ||
                            behaviorType == typeof (UseTownPortalTag) ||
                            behaviorName.ToLower().Contains("townrun") ||
                            behaviorName.ToLower().Contains("townportal"))
                        {
                            profileTagCheck = true;
                        }
                    }

                    //bool isKillBounty =
                    //    !Player.ParticipatingInTieredLootRun &&
                    //    Player.ActiveBounty != null &&
                    //    Player.ActiveBounty.Info.KillCount > 0;

                    //bool inQuestArea = DataDictionary.QuestLevelAreaIds.Contains(Player.LevelAreaSnoIdId);

                    bool usingTownPortal = TrinityTownRun.IsTryingToTownPortal();

                    // Highest weight found as we progress through, so we can pick the best target at the end (the one with the highest weight)
                    HighestWeightFound = 0;

                    var isBlockedByMonsters = ObjectCache.Count(u => u.HitPoints > 0 && u.Distance <= 8f) >= 3;
                    var isStuck = Navigator.StuckHandler.IsStuck;

                    var monstersWithReflectActive = new List<TrinityCacheObject>();
                    var elites = new List<TrinityCacheObject>();

                    foreach (var unit in ObjectCache.Where(u => u.IsUnit))
                    {
                        if (unit.MonsterAffixes.HasFlag(MonsterAffixes.ReflectsDamage) && unit.HasReflectDamage())
                            monstersWithReflectActive.Add(unit);

                        if (unit.IsBossOrEliteRareUnique)
                            elites.Add(unit);
                    }

                    #endregion

                    Logger.Log(TrinityLogLevel.Debug, LogCategory.Weight,
                        "Starting weights: packSize={0} packRadius={1} MovementSpeed={2:0.0} Elites={3} AoEs={4} disableIgnoreTag={5} ({6}) closeRangePriority={7} townRun={8} questingArea={9} level={10} isQuestingMode={11} healthGlobeEmerg={12} hiPriHG={13} hiPriShrine={14}",
                        CombatBase.CombatOverrides.EffectiveTrashSize, CombatBase.CombatOverrides.EffectiveTrashRadius, movementSpeed,
                        eliteCount, avoidanceCount, profileTagCheck, behaviorName,
                        prioritizeCloseRangeUnits, TrinityTownRun.IsTryingToTownPortal(),
                        DataDictionary.QuestLevelAreaIds.Contains(Player.LevelAreaId), Player.Level,
                        CombatBase.IsQuestingMode, healthGlobeEmergency, hiPriorityHealthGlobes, hiPriorityShrine);

                    if (Settings.Combat.Misc.GoblinPriority == GoblinPriority.Kamikaze)
                    {
                        var goblin = ObjectCache.FirstOrDefault(u => u.IsTreasureGoblin && u.Distance <= 200f);
                        if (goblin != null && !isStuck && !isBlockedByMonsters)
                        {
                            CombatBase.IsDoingGoblinKamakazi = true;
                            CombatBase.KamakaziGoblin = goblin;
                            Logger.Log("Going Kamakazi on Goblin '{0} ({1})' Distance={2}", goblin.InternalName,
                                goblin.ActorSNO, goblin.Distance);
                            CurrentTarget = goblin;
                        }
                        else
                        {
                            CombatBase.IsDoingGoblinKamakazi = false;
                        }
                    }
                    else
                    {
                        CombatBase.IsDoingGoblinKamakazi = false;
                    }

                    var riftProgressionKillAll = RiftProgression.IsInRift && !RiftProgression.IsGaurdianSpawned && !RiftProgression.RiftComplete && 
                        Settings.Combat.Misc.RiftProgressionAlwaysKillPct < 100 && RiftProgression.CurrentProgressionPct < 100 && 
                        RiftProgression.CurrentProgressionPct >= Settings.Combat.Misc.RiftProgressionAlwaysKillPct;

                    if (riftProgressionKillAll != _riftProgressionKillAll)
                    {
                        _riftProgressionKillAll = riftProgressionKillAll;
                        if (riftProgressionKillAll)
                        {
                            Logger.Log($"Rift Progression is now at {RiftProgression.CurrentProgressionPct} - Killing everything!");
                            CombatBase.CombatMode = CombatMode.KillAll;
                        }
                        else
                        {
                            Logger.LogVerbose($"Reverting rift progression kill all mode back to normal combat");
                            CombatBase.CombatMode = CombatMode.On;
                        }
                    }

                    foreach (TrinityCacheObject cacheObject in ObjectCache.OrderBy(c => c.Distance))
                    {
                        if (cacheObject == null || !cacheObject.IsFullyValid())
                            continue;

                        cacheObject.Weight = 0;
                        cacheObject.WeightInfo = string.Empty;

                        if (CombatBase.IsDoingGoblinKamakazi)
                        {
                            if (cacheObject.RActorGuid == CombatBase.KamakaziGoblin.RActorGuid && !isStuck && !isBlockedByMonsters)
                            {
                                cacheObject.Weight = MaxWeight;
                                cacheObject.WeightInfo += string.Format("Maxxing {0} - Goblin Kamakazi Run ",
                                    cacheObject.InternalName);
                                break;
                            }
                            continue;
                        }

                        if (PlayerMover.IsBlocked && Settings.Combat.Misc.AttackWhenBlocked)
                        {
                            cacheObject.WeightInfo += "Player is blocked ";

                            if (cacheObject.Distance > 15f && !cacheObject.IsBossOrEliteRareUnique && cacheObject.Type != TrinityObjectType.ProgressionGlobe)
                            {
                                cacheObject.Weight = 0;
                                cacheObject.WeightInfo += "Ignoring Blocked Far Away ";
                                continue;
                            }
                        }

                        if (cacheObject.TrinityItemType == TrinityItemType.HoradricRelic && Player.BloodShards >= Player.MaxBloodShards)
                        {
                            cacheObject.Weight = 0;
                            cacheObject.WeightInfo += string.Format("Max BloodShards ", cacheObject.InternalName);
                            continue;
                        }

                        //if (cacheObject.TrinityItemType == TrinityItemType.HoradricRelic && IsBloodshardBlacklisted)
                        //{
                        //    cacheObject.Weight = 0;
                        //    cacheObject.WeightInfo += string.Format("BloodShards Blacklisted", cacheObject.InternalName);
                        //    continue;
                        //}

                        cacheObject.Weight = MinWeight;
                        switch (cacheObject.Type)
                        {
                                #region Unit

                            case TrinityObjectType.Unit:
                            {
                                    #region Unit Variables

                                double riftBoost = RiftValueFormula(cacheObject);

                                var isInsideHighRiftValueCluster = riftBoost > 0;

                                bool isInHotSpot = GroupHotSpots.CacheObjectIsInHotSpot(cacheObject) || cacheObject.IsNavBlocking();

                                bool elitesInRangeOfUnit = !CombatBase.IgnoringElites &&
                                                           ObjectCache.Any(
                                                               u =>
                                                                   u.ACDGuid != cacheObject.ACDGuid &&
                                                                   u.IsEliteRareUnique &&
                                                                   u.Position.Distance(cacheObject.Position) <= 15f);

                                int nearbyTrashCount =
                                    ObjectCache.Count(u => u.IsUnit && u.HitPoints > 0 && u.IsTrashMob &&
                                                            cacheObject.Position.Distance(u.Position) <= CombatBase.CombatOverrides.EffectiveTrashRadius);

                                bool ignoreSummoner = cacheObject.IsSummoner && !Settings.Combat.Misc.ForceKillSummoners;


                                var isBoss = cacheObject.CommonData.MonsterQualityLevel ==
                                             Zeta.Game.Internals.Actors.MonsterQuality.Boss;
                                var isUnique = cacheObject.CommonData.MonsterQualityLevel ==
                                               Zeta.Game.Internals.Actors.MonsterQuality.Unique;
                                var isRare = cacheObject.CommonData.MonsterQualityLevel ==
                                             Zeta.Game.Internals.Actors.MonsterQuality.Rare;
                                var isMinion = cacheObject.CommonData.MonsterQualityLevel ==
                                               Zeta.Game.Internals.Actors.MonsterQuality.Minion;
                                var isChampion = cacheObject.CommonData.MonsterQualityLevel ==
                                                 Zeta.Game.Internals.Actors.MonsterQuality.Champion;

                                    #endregion


                                if (CombatBase.CombatMode == CombatMode.KillAll)
                                {
                                    //Dist:                160     140     120     100      80     60     40      20      0
                                    //Weight (25k Max):    -77400  -53400  -32600  -15000  -600   10600  18600   23400   25000
                                    //
                                    //Formula:   MaxWeight-(Distance * Distance * RangeFactor)
                                    //           RangeFactor effects how quickly weights go into negatives on far distances.                                                                    

                                    var ignoreTrashTooFarAway = cacheObject.IsTrashMob && cacheObject.Distance > Settings.Combat.Misc.NonEliteRange;
                                    var ignoreElitesTooFarAway = cacheObject.IsEliteRareUnique && cacheObject.Distance > Settings.Combat.Misc.EliteRange;
                                    if (ignoreTrashTooFarAway || ignoreElitesTooFarAway)
                                    {
                                        cacheObject.WeightInfo += string.Format("Ignore Far Away Stuff TrashRange={0} EliteRange={1}",
                                        Settings.Combat.Misc.NonEliteRange, Settings.Combat.Misc.EliteRange);
                                        cacheObject.Weight = 0;
                                        break;
                                    }

                                    cacheObject.Weight = 25000 - (cacheObject.Distance * cacheObject.Distance * 4);
                                    cacheObject.WeightInfo += "Kill All Mode";
                                    break;
                                }


                                    // Only ignore monsters we have a rift value for and below the settings threshold.
                                    if (RiftProgression.IsInRift && cacheObject.RiftValuePct > 0 && cacheObject.RiftValuePct < Settings.Combat.Misc.RiftValueIgnoreUnitsBelow && 
                                        !cacheObject.IsBossOrEliteRareUnique && !PlayerMover.IsBlocked)
                                    {
                                        cacheObject.WeightInfo += string.Format("Ignoring {0} - low rift value ({1} / Setting={2}) ",
                                            cacheObject.InternalName, cacheObject.RiftValuePct, Settings.Combat.Misc.RiftValueIgnoreUnitsBelow);

                                        break;
                                    }


                                    cacheObject.WeightInfo =
                                string.Format(
                                    "ShouldIgnore={3} nearbyCount={0} radiusDistance={1:0} hotspot={2} elitesInRange={4} hitPointsPc={5:0.0} summoner={6} quest={7} minimap={8} bounty={9} ",
                                    nearbyTrashCount, cacheObject.RadiusDistance, isInHotSpot,
                                    usingTownPortal, elitesInRangeOfUnit, cacheObject.HitPointsPct,
                                    ignoreSummoner, cacheObject.IsQuestMonster, cacheObject.IsMinimapActive,
                                    cacheObject.IsBountyObjective);

                                #region Basic Checks

                                if (cacheObject.HitPointsPct <= 0)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Ignoring {0} - is dead", cacheObject.InternalName);
                                    break;
                                }

                                if (Player.InActiveEvent && ObjectCache.Any(o => o.IsEventObject) && !PlayerMover.IsBlocked)
                                {
                                    Vector3 eventObjectPosition =
                                        ObjectCache.FirstOrDefault(o => o.IsEventObject).Position;

                                    if (!cacheObject.IsQuestMonster &&
                                        cacheObject.Position.Distance(eventObjectPosition) > 35)
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Too Far From Event",
                                                cacheObject.InternalName);
                                        break;
                                    }
                                }

                                if (healthGlobeEmergency && cacheObject.Type == TrinityObjectType.HealthGlobe||
                                        getHiPriorityShrine && cacheObject.Type == TrinityObjectType.Shrine ||
                                        getHiPriorityContainer && cacheObject.Type == TrinityObjectType.Container && !PlayerMover.IsBlocked)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Ignoring {0} for Priotiy Container / Shrine / Goblin",
                                            cacheObject.InternalName);
                                    break;
                                }
                                //Monster is in cache but not within kill range
                                if (!cacheObject.IsBoss && !cacheObject.IsTreasureGoblin &&
                                    LastTargetRactorGUID != cacheObject.RActorGuid &&
                                    !cacheObject.IsQuestMonster && !cacheObject.IsBountyObjective &&
                                    cacheObject.RadiusDistance > DistanceForObjectType(cacheObject))
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - out of Kill Range ",
                                        cacheObject.InternalName);
                                    break;
                                }

                                if (cacheObject.IsTreasureGoblin)
                                {
                                    // Original TrinityPlugin stuff for priority handling now
                                    switch (Settings.Combat.Misc.GoblinPriority)
                                    {
                                        case GoblinPriority.Normal:
                                            // Treating goblins as "normal monsters". Ok so I lied a little in the config, they get a little extra weight really! ;)
                                            cacheObject.WeightInfo += "GoblinNormal ";
                                            cacheObject.Weight += 500d;
                                            break;
                                        case GoblinPriority.Prioritize:
                                            // Super-high priority option below... 
                                            cacheObject.WeightInfo += "GoblinPrioritize ";
                                            cacheObject.Weight = 1000d;
                                            break;
                                        case GoblinPriority.Kamikaze:
                                            // KAMIKAZE SUICIDAL TREASURE GOBLIN RAPE AHOY!
                                            cacheObject.WeightInfo += "GoblinKamikaze ";
                                            cacheObject.Weight = MaxWeight;
                                            break;
                                    }
                                }

                                    #endregion

                                if (isInsideHighRiftValueCluster)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format(" [Inside Rift Value Cluster: {0:0.0}, Threshold={1:0.0}] ",
                                            cacheObject.RiftValueInRadius, Settings.Combat.Misc.RiftValueAlwaysKillClusterValue);
                                }

                                    // Ignore trash mobs
                                    if (cacheObject.IsInvulnerable)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Ignoring {0} because of Invulnerability ",
                                            cacheObject.InternalName);
                                    cacheObject.Weight = MinWeight;
                                }
                                else if (Player.CurrentHealthPct <= 0.25)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Adding {0} Below Health Threshold ",
                                            cacheObject.InternalName);
                                }
                                else if (cacheObject.IsQuestMonster || cacheObject.IsEventObject ||
                                         cacheObject.IsBountyObjective)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Adding {0} Quest Monster | Bounty | Event Objective ",
                                            cacheObject.InternalName);
                                    //cacheObject.Weight += 1000d;
                                }
                                else if (cacheObject.Distance < 25 && Player.IsCastingTownPortalOrTeleport())
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Adding {0} because of Town Portal",
                                            cacheObject.InternalName);
                                }
                                else if (isInHotSpot)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Adding {0} due to being in Path or Hotspot ",
                                            cacheObject.InternalName);
                                }
                                else if (prioritizeCloseRangeUnits)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format(
                                            "Adding {0} because we seem to be stuck *OR* if not ranged and currently rooted ",
                                            cacheObject.InternalName);
                                }
                                else if (DataDictionary.MonsterCustomWeights.ContainsKey(cacheObject.ActorSNO) && !IsHighLevelGrift)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format(
                                            "Adding {0} because monsters from the dictionary/hashlist set at the top of the code ",
                                            cacheObject.InternalName);
                                }
                                else if ((cacheObject.ActorSNO == 210120 || cacheObject.ActorSNO == 210268) && cacheObject.Distance <= 25f)
                                {
                                    cacheObject.WeightInfo += string.Format("Adding {0} because of Blocking ",
                                        cacheObject.InternalName);
                                }
                                else
                                {

                                    var isAlwaysKillByValue = RiftProgression.IsInRift && cacheObject.RiftValuePct > 0 && cacheObject.RiftValuePct > Settings.Combat.Misc.RiftValueAlwaysKillUnitsAbove && cacheObject.IsTrashMob;
                                    if (isAlwaysKillByValue)
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("IsHighRiftValue {0}", cacheObject.RiftValuePct);
                                    }

                                    if (Settings.Combat.Misc.IgnoreHighHitePointTrash && !isAlwaysKillByValue)
                                    {
                                        HashSet<string> highHitPointTrashMobNames = new HashSet<string>
                                        {
                                            "mallet", //
                                            "monstrosity", //
                                            "triune_berserker", //
                                            "beast_d",
                                            "thousandpounder", //5581
                                            "westmarchbrute", //258678, 332679
                                            "unburied" //6359
                                        };

                                        var unitName = cacheObject.InternalName.ToLower();
                                        if (highHitPointTrashMobNames.Any(name => unitName.Contains(name)) && !PlayerMover.IsBlocked)
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format("Ignoring {0} for High Hp Mob.", cacheObject.InternalName);
                                            break;
                                        }
                                    }

                                    #region Trash Mob                                    

                                    if (cacheObject.IsTrashMob)
                                    {
                                        if (cacheObject.HitPointsPct < Settings.Combat.Misc.ForceKillTrashBelowHealth)
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format("Adding {0} because it is below the minimum trash mob health ",
                                                    cacheObject.InternalName);
                                        }
                                        else if (cacheObject.IsSummoner && Settings.Combat.Misc.ForceKillSummoners)
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format("Adding {0} because he is a summoner ",
                                                    cacheObject.InternalName);
                                            //cacheObject.Weight += 500d;
                                        }
                                        else if ((cacheObject.HitPointsPct <
                                                Settings.Combat.Misc.IgnoreTrashBelowHealthDoT) &&
                                            cacheObject.HasDotDPS && !PlayerMover.IsBlocked)
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format(
                                                    "Ignoring {0} - Hitpoints below Health/DoT Threshold ",
                                                    cacheObject.InternalName);
                                            break;
                                        }
                                        else if (nearbyTrashCount < CombatBase.CombatOverrides.EffectiveTrashSize &&
                                                !isAlwaysKillByValue && !PlayerMover.IsBlocked && !isInsideHighRiftValueCluster)

                                        {
                                            cacheObject.WeightInfo += string.Format("Ignoring {0} below TrashPackSize", cacheObject.InternalName);
                                            break;
                                        }
                                        cacheObject.WeightInfo += string.Format("Adding {0} by Default.",
                                            cacheObject.InternalName);
                                    }

                                    #endregion

                                    #region Elite / Rares / Uniques

                                    if (isUnique || isBoss || isRare || isMinion || isChampion)
                                    {
                                        //XZ - Please add Ignore below health for elite.
                                        //if ((cacheObject.HitPointsPct <
                                        //     Settings.Combat.Misc.IgnoreEliteBelowHealthDoT) &&
                                        //    cacheObject.HasDotDPS)
                                        //{
                                        //    cacheObject.WeightInfo +=
                                        //        string.Format("Ignoring {0} - Hitpoints below Health/DoT Threshold ", cacheObject.InternalName);
                                        //    break;
                                        //}
                                        if (cacheObject.HitPointsPct <= Settings.Combat.Misc.ForceKillElitesHealth && !cacheObject.IsMinion)
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format("Adding {0} for Elite Under Health Threshold.",
                                                    cacheObject.InternalName);
                                        }
                                        else if (TargetUtil.NumMobsInRangeOfPosition(cacheObject.Position,
                                            CombatBase.CombatOverrides.EffectiveTrashRadius) >=
                                            CombatBase.CombatOverrides.EffectiveTrashSize &&
                                            Settings.Combat.Misc.ForceKillClusterElites)
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format("Adding {0} for Elite Inside Cluster.",
                                                    cacheObject.InternalName);
                                        }
                                        else if ((Settings.Combat.Misc.IgnoreElites ||
                                                Settings.Combat.Misc.IgnoreRares && isRare ||
                                                Settings.Combat.Misc.IgnoreMinions && isMinion ||
                                                Settings.Combat.Misc.IgnoreChampions && isChampion) &&
                                                !cacheObject.IsBoss)
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format("Ignoring {0} because of Ignore Settings.",
                                                    cacheObject.InternalName);
                                            break;
                                        }
                                        if (Settings.Combat.Misc.IgnoreMonstersWhileReflectingDamage &&
                                            monstersWithReflectActive.Any(
                                                u => u.RActorGuid == cacheObject.RActorGuid))
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format("Ignoring {0} due to reflect damage buff ",
                                                    cacheObject.InternalName);
                                            break;
                                        }
                                        cacheObject.WeightInfo += string.Format("Adding {0} default Elite ",
                                            cacheObject.InternalName);
                                        cacheObject.Weight += EliteFormula(cacheObject);
                                    }

                                    #endregion
                                }
                                // Monsters near players given higher weight (Add Variable for this to Settings).
                                //if (Settings.Combat.Misc.PrioritizeHelpingFriends && cacheObject.Weight > 0)
                                //{
                                //    var group = 0.0;
                                //    foreach (
                                //        var player in
                                //            ObjectCache.Where(
                                //                p =>
                                //                    p.Type == TrinityObjectType.Player &&
                                //                    p.ACDId != Player.ACDId))
                                //    {
                                //        group +=
                                //            Math.Max(
                                //                ((55f - cacheObject.Position.Distance(player.Position)) / 55f * 500d),
                                //                2d);
                                //    }
                                //    if (group > 100.0)
                                //    {
                                //        objWeightInfo += string.Format("group{0:0} ", group);
                                //    }
                                //    cacheObject.Weight += group;
                                //}

                                // If any units between us and target, reduce weight, for monk and barb
                                //if (CombatBase.KiteDistance <= 0 && cacheObject.RadiusDistance > 9f &&
                                //    CacheData.MonsterObstacles.Any(
                                //        cp =>
                                //            MathUtil.IntersectsPath(cp.Position, cp.Radius*1.2f, Player.Position,
                                //                cacheObject.Position)))
                                //{
                                //    // Monk
                                //    if (Player.ActorClass == ActorClass.Monk && !Skills.Monk.DashingStrike.CanCast() &&
                                //        !Skills.Monk.Epiphany.IsBuffActive)
                                //    {
                                //        cacheObject.WeightInfo += "MonsterObstacles";
                                //        cacheObject.Weight = 1;
                                //    }

                                //    // Barb
                                //    if (Player.ActorClass == ActorClass.Barbarian && !Skills.Barbarian.Whirlwind.CanCast() &&
                                //        !Skills.Barbarian.FuriousCharge.CanCast() && !Skills.Barbarian.Leap.CanCast())
                                //    {
                                //        cacheObject.WeightInfo += "MonsterObstacles";
                                //        cacheObject.Weight = 1;
                                //    }
                                //}

                                var dist = ObjectDistanceFormula(cacheObject);
                                var last = LastTargetFormula(cacheObject);
                                var pack = PackDensityFormula(cacheObject);
                                var health = UnitHealthFormula(cacheObject);
                                var path = PathBlockedFormula(cacheObject);
                                var reflect = ReflectiveMonsterNearFormula(cacheObject, monstersWithReflectActive);
                                var elite = EliteMonsterNearFormula(cacheObject, elites);
                                var aoe = AoENearFormula(cacheObject) + AoEInPathFormula(cacheObject);                               
                                                           
                                cacheObject.Weight += dist + last + pack + health + path + reflect + elite + aoe + riftBoost;

                                cacheObject.WeightInfo += string.Format(" dist={0:0.0} last={1:0.0} pack={2:0.0} health={3:0.0} path={4:0.0} reflect={5:0.0} elite={6:0.0} aoe={7:0.0} riftBoost={8:0.0} (riftValue={9:0.0})",
                                    dist, last, pack, health, path, reflect, elite, aoe, riftBoost, cacheObject.RiftValuePct);

                                break;
                            }

                                #endregion

                                #region Hotspot

                            case TrinityObjectType.HotSpot:
                            {
                                // If there's monsters in our face, ignore
                                if (prioritizeCloseRangeUnits)
                                    break;

                                // if we started cache refresh with a target already
                                if (LastTargetRactorGUID != -1)
                                    break;

                                // If it's very close, ignore
                                if (cacheObject.Distance <= 50)
                                {
                                    break;
                                }
                                else if (
                                    !CacheData.TimeBoundAvoidance.Any(
                                        aoe => aoe.Position.Distance(cacheObject.Position) <= aoe.Radius))
                                {
                                    float maxDist = 2500;
                                    cacheObject.Weight = (maxDist - cacheObject.Distance)/maxDist*50000d;
                                }
                                break;
                            }

                                #endregion

                                #region Item

                            case TrinityObjectType.Item:
                            {
                                bool isTwoSquare = true;
                                var item = cacheObject.Item;
                                if (item != null)
                                {
                                    var commonData = item.CommonData;
                                    if (commonData != null && commonData.IsValid)
                                        isTwoSquare = commonData.IsTwoSquareItem;
                                }


                                    // Campaign A5 Quest "Lost Treasure of the Nephalem" - have to interact with nephalem switches first... 
                                    // Quest: x1_Adria, Id: 257120, Step: 108 - disable all looting, pickup, and objects
                                    if (Player.WorldType != Act.OpenWorld && Player.CurrentQuestSNO == 257120 &&
                                    Player.CurrentQuestStep == 108)
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} For Quest",
                                        cacheObject.InternalName);
                                    break;
                                }

                                if (Player.ParticipatingInTieredLootRun && ObjectCache.Any(m => m.IsUnit && m.IsBoss))
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} Loot Run Boss",
                                        cacheObject.InternalName);
                                    break;
                                }

                                //var dropAllLegendaries = Settings.Loot.TownRun.DropInTownOption == DropInTownOption.All &&
                                //                         ZetaDia.IsInTown;

                                //var dropUnidentified = Settings.Loot.TownRun.KeepLegendaryUnid &&
                                //                       Settings.Loot.TownRun.DropInTownOption != DropInTownOption.None &&
                                //                       ZetaDia.IsInTown;

                                //if (dropAllLegendaries || dropUnidentified)
                                //{
                                //    cacheObject.WeightInfo +=
                                //        string.Format("Ignoring {0} Disabled Looting In Town Drop Legendaries",
                                //            cacheObject.InternalName);

                                //    break;
                                //}


                                if (Player.IsInTown)
                                {
                                    if (Settings.Loot.Pickup.DontPickupInTown)
                                    {
                                        cacheObject.WeightInfo += $"Ignoring DontPickUpInTown Setting.";
                                        break;
                                    }

                                    var testItem = ActorManager.GetItemByAnnId(cacheObject.AnnId);
                                    if (testItem == null || !testItem.CanPickupItem())
                                    {
                                        cacheObject.WeightInfo += $"Ignoring {cacheObject.InternalName} - unable to pickup.";
                                        break;
                                    }
                                }

                                if (DropItems.DroppedItemAnnIds.Contains(cacheObject.AnnId))
                                {
                                    cacheObject.WeightInfo += $"Ignoring previously dropped item";
                                }

                                // Don't pickup items if we're doing a TownRun
                                if (TrinityItemManager.FindValidBackpackLocation(isTwoSquare) == new Vector2(-1, -1))
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} for TownRun",
                                        cacheObject.InternalName);
                                    break;
                                }

                                // Death's Breath Priority
                                if (cacheObject.ActorSNO == 361989 || cacheObject.ActorSNO == 449044)
                                {
                                    if (!Settings.Loot.Pickup.PickupDeathsBreath)
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} due to settings", cacheObject.InternalName);
                                        break;
                                    }

                                    cacheObject.Weight = MaxWeight;
                                    cacheObject.WeightInfo += string.Format("Adding {0} - Death's Breath",
                                        cacheObject.InternalName);
                                    break;
                                }
                                //if (!LootableItems.Contains(cacheObject.ACDId))
                                //{
                                //    cacheObject.Weight = MaxWeight;
                                //    cacheObject.WeightInfo += string.Format("Adding {0} - Item does not belong to us.",
                                //        cacheObject.InternalName);
                                //    break;
                                //}

                                // Give legendaries max weight, always
                                    if (cacheObject.ItemQuality >= ItemQuality.Legendary)
                                {
                                    // Ignore Legendaries in AoE
                                    if (Settings.Loot.Pickup.IgnoreLegendaryInAoE &&
                                        CacheData.TimeBoundAvoidance.Any(
                                            aoe => cacheObject.Position.Distance(aoe.Position) <= aoe.Radius))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Legendary in AoE", cacheObject.InternalName);
                                        break;
                                    }

                                    // Ignore Legendaries near Elites
                                    if (Settings.Loot.Pickup.IgnoreLegendaryNearElites &&
                                        ObjectCache.Any(
                                            u =>
                                                u.IsEliteRareUnique &&
                                                u.Position.Distance(cacheObject.Position) <= 15f))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Legendary near Elite",
                                                cacheObject.InternalName);
                                        break;
                                    }
                                    cacheObject.Weight = MaxWeight;
                                    cacheObject.WeightInfo += string.Format("Adding {0} - Legendary",
                                        cacheObject.InternalName);
                                    break;
                                }

                                //Non Legendaries
                                if (cacheObject.ItemQuality < ItemQuality.Legendary)
                                {
                                    // Ignore Non-Legendaries in AoE
                                    if (Settings.Loot.Pickup.IgnoreNonLegendaryInAoE &&
                                        CacheData.TimeBoundAvoidance.Any(
                                            aoe => cacheObject.Position.Distance(aoe.Position) <= aoe.Radius))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Legendary in AoE", cacheObject.InternalName);
                                        break;
                                    }
                                    // Ignore Non-Legendaries near Elites
                                    if (Settings.Loot.Pickup.IgnoreNonLegendaryNearElites &&
                                        ObjectCache.Any(
                                            u =>
                                                u.IsEliteRareUnique &&
                                                u.Position.Distance(cacheObject.Position) <= 15f))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Non Legendary near Elite",
                                                cacheObject.InternalName);
                                        break;
                                    }
                                }



                                //if (Player.ActorClass == ActorClass.Monk && Hotbar.Contains(SNOPower.Monk_TempestRush) &&
                                //    TimeSinceUse(SNOPower.Monk_TempestRush) < 1000 &&
                                //    cacheObject.ItemQuality < ItemQuality.Legendary)
                                //{
                                //    cacheObject.Weight = 500;
                                //    cacheObject.WeightInfo += " MonkTR Weight";
                                //}

                                cacheObject.Weight += ObjectDistanceFormula(cacheObject) +
                                                      LastTargetFormula(cacheObject) +
                                                      EliteMonsterNearFormula(cacheObject, elites) +
                                                      AoENearFormula(cacheObject) +
                                                      AoEInPathFormula(cacheObject);
                                break;
                            }

                                #endregion

                                #region Gold

                            case TrinityObjectType.Gold:
                            {
                                if (!Settings.Loot.Pickup.PickupGold)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Ignoring {0} - Pick Up Gold Setting.", cacheObject.InternalName);
                                    break;
                                }
                                //Ignore because we are blocked by objects or mobs.
                                if (IsNavBlocked(cacheObject))
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - Nav Blocked.",
                                        cacheObject.InternalName);
                                    break;
                                }

                                //Ignore because we are TownPortaling
                                if (TrinityTownRun.IsTryingToTownPortal() )
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - Town Portal.",
                                        cacheObject.InternalName);
                                    break;
                                }
                                // Campaign A5 Quest "Lost Treasure of the Nephalem" - have to interact with nephalem switches first... 
                                // Quest: x1_Adria, Id: 257120, Step: 108 - disable all looting, pickup, and objects
                                if (Player.WorldType != Act.OpenWorld && Player.CurrentQuestSNO == 257120 &&
                                    Player.CurrentQuestStep == 108)
                                {
                                    cacheObject.Weight = 0;
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - DisableForQuest",
                                        cacheObject.InternalName);
                                    break;
                                }

                                // Ignore gold near Elites
                                if (Settings.Loot.Pickup.IgnoreGoldNearElites &&
                                    ObjectCache.Any(
                                        u =>
                                            u.IsEliteRareUnique &&
                                            u.Position.Distance(cacheObject.Position) <= 15f))
                                {
                                    break;
                                }

                                // Ignore gold in AoE
                                if (Settings.Loot.Pickup.IgnoreGoldInAoE &&
                                    CacheData.TimeBoundAvoidance.Any(
                                        aoe => cacheObject.Position.Distance(aoe.Position) <= aoe.Radius))
                                {
                                    break;
                                }

                                cacheObject.Weight += ObjectDistanceFormula(cacheObject) +
                                                      LastTargetFormula(cacheObject) +
                                                      EliteMonsterNearFormula(cacheObject, elites) +
                                                      AoENearFormula(cacheObject) +
                                                      AoEInPathFormula(cacheObject);

                                break;
                            }

                                #endregion

                                #region Power Globe

                            case TrinityObjectType.PowerGlobe:
                            {  
                                if (Settings.Combat.Misc.IgnorePowerGlobes)
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - Power Globe Setting.", cacheObject.InternalName);
                                        break;
                                }

                                    //Ignore because we are blocked by objects or mobs.
                                    if (IsNavBlocked(cacheObject))
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - Nav Blocked.",
                                        cacheObject.InternalName);
                                    break;
                                }
                                //Ignore because we are TownPortaling
                                if (TrinityTownRun.IsTryingToTownPortal())
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - Town Portal.",
                                        cacheObject.InternalName);
                                    break;
                                }

                                cacheObject.Weight += ObjectDistanceFormula(cacheObject) +
                                                      LastTargetFormula(cacheObject) +
                                                      EliteMonsterNearFormula(cacheObject, elites) +
                                                      AoENearFormula(cacheObject) +
                                                      AoEInPathFormula(cacheObject);
                                break;
                            }

                                #endregion

                                #region Progression Globe

                            case TrinityObjectType.ProgressionGlobe:
                            {
                                ////Ignore because we are blocked by objects or mobs.
                                //if (IsNavBlocked(cacheObject))
                                //{
                                //    cacheObject.WeightInfo += string.Format("Ignoring {0} - Nav Blocked.",
                                //        cacheObject.InternalName);
                                //    break;
                                //}
                                ////Ignore because we are TownPortaling
                                //if (TownRun.IsTryingToTownPortal())
                                //{
                                //    cacheObject.WeightInfo += string.Format("Ignoring {0} - Town Portal.",
                                //        cacheObject.InternalName);
                                //    break;
                                //}

                                if (Settings.Combat.Misc.IgnoreNormalProgressionGlobes && DataDictionary.NormalProgressionGlobeSNO.Contains(cacheObject.ActorSNO))
                                    {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} because of settings.", cacheObject.InternalName);
                                    break;
                                }

                                if (Settings.Combat.Misc.IgnoreGreaterProgressionGlobes && DataDictionary.GreaterProgressionGlobeSNO.Contains(cacheObject.ActorSNO))
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} because of settings.", cacheObject.InternalName);
                                    break;
                                }

                                if (cacheObject.Distance <= 150f)
                                {
                                    cacheObject.WeightInfo += string.Format("Maxxing {0} - Progression Globe.",
                                        cacheObject.InternalName);
                                    cacheObject.Weight += MaxWeight;
                                    break;
                                }

                                cacheObject.Weight += ObjectDistanceFormula(cacheObject) +
                                                      LastTargetFormula(cacheObject) +
                                                      EliteMonsterNearFormula(cacheObject, elites) +
                                                      AoENearFormula(cacheObject) +
                                                      AoEInPathFormula(cacheObject);
                                break;
                            }

                                #endregion

                                #region Health Globe && Health Wells

                            case TrinityObjectType.HealthWell:
                            case TrinityObjectType.HealthGlobe:
                            {
                                if (!Settings.Combat.Misc.CollectHealthGlobe)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Ignoring {0} - Collect Health Globe Setting.",
                                            cacheObject.InternalName);
                                }

                                //Ignore because we are blocked by objects or mobs.
                                if (IsNavBlocked(cacheObject))
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - Nav Blocked.",
                                        cacheObject.InternalName);
                                    break;
                                }

                                //Ignore because we are TownPortaling
                                if (Player.IsCastingPortal)
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - Town Portal.",
                                        cacheObject.InternalName);
                                    break;
                                }

                                // do not collect health globes if we are kiting and health globe is too close to monster or avoidance
                                if (CombatBase.KiteDistance > 0)
                                {
                                    if (
                                        CacheData.MonsterObstacles.Any(
                                            m => m.Position.Distance(cacheObject.Position) < CombatBase.KiteDistance))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Kiting with Monster Obstacles.",
                                                cacheObject.InternalName);
                                        break;
                                    }
                                    if (
                                        CacheData.TimeBoundAvoidance.Any(
                                            m => m.Position.Distance(cacheObject.Position) < CombatBase.KiteDistance))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Kiting with Time Bound Avoidance.",
                                                cacheObject.InternalName);
                                        break;
                                    }
                                }

                                if (Player.CurrentHealthPct > CombatBase.EmergencyHealthGlobeLimit)
                                {
                                    //XZ - Add gui Variable for Party MemberHealth
                                    if (
                                        ObjectCache.Any(
                                            p => p.Type == TrinityObjectType.Player && p.RActorGuid != Player.RActorGuid))
                                    {
                                        double minPartyHealth =
                                            ObjectCache.Where(
                                                p =>
                                                    p.Type == TrinityObjectType.Player &&
                                                    p.RActorGuid != Player.RActorGuid)
                                                .Min(p => p.HitPointsPct);
                                        if (minPartyHealth <= 25)
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format("Adding {0} - Party Health Below Threshold",
                                                    cacheObject.InternalName);
                                            cacheObject.Weight += hiPriorityHealthGlobes
                                                ? MaxWeight
                                                : (1d - minPartyHealth)*5000d +
                                                  EliteMonsterNearFormula(cacheObject, elites) -
                                                  PackDensityFormula(cacheObject);
                                            break;
                                        }
                                    }
                                    double myHealth = Player.CurrentHealthPct;
                                    // DH's logic with Blood Vengeance passive
                                    if (Player.ActorClass == ActorClass.DemonHunter &&
                                        Player.PrimaryResource <= 10 &&
                                        CacheData.Hotbar.PassiveSkills.Contains(
                                            SNOPower.DemonHunter_Passive_Vengeance))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Adding {0} - Reapes Wraps.", cacheObject.InternalName);
                                        cacheObject.Weight += hiPriorityHealthGlobes
                                            ? MaxWeight
                                            : (1d - Player.PrimaryResource)*5000d +
                                              EliteMonsterNearFormula(cacheObject, elites) -
                                              PackDensityFormula(cacheObject);
                                        break;
                                    }

                                    // WD's logic with Gruesome Feast passive
                                    if (Player.ActorClass == ActorClass.Witchdoctor &&
                                        Player.PrimaryResource <= 1200 &&
                                        CacheData.Hotbar.PassiveSkills.Contains(
                                            SNOPower.Witchdoctor_Passive_GruesomeFeast))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Adding {0} - Reapes Wraps.", cacheObject.InternalName);
                                        cacheObject.Weight += hiPriorityHealthGlobes
                                            ? MaxWeight
                                            : (1d - Player.PrimaryResource)*5000d +
                                              EliteMonsterNearFormula(cacheObject, elites) -
                                              PackDensityFormula(cacheObject);
                                        break;
                                    }

                                    //Reapers Wraps Equipped
                                    if (Legendary.ReapersWraps.IsEquipped && Player.PrimaryResource <= 50)
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Adding {0} - Reapes Wraps.", cacheObject.InternalName);
                                        cacheObject.Weight += hiPriorityHealthGlobes
                                            ? MaxWeight
                                            : (1d - Player.PrimaryResource)*5000d +
                                              EliteMonsterNearFormula(cacheObject, elites) -
                                              PackDensityFormula(cacheObject);
                                        break;
                                    }
                                    //XZ - Set this to be a value to ignore globes above certain health.
                                    if (ZetaDia.Me.HitpointsCurrentPct > 0.80)
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Over 80% health.",
                                                cacheObject.InternalName);
                                        break;
                                    }
                                }
                                else
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format(
                                            "Maxxing {0} - Player.CurrentHealthPct < CombatBase.EmergencyHealthGlobeLimit.",
                                            cacheObject.InternalName);
                                    cacheObject.Weight = MaxWeight;
                                    break;
                                }

                                cacheObject.Weight += ObjectDistanceFormula(cacheObject) +
                                                      LastTargetFormula(cacheObject) +
                                                      EliteMonsterNearFormula(cacheObject, elites) +
                                                      AoENearFormula(cacheObject) +
                                                      AoEInPathFormula(cacheObject);
                                break;
                            }

                                #endregion

                                #region Shrine

                            case TrinityObjectType.CursedShrine:
                            case TrinityObjectType.Shrine:
                            {
                                if (cacheObject.IsUsed)
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - Used.", cacheObject.InternalName);
                                    break;
                                }

                                if (!cacheObject.IsQuestMonster)
                                {
                                    if (!Settings.WorldObject.UseShrine)
                                    {
                                        cacheObject.WeightInfo += $"Ignoring {cacheObject.InternalName} - Dont use shrines setting.";
                                        break;
                                    }

                                    //XZ - Please Add this.
                                    //if (!Settings.WorldObject.UseCursedShrine && cacheObject.Type == TrinityObjectType.CursedShrine)
                                    //    break;

                                    //Ignore because we are blocked by objects or mobs.
                                    if (IsNavBlocked(cacheObject))
                                    {
                                        cacheObject.WeightInfo += string.Format("Ignoring {0} - Nav Blocked.", cacheObject.InternalName);
                                        break;
                                    }

                                    //Ignore because we are TownPortaling
                                    if (TrinityTownRun.IsTryingToTownPortal())
                                    {
                                        cacheObject.WeightInfo += string.Format("Ignoring {0} - Town Portal.", cacheObject.InternalName);
                                        break;
                                    }
                                }
                                // Campaign A5 Quest "Lost Treasure of the Nephalem" - have to interact with nephalem switches first... 
                                // Quest: x1_Adria, Id: 257120, Step: 108 - disable all looting, pickup, and objects
                                if (Player.WorldType != Act.OpenWorld && Player.CurrentQuestSNO == 257120 &&
                                    Player.CurrentQuestStep == 108)
                                {
                                    cacheObject.Weight = 0;
                                    cacheObject.WeightInfo +=
                                        string.Format("Ignoring {0} - Diable for Quest", cacheObject.InternalName);
                                    break;
                                }
                                if (GetShrineType(cacheObject) != ShrineTypes.RunSpeed &&
                                    GetShrineType(cacheObject) != ShrineTypes.Speed &&
                                    GetShrineType(cacheObject) != ShrineTypes.Fortune)
                                {
                                    if (Settings.WorldObject.HiPriorityShrines)
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Adding {0} - High Priority Shrine", cacheObject.InternalName);
                                        cacheObject.Weight += MaxWeight;
                                        break;
                                    }
                                    if (CacheData.Inventory.KanaisCubeIds.Contains(Legendary.NemesisBracers.Id) ||
                                        Legendary.NemesisBracers.IsEquipped)
                                    {
                                        if (elites.Any())
                                            cacheObject.Weight -= elites.Count*100;
                                        else
                                            cacheObject.Weight += 500d;
                                    }

                                    if (elites.Any())
                                    {
                                        cacheObject.Weight = elites.Count*250;
                                        cacheObject.WeightInfo +=
                                            string.Format("Adding {0} - Higher Priority Shrine for Elites",
                                                cacheObject.InternalName);
                                    }
                                    cacheObject.Weight += PackDensityFormula(cacheObject);
                                }

                                cacheObject.Weight += ObjectDistanceFormula(cacheObject) +
                                                      LastTargetFormula(cacheObject) +
                                                      //EliteMonsterNearFormula(cacheObject, elites) +
                                                      AoENearFormula(cacheObject) +
                                                      AoEInPathFormula(cacheObject);
                                break;
                            }

                                #endregion

                                #region Door and Barricade

                            case TrinityObjectType.Barricade:
                            case TrinityObjectType.Door:
                            {
                                if (cacheObject.IsUsed)
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - Used.", cacheObject.InternalName);
                                    break;
                                }

                                    if (!cacheObject.IsQuestMonster)
                                {
                                    //Ignore because we are blocked by objects or mobs.
                                    if (IsNavBlocked(cacheObject))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Nav Blocked.", cacheObject.InternalName);
                                        break;
                                    }

                                    //Ignore because we are TownPortaling
                                    if (TrinityTownRun.IsTryingToTownPortal() && cacheObject.Distance > 25f)
                                        {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Town Portal.", cacheObject.InternalName);
                                        break;
                                    }

                                    // Need to Prioritize, forget it!
                                    if (prioritizeCloseRangeUnits)
                                    {
                                        if (prioritizeCloseRangeUnits)
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format(
                                                    "Ignoring {0} - We seem to be stuck *OR* if not ranged and currently rooted ",
                                                    cacheObject.InternalName);
                                        }
                                        break;
                                    }
                                }
                                // We're standing on the damn thing... open it!!
                                if (cacheObject.RadiusDistance <= 10f)
                                {
                                    cacheObject.Weight = MaxWeight;
                                    cacheObject.WeightInfo +=
                                        string.Format("Maxxing {0} - Door in Close Distance.", cacheObject.InternalName);
                                }
                                cacheObject.Weight += ObjectDistanceFormula(cacheObject) +
                                                      LastTargetFormula(cacheObject) +
                                                      EliteMonsterNearFormula(cacheObject, elites) +
                                                      PackDensityFormula(cacheObject) +
                                                      AoENearFormula(cacheObject) +
                                                      AoEInPathFormula(cacheObject);
                                break;
                            }

                                #endregion

                                #region Destructible

                            case TrinityObjectType.Destructible:
                            {
                                if (!cacheObject.IsQuestMonster)
                                {
                                    //Ignore because we are blocked by objects or mobs.
                                    if (IsNavBlocked(cacheObject))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Nav Blocked.", cacheObject.InternalName);
                                        break;
                                    }

                                    //Ignore because we are TownPortaling
                                    if (TrinityTownRun.IsTryingToTownPortal() && cacheObject.Distance > 25f)
                                        {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Town Portal.", cacheObject.InternalName);
                                        break;
                                    }

                                    // Need to Prioritize, forget it!
                                    if (prioritizeCloseRangeUnits)
                                    {
                                        if (prioritizeCloseRangeUnits)
                                        {
                                            cacheObject.WeightInfo +=
                                                string.Format(
                                                    "Ignoring {0} - We seem to be stuck *OR* if not ranged and currently rooted ",
                                                    cacheObject.InternalName);
                                        }
                                        break;
                                    }
                                }
                                if (DataDictionary.ForceDestructibles.Contains(cacheObject.ActorSNO))
                                {
                                    cacheObject.Weight = 100d;
                                    break;
                                }

                                // Not Stuck, skip!
                                if (Settings.WorldObject.DestructibleOption == DestructibleIgnoreOption.OnlyIfStuck &&
                                    cacheObject.RadiusDistance > 0 &&
                                    (DateTime.UtcNow.Subtract(PlayerMover.LastGeneratedStuckPosition).TotalSeconds > 3))
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Ignoring {0} - Destructible Settings.", cacheObject.InternalName);
                                    break;
                                }

                                //// We're standing on the damn thing... break it
                                if(cacheObject.Distance < 3f)
                                {
                                    cacheObject.Weight = MaxWeight;
                                    cacheObject.WeightInfo +=
                                        string.Format("Maxxing {0} - Close Distance.", cacheObject.InternalName);
                                }

                                //// Fix for WhimsyShire Pinata
                                if (DataDictionary.ResplendentChestIds.Contains(cacheObject.ActorSNO))
                                    cacheObject.Weight += 500d;
                                cacheObject.Weight += ObjectDistanceFormula(cacheObject) +
                                                      LastTargetFormula(cacheObject) +
                                                      EliteMonsterNearFormula(cacheObject, elites) -
                                                      PackDensityFormula(cacheObject) +
                                                      AoENearFormula(cacheObject) +
                                                      AoEInPathFormula(cacheObject);
                                break;
                            }

                                #endregion

                                #region Interactables

                            case TrinityObjectType.Interactable:
                            {
                                if (cacheObject.IsUsed)
                                {
                                    cacheObject.WeightInfo += string.Format("Ignoring {0} - Used.", cacheObject.InternalName);
                                    break;
                                }

                                    if (!cacheObject.IsQuestMonster)
                                {
                                    //Ignore because we are blocked by objects or mobs.
                                    if (IsNavBlocked(cacheObject))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Nav Blocked.", cacheObject.InternalName);
                                        break;
                                    }

                                    //Ignore because we are TownPortaling
                                    if (TrinityTownRun.IsTryingToTownPortal() && cacheObject.Distance > 25f)
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Town Portal.", cacheObject.InternalName);
                                        break;
                                    }

                                    // Need to Prioritize, forget it!
                                    if (prioritizeCloseRangeUnits)
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format(
                                                "Ignoring {0} - We seem to be stuck *OR* if not ranged and currently rooted ",
                                                cacheObject.InternalName);
                                        break;
                                    }
                                }
                                // Campaign A5 Quest "Lost Treasure of the Nephalem" - have to interact with nephalem switches first... 
                                // Quest: x1_Adria, Id: 257120, Step: 108 - disable all looting, pickup, and objects
                                if (Player.WorldType != Act.OpenWorld && Player.CurrentQuestSNO == 257120 &&
                                    Player.CurrentQuestStep == 108)
                                {
                                    cacheObject.Weight = MaxWeight;
                                    cacheObject.WeightInfo +=
                                        string.Format("Adding {0} - Campaign A5 Quest Lost Treasure of the Nephalem",
                                            cacheObject.InternalName);
                                    break;
                                }

                                // nearby monsters attacking us - don't try to use headtone
                                if (cacheObject.Object is DiaGizmo && cacheObject.Gizmo != null &&
                                    cacheObject.Gizmo.CommonData != null &&
                                    cacheObject.Gizmo.CommonData.ActorInfo != null &&
                                    cacheObject.Gizmo.CommonData.ActorInfo.GizmoType == GizmoType.Headstone &&
                                    ObjectCache.Any(u => u.IsUnit && u.RadiusDistance < 25f && u.IsFacingPlayer))
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Ignoring {0} - Units Near Headstone. ", cacheObject.InternalName);
                                    break;
                                }

                                if (DataDictionary.HighPriorityInteractables.Contains(cacheObject.ActorSNO) &&
                                    cacheObject.RadiusDistance <= 30f)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Maxxing {0} - High Priority Interactable.. ",
                                            cacheObject.InternalName);
                                    cacheObject.Weight = MaxWeight;
                                    break;
                                }

                                if (cacheObject.IsQuestMonster)
                                {
                                    cacheObject.Weight += 5000d;
                                }
                                cacheObject.Weight += ObjectDistanceFormula(cacheObject) +
                                                      LastTargetFormula(cacheObject) +
                                                      EliteMonsterNearFormula(cacheObject, elites) -
                                                      PackDensityFormula(cacheObject) +
                                                      AoENearFormula(cacheObject) +
                                                      AoEInPathFormula(cacheObject);

                                break;
                            }

                                #endregion

                                #region Container

                            case TrinityObjectType.Container:
                            {
                                if (!cacheObject.IsQuestMonster)
                                {
                                    //Ignore because we are blocked by objects or mobs.
                                    if (IsNavBlocked(cacheObject))
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Nav Blocked.", cacheObject.InternalName);
                                        break;
                                    }

                                    //Ignore because we are TownPortaling
                                    if (TrinityTownRun.IsTryingToTownPortal())
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Town Portal.", cacheObject.InternalName);
                                        break;
                                    }

                                    // Need to Prioritize, forget it!
                                    if (prioritizeCloseRangeUnits)
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format(
                                                "Ignoring {0} - We seem to be stuck *OR* if not ranged and currently rooted ",
                                                cacheObject.InternalName);
                                        break;
                                    }
                                }

                                float maxRange = Settings.WorldObject.ContainerOpenRange;
                                var isRare = cacheObject.InternalName.ToLower().Contains("chest_rare") ||
                                             DataDictionary.ResplendentChestIds.Contains(cacheObject.ActorSNO);
                                if (isRare)
                                    maxRange = 250f;
                                if (cacheObject.Distance > maxRange)
                                {
                                    cacheObject.WeightInfo +=
                                        string.Format("Ignoring {0} - Too Far away. ", cacheObject.InternalName);
                                    break;
                                }

                                if (Legendary.HarringtonWaistguard.IsEquipped)
                                {
                                    if (Legendary.HarringtonWaistguard.IsBuffActive)
                                    {
                                        cacheObject.WeightInfo +=
                                            string.Format("Ignoring {0} - Harring Buff is Already up. ",
                                                cacheObject.InternalName);
                                        break;
                                    }
                                    cacheObject.Weight += 1000d;
                                }
                                cacheObject.Weight += ObjectDistanceFormula(cacheObject) +
                                                      LastTargetFormula(cacheObject) +
                                                      EliteMonsterNearFormula(cacheObject, elites) -
                                                      PackDensityFormula(cacheObject) +
                                                      AoENearFormula(cacheObject) +
                                                      AoEInPathFormula(cacheObject);
                                break;
                            }

                                #endregion

                        }

                        cacheObject.WeightInfo += cacheObject.IsNPC ? " IsNPC" : "";
                        cacheObject.WeightInfo += cacheObject.NPCIsOperable ? " IsOperable" : "";

                        Logger.Log(TrinityLogLevel.Debug, LogCategory.Weight,
                            "Weight={0:0} name={1} sno={2} type={3} R-Dist={4:0} IsElite={5} RAGuid={6} {7}",
                            cacheObject.Weight, cacheObject.InternalName, cacheObject.ActorSNO, cacheObject.Type,
                            cacheObject.RadiusDistance, cacheObject.IsEliteRareUnique,
                            cacheObject.RActorGuid, cacheObject.WeightInfo);
                        cacheObject.WeightInfo = cacheObject.WeightInfo;

                        // Use the highest weight, and if at max weight, the closest
                        bool pickNewTarget = cacheObject.Weight > 0 &&
                                             (cacheObject.Weight > HighestWeightFound ||
                                              (cacheObject.Weight == HighestWeightFound &&
                                               cacheObject.Distance < CurrentTarget.Distance));

                        if (!pickNewTarget) continue;
                        CurrentTarget = cacheObject;
                        HighestWeightFound = cacheObject.Weight;
                    }

                    // Set Record History
                    if (CurrentTarget != null && CurrentTarget.InternalName != null && CurrentTarget.ActorSNO > 0 &&
                        CurrentTarget.RActorGuid != LastTargetRactorGUID ||
                        CurrentTarget != null && CurrentTarget.IsMarker)
                    {
                        RecordTargetHistory();
                        Logger.Log(TrinityLogLevel.Debug, LogCategory.UserInformation,
                            "Target changed to {0} // {1} ({2}) {3}", CurrentTarget.ActorSNO, CurrentTarget.InternalName,
                            CurrentTarget.Type, CurrentTarget.WeightInfo);
                    }
                }
            }

            public static void RecordTargetHistory()
            {
                int timesBeenPrimaryTarget;

                string objectKey = CurrentTarget.Type.ToString() + CurrentTarget.Position + CurrentTarget.InternalName +
                                   CurrentTarget.ItemLevel + CurrentTarget.ItemQuality + CurrentTarget.HitPoints;

                if (CacheData.PrimaryTargetCount.TryGetValue(objectKey, out timesBeenPrimaryTarget))
                {
                    timesBeenPrimaryTarget++;
                    CacheData.PrimaryTargetCount[objectKey] = timesBeenPrimaryTarget;
                    CurrentTarget.TimesBeenPrimaryTarget = timesBeenPrimaryTarget;
                    CurrentTarget.HasBeenPrimaryTarget = true;

                    bool isEliteLowHealth = CurrentTarget.HitPointsPct <= 0.75 && CurrentTarget.IsBossOrEliteRareUnique;
                    bool isLegendaryItem = CurrentTarget.Type == TrinityObjectType.Item &&
                                           CurrentTarget.ItemQuality >= ItemQuality.Legendary;

                    bool isHoradricRelic = ((CurrentTarget.InternalName.ToLower().Contains("horadricrelic") || CurrentTarget.TrinityItemType == TrinityItemType.HoradricRelic) &&
                                            CurrentTarget.TimesBeenPrimaryTarget > 15);

                    if ((!CurrentTarget.IsBoss && CurrentTarget.TimesBeenPrimaryTarget > 50 && !isEliteLowHealth &&
                         !isLegendaryItem) || isHoradricRelic ||
                        (CurrentTarget.TimesBeenPrimaryTarget > 200 && isLegendaryItem))
                    {
                        Logger.Log(TrinityLogLevel.Info, LogCategory.UserInformation,
                            "Blacklisting target {0} ActorSnoId={1} RActorGUID={2} due to possible stuck/flipflop!",
                            CurrentTarget.InternalName, CurrentTarget.ActorSNO, CurrentTarget.RActorGuid);

                        var expires = CurrentTarget.IsMarker
                            ? DateTime.UtcNow.AddSeconds(60)
                            : DateTime.UtcNow.AddSeconds(30);

                        HandleBloodshardBlacklistStuck(isHoradricRelic);

                        // Add to generic blacklist for safety, as the RActorGUID on items and gold can change as we move away and get closer to the items (while walking around corners)
                        // So we can't use any ID's but rather have to use some data which never changes (actorSNO, position, type, worldID)
                        GenericBlacklist.AddToBlacklist(new GenericCacheObject
                        {
                            Key = CurrentTarget.ObjectHash,
                            Value = null,
                            Expires = expires
                        });
                    }
                }
                else
                {
                    // Add to Primary Target Cache Count
                    CacheData.PrimaryTargetCount.Add(objectKey, 1);
                }
            }

            /// <summary>
            /// Due to the ACDGuids etc changing for every failed pickup attempt of a blood shard
            /// Normal blacklisting will not work. this is a temporary workaround until nesox fixes the CPlayer offsets.
            /// </summary>
            private static void HandleBloodshardBlacklistStuck(bool isHoradricRelic)
            {
                // Reset counter every time we target something other than a relic
                if (!isHoradricRelic)
                {
                    _bloodshardBlacklistCount = 0;
                    return;
                }
                    
                // Increment counter every time a bloodshard is being blacklisted
                if (!IsBloodshardBlacklisted)
                {           
                    _bloodshardBlacklistCount++;

                    // After 10 sequential blacklists of shards, inact some serious business.
                    if (_bloodshardBlacklistCount >= 10)
                    {
                        Logger.Log("Bloodshard blacklist stuck detected, proper blacklisting for 1 minute");
                        BloodshardsBlacklistExpiry = DateTime.UtcNow.Add(TimeSpan.FromMinutes(1));
                    }
                }
            }

            private static int _bloodshardBlacklistCount;

            public static bool IsBloodshardBlacklisted
            {
                get { return DateTime.UtcNow > BloodshardsBlacklistExpiry; }
            }
            
            public static DateTime BloodshardsBlacklistExpiry = DateTime.MinValue;
            private static bool _riftProgressionKillAll;
        }
    }
}