import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState, type FormEvent } from 'react'
import { applicationKeys, createApplication, getApplications } from '../../api/applications.ts'
import type { CreatedApplication } from '../../api/types.ts'
import { formatDate } from '../../lib/format.ts'
import { ApiKeyReveal } from './ApiKeyReveal.tsx'
import { APPLICATION_NAME_MAX_LENGTH } from './constants.ts'

export function ApplicationsPage() {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const [created, setCreated] = useState<CreatedApplication & { name: string }>()

  const applications = useQuery({ queryKey: applicationKeys.all, queryFn: ({ signal }) => getApplications(signal) })

  const create = useMutation({
    mutationFn: createApplication,
    // Not kept by the cache of mutations, as the response contains a plain-text API Key.
    gcTime: 0,
    onSuccess: (response, submittedName) => {
      setCreated({ ...response, name: submittedName.trim() })
      setName('')
      void queryClient.invalidateQueries({ queryKey: applicationKeys.all })
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    create.mutate(name)
  }

  return (
    <main className="page">
      <h1>Applications</h1>
      <p className="hint">An Application is what sends Log Events to Logbook, with one or more API Keys.</p>

      {created && (
        <>
          <p>
            <Link to="/applications/$applicationId" params={{ applicationId: created.id }}>
              {created.name}
            </Link>{' '}
            was created.
          </p>
          <ApiKeyReveal apiKey={created.apiKey} onDone={() => setCreated(undefined)} />
        </>
      )}

      <form className="inline-form" onSubmit={submit}>
        <label className="field">
          <span>New Application</span>
          <input
            className="input"
            required
            maxLength={APPLICATION_NAME_MAX_LENGTH}
            placeholder="Shop Web App"
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        </label>
        <button type="submit" className="button primary" disabled={create.isPending || name.trim() === ''}>
          {create.isPending ? 'Creating…' : 'Create'}
        </button>
      </form>
      {create.isError && (
        <p className="message error" role="alert">
          {create.error.message}
        </p>
      )}

      {applications.isError ? (
        <p className="message error" role="alert">
          {applications.error.message}
        </p>
      ) : applications.isPending ? (
        <p>Loading…</p>
      ) : applications.data.length === 0 ? (
        <p className="empty">No Applications yet. Create the first one above.</p>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Created</th>
              <th scope="col">
                <span className="visually-hidden">Logs</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {applications.data.map((application) => (
              <tr key={application.id}>
                <td>
                  <Link to="/applications/$applicationId" params={{ applicationId: application.id }}>
                    {application.name}
                  </Link>
                </td>
                <td>{formatDate(application.createdAt)}</td>
                <td className="table-action">
                  <Link to="/" search={{ app: application.id }}>
                    View logs
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </main>
  )
}
