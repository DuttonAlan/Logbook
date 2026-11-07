import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from '@tanstack/react-router'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { ApiError } from './api/client.ts'
import { getSession, subscribeToSession } from './auth/session.ts'
import { SessionGate } from './auth/SessionGate.tsx'
import './index.css'
import { router } from './router.tsx'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // A wrong request stays wrong, whereas a server that is busy or restarting comes back.
      retry: (failureCount, error) => failureCount < 2 && !(error instanceof ApiError && error.status < 500),
      refetchOnWindowFocus: false,
    },
  },
})

// Log Events and Applications do not stay in memory once the session is over.
subscribeToSession(() => {
  if (!getSession()?.isSignedIn) {
    queryClient.clear()
  }
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SessionGate>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </SessionGate>
  </StrictMode>,
)
