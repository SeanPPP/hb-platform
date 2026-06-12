import { create } from 'zustand'
import type { AccessControl, CurrentUser, LoginRequest, NavigationMenuDto } from '../types/auth'
import { buildAccess } from '../utils/access'
import { getCurrentUser, login, logout } from '../services/auth'
import { fetchNavigationMenu } from '../services/navigationService'
import type { RequestError } from '../utils/request'

interface AuthState {
  currentUser: CurrentUser | null
  access: AccessControl
  navigationMenu: NavigationMenuDto[] | undefined
  initialized: boolean
  loading: boolean
  loginLoading: boolean
  fetchCurrentUser: () => Promise<CurrentUser | null>
  refreshCurrentUserSilently: () => Promise<CurrentUser | null>
  login: (payload: LoginRequest) => Promise<void>
  logout: () => Promise<void>
  clearAuth: () => void
}

const emptyAccess = buildAccess(null)

const fetchAndSetMenu = async (
  set: (partial: Partial<AuthState>) => void,
) => {
  try {
    const menu = await fetchNavigationMenu()
    set({ navigationMenu: menu })
  } catch {
    set({ navigationMenu: undefined })
  }
}

export const useAuthStore = create<AuthState>((set) => ({
  currentUser: null,
  access: emptyAccess,
  navigationMenu: undefined,
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
      const access = buildAccess(currentUser)
      set({
        currentUser,
        access,
        initialized: true,
        loading: false,
      })
      // 异步拉取导航菜单（不阻塞）
      void fetchAndSetMenu(set)
      return currentUser
    } catch (error) {
      const requestError = error as RequestError
      if (requestError.status !== 401) {
        console.error('获取当前用户失败', error)
      }
      set({
        currentUser: null,
        access: emptyAccess,
        navigationMenu: undefined,
        initialized: true,
        loading: false,
      })
      return null
    }
  },
  refreshCurrentUserSilently: async () => {
    try {
      const currentUser = await getCurrentUser()
      const access = buildAccess(currentUser)
      set({
        currentUser,
        access,
        initialized: true,
      })

      try {
        const menu = await fetchNavigationMenu()
        set({ navigationMenu: menu })
      } catch (error) {
        console.error('静默刷新导航菜单失败', error)
      }

      return currentUser
    } catch (error) {
      const requestError = error as RequestError
      if (requestError.status !== 401) {
        console.error('静默刷新当前用户失败', error)
      }
      return null
    }
  },
  login: async (payload) => {
    set({ loginLoading: true })
    try {
      await login(payload)
      const currentUser = await getCurrentUser()
      const access = buildAccess(currentUser)
      set({
        currentUser,
        access,
        initialized: true,
        loading: false,
        loginLoading: false,
      })
      void fetchAndSetMenu(set)
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
        navigationMenu: undefined,
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
      navigationMenu: undefined,
      initialized: true,
      loading: false,
      loginLoading: false,
    }),
}))
