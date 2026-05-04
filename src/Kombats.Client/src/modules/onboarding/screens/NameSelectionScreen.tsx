import { useRef, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { AnimatePresence, motion } from 'motion/react';
import { gameKeys } from '@/app/query-client';
import * as characterApi from '@/transport/http/endpoints/character';
import { usePlayerStore } from '@/modules/player/store';
import {
  DEFAULT_AVATAR_ID,
  SELECTABLE_AVATARS,
  getAvatarAsset,
  type AvatarId,
} from '@/modules/player/avatar-assets';
import { Button } from '@/ui/components/Button';
import { TextInput } from '@/ui/components/TextInput';
import { OnboardingCard } from '../components/OnboardingCard';
import { AvatarCard } from '../components/AvatarCard';
import { NAME_MAX } from '../components/NameInput';
import { mapNameMutationError, validateName } from '../name-validation';

interface SubmitArgs {
  name: string;
  avatarId: AvatarId;
  expectedRevision: number;
}

async function submitOnboarding({ name, avatarId, expectedRevision }: SubmitArgs) {
  await characterApi.setName({ name });
  // setName increments the character revision by one server-side; the next
  // mutation must pass that incremented value so the avatar write is
  // optimistic-concurrency-safe.
  const avatarResponse = await characterApi.changeAvatar({
    expectedRevision: expectedRevision + 1,
    avatarId,
  });
  return { name, avatarResponse };
}

// Oversized fighter sprite drop shadow — matches the hero anchor used on the
// lobby + searching screens (DESIGN_REFERENCE.md §3.16).
const fighterSpriteFilter = 'drop-shadow(0 25px 50px rgba(var(--rgb-black), 0.9))';

export function NameSelectionScreen() {
  const [name, setName] = useState('');
  const [touched, setTouched] = useState(false);
  const [selectedAvatarId, setSelectedAvatarId] = useState<AvatarId>(DEFAULT_AVATAR_ID);
  const [serverErrorOverride, setServerErrorOverride] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const updateCharacter = usePlayerStore((s) => s.updateCharacter);
  const character = usePlayerStore((s) => s.character);

  const selectedAvatar =
    SELECTABLE_AVATARS.find((a) => a.id === selectedAvatarId) ?? SELECTABLE_AVATARS[0];

  // Refs for the roving-tabindex avatar grid. Arrow keys cycle focus and
  // selection across the 5 cards; only the selected card sits in the tab
  // order, mirroring the ARIA Authoring Practices radio-group pattern.
  const avatarButtonRefs = useRef<Array<HTMLButtonElement | null>>([]);

  function handleAvatarGridKeyDown(e: React.KeyboardEvent<HTMLDivElement>) {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight' && e.key !== 'Home' && e.key !== 'End') {
      return;
    }
    e.preventDefault();
    const currentIndex = SELECTABLE_AVATARS.findIndex((a) => a.id === selectedAvatarId);
    const lastIndex = SELECTABLE_AVATARS.length - 1;
    let nextIndex = currentIndex;
    if (e.key === 'ArrowLeft') {
      nextIndex = currentIndex <= 0 ? lastIndex : currentIndex - 1;
    } else if (e.key === 'ArrowRight') {
      nextIndex = currentIndex >= lastIndex ? 0 : currentIndex + 1;
    } else if (e.key === 'Home') {
      nextIndex = 0;
    } else {
      nextIndex = lastIndex;
    }
    setSelectedAvatarId(SELECTABLE_AVATARS[nextIndex].id);
    avatarButtonRefs.current[nextIndex]?.focus();
  }

  const mutation = useMutation({
    mutationFn: submitOnboarding,
    onSuccess: (result) => {
      if (character) {
        updateCharacter({
          ...character,
          name: result.name,
          onboardingState: 'Named',
          revision: result.avatarResponse.revision,
          avatarId: result.avatarResponse.avatarId,
        });
      }
      queryClient.invalidateQueries({ queryKey: gameKeys.state() });
    },
  });

  const validation = validateName(name);
  const showClientError = touched && !validation.ok;
  const canSubmit = !mutation.isPending && validation.ok;

  function handleSubmit() {
    if (!validation.ok) {
      setTouched(true);
      return;
    }
    if (!character) {
      setServerErrorOverride('Character not loaded.');
      return;
    }
    setServerErrorOverride(null);
    mutation.mutate({
      name: validation.trimmed,
      avatarId: selectedAvatarId,
      expectedRevision: character.revision,
    });
  }

  const serverError = serverErrorOverride
    ? serverErrorOverride
    : mutation.isError
      ? mapNameMutationError(mutation.error)
      : null;

  const inputError = showClientError ? validation.error : (serverError ?? undefined);

  return (
    <div className="absolute inset-0">
      {/* Fighter sprite anchored to the bottom-left of the viewport — the
          OnboardingShell's scene background (and its overlays) sits behind. */}
      <div className="pointer-events-none absolute bottom-0 left-0 flex flex-col items-center">
        <AnimatePresence mode="wait">
          <motion.img
            key={selectedAvatar.id}
            src={getAvatarAsset(selectedAvatar.id)}
            alt={selectedAvatar.name}
            className="h-[82vh] w-auto object-contain"
            style={{
              filter: fighterSpriteFilter,
              marginBottom: '-17vh',
            }}
            initial={{ opacity: 0, x: -30 }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: -20 }}
            transition={{ duration: 0.35, ease: 'easeOut' }}
            draggable={false}
          />
        </AnimatePresence>
      </div>

      {/* Center forge card. Offset is pulled to the right of dead-center so it
          clears the bottom-left fighter sprite — translate(-42%, -52%). */}
      <div
        className="absolute left-1/2 top-1/2 w-[540px] max-w-[calc(100vw-2rem)]"
        style={{ transform: 'translate(-42%, -52%)' }}
      >
        <div className="rounded-md border-[0.5px] border-border-subtle bg-glass p-8 shadow-[var(--shadow-panel)] backdrop-blur-[20px] sm:p-10">
          <OnboardingCard
            eyebrow="Welcome"
            title="Choose Your Look"
            subtitle="Pick a display name and the avatar that will represent you"
          >
            <form
              onSubmit={(e) => {
                e.preventDefault();
                handleSubmit();
              }}
              className="flex w-full flex-col gap-6"
            >
              <fieldset className="flex flex-col gap-3">
                <legend className="mb-1 text-center text-[11px] font-medium uppercase tracking-[0.18em] text-text-muted">
                  Avatar
                </legend>
                <div className="grid grid-cols-5 gap-2" onKeyDown={handleAvatarGridKeyDown}>
                  {SELECTABLE_AVATARS.map((avatar, index) => {
                    const isSelected = avatar.id === selectedAvatarId;
                    return (
                      <AvatarCard
                        key={avatar.id}
                        avatar={avatar}
                        selected={isSelected}
                        disabled={mutation.isPending}
                        onSelect={() => setSelectedAvatarId(avatar.id)}
                        tabIndex={isSelected ? 0 : -1}
                        buttonRef={(el) => {
                          avatarButtonRefs.current[index] = el;
                        }}
                      />
                    );
                  })}
                </div>
              </fieldset>

              <TextInput
                label="Display Name"
                placeholder="e.g. Kazumi"
                value={name}
                onChange={(e) => {
                  setName(e.target.value);
                  setServerErrorOverride(null);
                  if (mutation.isError) mutation.reset();
                }}
                onBlur={() => setTouched(true)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    handleSubmit();
                  }
                }}
                error={inputError}
                disabled={mutation.isPending}
                charCount={{ current: validation.trimmed.length, max: NAME_MAX }}
                maxLength={NAME_MAX}
                autoComplete="off"
                spellCheck={false}
                autoFocus
              />

              <div className="flex items-center justify-center">
                <Button
                  type="submit"
                  variant="primary"
                  size="lg"
                  loading={mutation.isPending}
                  disabled={!canSubmit}
                >
                  Continue
                </Button>
              </div>
            </form>
          </OnboardingCard>
        </div>
      </div>
    </div>
  );
}
