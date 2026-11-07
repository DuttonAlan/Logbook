import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, getRouteApi, useNavigate } from '@tanstack/react-router'
import { useState, type FormEvent } from 'react'
import { applicationKeys, changeRetention, deleteApplication, getApplication, issueApiKey, renameApplication, revokeApiKey } from '../../api/applications.ts'
import { ApiError } from '../../api/client.ts'
import type { ApiKey, Application, IssuedApiKey } from '../../api/types.ts'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { formatDate, fromLocalInput } from '../../lib/format.ts'
import { ApiKeyReveal } from './ApiKeyReveal.tsx'
import { APPLICATION_NAME_MAX_LENGTH } from './constants.ts'

const route = getRouteApi('/applications/$applicationId')

function keyStatus(apiKey: ApiKey): { label: string; tone: string } {
  if (apiKey.revokedAt) {
    return { label: `Revoked ${formatDate(apiKey.revokedAt)}`, tone: 'off' }
  }

  if (!apiKey.isActive) {
    return { label: 'Expired', tone: 'off' }
  }

  return { label: 'Active', tone: 'good' }
}

function RenameForm({ application }: { application: Application }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState(application.name)

  const rename = useMutation({
    mutationFn: () => renameApplication(application.id, name),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: applicationKeys.all }),
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    rename.mutate()
  }

  return (
    <form className="inline-form" onSubmit={submit}>
      <label className="field">
        <span>Name</span>
        <input
          className="input"
          required
          maxLength={APPLICATION_NAME_MAX_LENGTH}
          value={name}
          onChange={(event) => {
            setName(event.target.value)
            rename.reset()
          }}
        />
      </label>
      <button type="submit" className="button" disabled={rename.isPending || name.trim() === '' || name.trim() === application.name}>
        Rename
      </button>
      {rename.isSuccess && <span role="status">Renamed.</span>}
      {rename.isError && (
        <span className="message error" role="alert">
          {rename.error.message}
        </span>
      )}
    </form>
  )
}

function ApiKeys({ application }: { application: Application }) {
  const queryClient = useQueryClient()
  const [expiresAt, setExpiresAt] = useState('')
  const [issued, setIssued] = useState<IssuedApiKey>()
  const [revoking, setRevoking] = useState<ApiKey>()

  const refresh = () => queryClient.invalidateQueries({ queryKey: applicationKeys.detail(application.id) })

  const issue = useMutation({
    mutationFn: () => issueApiKey(application.id, fromLocalInput(expiresAt) ?? null),
    // Not kept by the cache of mutations, as the response contains a plain-text API Key.
    gcTime: 0,
    onSuccess: (apiKey) => {
      setIssued(apiKey)
      setExpiresAt('')
      void refresh()
    },
  })

  const revoke = useMutation({
    mutationFn: (apiKey: ApiKey) => revokeApiKey(application.id, apiKey.id),
    onSuccess: () => {
      setRevoking(undefined)
      void refresh()
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    issue.mutate()
  }

  return (
    <section>
      <h2>API Keys</h2>
      <p className="hint">To replace a key without downtime: issue a new one, update your application, then revoke the old one.</p>

      {issued && <ApiKeyReveal apiKey={issued} onDone={() => setIssued(undefined)} />}

      <table className="table">
        <thead>
          <tr>
            <th scope="col">Id</th>
            <th scope="col">Created</th>
            <th scope="col">Expires</th>
            <th scope="col">Status</th>
            <th scope="col">
              <span className="visually-hidden">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {application.apiKeys.map((apiKey) => {
            const status = keyStatus(apiKey)

            return (
              <tr key={apiKey.id}>
                <td>
                  <code>{apiKey.id}</code>
                </td>
                <td>{formatDate(apiKey.createdAt)}</td>
                <td>{apiKey.expiresAt ? formatDate(apiKey.expiresAt) : 'Never'}</td>
                <td>
                  <span className={`key-status ${status.tone}`}>{status.label}</span>
                </td>
                <td className="table-action">
                  {!apiKey.revokedAt && (
                    <button type="button" className="button small" onClick={() => setRevoking(apiKey)}>
                      Revoke
                    </button>
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>

      <form className="inline-form" onSubmit={submit}>
        <label className="field">
          <span>Expires (optional)</span>
          <input className="input" type="datetime-local" value={expiresAt} onChange={(event) => setExpiresAt(event.target.value)} />
        </label>
        <button type="submit" className="button" disabled={issue.isPending}>
          {issue.isPending ? 'Issuing…' : 'Issue API Key'}
        </button>
      </form>
      {issue.isError && (
        <p className="message error" role="alert">
          {issue.error.message}
        </p>
      )}

      {revoking && (
        <ConfirmDialog
          title="Revoke this API Key?"
          confirmLabel="Revoke"
          isPending={revoke.isPending}
          error={revoke.error?.message}
          onConfirm={() => revoke.mutate(revoking)}
          onCancel={() => {
            setRevoking(undefined)
            revoke.reset()
          }}
        >
          <p>
            An application using <code>{revoking.id}</code> can no longer send Log Events. This cannot be undone.
          </p>
        </ConfirmDialog>
      )}
    </section>
  )
}

const MAX_RETENTION_DAYS = 3650

function RetentionSection({ application }: { application: Application }) {
  const queryClient = useQueryClient()
  const [days, setDays] = useState(application.retentionDays?.toString() ?? '')
  const [isConfirming, setIsConfirming] = useState(false)

  const retentionDays = days.trim() === '' ? null : Number(days)

  const change = useMutation({
    mutationFn: () => changeRetention(application.id, retentionDays),
    onSuccess: () => {
      setIsConfirming(false)
      void queryClient.invalidateQueries({ queryKey: applicationKeys.detail(application.id) })
    },
  })

  // Going back to the default can be shorter as well, so that asks, too.
  const canDeleteLogEvents = retentionDays === null || retentionDays < application.effectiveRetentionDays

  function submit(event: FormEvent) {
    event.preventDefault()

    if (canDeleteLogEvents) {
      setIsConfirming(true)
    } else {
      change.mutate()
    }
  }

  return (
    <section>
      <h2>Retention</h2>
      <p>
        Log Events are kept for {application.effectiveRetentionDays} days{application.retentionDays === null && ', the default of this server'}. Older ones are removed
        automatically and cannot be recovered.
      </p>
      <form className="inline-form" onSubmit={submit}>
        <label className="field">
          <span>Days (empty: the default)</span>
          <input
            className="input"
            type="number"
            min={1}
            max={MAX_RETENTION_DAYS}
            step={1}
            value={days}
            onChange={(event) => {
              setDays(event.target.value)
              change.reset()
            }}
          />
        </label>
        <button type="submit" className="button" disabled={change.isPending || retentionDays === application.retentionDays}>
          Change retention
        </button>
        {change.isSuccess && <span role="status">Changed.</span>}
      </form>
      {change.isError && !isConfirming && (
        <p className="message error" role="alert">
          {change.error.message}
        </p>
      )}
      {isConfirming && (
        <ConfirmDialog
          title="Change the retention?"
          confirmLabel="Change retention"
          isPending={change.isPending}
          error={change.error?.message}
          onConfirm={() => change.mutate()}
          onCancel={() => {
            setIsConfirming(false)
            change.reset()
          }}
        >
          <p>Log Events past the new retention are removed with the next retention run, by default within an hour. This cannot be undone.</p>
        </ConfirmDialog>
      )}
    </section>
  )
}

function DeleteSection({ application }: { application: Application }) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [isConfirming, setIsConfirming] = useState(false)

  const remove = useMutation({
    mutationFn: () => deleteApplication(application.id),
    onSuccess: async () => {
      queryClient.removeQueries({ queryKey: applicationKeys.detail(application.id) })
      await queryClient.invalidateQueries({ queryKey: applicationKeys.all })
      await navigate({ to: '/applications' })
    },
  })

  return (
    <section>
      <h2>Delete</h2>
      <p>Deletes this Application with its API Keys and all its Log Events. This cannot be undone, and takes a while for many Log Events.</p>
      <button type="button" className="button danger" onClick={() => setIsConfirming(true)}>
        Delete Application
      </button>
      {isConfirming && (
        <ConfirmDialog
          title="Delete this Application?"
          confirmLabel="Delete"
          typeToConfirm={application.name}
          isPending={remove.isPending}
          error={remove.error?.message}
          onConfirm={() => remove.mutate()}
          onCancel={() => {
            setIsConfirming(false)
            remove.reset()
          }}
        >
          <p>All its Log Events are deleted, and its applications can no longer send any.</p>
        </ConfirmDialog>
      )}
    </section>
  )
}

export function ApplicationPage() {
  const { applicationId } = route.useParams()

  const application = useQuery({
    queryKey: applicationKeys.detail(applicationId),
    queryFn: ({ signal }) => getApplication(applicationId, signal),
  })

  return (
    <main className="page">
      <p>
        <Link to="/applications">← Applications</Link>
      </p>
      {application.isPending ? (
        <p>Loading…</p>
      ) : application.isError ? (
        <p className="message error" role="alert">
          {application.error instanceof ApiError && application.error.status === 404 ? 'This Application does not exist.' : application.error.message}
        </p>
      ) : (
        <>
          <header className="page-header">
            <h1>{application.data.name}</h1>
            <Link to="/" search={{ app: application.data.id }} className="button">
              View logs
            </Link>
          </header>
          <p className="hint">
            Created {formatDate(application.data.createdAt)}. Id <code>{application.data.id}</code>
          </p>
          <RenameForm key={application.data.name} application={application.data} />
          <ApiKeys application={application.data} />
          <RetentionSection key={application.data.retentionDays ?? 'default'} application={application.data} />
          <DeleteSection application={application.data} />
        </>
      )}
    </main>
  )
}
