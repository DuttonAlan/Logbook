import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from '@tanstack/react-router'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { ApiError } from './api/client.ts'
import { getAdminKey, subscribeToAdminKey } from './auth/adminKey.ts'
import { AdminKeyGate } from './auth/AdminKeyGate.tsx'
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

// Log Events and Applications do not stay in memory once the admin key is gone.
subscribeToAdminKey(() => {
  if (!getAdminKey()) {
    queryClient.clear()
  }
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AdminKeyGate>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </AdminKeyGate>
  </StrictMode>,
)
