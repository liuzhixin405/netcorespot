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
      const authResponse = await authApi.login(credentials);
      
      // 检查token是否存在
      if (!authResponse.token) {
        return { success: false, error: '登录响应格式错误：缺少token' };
      }
      
      // 保存token
      localStorage.setItem('token', authResponse.token);
      
      // 获取完整用户信息
      try {
        const user = await authApi.getCurrentUser();
        this.currentUser = user;
        return { success: true, user };
      } catch (getUserError: any) {
        // 即使获取用户信息失败，也可以从登录响应中构造用户对象
        const user: User = {
          id: 0, // 临时ID，实际应该从后端获取
          username: authResponse.username,
          email: authResponse.email,
          createdAt: new Date().toISOString(),
        };
        
        this.currentUser = user;
        return { success: true, user };
      }
    } catch (error: any) {
      
      // 检查是否是超时或网络连接问题
      if (error.code === 'ECONNABORTED' || 
          error.message?.includes('timeout') || 
          error.message?.includes('Network Error') ||
          error.message?.includes('ERR_NETWORK') ||
          error.message?.includes('ERR_CONNECTION_REFUSED') ||
          error.message?.includes('ERR_SSL_PROTOCOL_ERROR')) {
        return { success: false, error: '连接超时，请检查网络连接或稍后重试' };
      }
      
      // 检查是否是服务器错误
      if (error.response?.status >= 500) {
        return { success: false, error: '服务器暂时不可用，请稍后重试' };
      }
      
      // 检查是否是连接问题导致的401错误
      if (error.response?.status === 401) {
        // 如果后端返回401，可能是连接问题而不是认证问题
        // 检查是否有具体的错误信息
        const backendError = error.response?.data?.message || error.response?.data?.error;
        if (backendError) {
          return { success: false, error: backendError };
        } else {
          // 没有具体错误信息，可能是连接问题
          return { success: false, error: '无法连接到服务器，请检查网络连接' };
        }
      }
      
      // 其他错误（包括账号密码错误）
      const errorMessage = error.response?.data?.message || error.response?.data?.error || error.message || '登录失败';
      return { success: false, error: errorMessage };
    }
  }

  // 注册
  async register(userData: RegisterRequest): Promise<{ success: boolean; user?: User; error?: string }> {
    try {
      const authResponse = await authApi.register(userData);
      
      // 保存token
      localStorage.setItem('token', authResponse.token);
      
      // 获取完整用户信息
      const user = await authApi.getCurrentUser();
      this.currentUser = user;
      
      return { success: true, user };
    } catch (error: any) {
      console.error('注册过程出错:', error);
      
      // 检查是否是超时或网络连接问题
      if (error.code === 'ECONNABORTED' || 
          error.message?.includes('timeout') || 
          error.message?.includes('Network Error') ||
          error.message?.includes('ERR_NETWORK') ||
          error.message?.includes('ERR_CONNECTION_REFUSED') ||
          error.message?.includes('ERR_SSL_PROTOCOL_ERROR')) {
        return { success: false, error: '连接超时，请检查网络连接或稍后重试' };
      }
      
      // 检查是否是服务器错误
      if (error.response?.status >= 500) {
        return { success: false, error: '服务器暂时不可用，请稍后重试' };
      }
      
      // 检查是否是连接问题导致的401错误
      if (error.response?.status === 401) {
        const backendError = error.response?.data?.message || error.response?.data?.error;
        if (backendError) {
          return { success: false, error: backendError };
        } else {
          return { success: false, error: '无法连接到服务器，请检查网络连接' };
        }
      }
      
      // 其他错误
      const errorMessage = error.response?.data?.message || error.response?.data?.error || error.message || '注册失败';
      return { success: false, error: errorMessage };
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
        console.log('Token已过期');
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
    
    if (!token) {
      return null;
    }

    // 临时直接从token解析用户信息，跳过API调用
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      console.log('从token解析用户信息:', payload);
      const user: User = {
        id: parseInt(payload.nameid) || 0,
        username: payload.unique_name || 'Unknown',
        email: payload.email || '',
        createdAt: new Date().toISOString(),
      };
      
      this.currentUser = user;
      console.log('用户初始化成功:', user);
      return user;
    } catch (tokenError) {
      console.error('token解析失败:', tokenError);
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
