using Kombats.Battle.Realtime.Contracts;
using Kombats.Bff.Application.Narration.Feed;

namespace Kombats.Bff.Application.Narration;

/// <summary>
/// Implements 7 commentator triggers with max-fire limits and anti-spam (max 1 per turn).
/// Priority order (highest first): near-death > big-hit > first-blood > mutual-miss > stalemate > double-dodge.
/// </summary>
public sealed class DefaultCommentatorPolicy : ICommentatorPolicy
{
    private const decimal NearDeathThreshold = 0.25m;
    private const decimal BigHitThreshold = 0.50m;
    private const int MaxBigHitFires = 2;

    public CommentatorCue? Evaluate(
        TurnResolutionLogRealtime turnLog,
        BattleParticipantSnapshot participants,
        CommentatorState state,
        int? playerAHp, int? playerBHp,
        int? playerAMaxHp, int? playerBMaxHp)
    {
        // Priority 1: Near death (any player below 25% max HP)
        var nearDeath = CheckNearDeath(turnLog, participants, state, playerAHp, playerBHp, playerAMaxHp, playerBMaxHp);
        if (nearDeath is not null) return nearDeath;

        // Priority 2: Big hit (single attack > 50% target max HP)
        var bigHit = CheckBigHit(turnLog, participants, state, playerAMaxHp, playerBMaxHp);
        if (bigHit is not null) return bigHit;

        // Priority 3: First blood (first turn with any damage > 0)
        var firstBlood = CheckFirstBlood(turnLog, participants, state);
        if (firstBlood is not null) return firstBlood;

        // Priority 4: Double dodge (both attacks dodged same turn)
        var doubleDodge = CheckDoubleDodge(turnLog, participants, state);
        if (doubleDodge is not null) return doubleDodge;

        // Priority 5: Mutual miss / stalemate
        var stalemate = CheckStalemate(turnLog, participants, state);
        if (stalemate is not null) return stalemate;

        return null;
    }

    public CommentatorCue? EvaluateBattleEnd(
        BattleEndReasonRealtime reason,
        Guid? winnerPlayerId,
        BattleParticipantSnapshot participants,
        CommentatorState state)
    {
        if (reason == BattleEndReasonRealtime.Normal && winnerPlayerId.HasValue && !state.KnockoutFired)
        {
            state.KnockoutFired = true;
            return new CommentatorCue
            {
                Category = "commentary.knockout",
                Kind = FeedEntryKind.CommentaryKnockout,
                Context = new NarrationContext
                {
                    WinnerName = participants.ResolveName(winnerPlayerId.Value),
                    PlayerAName = participants.ResolveName(participants.PlayerAId),
                    PlayerBName = participants.ResolveName(participants.PlayerBId)
                }
            };
        }

        if ((reason == BattleEndReasonRealtime.DoubleForfeit ||
             (reason == BattleEndReasonRealtime.Normal && !winnerPlayerId.HasValue))
            && !state.DrawFired)
        {
            state.DrawFired = true;
            return new CommentatorCue
            {
                Category = "commentary.draw",
                Kind = FeedEntryKind.CommentaryDraw,
                Context = new NarrationContext
                {
                    PlayerAName = participants.ResolveName(participants.PlayerAId),
                    PlayerBName = participants.ResolveName(participants.PlayerBId)
                }
            };
        }

        return null;
    }

    private static CommentatorCue? CheckNearDeath(
        TurnResolutionLogRealtime turnLog,
        BattleParticipantSnapshot participants,
        CommentatorState state,
        int? playerAHp, int? playerBHp,
        int? playerAMaxHp, int? playerBMaxHp)
    {
        // Check Player A near death.
        // Suppress when A is dying on this turn — defeat.knockout already covers
        // the killing blow, and pre-turn HP would render misleadingly (e.g.
        // "Only 2 HP left!" about a player who just hit zero).
        if (!state.NearDeathPlayerAFired && playerAHp.HasValue && playerAMaxHp.HasValue && playerAMaxHp.Value > 0)
        {
            var playerAPostTurnHp = playerAHp.Value - turnLog.BtoA.Damage;
            if (playerAPostTurnHp > 0 && (decimal)playerAHp.Value / playerAMaxHp.Value < NearDeathThreshold)
            {
                state.NearDeathPlayerAFired = true;
                return new CommentatorCue
                {
                    Category = "commentary.near_death",
                    Kind = FeedEntryKind.CommentaryNearDeath,
                    Context = new NarrationContext
                    {
                        AttackerName = participants.ResolveName(participants.PlayerAId),
                        RemainingHp = playerAHp.Value,
                        MaxHp = playerAMaxHp.Value,
                        PlayerAName = participants.ResolveName(participants.PlayerAId),
                        PlayerBName = participants.ResolveName(participants.PlayerBId)
                    }
                };
            }
        }

        // Check Player B near death (symmetric)
        if (!state.NearDeathPlayerBFired && playerBHp.HasValue && playerBMaxHp.HasValue && playerBMaxHp.Value > 0)
        {
            var playerBPostTurnHp = playerBHp.Value - turnLog.AtoB.Damage;
            if (playerBPostTurnHp > 0 && (decimal)playerBHp.Value / playerBMaxHp.Value < NearDeathThreshold)
            {
                state.NearDeathPlayerBFired = true;
                return new CommentatorCue
                {
                    Category = "commentary.near_death",
                    Kind = FeedEntryKind.CommentaryNearDeath,
                    Context = new NarrationContext
                    {
                        AttackerName = participants.ResolveName(participants.PlayerBId),
                        RemainingHp = playerBHp.Value,
                        MaxHp = playerBMaxHp.Value,
                        PlayerAName = participants.ResolveName(participants.PlayerAId),
                        PlayerBName = participants.ResolveName(participants.PlayerBId)
                    }
                };
            }
        }

        return null;
    }

    private static CommentatorCue? CheckBigHit(
        TurnResolutionLogRealtime turnLog,
        BattleParticipantSnapshot participants,
        CommentatorState state,
        int? playerAMaxHp, int? playerBMaxHp)
    {
        if (state.BigHitCount >= MaxBigHitFires) return null;

        // A→B: attacker is A, defender (target) is B, compare damage vs B's max HP
        if (turnLog.AtoB.Damage > 0 && playerBMaxHp.HasValue && playerBMaxHp.Value > 0)
        {
            if ((decimal)turnLog.AtoB.Damage / playerBMaxHp.Value > BigHitThreshold)
            {
                state.BigHitCount++;
                return new CommentatorCue
                {
                    Category = "commentary.big_hit",
                    Kind = FeedEntryKind.CommentaryBigHit,
                    Context = new NarrationContext
                    {
                        AttackerName = participants.ResolveName(turnLog.AtoB.AttackerId),
                        DefenderName = participants.ResolveName(turnLog.AtoB.DefenderId),
                        Damage = turnLog.AtoB.Damage,
                        PlayerAName = participants.ResolveName(participants.PlayerAId),
                        PlayerBName = participants.ResolveName(participants.PlayerBId)
                    }
                };
            }
        }

        // B→A: attacker is B, defender (target) is A, compare damage vs A's max HP
        if (turnLog.BtoA.Damage > 0 && playerAMaxHp.HasValue && playerAMaxHp.Value > 0)
        {
            if ((decimal)turnLog.BtoA.Damage / playerAMaxHp.Value > BigHitThreshold)
            {
                state.BigHitCount++;
                return new CommentatorCue
                {
                    Category = "commentary.big_hit",
                    Kind = FeedEntryKind.CommentaryBigHit,
                    Context = new NarrationContext
                    {
                        AttackerName = participants.ResolveName(turnLog.BtoA.AttackerId),
                        DefenderName = participants.ResolveName(turnLog.BtoA.DefenderId),
                        Damage = turnLog.BtoA.Damage,
                        PlayerAName = participants.ResolveName(participants.PlayerAId),
                        PlayerBName = participants.ResolveName(participants.PlayerBId)
                    }
                };
            }
        }

        return null;
    }

    private static CommentatorCue? CheckFirstBlood(
        TurnResolutionLogRealtime turnLog,
        BattleParticipantSnapshot participants,
        CommentatorState state)
    {
        if (state.FirstBloodFired) return null;

        if (turnLog.AtoB.Damage > 0 || turnLog.BtoA.Damage > 0)
        {
            state.FirstBloodFired = true;
            return new CommentatorCue
            {
                Category = "commentary.first_blood",
                Kind = FeedEntryKind.CommentaryFirstBlood,
                Context = new NarrationContext
                {
                    PlayerAName = participants.ResolveName(participants.PlayerAId),
                    PlayerBName = participants.ResolveName(participants.PlayerBId)
                }
            };
        }

        return null;
    }

    private static CommentatorCue? CheckDoubleDodge(
        TurnResolutionLogRealtime turnLog,
        BattleParticipantSnapshot participants,
        CommentatorState state)
    {
        if (state.DoubleDodgeFired) return null;

        if (turnLog.AtoB.Outcome == AttackOutcomeRealtime.Dodged &&
            turnLog.BtoA.Outcome == AttackOutcomeRealtime.Dodged)
        {
            state.DoubleDodgeFired = true;
            return new CommentatorCue
            {
                Category = "commentary.mutual_miss",
                Kind = FeedEntryKind.CommentaryMutualMiss,
                Context = new NarrationContext
                {
                    PlayerAName = participants.ResolveName(participants.PlayerAId),
                    PlayerBName = participants.ResolveName(participants.PlayerBId)
                }
            };
        }

        return null;
    }

    private static CommentatorCue? CheckStalemate(
        TurnResolutionLogRealtime turnLog,
        BattleParticipantSnapshot participants,
        CommentatorState state)
    {
        if (state.DoubleNoActionFired) return null;

        if (turnLog.AtoB.Outcome == AttackOutcomeRealtime.NoAction &&
            turnLog.BtoA.Outcome == AttackOutcomeRealtime.NoAction)
        {
            state.DoubleNoActionFired = true;
            return new CommentatorCue
            {
                Category = "commentary.stalemate",
                Kind = FeedEntryKind.CommentaryStalemate,
                Context = new NarrationContext
                {
                    PlayerAName = participants.ResolveName(participants.PlayerAId),
                    PlayerBName = participants.ResolveName(participants.PlayerBId)
                }
            };
        }

        return null;
    }
}
