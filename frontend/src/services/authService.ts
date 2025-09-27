import { authApi } from '../api/auth';
import { User, LoginRequest, RegisterRequest } from '../types';

export class AuthService {
  private static instance: AuthService;
  private currentUser: User | null = null;

  private constructor() {}

  public static getInstance(): AuthService {
    if (!AuthService.instance) {
      AuthService.instance = new AuthService();
    }
    return AuthService.instance;
  }

  // 登录
  async login(credentials: LoginRequest): Promise<{ success: boolean; user?: User; error?: string }> {
    try {
      const resp = await authApi.login(credentials);
      if (!resp.success || !resp.data) {
        return { success: false, error: resp.error || '登录失败' };
      }
      const authData = resp.data;
      if (!authData.token) return { success: false, error: '缺少token' };
      localStorage.setItem('token', authData.token);
      const userObj = authData.user;
      if (userObj) {
        this.currentUser = { id: userObj.id, username: userObj.username, email: userObj.email, createdAt: userObj.createdAt, lastLoginAt: userObj.lastLoginAt };
        return { success: true, user: this.currentUser };
      }
      // 兜底：再调 /auth/me
      const me = await authApi.getCurrentUser();
      if (me.success && me.data) {
        const u = me.data; this.currentUser = { id: u.id, username: u.username, email: u.email, createdAt: u.createdAt, lastLoginAt: u.lastLoginAt };
        return { success: true, user: this.currentUser };
      }
      return { success: true, user: { id: 0, username: 'Unknown', email: '', createdAt: new Date().toISOString() } };
    } catch (error: any) {
      const msg = error.response?.data?.error || error.response?.data?.message || error.message || '登录失败';
      return { success: false, error: msg };
    }
  }

  // 注册
  async register(userData: RegisterRequest): Promise<{ success: boolean; user?: User; error?: string }> {
    try {
      const resp = await authApi.register(userData);
      if (!resp.success || !resp.data) {
        return { success: false, error: resp.error || '注册失败' };
      }
      const authData = resp.data;
      localStorage.setItem('token', authData.token);
      const userObj = authData.user;
      if (userObj) {
        this.currentUser = { id: userObj.id, username: userObj.username, email: userObj.email, createdAt: userObj.createdAt, lastLoginAt: userObj.lastLoginAt };
        return { success: true, user: this.currentUser };
      }
      const me = await authApi.getCurrentUser();
      if (me.success && me.data) {
        const u = me.data; this.currentUser = { id: u.id, username: u.username, email: u.email, createdAt: u.createdAt, lastLoginAt: u.lastLoginAt };
        return { success: true, user: this.currentUser };
      }
      return { success: true, user: { id: 0, username: userData.username, email: userData.email, createdAt: new Date().toISOString() } };
    } catch (error: any) {
      const msg = error.response?.data?.error || error.response?.data?.message || error.message || '注册失败';
      return { success: false, error: msg };
    }
  }

  // 登出
  logout(): void {
    localStorage.removeItem('token');
    this.currentUser = null;
    // 可以调用后端登出API
    authApi.logout().catch(() => {
      // 忽略登出API错误
    });
  }

  // 获取当前用户
  getCurrentUser(): User | null {
    return this.currentUser;
  }

  // 检查是否已登录
  isAuthenticated(): boolean {
    const token = localStorage.getItem('token');
    if (!token) {
      return false;
    }
    
    // 检查token是否过期
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000; // JWT exp是秒，需要转换为毫秒
      if (Date.now() >= exp) {
        localStorage.removeItem('token');
        this.currentUser = null;
        return false;
      }
    } catch (error) {
      console.error('Token格式错误:', error);
      localStorage.removeItem('token');
      this.currentUser = null;
      return false;
    }
    
    return !!this.currentUser;
  }

  // 初始化用户状态（从token恢复）
  async initializeUser(): Promise<User | null> {
    const token = localStorage.getItem('token');
    if (!token) return null;
    try {
      const me = await authApi.getCurrentUser();
      if (me.success && me.data) {
        const u = me.data; this.currentUser = { id: u.id, username: u.username, email: u.email, createdAt: u.createdAt, lastLoginAt: u.lastLoginAt };
        return this.currentUser;
      }
      return null;
    } catch { return null; }
  }

  // 验证表单
  validateLoginForm(username: string, password: string): { valid: boolean; errors: string[] } {
    const errors: string[] = [];
    
    if (!username.trim()) {
      errors.push('用户名不能为空');
    }
    
    if (!password.trim()) {
      errors.push('密码不能为空');
    }
    
    return { valid: errors.length === 0, errors };
  }

  validateRegisterForm(email: string, username: string, password: string): { valid: boolean; errors: string[] } {
    const errors: string[] = [];
    
    if (!email.trim()) {
      errors.push('邮箱不能为空');
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      errors.push('邮箱格式不正确');
    }
    
    if (!username.trim()) {
      errors.push('用户名不能为空');
    } else if (username.length < 3) {
      errors.push('用户名至少3个字符');
    }
    
    if (!password.trim()) {
      errors.push('密码不能为空');
    } else if (password.length < 6) {
      errors.push('密码至少6个字符');
    }
    
    return { valid: errors.length === 0, errors };
  }
}

// 导出单例实例
export const authService = AuthService.getInstance();
