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
      
      // 注意：后端直接返回 LoginResponse，不是包装格式
      // 检查是否是错误响应（包装格式）
      if (resp && typeof resp === 'object') {
        // 如果有 success 字段，说明是包装格式（错误情况）
        if ('success' in resp && !resp.success) {
          return { success: false, error: (resp as any).error || '登录失败' };
        }
        
        // 如果有 data 字段，说明是包装格式（成功情况）
        if ('data' in resp) {
          const authData = (resp as any).data;
          if (!authData || !authData.token) {
            return { success: false, error: '缺少token' };
          }
          localStorage.setItem('token', authData.token);
          
          this.currentUser = { 
            id: String(authData.userId), 
            username: authData.username, 
            email: authData.email || '', 
            createdAt: new Date().toISOString(),
            lastLoginAt: undefined
          };
          return { success: true, user: this.currentUser };
        }
        
        // 否则，直接是 LoginResponse 对象 { userId, username, email, token }
        const authData = resp as any;
        if (!authData.token) {
          return { success: false, error: '缺少token' };
        }
        
        localStorage.setItem('token', authData.token);
        
        // 直接从扁平结构构建User对象
        if (authData.userId !== undefined && authData.username) {
          this.currentUser = { 
            id: String(authData.userId), 
            username: authData.username, 
            email: authData.email || '', 
            createdAt: new Date().toISOString(),
            lastLoginAt: undefined
          };
          return { success: true, user: this.currentUser };
        }
      }
      
      return { success: false, error: '登录响应格式错误' };
    } catch (error: any) {
      const msg = error.response?.data?.error || error.response?.data?.message || error.message || '登录失败';
      return { success: false, error: msg };
    }
  }

  // 注册
  async register(userData: RegisterRequest): Promise<{ success: boolean; user?: User; error?: string }> {
    try {
      const resp = await authApi.register(userData);
      
      // 注意：后端直接返回 RegisterResponse，不是包装格式
      if (resp && typeof resp === 'object') {
        // 如果有 success 字段，说明是包装格式（错误情况）
        if ('success' in resp && !resp.success) {
          return { success: false, error: (resp as any).error || '注册失败' };
        }
        
        // 如果有 data 字段，说明是包装格式
        if ('data' in resp) {
          const authData = (resp as any).data;
          if (!authData || !authData.token) {
            return { success: false, error: '缺少token' };
          }
          localStorage.setItem('token', authData.token);
          
          this.currentUser = { 
            id: String(authData.userId),
            username: authData.username, 
            email: authData.email || '', 
            createdAt: new Date().toISOString(),
            lastLoginAt: undefined
          };
          return { success: true, user: this.currentUser };
        }
        
        // 否则，直接是响应对象
        const authData = resp as any;
        if (!authData.token) {
          return { success: false, error: '缺少token' };
        }
        
        localStorage.setItem('token', authData.token);
        
        if (authData.userId !== undefined && authData.username) {
          this.currentUser = { 
            id: String(authData.userId),
            username: authData.username, 
            email: authData.email || '', 
            createdAt: new Date().toISOString(),
            lastLoginAt: undefined
          };
          return { success: true, user: this.currentUser };
        }
      }
      
      return { success: false, error: '注册响应格式错误' };
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
    
    // 先检查token是否过期，避免无效请求
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000; // JWT exp是秒，转换为毫秒
      if (Date.now() >= exp) {
        // Token已过期，清理并返回null
        localStorage.removeItem('token');
        this.currentUser = null;
        return null;
      }
    } catch (error) {
      // Token格式错误，清理并返回null
      console.error('Token格式错误:', error);
      localStorage.removeItem('token');
      this.currentUser = null;
      return null;
    }
    
    // Token有效，调用API获取用户信息
    try {
      const me = await authApi.getCurrentUser();
      if (me.success && me.data) {
        this.currentUser = me.data;
        return this.currentUser;
      }
      return null;
    } catch (error) {
      // API调用失败（如401），清理token
      console.error('获取用户信息失败:', error);
      localStorage.removeItem('token');
      this.currentUser = null;
      return null;
    }
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
