import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, ApiError } from './api'
import { can as canFn } from './permisos'
import { clearSession, getToken, loadSession, saveSession, setUnauthorizedHandler, updateStoredUser } from './session'
import type { Permiso, User } from './types'

interface AuthCtx {
  user: User | null
  /** true mientras se valida la sesión guardada contra /auth/me. */
  loading: boolean
  login: (email: string, password: string, remember: boolean) => Promise<User>
  register: (d: { email: string; password: string; nombre_completo: string; rol: string; invite_code?: string }) => Promise<User>
  logout: () => void
  /** Vuelve a leer el usuario del servidor (tras editar el perfil). */
  refresh: () => Promise<void>
  can: (p: Permiso) => boolean
}

const Ctx = createContext<AuthCtx | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(() => loadSession().user)
  const [loading, setLoading] = useState<boolean>(() => !!loadSession().token)

  const queryClient = useQueryClient()

  // Al salir se vacía la caché: la siguiente cuenta en este navegador no debe ver datos de la anterior.
  const logout = useCallback(() => { clearSession(); queryClient.clear(); setUser(null) }, [queryClient])

  // Cualquier 401 del API cierra la sesión (el router redirige a /login).
  useEffect(() => { setUnauthorizedHandler(logout); return () => setUnauthorizedHandler(null) }, [logout])

  // Revalida la sesión guardada y refresca el usuario (permisos incluidos).
  useEffect(() => {
    if (!getToken()) return
    let cancelled = false
    api.me()
      .then(res => { if (!cancelled) { setUser(res.data); updateStoredUser(res.data) } })
      // Solo un 401 invalida la sesión; un fallo de red o un 500 no debe sacar al usuario.
      .catch((e) => { if (!cancelled && e instanceof ApiError && e.status === 401) logout() })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [logout])

  const login = useCallback(async (email: string, password: string, remember: boolean) => {
    const res = await api.login(email, password)
    queryClient.clear()
    saveSession(res.data.token, res.data.user, remember)
    setUser(res.data.user)
    return res.data.user
  }, [queryClient])

  const register = useCallback(async (d: Parameters<typeof api.register>[0]) => {
    const res = await api.register(d)
    queryClient.clear()
    saveSession(res.data.token, res.data.user, true)
    setUser(res.data.user)
    return res.data.user
  }, [queryClient])

  const refresh = useCallback(async () => {
    const res = await api.me()
    setUser(res.data); updateStoredUser(res.data)
  }, [])

  const value = useMemo<AuthCtx>(() => ({ user, loading, login, register, logout, refresh, can: (p) => canFn(user, p) }), [user, loading, login, register, logout, refresh])
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useAuth(): AuthCtx {
  const c = useContext(Ctx)
  if (!c) throw new Error('useAuth debe usarse dentro de <AuthProvider>')
  return c
}
