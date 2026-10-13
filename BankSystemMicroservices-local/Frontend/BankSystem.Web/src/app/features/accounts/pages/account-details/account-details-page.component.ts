import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { Account } from '../../models/account.models';
import { AccountService } from '../../services/account.service';

@Component({
  selector: 'app-account-details-page',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe, RouterLink],
  templateUrl: './account-details-page.component.html',
  styleUrl: './account-details-page.component.scss',
})
export class AccountDetailsPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly accountService = inject(AccountService);

  readonly account = signal<Account | null>(null);
  readonly isLoading = signal(true);
  readonly error = signal<string | null>(null);

  constructor() {
    const accountId = this.route.snapshot.paramMap.get('id');
    if (accountId) {
      this.loadAccount(accountId);
    } else {
      this.isLoading.set(false);
      this.error.set('Identifiant de compte invalide.');
    }
  }

  private loadAccount(accountId: string): void {
    this.accountService.getById(accountId).subscribe({
      next: (account) => {
        this.account.set(account);
        this.isLoading.set(false);
      },
      error: (error) => {
        this.error.set(error.error?.detail ?? 'Impossible de charger ce compte.');
        this.isLoading.set(false);
      },
    });
  }
}
