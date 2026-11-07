import { useEffect, useRef, useState } from 'react'

const TYPING_DELAY = 350

// Applies typed text after a short pause, and without an entry per keystroke in the history of the browser.
export function useDebouncedValue(value: string, apply: (value: string) => void) {
  const [draft, setDraft] = useState(value)
  const latestApply = useRef(apply)

  // Read through a reference: the callback is new with every render, and a live tail renders more often than this delay,
  // so restarting the timer for it would mean that it never ends.
  useEffect(() => {
    latestApply.current = apply
  })

  useEffect(() => setDraft(value), [value])

  useEffect(() => {
    if (draft === value) {
      return
    }

    const timer = setTimeout(() => latestApply.current(draft), TYPING_DELAY)

    return () => clearTimeout(timer)
  }, [draft, value])

  return [draft, setDraft] as const
}
