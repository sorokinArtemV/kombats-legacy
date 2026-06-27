import femaleArcher from '@/ui/assets/fighters/female_archer.png';
import femaleNinja from '@/ui/assets/fighters/female_ninja.png';
import ronin from '@/ui/assets/fighters/ronin.png';
import shadowAssassin from '@/ui/assets/fighters/shadow_assassin.png';
import shadowOni from '@/ui/assets/fighters/shadow_oni.png';
import silhouette from '@/ui/assets/silhouette.png';

export const AVATAR_IDS = [
  'female_archer',
  'female_ninja',
  'ronin',
  'shadow_assassin',
  'shadow_oni',
  'silhouette',
] as const;

export type AvatarId = (typeof AVATAR_IDS)[number];

export const DEFAULT_AVATAR_ID: AvatarId = 'shadow_oni';

const AVATAR_ASSETS: Record<AvatarId, string> = {
  female_archer: femaleArcher,
  female_ninja: femaleNinja,
  ronin,
  shadow_assassin: shadowAssassin,
  shadow_oni: shadowOni,
  silhouette,
};

export interface SelectableAvatar {
  id: AvatarId;
  name: string;
  // Focal point for `object-position` when cropping the portrait into a small
  // card — each piece of source art has its head sit at a slightly different
  // height, so the crop has to follow.
  focal: string;
}

// Player-facing skin choices for the onboarding avatar grid. `silhouette` is
// reserved for the body-zone selector mask and is excluded.
export const SELECTABLE_AVATARS: readonly SelectableAvatar[] = [
  { id: 'ronin', name: 'Takeshi', focal: '50% 10%' },
  { id: 'shadow_oni', name: 'Shadow', focal: '50% 8%' },
  { id: 'shadow_assassin', name: 'Raiden', focal: '50% 10%' },
  { id: 'female_ninja', name: 'Akemi', focal: '50% 12%' },
  { id: 'female_archer', name: 'Kasumi', focal: '50% 12%' },
];

export function getAvatarAsset(avatarId: string | null | undefined): string {
  if (!avatarId) return AVATAR_ASSETS[DEFAULT_AVATAR_ID];
  return AVATAR_ASSETS[avatarId as AvatarId] ?? AVATAR_ASSETS[DEFAULT_AVATAR_ID];
}
