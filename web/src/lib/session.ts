import type { User } from './types'

// Mismas claves que la app anterior: quien ya tenía sesión abierta en este dominio no tiene que volver a entrar.
const TOKEN_KEY = 'auth_token'
const USER_KEY = 'auth_user'

let token: string | null = null
let onUnauthorized: (() => void) | null = null

function read(key: string): string | null {
  return localStorage.getItem(key) ?? sessionStorage.getItem(key)
}

export function loadSession(): { token: string | null; user: User | null } {
  token = read(TOKEN_KEY)
  let user: User | null = null
  try { user = JSON.parse(read(USER_KEY) || 'null') } catch { /* sesión corrupta */ }
  return { token, user }
}

/** `remember=false` mantiene la sesión solo mientras la pestaña esté abierta. */
export function saveSession(t: string, user: User, remember = true) {
  token = t
  clearStorage()
  const store = remember ? localStorage : sessionStorage
  store.setItem(TOKEN_KEY, t)
  store.setItem(USER_KEY, JSON.stringify(user))
}

export function updateStoredUser(user: User) {
  const store = localStorage.getItem(TOKEN_KEY) ? localStorage : sessionStorage
  store.setItem(USER_KEY, JSON.stringify(user))
}

function clearStorage() {
  for (const s of [localStorage, sessionStorage]) { s.removeItem(TOKEN_KEY); s.removeItem(USER_KEY) }
}

export function clearSession() { token = null; clearStorage() }
export const getToken = () => token
export const setUnauthorizedHandler = (fn: (() => void) | null) => { onUnauthorized = fn }
export const notifyUnauthorized = () => onUnauthorized?.()
