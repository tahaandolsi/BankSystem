import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AccountService } from '../../services/account.service';
import { Account } from '../../models/account.models';

@Component({
  selector: 'app-accounts-page',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, RouterLink],
  templateUrl: './accounts-page.component.html',
  styleUrl: './accounts-page.component.scss',
})
export class AccountsPageComponent {
  private readonly accountService = inject(AccountService);

  readonly accounts = signal<Account[]>([]);
  readonly isLoading = signal(true);
  readonly error = signal<string | null>(null);

  constructor() {
    this.loadAccounts();
  }

  loadAccounts(): void {
    this.isLoading.set(true);
    this.error.set(null);
debugger
    this.accountService.getMyAccounts().subscribe({
      next: (accounts) => {
        this.accounts.set(accounts);
        this.isLoading.set(false);
      },
      error: (error) => {
        this.error.set(error.error?.detail ?? 'Impossible de charger les comptes.');
        this.isLoading.set(false);
      },
    });
  }
}
