import axios, { AxiosInstance, AxiosResponse, InternalAxiosRequestConfig } from 'axios';

// API基础配置
export const API_BASE_URL = process.env.REACT_APP_API_URL || 'http://localhost:5000/api';

// 创建axios实例
export const apiClient: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  timeout: 30000, // 增加到30秒，处理慢响应
  headers: {
    'Content-Type': 'application/json',
  },
});

// 请求拦截器 - 添加认证token
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = localStorage.getItem('token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => {
    return Promise.reject(error);
  }
);

// 响应拦截器 - 处理通用错误
apiClient.interceptors.response.use(
  (response: AxiosResponse) => {
    return response;
  },
  (error) => {
    // 记录详细的错误信息用于调试
    console.error('API Error:', {
      message: error.message,
      code: error.code,
      status: error.response?.status,
      data: error.response?.data,
      config: {
        url: error.config?.url,
        method: error.config?.method,
        timeout: error.config?.timeout
      }
    });

    // 处理超时错误
    if (error.code === 'ECONNABORTED' || error.message?.includes('timeout')) {
      error.message = '请求超时，请检查网络连接或稍后重试';
    }

    // 处理网络错误
    if (error.message?.includes('Network Error') || 
        error.message?.includes('ERR_NETWORK') ||
        error.message?.includes('ERR_CONNECTION_REFUSED') ||
        error.message?.includes('ERR_SSL_PROTOCOL_ERROR')) {
      error.message = '网络连接失败，请检查网络设置';
    }

    if (error.response?.status === 401) {
      // 检查是否是登录/注册请求
      const isAuthRequest = error.config?.url?.includes('/auth/login') || 
                           error.config?.url?.includes('/auth/register');
      
      
      // 不再执行页面跳转，让上层组件处理401错误
      // 这样可以避免页面刷新，保持调试信息
    }
    
    return Promise.reject(error);
  }
);

// 通用API响应类型
export interface ApiResponse<T = any> {
  success: boolean;
  data: T;
  message?: string;
  error?: string;
}

// 通用API方法
export class BaseApi {
  protected client: AxiosInstance;

  constructor(client: AxiosInstance = apiClient) {
    this.client = client;
  }

  protected async get<T>(url: string, config?: any): Promise<T> {
    const response = await this.client.get<ApiResponse<T> | T>(url, config);
    // 兼容两种响应格式：{ success: boolean, data: T } 或直接返回 T
    if (typeof response.data === 'object' && response.data !== null && 'data' in response.data) {
      return (response.data as ApiResponse<T>).data;
    }
    return response.data as T;
  }

  protected async post<T>(url: string, data?: any, config?: any): Promise<T> {
    try {
      const response = await this.client.post<ApiResponse<T> | T>(url, data, config);
      
      // 兼容两种响应格式：{ success: boolean, data: T } 或直接返回 T
      if (typeof response.data === 'object' && response.data !== null && 'data' in response.data) {
        return (response.data as ApiResponse<T>).data;
      }
      return response.data as T;
    } catch (error: any) {
      
      // 如果后端返回了错误信息，将其作为错误抛出
      if (error.response?.data) {
        const errorData = error.response.data;
        if (typeof errorData === 'object' && errorData !== null) {
          // 如果后端返回了具体的错误信息，使用它
          if (errorData.message) {
            error.message = errorData.message;
          } else if (errorData.error) {
            error.message = errorData.error;
          }
        }
      }
      
      throw error;
    }
  }

  protected async put<T>(url: string, data?: any, config?: any): Promise<T> {
    const response = await this.client.put<ApiResponse<T> | T>(url, data, config);
    // 兼容两种响应格式：{ success: boolean, data: T } 或直接返回 T
    if (typeof response.data === 'object' && response.data !== null && 'data' in response.data) {
      return (response.data as ApiResponse<T>).data;
    }
    return response.data as T;
  }

  protected async delete<T>(url: string, config?: any): Promise<T> {
    const response = await this.client.delete<ApiResponse<T> | T>(url, config);
    // 兼容两种响应格式：{ success: boolean, data: T } 或直接返回 T
    if (typeof response.data === 'object' && response.data !== null && 'data' in response.data) {
      return (response.data as ApiResponse<T>).data;
    }
    return response.data as T;
  }
}
