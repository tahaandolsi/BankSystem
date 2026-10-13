import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import { Movement } from '../models/movement.models';
import { MovementService } from '../services/movement.service';

@Component({
  selector: 'app-movements-page',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe, ReactiveFormsModule],
  templateUrl: './movements-page.component.html',
  styleUrl: './movements-page.component.scss',
})
export class MovementsPageComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly movementService = inject(MovementService);
  private readonly route = inject(ActivatedRoute);

  readonly movements = signal<Movement[]>([]);
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);
  readonly form = this.formBuilder.nonNullable.group({
    accountId: ['', [Validators.required, Validators.pattern(/^[0-9a-fA-F-]{36}$/)]],
    fromDate: [''],
    toDate: [''],
  });

  constructor() {
    const accountId = this.route.snapshot.queryParamMap.get('accountId');
    if (accountId) {
      this.form.controls.accountId.setValue(accountId);
      this.loadMovements();
    }
  }

  loadMovements(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { accountId, fromDate, toDate } = this.form.getRawValue();
    this.error.set(null);
    this.isLoading.set(true);
    this.movementService.getByAccount(accountId, { fromDate, toDate, page: 1, pageSize: 50 }).subscribe({
      next: (movements) => {
        this.movements.set(movements);
        this.isLoading.set(false);
      },
      error: (error) => {
        this.error.set(error.error?.detail ?? error.error?.error ?? 'Impossible de charger les mouvements.');
        this.isLoading.set(false);
      },
    });
  }
}
