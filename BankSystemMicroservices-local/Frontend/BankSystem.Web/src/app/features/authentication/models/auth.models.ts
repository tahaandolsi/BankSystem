export interface LoginRequest {
  userName: string;
  password: string;
}

export interface RegisterRequest {
  userName: string;
  email: string;
  firstName: string;
  lastName: string;
  password: string;
  confirmPassword: string;
}

export interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiry: string;
  refreshTokenExpiry: string;
}

export interface UserResponse {
  id: string;
  userName: string;
  email: string;
  firstName: string | null;
  lastName: string | null;
  isEmailConfirmed: boolean;
  isActive: boolean;
  createdAt: string;
}
