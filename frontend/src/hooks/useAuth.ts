import { useState, useEffect, useCallback } from 'react';
import { authService } from '../services/authService';
import { User, LoginRequest, RegisterRequest } from '../types';

interface UseAuthReturn {
  user: User | null;
  loading: boolean;
  isAuthenticated: boolean;
  login: (credentials: LoginRequest) => Promise<boolean>;
  register: (userData: RegisterRequest) => Promise<boolean>;
  logout: () => void;
  refreshUser: () => Promise<void>;
}

export const useAuth = (): UseAuthReturn => {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  // 初始化用户状态
  useEffect(() => {
    const initializeUser = async () => {
      try {
        const currentUser = await authService.initializeUser();
        setUser(currentUser);
      } catch (error) {
        console.error('初始化用户状态失败:', error);
        setUser(null);
      } finally {
        setLoading(false);
      }
    };

    initializeUser();
  }, []);

  // 登录
  const login = useCallback(async (credentials: LoginRequest): Promise<boolean> => {
    setLoading(true);
    try {
      const result = await authService.login(credentials);
      if (result.success && result.user) { setUser(result.user); return true; }
      return false;
    } catch { return false; } finally { setLoading(false); }
  }, []);

  // 注册
  const register = useCallback(async (userData: RegisterRequest): Promise<boolean> => {
    setLoading(true);
    try {
      const result = await authService.register(userData);
      if (result.success && result.user) { setUser(result.user); return true; }
      return false;
    } catch { return false; } finally { setLoading(false); }
  }, []);

  // 登出
  const logout = useCallback(() => {
    authService.logout();
    setUser(null);
  }, []);

  // 刷新用户信息
  const refreshUser = useCallback(async (): Promise<void> => {
    try {
      const currentUser = await authService.initializeUser();
      setUser(currentUser);
    } catch (error) {
      console.error('刷新用户信息失败:', error);
    }
  }, []);

  return {
    user,
    loading,
    isAuthenticated: !!user,
    login,
    register,
    logout,
    refreshUser
  };
};
