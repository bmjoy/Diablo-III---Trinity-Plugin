﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trinity.Reference;
using Zeta.Game.Internals.Actors;

namespace Trinity.Combat.Abilities.PhelonsPlayground.Barbarian
{
    partial class Barbarian
    {
        internal class ZDps
        {
            public static TrinityPower PowerSelector()
            {
                TrinityCacheObject target;

                if (ShouldThreateningShout)
                    return CastThreateningShout;

                if (ShouldAncientSpear(out target))
                    return CastAncientSpear(target);

                if (ShouldRend(out target))
                    return CastRend(target);

                if (ShouldBash(out target))
                    return CastBash(target);

                if (ShouldWhirlWind(out target))
                    return CastWhirlWind(target);

                if (ShouldFuriousCharge(out target))
                    return CastFuriousCharge(target);

                return null;
            }

            public static bool ShouldRend(out TrinityCacheObject target)
            {
                target = null;

                if (!Skills.Barbarian.Rend.CanCast())
                    return false;

                target = PhelonUtils.BestAuraUnit(SNOPower.Barbarian_Rend);
                return target != null && target.Distance <= 12 && Player.PrimaryResourcePct > 0.50;
            }

            public static TrinityPower CastRend(TrinityCacheObject target)
            {
                return new TrinityPower(SNOPower.Barbarian_Rend, 12f, target.ACDGuid);
            }

            public static bool ShouldBash(out TrinityCacheObject target)
            {
                target = null;

                if (!Skills.Barbarian.Bash.CanCast() ||
                    PhelonGroupSupport.UnitsToPull(PhelonGroupSupport.Monk.Position).FirstOrDefault() != null)
                    return false;

                target =
                    PhelonUtils.SafeList()
                        .OrderBy(y => y.Distance)
                        .FirstOrDefault(x => x.IsUnit && !x.HasDebuff(SNOPower.Barbarian_Bash));
                return target != null && target.Distance <= 12;
            }

            public static TrinityPower CastBash(TrinityCacheObject target)
            {
                return new TrinityPower(SNOPower.Barbarian_Bash, 12f, target.ACDGuid);
            }


            public static bool ShouldThreateningShout
            {
                get
                {
                    return Skills.Barbarian.ThreateningShout.CanCast() &&
                           PhelonUtils.BestAuraUnit(SNOPower.Barbarian_ThreateningShout, 10) != null;
                }
            }

            public static TrinityPower CastThreateningShout
            {
                get { return new TrinityPower(SNOPower.Barbarian_ThreateningShout, 25, Player.Position); }
            }

            public static bool ShouldWhirlWind(out TrinityCacheObject target)
            {
                target = null;

                if (!Skills.Barbarian.Whirlwind.CanCast())
                    return false;

                target = PhelonGroupSupport.UnitsToPull(PhelonGroupSupport.Monk.Position).FirstOrDefault() ??
                         PhelonUtils.ClosestHealthGlobe() ??
                         PhelonGroupSupport.Monk ??
                         PhelonTargeting.BestAoeUnit(45, true);

                return target != null && target.Distance <= 90 && Player.PrimaryResource > 25;
            }

            public static TrinityPower CastWhirlWind(TrinityCacheObject target)
            {
                return new TrinityPower(SNOPower.Barbarian_Whirlwind, 75, target.ACDGuid);
            }

            public static bool ShouldFuriousCharge(out TrinityCacheObject target)
            {
                target = null;

                if (!Skills.Barbarian.FuriousCharge.CanCast())
                    return false;

                target = PhelonUtils.BestPierceOrClusterUnit(10, 38);

                return target != null && Player.PrimaryResourcePct < 0.25;
            }

            public static TrinityPower CastFuriousCharge(TrinityCacheObject target)
            {
                return new TrinityPower(SNOPower.Barbarian_FuriousCharge, 38,
                    PhelonUtils.PointBehind(target.Position));
            }

            public static bool ShouldAncientSpear(out TrinityCacheObject target)
            {
                target = null;

                if (!Skills.Barbarian.AncientSpear.CanCast())
                    return false;

                target = PhelonGroupSupport.UnitsToPull(PhelonGroupSupport.Monk.Position).FirstOrDefault();

                return target != null && target.Distance <= 60 && Player.PrimaryResourcePct > 0.90 &&
                       TimeSincePowerUse(SNOPower.X1_Barbarian_AncientSpear) > 1500;
            }

            public static TrinityPower CastAncientSpear(TrinityCacheObject target)
            {
                return new TrinityPower(SNOPower.X1_Barbarian_AncientSpear, 60f,
                    target.ACDGuid);
            }
        }
    }
}