import { Component, type ErrorInfo, type ReactNode } from 'react'

interface State { error: Error | null }

/** Evita la pantalla en blanco: un fallo de render (o un archivo viejo tras desplegar) ofrece recargar. */
export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State { return { error } }

  componentDidCatch(error: Error, info: ErrorInfo) { console.error('Error de interfaz:', error, info.componentStack) }

  render() {
    if (!this.state.error) return this.props.children
    return (
      <div role="alert" className="grid min-h-full place-items-center p-6 text-center">
        <div className="max-w-sm space-y-4">
          <h1 className="text-xl font-extrabold">Algo salió mal</h1>
          <p className="text-sm text-fg-2">Ocurrió un error inesperado. Recarga la página; si el problema continúa, vuelve a intentarlo en unos minutos.</p>
          <button type="button" onClick={() => window.location.reload()} className="rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white">
            Recargar
          </button>
        </div>
      </div>
    )
  }
}
