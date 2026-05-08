import axios, { AxiosInstance, AxiosResponse, InternalAxiosRequestConfig } from 'axios';

export const API_BASE_URL = process.env.REACT_APP_API_URL || 'http://localhost:5000/api';

export const apiClient: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
});

apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = localStorage.getItem('token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

apiClient.interceptors.response.use(
  (response: AxiosResponse) => response,
  (error) => {
    console.error('API Error:', {
      message: error.message,
      code: error.code,
      status: error.response?.status,
      data: error.response?.data,
      config: {
        url: error.config?.url,
        method: error.config?.method,
        timeout: error.config?.timeout,
      },
    });

    if (error.code === 'ECONNABORTED' || error.message?.includes('timeout')) {
      error.message = '请求超时，请检查网络连接或稍后重试';
    }

    if (
      error.message?.includes('Network Error') ||
      error.message?.includes('ERR_NETWORK') ||
      error.message?.includes('ERR_CONNECTION_REFUSED') ||
      error.message?.includes('ERR_SSL_PROTOCOL_ERROR')
    ) {
      error.message = '网络连接失败，请检查后端服务和网络设置';
    }

    return Promise.reject(error);
  }
);

export interface ApiResponse<T = any> {
  success: boolean;
  data: T;
  message?: string;
  error?: string;
}

export class BaseApi {
  protected client: AxiosInstance;

  constructor(client: AxiosInstance = apiClient) {
    this.client = client;
  }

  protected unwrap<T>(response: AxiosResponse<ApiResponse<T> | T>): T {
    if (typeof response.data === 'object' && response.data !== null && 'data' in response.data) {
      return (response.data as ApiResponse<T>).data;
    }
    return response.data as T;
  }

  protected normalizeError(error: any): never {
    const errorData = error.response?.data;
    if (typeof errorData === 'object' && errorData !== null) {
      error.message = errorData.error || errorData.message || error.message;
    }
    throw error;
  }

  protected async get<T>(url: string, config?: any): Promise<T> {
    const response = await this.client.get<ApiResponse<T> | T>(url, config);
    return this.unwrap(response);
  }

  protected async post<T>(url: string, data?: any, config?: any): Promise<T> {
    try {
      const response = await this.client.post<ApiResponse<T> | T>(url, data, config);
      return this.unwrap(response);
    } catch (error: any) {
      this.normalizeError(error);
    }
  }

  protected async put<T>(url: string, data?: any, config?: any): Promise<T> {
    const response = await this.client.put<ApiResponse<T> | T>(url, data, config);
    return this.unwrap(response);
  }

  protected async delete<T>(url: string, config?: any): Promise<T> {
    const response = await this.client.delete<ApiResponse<T> | T>(url, config);
    return this.unwrap(response);
  }
}
