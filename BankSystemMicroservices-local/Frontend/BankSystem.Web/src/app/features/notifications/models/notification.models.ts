export interface Notification {
  id: string;
  userId: string;
  type: string;
  status: string;
  subject: string;
  body: string;
  createdAt: string;
  sentAt: string | null;
  error: string | null;
}
