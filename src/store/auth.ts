import { create } from 'zustand'
import type { AccessControl, CurrentUser, LoginRequest } from '../types/auth'
import { buildAccess } from '../utils/access'
import { getCurrentUser, login, logout } from '../services/auth'
import type { RequestError } from '../utils/request'

interface AuthState {
  currentUser: CurrentUser | null
  access: AccessControl
  initialized: boolean
  loading: boolean
  loginLoading: boolean
  fetchCurrentUser: () => Promise<CurrentUser | null>
  login: (payload: LoginRequest) => Promise<void>
  logout: () => Promise<void>
  clearAuth: () => void
}

const emptyAccess = buildAccess(null)

export const useAuthStore = create<AuthState>((set) => ({
  currentUser: null,
  access: emptyAccess,
  initialized: false,
  loading: false,
  loginLoading: false,
  fetchCurrentUser: async () => {
    set((state) => ({
      ...state,
      loading: true,
    }))

    try {
      const currentUser = await getCurrentUser()
      set({
        currentUser,
        access: buildAccess(currentUser),
        initialized: true,
        loading: false,
      })
      return currentUser
    } catch (error) {
      const requestError = error as RequestError
      if (requestError.status !== 401) {
        console.error('获取当前用户失败', error)
      }
      set({
        currentUser: null,
        access: emptyAccess,
        initialized: true,
        loading: false,
      })
      return null
    }
  },
  login: async (payload) => {
    set({ loginLoading: true })
    try {
      await login(payload)
      const currentUser = await getCurrentUser()
      set({
        currentUser,
        access: buildAccess(currentUser),
        initialized: true,
        loading: false,
        loginLoading: false,
      })
    } catch (error) {
      set({ loginLoading: false })
      throw error
    }
  },
  logout: async () => {
    try {
      await logout()
    } finally {
      set({
        currentUser: null,
        access: emptyAccess,
        initialized: true,
        loading: false,
        loginLoading: false,
      })
    }
  },
  clearAuth: () =>
    set({
      currentUser: null,
      access: emptyAccess,
      initialized: true,
      loading: false,
      loginLoading: false,
    }),
}))
