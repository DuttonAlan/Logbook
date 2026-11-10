import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useRouter, useRouterState } from '@tanstack/react-router'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { changeSavedSearch, createSavedSearch, deleteSavedSearch, getSavedSearches, savedSearchKeys } from '../../api/savedSearches.ts'
import type { SavedSearch } from '../../api/types.ts'

// The live tail is a way of watching, not part of what is searched for.
function currentQuery(searchStr: string): string {
  const params = new URLSearchParams(searchStr)

  params.delete('live')

  return params.toString()
}

interface ManageDialogProps {
  query: string
  savedSearches: SavedSearch[]
  onOpen: (id: string) => void
  onClose: () => void
}

function ManageDialog({ query, savedSearches, onOpen, onClose }: ManageDialogProps) {
  const dialog = useRef<HTMLDialogElement>(null)
  const queryClient = useQueryClient()
  const [name, setName] = useState('')

  useEffect(() => {
    dialog.current?.showModal()
  }, [])

  const refresh = () => queryClient.invalidateQueries({ queryKey: savedSearchKeys.all })

  const save = useMutation({
    mutationFn: () => {
      const existing = savedSearches.find((item) => item.name === name.trim())

      // The same name replaces what it searches for, as saving again after changing a filter usually means.
      return existing ? changeSavedSearch(existing.id, existing.name, query) : createSavedSearch(name.trim(), query)
    },
    onSuccess: () => {
      setName('')
      void refresh()
    },
  })

  const remove = useMutation({ mutationFn: deleteSavedSearch, onSuccess: () => void refresh() })

  function submit(event: FormEvent) {
    event.preventDefault()

    if (name.trim() !== '' && !save.isPending) {
      save.mutate()
    }
  }

  const replaces = savedSearches.some((item) => item.name === name.trim())

  return (
    <dialog ref={dialog} className="dialog wide" onCancel={onClose}>
      <h2>Saved searches</h2>
      <p className="hint">Saved searches keep all filters of the log explorer, and everyone who can sign in sees them.</p>
      <form className="inline-form" onSubmit={submit}>
        <label className="field">
          <span>Save the current search as</span>
          <input className="input" autoFocus maxLength={100} placeholder="Errors in Production" value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <button type="submit" className="button primary" disabled={name.trim() === '' || save.isPending}>
          {replaces ? 'Replace' : 'Save'}
        </button>
      </form>
      {save.isError && (
        <p className="message error" role="alert">
          {save.error.message}
        </p>
      )}
      {remove.isError && (
        <p className="message error" role="alert">
          {remove.error.message}
        </p>
      )}
      {savedSearches.length > 0 && (
        <table className="table compact">
          <tbody>
            {savedSearches.map((item) => (
              <tr key={item.id}>
                <td>
                  <button type="button" className="link-button" onClick={() => onOpen(item.id)}>
                    {item.name}
                  </button>
                </td>
                <td className="table-action">
                  <button type="button" className="button small danger" disabled={remove.isPending} onClick={() => remove.mutate(item.id)}>
                    Delete
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <div className="dialog-actions">
        <button type="button" className="button" onClick={onClose}>
          Close
        </button>
      </div>
    </dialog>
  )
}

interface SavedSearchesProps {
  // Called before a saved search is opened, so that its relative time range counts from now, as any other new search.
  onOpen: () => void
}

export function SavedSearches({ onOpen }: SavedSearchesProps) {
  const router = useRouter()
  const searchStr = useRouterState({ select: (state) => state.location.searchStr })
  const [isManaging, setIsManaging] = useState(false)

  const savedSearches = useQuery({ queryKey: savedSearchKeys.all, queryFn: ({ signal }) => getSavedSearches(signal) })

  const query = currentQuery(searchStr)
  const active = savedSearches.data?.find((item) => item.query === query)

  function open(id: string) {
    const item = savedSearches.data?.find((savedSearch) => savedSearch.id === id)

    if (item) {
      onOpen()

      // Through the history, so that the router reads the query string like any link to the log explorer.
      router.history.push(`/?${item.query}`)
    }
  }

  return (
    <>
      <select className="input" aria-label="Saved searches" value={active?.id ?? ''} onChange={(event) => open(event.target.value)}>
        <option value="" disabled>
          {savedSearches.data?.length ? 'Saved searches…' : 'No saved searches'}
        </option>
        {savedSearches.data?.map((item) => (
          <option key={item.id} value={item.id}>
            {item.name}
          </option>
        ))}
      </select>
      <button type="button" className="button" onClick={() => setIsManaging(true)}>
        Save…
      </button>
      {isManaging && (
        <ManageDialog
          query={query}
          savedSearches={savedSearches.data ?? []}
          onOpen={(id) => {
            setIsManaging(false)
            open(id)
          }}
          onClose={() => setIsManaging(false)}
        />
      )}
    </>
  )
}
