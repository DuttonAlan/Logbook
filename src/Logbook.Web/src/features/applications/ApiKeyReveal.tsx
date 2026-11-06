import type { IssuedApiKey } from '../../api/types.ts'
import { CopyButton } from '../../components/CopyButton.tsx'
import { formatDate } from '../../lib/format.ts'

// A plain-text API Key is only returned once, so it stays in this component's state until it is dismissed.
export function ApiKeyReveal({ apiKey, onDone }: { apiKey: IssuedApiKey; onDone: () => void }) {
  return (
    <section className="panel reveal" role="status">
      <h2>Store this API Key now</h2>
      <p>It is shown only once and cannot be retrieved later.</p>
      <div className="reveal-key">
        <code>{apiKey.key}</code>
        <CopyButton text={apiKey.key} />
      </div>
      {apiKey.expiresAt && <p className="hint">It expires on {formatDate(apiKey.expiresAt)}.</p>}
      <p className="hint">
        Give it to your application as the environment variable <code>Logbook__ApiKey</code>.
      </p>
      <button type="button" className="button primary" onClick={onDone}>
        I have stored it
      </button>
    </section>
  )
}
