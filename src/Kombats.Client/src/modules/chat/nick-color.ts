const NICK_COLORS = [
  '#7EB8D4', // Steel Blue
  '#7EC48A', // Sage Green
  '#C97B7B', // Muted Rose
  '#A98FD4', // Lavender
  '#D4A86A', // Warm Sand
];

export function getNickColor(playerId: string): string {
  let sum = 0;
  for (let i = 0; i < playerId.length; i++) {
    sum += playerId.charCodeAt(i);
  }
  return NICK_COLORS[sum % NICK_COLORS.length];
}
