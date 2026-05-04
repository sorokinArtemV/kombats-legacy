using Kombats.Battle.Realtime.Contracts;
using Kombats.Bff.Application.Narration.Feed;
using Kombats.Bff.Application.Narration.Templates;

namespace Kombats.Bff.Application.Narration;

/// <summary>
/// Orchestrates template selection, rendering, commentator evaluation, and feed assembly
/// for each battle event. All operations are deterministic given the same inputs.
/// </summary>
public sealed class NarrationPipeline : INarrationPipeline
{
    private readonly ITemplateCatalog _catalog;
    private readonly ITemplateSelector _selector;
    private readonly INarrationRenderer _renderer;
    private readonly ICommentatorPolicy _commentator;
    private readonly IFeedAssembler _assembler;

    public NarrationPipeline(
        ITemplateCatalog catalog,
        ITemplateSelector selector,
        INarrationRenderer renderer,
        ICommentatorPolicy commentator,
        IFeedAssembler assembler)
    {
        _catalog = catalog;
        _selector = selector;
        _renderer = renderer;
        _commentator = commentator;
        _assembler = assembler;
    }

    public BattleFeedUpdate GenerateTurnFeed(
        Guid battleId,
        TurnResolvedRealtime turnResolved,
        BattleParticipantSnapshot participants,
        CommentatorState commentatorState,
        int? playerAHp, int? playerBHp,
        int? playerAMaxHp, int? playerBMaxHp)
    {
        var entries = new List<BattleFeedEntry>();
        var turnIndex = turnResolved.TurnIndex;

        if (turnResolved.Log is not null)
        {
            // Sequence 0: A→B attack narration
            var atoBEntry = RenderAttack(battleId, turnIndex, 0, turnResolved.Log.AtoB, participants);
            entries.Add(atoBEntry);

            // Sequence 1: B→A attack narration
            var btoAEntry = RenderAttack(battleId, turnIndex, 1, turnResolved.Log.BtoA, participants);
            entries.Add(btoAEntry);

            // Sequence 2: Commentary (if triggered)
            var cue = _commentator.Evaluate(
                turnResolved.Log, participants, commentatorState,
                playerAHp, playerBHp, playerAMaxHp, playerBMaxHp);
            if (cue is not null)
            {
                var commentaryEntry = RenderCommentary(battleId, turnIndex, 2, cue);
                entries.Add(commentaryEntry);
            }
        }

        return _assembler.CreateUpdate(battleId, entries);
    }

    public BattleFeedUpdate GenerateBattleStartFeed(
        Guid battleId,
        BattleParticipantSnapshot participants)
    {
        var context = new NarrationContext
        {
            PlayerAName = participants.ResolveName(participants.PlayerAId),
            PlayerBName = participants.ResolveName(participants.PlayerBId)
        };

        var templates = _catalog.GetTemplates("battle.start");
        var template = _selector.Select(templates, battleId, 0, 0);
        var text = _renderer.Render(template.Template, context.ToPlaceholders());
        var entry = _assembler.CreateEntry(battleId, 0, 0, FeedEntryKind.BattleStart, template, text);

        return _assembler.CreateUpdate(battleId, [entry]);
    }

    public BattleFeedUpdate GenerateBattleEndFeed(
        Guid battleId,
        BattleEndedRealtime ended,
        BattleParticipantSnapshot participants,
        CommentatorState commentatorState)
    {
        var entries = new List<BattleFeedEntry>();
        // Use turnIndex int.MaxValue for end-of-battle entries to sort after all turns
        var turnIndex = int.MaxValue;

        if (ended.Reason == BattleEndReasonRealtime.Normal && ended.WinnerPlayerId.HasValue)
        {
            var winnerId = ended.WinnerPlayerId.Value;
            var loserId = participants.GetOpponentId(winnerId);

            // Sequence 0: PlayerDefeated
            var defeatContext = new NarrationContext
            {
                WinnerName = participants.ResolveName(winnerId),
                LoserName = participants.ResolveName(loserId),
                PlayerAName = participants.ResolveName(participants.PlayerAId),
                PlayerBName = participants.ResolveName(participants.PlayerBId)
            };
            var defeatTemplates = _catalog.GetTemplates("defeat.knockout");
            var defeatTemplate = _selector.Select(defeatTemplates, battleId, turnIndex, 0);
            var defeatText = _renderer.Render(defeatTemplate.Template, defeatContext.ToPlaceholders());
            entries.Add(_assembler.CreateEntry(battleId, turnIndex, 0, FeedEntryKind.DefeatKnockout, defeatTemplate, defeatText));

            // Sequence 1: BattleEnd result
            var endContext = defeatContext;
            var endTemplates = _catalog.GetTemplates("battle.end.victory");
            var endTemplate = _selector.Select(endTemplates, battleId, turnIndex, 1);
            var endText = _renderer.Render(endTemplate.Template, endContext.ToPlaceholders());
            entries.Add(_assembler.CreateEntry(battleId, turnIndex, 1, FeedEntryKind.BattleEndVictory, endTemplate, endText));
        }
        else if (ended.Reason == BattleEndReasonRealtime.DoubleForfeit)
        {
            // Sequence 0: Forfeit entry
            var forfeitContext = new NarrationContext
            {
                PlayerAName = participants.ResolveName(participants.PlayerAId),
                PlayerBName = participants.ResolveName(participants.PlayerBId)
            };
            var forfeitTemplates = _catalog.GetTemplates("battle.end.forfeit");
            var forfeitTemplate = _selector.Select(forfeitTemplates, battleId, turnIndex, 0);
            var forfeitText = _renderer.Render(forfeitTemplate.Template, forfeitContext.ToPlaceholders());
            entries.Add(_assembler.CreateEntry(battleId, turnIndex, 0, FeedEntryKind.BattleEndForfeit, forfeitTemplate, forfeitText));

            // Sequence 1: Draw result
            var drawTemplates = _catalog.GetTemplates("battle.end.draw");
            var drawTemplate = _selector.Select(drawTemplates, battleId, turnIndex, 1);
            var drawText = _renderer.Render(drawTemplate.Template, forfeitContext.ToPlaceholders());
            entries.Add(_assembler.CreateEntry(battleId, turnIndex, 1, FeedEntryKind.BattleEndDraw, drawTemplate, drawText));
        }
        else
        {
            // Other end reasons (timeout, cancelled, etc.)
            var context = new NarrationContext
            {
                PlayerAName = participants.ResolveName(participants.PlayerAId),
                PlayerBName = participants.ResolveName(participants.PlayerBId)
            };
            var drawTemplates = _catalog.GetTemplates("battle.end.draw");
            var drawTemplate = _selector.Select(drawTemplates, battleId, turnIndex, 0);
            var drawText = _renderer.Render(drawTemplate.Template, context.ToPlaceholders());
            entries.Add(_assembler.CreateEntry(battleId, turnIndex, 0, FeedEntryKind.BattleEndDraw, drawTemplate, drawText));
        }

        // Sequence 2: Commentary (if triggered)
        var cue = _commentator.EvaluateBattleEnd(ended.Reason, ended.WinnerPlayerId, participants, commentatorState);
        if (cue is not null)
        {
            var commentaryEntry = RenderCommentary(battleId, turnIndex, 2, cue);
            entries.Add(commentaryEntry);
        }

        return _assembler.CreateUpdate(battleId, entries);
    }

    public BattleFeedEntry[] GenerateFullBattleFeed(BattleHistory history)
    {
        var participants = new BattleParticipantSnapshot(
            history.PlayerAId, history.PlayerBId,
            history.PlayerAName, history.PlayerBName);

        var commentatorState = new CommentatorState();
        var allEntries = new List<BattleFeedEntry>();

        // Battle start
        var startFeed = GenerateBattleStartFeed(history.BattleId, participants);
        allEntries.AddRange(startFeed.Entries);

        // Track HP through turns
        int? playerAHp = history.PlayerAMaxHp;
        int? playerBHp = history.PlayerBMaxHp;

        foreach (var turn in history.Turns.OrderBy(t => t.TurnIndex))
        {
            var turnLog = MapTurnToLog(history, turn);
            var turnResolved = new TurnResolvedRealtime
            {
                BattleId = history.BattleId,
                TurnIndex = turn.TurnIndex,
                PlayerAAction = turn.AtoBOutcome == "NoAction" ? "NoAction" : "Attack",
                PlayerBAction = turn.BtoAOutcome == "NoAction" ? "NoAction" : "Attack",
                Log = turnLog
            };

            // Use previous-turn HP for commentator triggers (matches live behavior where
            // BattleStateUpdated arrives after TurnResolved, so the pipeline sees pre-turn HP)
            var turnFeed = GenerateTurnFeed(
                history.BattleId, turnResolved, participants, commentatorState,
                playerAHp, playerBHp,
                history.PlayerAMaxHp, history.PlayerBMaxHp);

            allEntries.AddRange(turnFeed.Entries);

            // Update HP to post-turn values for next iteration
            playerAHp = turn.PlayerAHpAfter;
            playerBHp = turn.PlayerBHpAfter;
        }

        // Battle end (if ended)
        if (history.EndReason is not null)
        {
            var endReason = ParseEndReason(history.EndReason);
            var ended = new BattleEndedRealtime
            {
                BattleId = history.BattleId,
                Reason = endReason,
                WinnerPlayerId = history.WinnerPlayerId,
                EndedAt = DateTimeOffset.MinValue // Not used in narration
            };

            var endFeed = GenerateBattleEndFeed(history.BattleId, ended, participants, commentatorState);
            allEntries.AddRange(endFeed.Entries);
        }

        return allEntries.ToArray();
    }

    private BattleFeedEntry RenderAttack(
        Guid battleId,
        int turnIndex,
        int sequence,
        AttackResolutionRealtime attack,
        BattleParticipantSnapshot participants)
    {
        var (category, kind) = ResolveAttackCategory(attack);
        var context = new NarrationContext
        {
            AttackerName = participants.ResolveName(attack.AttackerId),
            DefenderName = participants.ResolveName(attack.DefenderId),
            AttackZone = attack.AttackZone,
            BlockZone = attack.DefenderBlockPrimary,
            Damage = attack.Damage,
            PlayerAName = participants.ResolveName(participants.PlayerAId),
            PlayerBName = participants.ResolveName(participants.PlayerBId)
        };

        var templates = _catalog.GetTemplates(category);
        var template = _selector.Select(templates, battleId, turnIndex, sequence);
        var text = _renderer.Render(template.Template, context.ToPlaceholders());

        return _assembler.CreateEntry(battleId, turnIndex, sequence, kind, template, text);
    }

    private BattleFeedEntry RenderCommentary(Guid battleId, int turnIndex, int sequence, CommentatorCue cue)
    {
        var templates = _catalog.GetTemplates(cue.Category);
        var template = _selector.Select(templates, battleId, turnIndex, sequence);
        var text = _renderer.Render(template.Template, cue.Context.ToPlaceholders());

        return _assembler.CreateEntry(battleId, turnIndex, sequence, cue.Kind, template, text);
    }

    private static (string category, FeedEntryKind kind) ResolveAttackCategory(AttackResolutionRealtime attack)
    {
        return attack.Outcome switch
        {
            AttackOutcomeRealtime.NoAction => ("attack.no_action", FeedEntryKind.AttackNoAction),
            AttackOutcomeRealtime.Dodged => ("attack.dodge", FeedEntryKind.AttackDodge),
            AttackOutcomeRealtime.Blocked => ("attack.block", FeedEntryKind.AttackBlock),
            AttackOutcomeRealtime.Hit => ("attack.hit", FeedEntryKind.AttackHit),
            AttackOutcomeRealtime.CriticalHit or
            AttackOutcomeRealtime.CriticalBypassBlock or
            AttackOutcomeRealtime.CriticalHybridBlocked => ("attack.crit", FeedEntryKind.AttackCrit),
            _ => ("attack.hit", FeedEntryKind.AttackHit)
        };
    }

    private static TurnResolutionLogRealtime MapTurnToLog(BattleHistory history, BattleHistoryTurn turn)
    {
        return new TurnResolutionLogRealtime
        {
            BattleId = history.BattleId,
            TurnIndex = turn.TurnIndex,
            AtoB = new AttackResolutionRealtime
            {
                AttackerId = history.PlayerAId,
                DefenderId = history.PlayerBId,
                TurnIndex = turn.TurnIndex,
                AttackZone = turn.AtoBAttackZone,
                DefenderBlockPrimary = turn.AtoBDefenderBlockPrimary,
                DefenderBlockSecondary = turn.AtoBDefenderBlockSecondary,
                WasBlocked = turn.AtoBWasBlocked,
                WasCrit = turn.AtoBWasCrit,
                Outcome = ParseOutcome(turn.AtoBOutcome),
                Damage = turn.AtoBDamage
            },
            BtoA = new AttackResolutionRealtime
            {
                AttackerId = history.PlayerBId,
                DefenderId = history.PlayerAId,
                TurnIndex = turn.TurnIndex,
                AttackZone = turn.BtoAAttackZone,
                DefenderBlockPrimary = turn.BtoADefenderBlockPrimary,
                DefenderBlockSecondary = turn.BtoADefenderBlockSecondary,
                WasBlocked = turn.BtoAWasBlocked,
                WasCrit = turn.BtoAWasCrit,
                Outcome = ParseOutcome(turn.BtoAOutcome),
                Damage = turn.BtoADamage
            }
        };
    }

    private static AttackOutcomeRealtime ParseOutcome(string outcome)
    {
        return Enum.TryParse<AttackOutcomeRealtime>(outcome, ignoreCase: true, out var result)
            ? result
            : AttackOutcomeRealtime.NoAction;
    }

    private static BattleEndReasonRealtime ParseEndReason(string reason)
    {
        return Enum.TryParse<BattleEndReasonRealtime>(reason, ignoreCase: true, out var result)
            ? result
            : BattleEndReasonRealtime.Unknown;
    }
}
