import { useEffect, useRef, useState, type ReactNode } from 'react'

interface ConfirmDialogProps {
  title: string
  children: ReactNode
  confirmLabel: string
  // The user has to type this before they can confirm, for what cannot be undone and has a large effect.
  typeToConfirm?: string
  isPending?: boolean
  error?: string
  onConfirm: () => void
  onCancel: () => void
}

export function ConfirmDialog({ title, children, confirmLabel, typeToConfirm, isPending, error, onConfirm, onCancel }: ConfirmDialogProps) {
  const dialog = useRef<HTMLDialogElement>(null)
  const [typed, setTyped] = useState('')

  useEffect(() => {
    dialog.current?.showModal()
  }, [])

  const canConfirm = !isPending && (typeToConfirm === undefined || typed === typeToConfirm)

  return (
    <dialog ref={dialog} className="dialog" onCancel={onCancel}>
      <form
        method="dialog"
        onSubmit={(event) => {
          event.preventDefault()

          if (canConfirm) {
            onConfirm()
          }
        }}
      >
        <h2>{title}</h2>
        {children}
        {typeToConfirm !== undefined && (
          <label className="field">
            <span>
              Type <strong>{typeToConfirm}</strong> to confirm
            </span>
            <input className="input" autoFocus value={typed} onChange={(event) => setTyped(event.target.value)} />
          </label>
        )}
        {error && (
          <p className="message error" role="alert">
            {error}
          </p>
        )}
        <div className="dialog-actions">
          <button type="button" className="button" onClick={onCancel}>
            Cancel
          </button>
          <button type="submit" className="button danger" disabled={!canConfirm}>
            {isPending ? 'Working…' : confirmLabel}
          </button>
        </div>
      </form>
    </dialog>
  )
}
