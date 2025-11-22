import { BaseApi } from './base';
import { User, LoginRequest, RegisterRequest, AuthResponse, ApiResponseDto } from '../types';

export class AuthApi extends BaseApi {
  // 用户登录（需返回完整包装，不能让 BaseApi.post 解包）
  async login(credentials: LoginRequest): Promise<ApiResponseDto<AuthResponse | null>> {
    const resp = await this.client.post<ApiResponseDto<AuthResponse | null>>('/auth/login', credentials);
    return resp.data; // 保留 success/data/message 结构
  }

  // 用户注册
  async register(userData: RegisterRequest): Promise<ApiResponseDto<AuthResponse | null>> {
    const resp = await this.client.post<ApiResponseDto<AuthResponse | null>>('/auth/register', userData);
    return resp.data;
  }

  // 获取当前用户信息（保持解包兼容 UserDto 简化使用）
  async getCurrentUser(): Promise<ApiResponseDto<User | null>> {
    const resp = await this.client.get<ApiResponseDto<User | null>>('/auth/me');
    return resp.data; // 返回实际数据体
  }

  // 用户登出
  async logout(): Promise<ApiResponseDto<boolean>> {
    const resp = await this.client.post<ApiResponseDto<boolean>>('/auth/logout');
    return resp.data;
  }

  async validateToken(): Promise<boolean> {
    try {
      const me = await this.getCurrentUser();
      return !!(me.success && me.data);
    } catch {
      return false;
    }
  }
}

// 导出单例实例
export const authApi = new AuthApi();
