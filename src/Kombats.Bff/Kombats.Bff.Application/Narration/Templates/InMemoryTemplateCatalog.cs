using Kombats.Bff.Application.Narration.Feed;

namespace Kombats.Bff.Application.Narration.Templates;

/// <summary>
/// Static registry of V1 narration templates organized by category.
/// </summary>
public sealed class InMemoryTemplateCatalog : ITemplateCatalog
{
    private static readonly Dictionary<string, List<NarrationTemplate>> Templates = BuildTemplates();

    public IReadOnlyList<NarrationTemplate> GetTemplates(string category)
    {
        return Templates.TryGetValue(category, out var list) ? list : [];
    }

    public IReadOnlyList<string> GetCategories()
    {
        return Templates.Keys.ToList();
    }

    private static Dictionary<string, List<NarrationTemplate>> BuildTemplates()
    {
        var templates = new Dictionary<string, List<NarrationTemplate>>(StringComparer.OrdinalIgnoreCase);

        void Add(string category, string template, FeedEntryTone tone, FeedEntrySeverity severity = FeedEntrySeverity.Normal)
        {
            if (!templates.TryGetValue(category, out var list))
            {
                list = [];
                templates[category] = list;
            }
            list.Add(new NarrationTemplate { Category = category, Template = template, Tone = tone, Severity = severity });
        }

        // attack.hit (5)
        Add("attack.hit", "{attackerName} lands a solid hit on {defenderName} for {damage} damage!", FeedEntryTone.Neutral);
        Add("attack.hit", "{attackerName} strikes {defenderName} in the {attackZone} for {damage} damage!", FeedEntryTone.Neutral);
        Add("attack.hit", "{attackerName} connects with a clean blow to {defenderName}'s {attackZone}! {damage} damage dealt.", FeedEntryTone.Aggressive);
        Add("attack.hit", "A swift attack from {attackerName} catches {defenderName} off guard. {damage} damage!", FeedEntryTone.Neutral);
        Add("attack.hit", "{attackerName} punishes {defenderName} with a {attackZone} strike for {damage} damage!", FeedEntryTone.Aggressive);

        // attack.crit (4)
        Add("attack.crit", "CRITICAL HIT! {attackerName} devastates {defenderName} for {damage} damage!", FeedEntryTone.Aggressive, FeedEntrySeverity.Important);
        Add("attack.crit", "{attackerName} finds a weak spot! Critical strike to {defenderName}'s {attackZone} for {damage} damage!", FeedEntryTone.Aggressive, FeedEntrySeverity.Important);
        Add("attack.crit", "A perfectly placed blow from {attackerName}! {defenderName} takes {damage} critical damage!", FeedEntryTone.Aggressive, FeedEntrySeverity.Important);
        Add("attack.crit", "{attackerName} unleashes a devastating critical attack on {defenderName}! {damage} damage!", FeedEntryTone.Aggressive, FeedEntrySeverity.Important);

        // attack.dodge (4)
        Add("attack.dodge", "{defenderName} nimbly dodges {attackerName}'s attack!", FeedEntryTone.Defensive);
        Add("attack.dodge", "{attackerName} swings at {defenderName} but misses completely!", FeedEntryTone.Defensive);
        Add("attack.dodge", "{defenderName} sidesteps the incoming blow from {attackerName}!", FeedEntryTone.Defensive);
        Add("attack.dodge", "Quick reflexes! {defenderName} evades {attackerName}'s strike!", FeedEntryTone.Defensive);

        // attack.block (4)
        Add("attack.block", "{defenderName} blocks {attackerName}'s attack with a solid guard!", FeedEntryTone.Defensive);
        Add("attack.block", "{attackerName}'s strike is deflected by {defenderName}'s {blockZone} block!", FeedEntryTone.Defensive);
        Add("attack.block", "{defenderName} raises a wall of defense against {attackerName}!", FeedEntryTone.Defensive);
        Add("attack.block", "{attackerName} can't break through {defenderName}'s guard!", FeedEntryTone.Defensive);

        // attack.no_action (3)
        Add("attack.no_action", "{attackerName} hesitates and takes no action.", FeedEntryTone.Neutral);
        Add("attack.no_action", "{attackerName} stands idle this turn.", FeedEntryTone.Neutral);
        Add("attack.no_action", "{attackerName} fails to act in time!", FeedEntryTone.Neutral);

        // battle.start (3)
        Add("battle.start", "The battle between {playerAName} and {playerBName} begins!", FeedEntryTone.System);
        Add("battle.start", "{playerAName} and {playerBName} face off! Let the combat begin!", FeedEntryTone.System);
        Add("battle.start", "Fighters ready! {playerAName} vs {playerBName}!", FeedEntryTone.System);

        // battle.end.victory (3)
        Add("battle.end.victory", "{winnerName} claims victory over {loserName}!", FeedEntryTone.Dramatic, FeedEntrySeverity.Critical);
        Add("battle.end.victory", "The battle is over! {winnerName} defeats {loserName}!", FeedEntryTone.Dramatic, FeedEntrySeverity.Critical);
        Add("battle.end.victory", "{winnerName} stands triumphant! {loserName} has been vanquished!", FeedEntryTone.Dramatic, FeedEntrySeverity.Critical);

        // battle.end.draw (2)
        Add("battle.end.draw", "The battle ends in a draw! Neither fighter could claim victory.", FeedEntryTone.Dramatic, FeedEntrySeverity.Critical);
        Add("battle.end.draw", "A stalemate! Both fighters are evenly matched and the battle ends without a winner.", FeedEntryTone.Dramatic, FeedEntrySeverity.Critical);

        // battle.end.forfeit (2)
        Add("battle.end.forfeit", "Both fighters have forfeited. The battle ends with no winner.", FeedEntryTone.System, FeedEntrySeverity.Important);
        Add("battle.end.forfeit", "Double forfeit! Neither combatant chose to fight.", FeedEntryTone.System, FeedEntrySeverity.Important);

        // defeat.knockout (3)
        Add("defeat.knockout", "{loserName} collapses! Knocked out by {winnerName}!", FeedEntryTone.Dramatic, FeedEntrySeverity.Critical);
        Add("defeat.knockout", "{loserName}'s HP reaches zero! {winnerName} delivers the final blow!", FeedEntryTone.Dramatic, FeedEntrySeverity.Critical);
        Add("defeat.knockout", "{loserName} can fight no more! {winnerName} wins by knockout!", FeedEntryTone.Dramatic, FeedEntrySeverity.Critical);

        // commentary.first_blood (3)
        Add("commentary.first_blood", "First blood! The battle's first wound has been dealt!", FeedEntryTone.Flavor, FeedEntrySeverity.Important);
        Add("commentary.first_blood", "The first blow lands! Blood has been drawn!", FeedEntryTone.Flavor, FeedEntrySeverity.Important);
        Add("commentary.first_blood", "And we have first blood! The fight is truly underway!", FeedEntryTone.Flavor, FeedEntrySeverity.Important);

        // commentary.mutual_miss (2)
        Add("commentary.mutual_miss", "Both fighters whiff! A turn of missed opportunities.", FeedEntryTone.Flavor);
        Add("commentary.mutual_miss", "Neither attack connects! The fighters circle each other cautiously.", FeedEntryTone.Flavor);

        // commentary.stalemate (2)
        Add("commentary.stalemate", "Both fighters stand idle. The tension builds...", FeedEntryTone.Flavor);
        Add("commentary.stalemate", "A turn of inaction from both combatants. Are they strategizing?", FeedEntryTone.Flavor);

        // commentary.near_death (3)
        Add("commentary.near_death", "{attackerName} is on the ropes with only {remainingHp} HP remaining!", FeedEntryTone.Flavor, FeedEntrySeverity.Important);
        Add("commentary.near_death", "Dangerously low! {attackerName} clings to life at {remainingHp} HP!", FeedEntryTone.Flavor, FeedEntrySeverity.Important);
        Add("commentary.near_death", "{attackerName} is one blow away from defeat! Only {remainingHp} HP left!", FeedEntryTone.Flavor, FeedEntrySeverity.Important);

        // commentary.big_hit (3)
        Add("commentary.big_hit", "What a massive hit! That attack took a huge chunk of HP!", FeedEntryTone.Flavor, FeedEntrySeverity.Important);
        Add("commentary.big_hit", "A devastating blow! That's going to leave a mark!", FeedEntryTone.Flavor, FeedEntrySeverity.Important);
        Add("commentary.big_hit", "Incredible power behind that strike! A punishing blow!", FeedEntryTone.Flavor, FeedEntrySeverity.Important);

        // commentary.knockout (3)
        Add("commentary.knockout", "And it's over! What a knockout finish!", FeedEntryTone.Flavor, FeedEntrySeverity.Critical);
        Add("commentary.knockout", "Knockout! The final blow seals the deal!", FeedEntryTone.Flavor, FeedEntrySeverity.Critical);
        Add("commentary.knockout", "Down and out! An emphatic end to this battle!", FeedEntryTone.Flavor, FeedEntrySeverity.Critical);

        // commentary.draw (2)
        Add("commentary.draw", "What an even match! Neither fighter could gain the upper hand.", FeedEntryTone.Flavor, FeedEntrySeverity.Important);
        Add("commentary.draw", "Equally matched from start to finish! A true contest of equals.", FeedEntryTone.Flavor, FeedEntrySeverity.Important);

        return templates;
    }
}
