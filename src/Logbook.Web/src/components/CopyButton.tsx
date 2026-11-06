import { useEffect, useState } from 'react'

export function CopyButton({ text, label = 'Copy' }: { text: string; label?: string }) {
  const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle')

  useEffect(() => {
    if (state === 'idle') {
      return
    }

    const timer = setTimeout(() => setState('idle'), 1500)

    return () => clearTimeout(timer)
  }, [state])

  function copy() {
    // The clipboard is only available on HTTPS and localhost.
    if (!navigator.clipboard) {
      setState('failed')
      return
    }

    navigator.clipboard.writeText(text).then(
      () => setState('copied'),
      () => setState('failed'),
    )
  }

  return (
    <button type="button" className="button small" onClick={copy}>
      {state === 'copied' ? 'Copied' : state === 'failed' ? 'Copy failed' : label}
    </button>
  )
}
