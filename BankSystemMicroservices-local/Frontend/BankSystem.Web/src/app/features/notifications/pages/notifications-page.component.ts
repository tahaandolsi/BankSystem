import { CommonModule, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';

import { Notification } from '../models/notification.models';
import { NotificationService } from '../services/notification.service';

@Component({
  selector: 'app-notifications-page',
  standalone: true,
  imports: [CommonModule, DatePipe],
  templateUrl: './notifications-page.component.html',
  styleUrl: './notifications-page.component.scss',
})
export class NotificationsPageComponent {
  private readonly notificationService = inject(NotificationService);

  readonly notifications = signal<Notification[]>([]);
  readonly isLoading = signal(true);
  readonly error = signal<string | null>(null);

  constructor() {
    this.loadNotifications();
  }

  loadNotifications(): void {
    this.isLoading.set(true);
    this.error.set(null);
    this.notificationService.getMine().subscribe({
      next: (notifications) => {
        this.notifications.set(notifications);
        this.isLoading.set(false);
      },
      error: (error) => {
        this.error.set(error.error?.detail ?? error.error?.error ?? 'Impossible de charger les notifications.');
        this.isLoading.set(false);
      },
    });
  }
}
