import { BaseApi } from './base';
import { User, LoginRequest, RegisterRequest, AuthResponse } from '../types';

export class AuthApi extends BaseApi {
  // 用户登录
  async login(credentials: LoginRequest): Promise<AuthResponse> {
    return this.post<AuthResponse>('/auth/login', credentials);
  }

  // 用户注册
  async register(userData: RegisterRequest): Promise<AuthResponse> {
    return this.post<AuthResponse>('/auth/register', userData);
  }

  // 获取当前用户信息
  async getCurrentUser(): Promise<User> {
    return this.get<User>('/auth/me');
  }

  // 用户登出
  async logout(): Promise<void> {
    return this.post<void>('/auth/logout');
  }

  // 刷新Token
  async refreshToken(): Promise<AuthResponse> {
    return this.post<AuthResponse>('/auth/refresh');
  }

  // 验证Token是否有效
  async validateToken(): Promise<boolean> {
    try {
      await this.getCurrentUser();
      return true;
    } catch {
      return false;
    }
  }
}

// 导出单例实例
export const authApi = new AuthApi();
