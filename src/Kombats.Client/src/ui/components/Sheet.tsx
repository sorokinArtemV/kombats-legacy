import { type ReactNode } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { clsx } from 'clsx';

interface SheetProps {
  open: boolean;
  onClose: () => void;
  title?: string;
  children: ReactNode;
  className?: string;
}

export function Sheet({ open, onClose, title, children, className }: SheetProps) {
  return (
    <Dialog.Root open={open} onOpenChange={(v) => !v && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-black/60 backdrop-blur-[2px]" />
        <Dialog.Content
          className={clsx(
            'fixed inset-y-0 right-0 z-50 flex w-full max-w-md flex-col border-l-[0.5px] border-border-subtle bg-glass-dense shadow-[var(--shadow-panel-lift)] outline-none backdrop-blur-[20px]',
            className,
          )}
        >
          {title && (
            <div className="flex items-center justify-between border-b-[0.5px] border-border-subtle px-5 py-3">
              <Dialog.Title className="text-[11px] font-medium uppercase tracking-[0.24em] text-accent-text">
                {title}
              </Dialog.Title>
              <Dialog.Close className="rounded-sm p-1 text-text-muted transition-colors duration-150 hover:text-kombats-gold">
                <span aria-hidden>&#x2715;</span>
              </Dialog.Close>
            </div>
          )}
          <div className="flex-1 overflow-hidden">{children}</div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
