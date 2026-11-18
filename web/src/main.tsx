import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { registerSW } from 'virtual:pwa-register'
import App from './App'
import { ErrorBoundary } from './components/ErrorBoundary'
import { VisorPdfHost } from './components/VisorPdf'
import { ToastProvider } from './components/ui/Toast'
import { AuthProvider } from './lib/auth'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: true } },
})

// Service worker (PWA + push). `autoUpdate`: se actualiza solo al detectar una versión nueva.
// La app instalada busca versiones nuevas cada minuto y al volver al primer plano, sin tener que reinstalar ni borrar datos.
registerSW({
  immediate: true,
  onRegisteredSW(_url, reg) {
    if (!reg) return
    const buscar = () => { void reg.update().catch(() => { /* sin red */ }) }
    setInterval(buscar, 60_000)
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') buscar() })
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ErrorBoundary>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <ToastProvider>
            <App />
            <VisorPdfHost />
          </ToastProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
    </ErrorBoundary>
  </StrictMode>,
)
