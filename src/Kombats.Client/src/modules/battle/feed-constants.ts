/**
 * Sentinel `turnIndex` the backend assigns to end-of-battle entries so they
 * sort after every real turn (see `NarrationPipeline.GenerateBattleEndFeed`
 * in `Kombats.Bff.Application`). It is an ordering key, not a user-visible
 * turn number — presentation must not render it as "T2147483647".
 */
export const END_OF_BATTLE_TURN_INDEX = 2147483647;
